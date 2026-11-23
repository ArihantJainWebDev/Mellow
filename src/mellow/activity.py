"""Evidence-based local activity recognition. Only explicit corrections go to disk."""
import base64
from contextlib import closing
import json
from pathlib import Path
import re
import sqlite3
import sys

from mellow.storage import History

GENERIC = set("""the and for with this that from window app tab new untitled blank welcome home page
    visual studio code microsoft edge chrome opera firefox browser brave vivaldi chatgpt codex chat
    google mozilla untitled editor file main clipboard explorer notes project discussion about working desktop terminal
    youtube video user screen looking appears open using title shows active selected""".split())
CATEGORIES = (
    ("studying", r"\b(lecture|syllabus|coursework|assignment|quiz|studying|textbook|problem set)\b"),
    ("shopping", r"\b(shopping|checkout|shopping cart|product comparison|add to cart|headphones for sale)\b"),
    ("watching a video", r"\b(watching a video|video playback|youtube video)\b"),
    ("playing a game", r"\b(gameplay|playing a game|minecraft|valorant|rocket league)\b"),
)


def tokens(text):
    return {t for t in re.findall(r"[^\W_]+", text.casefold()) if len(t) >= 3 and t not in GENERIC and not t.isdigit()}


def contains(text, phrase):
    return re.search(r"(?<!\w)" + re.escape(phrase) + r"(?!\w)", text, re.I) is not None


def evaluate(path, payload):
    rows = payload.get("scenes", [])[-6:]
    if not rows:
        return {"answer": "I haven’t observed an eligible foreground window yet."}
    current = rows[-1]
    app, title = current["app"], current["title"]
    vision = current.get("vision", "")
    evidence = title + " " + vision
    features = tokens(title) or tokens(vision)
    History(path)
    with closing(sqlite3.connect(path)) as db, db:
        profiles = [r[0] for r in db.execute("SELECT profile FROM workspace_windows UNION SELECT profile FROM workspace_links")
                    if r[0] != "default"]
        action = payload.get("action", "read")
        label = " ".join(payload.get("label", "").lower().split())
        if action == "forget":
            count = db.execute("DELETE FROM activity_corrections WHERE label=?", (label,)).rowcount
            return {"answer": f"Removed {count} saved activity correction(s) for {label}."}
        if action == "correct":
            if not 1 <= len(label) <= 80 or not features:
                return {"answer": "I need a distinctive window title or visible topic and a short activity name to learn that correction."}
            db.execute("INSERT INTO activity_corrections(app,features,label) VALUES(?,?,?) "
                       "ON CONFLICT(app,features) DO UPDATE SET label=excluded.label",
                       (app.casefold(), json.dumps(sorted(features)), label))
            return {"answer": f"Learned “{label}” for similar {app} windows mentioning " + ", ".join(sorted(features)[:4]) + "."}
        ranked = []
        for encoded, learned in db.execute("SELECT features,label FROM activity_corrections WHERE app=?", (app.casefold(),)):
            saved = set(json.loads(encoded))
            overlap = len(features & saved)
            if features == saved or (overlap >= 2 and overlap / len(features | saved) >= .5):
                ranked.append((overlap / len(features | saved), learned))
    why = []
    activity = ""
    if ranked:
        best = max(r[0] for r in ranked)
        winners = {name for score,name in ranked if score == best}
        if len(winners)==1:
            activity = winners.pop()
            why.append("You previously corrected a matching activity in this app.")
    projects = sorted(set(profiles + ["mellow"]), key=len, reverse=True)
    direct = [p for p in projects if contains(evidence,p)]
    prior = rows[:-1]
    if not activity and not ranked:
        categories = [name for name,pattern in CATEGORIES if re.search(pattern,evidence,re.I)]
        if len(direct)==1:
            activity = direct[0]
        elif not direct and len(categories)==1:
            activity = categories[0]
        elif not direct and not categories and not tokens(evidence) and app.casefold() in {"code","codex","chatgpt"}:
            # Only bridge a generic work window across immediately preceding, corroborating scenes.
            for project in projects:
                tail = prior[-2:]
                if len({r["app"].casefold() for r in tail})==2 and all(contains(r["title"]+" "+r.get("vision",""),project) for r in tail):
                    activity=project
                    why.append("This is a tentative continuation of the previous two windows.")
                    break
    why.append(f"Foreground: {app} · {title}.")
    if vision:
        why.append("Visible content: "+vision)
    if activity:
        for row in reversed(prior):
            if row["app"]!=app and contains(row["title"]+" "+row.get("vision",""),activity):
                why.append("Earlier: "+row["app"]+" · "+row["title"]+".")
                break
    setup = next((p for p in profiles if activity==p), "")
    study = bool(re.search(r"\b(study|studying|college work|coursework)\b",activity))
    headline = f"My best read is {activity}." if activity else "I’m unsure what task you’re doing."
    return {"answer":"\n".join([headline,*why]),"activity":activity,"setup":setup,"study":study}


def main():
    try:
        data=json.loads(base64.b64decode(sys.stdin.buffer.readline(65537)))
        reply=evaluate(Path("data/history.db"),data)
    except (ValueError, KeyError, TypeError, OSError, sqlite3.Error):
        reply={"answer":"Local activity recognition is unavailable."}
    print(base64.b64encode(json.dumps(reply).encode()).decode())


if __name__=="__main__":
    main()
