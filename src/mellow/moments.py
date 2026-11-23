"""Explicit local activity bookmarks. Screens, private messages and audio are never recorded here."""


def update_moments(db, action, values, now):
    def field(key, size, required=False):
        value = values.get(key, "")
        if not isinstance(value, str):
            raise ValueError("Invalid moment field")
        value = " ".join(value.split())
        if len(value) > size or (required and not value):
            raise ValueError("Use a short name for this moment.")
        return value

    if action == "moment-save":
        name, app, title = field("name", 160, True), field("app", 256, True), field("title", 200, True)
        description, track, artist = field("description", 400), field("title_of_track", 300), field("artist", 250)
        session = db.execute("SELECT id FROM study_sessions WHERE ended IS NULL").fetchone()
        mix = db.execute("""SELECT m.label FROM study_picks p JOIN study_mixes m ON m.id=p.mix_id
            WHERE p.session_id=? ORDER BY p.id DESC LIMIT 1""", (session[0] if session else None,)).fetchone()
        selected = mix[0] if mix else ""
        source = db.execute("SELECT source FROM study_settings WHERE id=1").fetchone()[0]
        if not session or not source or source != values.get("source") or session[0] != values.get("session"):
            track = artist = ""
        search = " ".join((name, app, title, description, track, artist, selected)).casefold()
        mid = db.execute("""INSERT INTO desktop_moments
            (at,name,app,title,description,track,artist,mix,search) VALUES(?,?,?,?,?,?,?,?,?)""",
            (now, name, app, title, description, track, artist, selected, search)).lastrowid
        return f"Remembered #{mid} · {name}."
    if action == "moment-delete":
        raw = str(values.get("id", "")).lstrip("#")
        if not raw.isdecimal() or len(raw) > 17:
            raise ValueError("Use the moment number, e.g. forget moment #2.")
        removed = db.execute("DELETE FROM desktop_moments WHERE id=?", (int(raw),)).rowcount
        return "Moment removed." if removed else "No moment with that number."
    return ""


def find_moments(db, query=""):
    if not isinstance(query,str) or len(query)>300:
        raise ValueError("Use a search of at most 300 characters.")
    terms = list(dict.fromkeys(query.casefold().split()))
    condition = " AND ".join("instr(search,?)>0" for _ in terms) if terms else "1"
    return [dict(row) for row in db.execute(
        "SELECT id,at,name,app,title,description,track,artist,mix FROM desktop_moments WHERE " +
        condition + " ORDER BY id DESC LIMIT 8", terms)]
