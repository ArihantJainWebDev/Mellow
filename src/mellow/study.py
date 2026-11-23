"""Local study sessions, music choices and observed media history."""
import base64
from contextlib import closing
import json
from pathlib import Path
import sqlite3
import sys
import time
from urllib.parse import parse_qs, urlsplit

from mellow.storage import History
from mellow.moments import find_moments, update_moments


def music_link(raw):
    """Save a canonical YouTube Music track or playlist; discard sharing parameters."""
    from re import fullmatch
    url = urlsplit(raw.strip())
    if url.scheme != "https" or url.netloc.lower() not in {"music.youtube.com", "www.youtube.com", "youtube.com", "youtu.be"}:
        raise ValueError("Paste a YouTube Music song or playlist link.")
    query = parse_qs(url.query)
    video = url.path[1:] if url.netloc.lower() == "youtu.be" else query.get("v", [""])[0]
    playlist = query.get("list", [""])[0]
    if url.path == "/playlist" and fullmatch(r"[A-Za-z0-9_-]{5,120}", playlist):
        return "https://music.youtube.com/playlist?list=" + playlist
    if (url.path == "/watch" or url.netloc.lower() == "youtu.be") and fullmatch(r"[A-Za-z0-9_-]{11}", video):
        return "https://music.youtube.com/watch?v=" + video
    raise ValueError("Use the share link for a specific song or playlist.")


def clean(value, limit):
    if not isinstance(value, str) or not 1 <= len(value.strip()) <= limit or any(ord(c) < 32 for c in value):
        raise ValueError("Check the text and try again.")
    return value.strip()


def pick(db, session, now, *, advance=False):
    items = db.execute("""SELECT m.id,m.label,m.url,COUNT(p.id) AS used,
        COALESCE(SUM(p.liked),0)*2 - COALESCE(SUM(p.skipped),0) AS preference,
        COALESCE(MAX(p.id),0) AS last_pick
        FROM study_mixes m LEFT JOIN study_picks p ON p.mix_id=m.id
        WHERE m.hidden=0 GROUP BY m.id ORDER BY preference DESC,used ASC,last_pick ASC,m.id""").fetchall()
    previous = db.execute("SELECT id,mix_id FROM study_picks WHERE session_id=? ORDER BY id DESC LIMIT 1",
                          (session,)).fetchone()
    if not items:
        return "", "Add a song or playlist when you want music."
    if advance and previous:
        alternatives = [row for row in items if row["id"] != previous["mix_id"]]
        if not alternatives:
            return "", "Add a second music link before switching mixes."
        db.execute("UPDATE study_picks SET skipped=1 WHERE id=?", (previous["id"],))
        items = alternatives
    # Give a newly added mix one trial before repeatedly choosing a favorite.
    choice = next((row for row in items if not row["used"]), items[0])
    db.execute("INSERT INTO study_picks(session_id,mix_id,picked_at) VALUES(?,?,?)", (session, choice["id"], now))
    return choice["url"], "Selected " + choice["label"] + "."


def handle(path, action, values, now=None):
    now = int(time.time() if now is None else now)
    History(path)
    with closing(sqlite3.connect(path, timeout=5)) as db, db:
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        db.execute("BEGIN IMMEDIATE" if action != "status" else "BEGIN")
        active = db.execute("SELECT id,started FROM study_sessions WHERE ended IS NULL").fetchone()
        sid = active["id"] if active else None
        note, launch, avoid = "", "", False
        if action == "add":
            label, url = clean(values["label"], 80), music_link(values["url"])
            row = db.execute("SELECT id,url FROM study_mixes WHERE label=? COLLATE NOCASE OR url=?", (label, url)).fetchall()
            if row and (len(row) != 1 or row[0]["url"] != url):
                raise ValueError("This music label already belongs to another link.")
            db.execute("INSERT INTO study_mixes(label,url) VALUES(?,?) ON CONFLICT(url) DO UPDATE SET label=excluded.label,hidden=0",
                       (label, url))
            note = "Saved " + label + "."
        elif action == "remove":
            changed = db.execute("UPDATE study_mixes SET hidden=1 WHERE label=? COLLATE NOCASE AND hidden=0",
                                 (clean(values["label"], 80),)).rowcount
            note = "Removed from the rotation." if changed else "No music with that label."
        elif action in {"start", "auto-start"}:
            if action == "auto-start":
                cue = db.execute("SELECT app,cue FROM study_settings WHERE id=1").fetchone()
                if not cue["cue"] or cue["app"] != values.get("app") or cue["cue"] != values.get("cue"):
                    raise ValueError("The study cue has changed.")
            if active:
                note = "Your session is already running."
            else:
                topic = values.get("topic") or (values.get("cue") if action=="auto-start" else "")
                if topic:
                    topic = clean(topic, 150)
                sid = db.execute("INSERT INTO study_sessions(started,topic) VALUES(?,?)", (now, topic)).lastrowid
                launch, note = pick(db, sid, now)
                note = "Study session started. " + note
        elif action == "end":
            if sid:
                db.execute("UPDATE study_sessions SET ended=? WHERE id=?", (now, sid))
                note = f"Session finished · {max(1,(now-active['started'])//60)} min."
            else:
                note = "No study session is running."
        elif action == "open":
            item = db.execute("""SELECT m.url,m.label FROM study_picks p JOIN study_mixes m ON m.id=p.mix_id
                WHERE p.session_id=? AND m.hidden=0 ORDER BY p.id DESC LIMIT 1""", (sid,)).fetchone()
            if not item:
                raise ValueError("No current mix. Add a link or choose Next mix.")
            launch, note = item["url"], "Selected " + item["label"] + "."
        elif action in {"next", "like"}:
            if not sid:
                raise ValueError("Start a study session first.")
            if action == "next":
                launch, note = pick(db, sid, now, advance=True)
            else:
                row = db.execute("SELECT id FROM study_picks WHERE session_id=? ORDER BY id DESC LIMIT 1", (sid,)).fetchone()
                if not row:
                    raise ValueError("Choose a study mix first.")
                db.execute("UPDATE study_picks SET liked=1 WHERE id=?", (row["id"],))
                note = "Preference saved for this mix."
        elif action in {"cue", "uncue", "bind", "unbind"}:
            if action == "cue":
                app, caption = clean(values["app"], 150), clean(values["cue"], 150)
                if len(caption) < 4:
                    raise ValueError("Use at least four characters of the study window title.")
                db.execute("UPDATE study_settings SET app=?,cue=? WHERE id=1", (app,caption))
                note = "Study cue saved. Keep that window active for 45 seconds to start."
            elif action == "uncue":
                db.execute("UPDATE study_settings SET app='',cue='' WHERE id=1")
                note = "Automatic study starts are off."
            elif action == "bind":
                source = clean(values["source"], 250)
                db.execute("UPDATE study_settings SET source=? WHERE id=1", (source,))
                note = "Linked the selected Windows media source for study sessions."
            else:
                db.execute("UPDATE study_settings SET source='' WHERE id=1")
                note = "Media source unlinked."
        elif action in {"enable-skip", "disable-skip"}:
            state = int(action=="enable-skip")
            db.execute("UPDATE study_settings SET auto_skip=? WHERE id=1", (state,))
            note = "Repeat-skip learning is on." if state else "Automatic song skips are off."
        elif action in {"song-keep", "song-skip"}:
            source = db.execute("SELECT source FROM study_settings WHERE id=1").fetchone()[0]
            if not sid or sid!=values.get("session") or not source or source!=values.get("source"):
                raise ValueError("Song feedback needs an active session and its linked player.")
            song = clean(values["title"], 300).casefold()
            artist = clean(values["artist"], 250).casefold() if values.get("artist") else ""
            vote = "keep" if action=="song-keep" else "skip"
            db.execute("""INSERT INTO study_song_feedback VALUES(?,?,?,?,?,?)
                ON CONFLICT(session_id,source,song,artist) DO UPDATE SET vote=excluded.vote,at=excluded.at""",
                (sid, source, song, artist, vote, now))
            note = "Marked this song to keep." if vote=="keep" else "Skip recorded for this study session."
        elif action == "track":
            expected = values.get("session")
            source = db.execute("SELECT source FROM study_settings WHERE id=1").fetchone()[0]
            if sid and sid == expected and source and source == values.get("source"):
                title = clean(values["title"], 300)
                artist = clean(values["artist"], 250) if values.get("artist") else ""
                if db.execute("SELECT auto_skip FROM study_settings WHERE id=1").fetchone()[0]:
                    feedback = db.execute("""SELECT vote FROM study_song_feedback
                        WHERE source=? AND song=? AND artist=? ORDER BY at DESC,session_id DESC LIMIT 2""",
                        (source, title.casefold(), artist.casefold())).fetchall()
                    avoid = len(feedback)==2 and all(r[0]=="skip" for r in feedback)
                last = db.execute("""SELECT source,title,artist FROM study_tracks WHERE session_id=?
                    ORDER BY id DESC LIMIT 1""", (sid,)).fetchone()
                if last is None or tuple(last) != (source, title, artist):
                    db.execute("INSERT INTO study_tracks(session_id,at,source,title,artist) VALUES(?,?,?,?,?)",
                               (sid, now, source, title, artist))
        elif action in {"moment-save", "moment-delete", "moment-search"}:
            note = update_moments(db, action, values, now)
        elif action not in {"status", "history", "sessions"}:
            raise ValueError("Unknown study request")
        settings = db.execute("SELECT app,cue,source,auto_skip FROM study_settings WHERE id=1").fetchone()
        session = db.execute("SELECT id,started,ended,topic FROM study_sessions ORDER BY id DESC LIMIT 1").fetchone()
        mix = db.execute("""SELECT m.label FROM study_picks p JOIN study_mixes m ON m.id=p.mix_id
            WHERE p.session_id=? ORDER BY p.id DESC LIMIT 1""", (session["id"] if session else 0,)).fetchone()
        mixes = [r[0] for r in db.execute("SELECT label FROM study_mixes WHERE hidden=0 ORDER BY label COLLATE NOCASE")]
        tracks = [dict(r) for r in db.execute("""SELECT at,title,artist FROM study_tracks
            ORDER BY id DESC LIMIT 8""")]
        due = [dict(r) for r in db.execute("SELECT id,title,due FROM assignments WHERE done=0 ORDER BY due,id LIMIT 3")]
        sessions = [dict(r) for r in db.execute("""SELECT started,ended,topic FROM study_sessions
            WHERE ended IS NOT NULL ORDER BY id DESC LIMIT 6""")]
        return dict(note=note, launch=launch, avoid=avoid, sessions=sessions, topic=session["topic"] if session else "", active=bool(session and session["ended"] is None),
                    session=session["id"] if session else 0, started=session["started"] if session else 0,
                    ended=session["ended"] if session and session["ended"] else 0,
                    mix=mix[0] if mix else "", mixes=mixes, tracks=tracks, due=due,
                    moments=find_moments(db, values.get("query", "") if action=="moment-search" else ""), **dict(settings))


def main():
    try:
        data = json.loads(base64.b64decode(sys.stdin.buffer.readline(32769), validate=False))
        out = handle(Path("data/history.db"), data["action"], data)
    except (ValueError, KeyError, TypeError) as exc:
        out = {"error": str(exc)}
    except (OSError, sqlite3.Error):
        out = {"error": "Could not access local study data."}
    print(base64.b64encode(json.dumps(out).encode()).decode())


if __name__ == "__main__":
    main()
