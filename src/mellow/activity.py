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
    visual studio code microsoft edge chrome opera firefox browser brave vivaldi chatgpt codex qoder chat
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


def observation(scene):
    vision = scene.get("vision")
    return vision if isinstance(vision, str) and vision.strip() else ""


def scene_evidence(scene):
    return scene["title"] + " " + observation(scene)


def evaluate(path, payload):
    rows = payload.get("scenes", [])[-6:]
    if not rows:
        return {"answer": "I haven’t observed an eligible foreground window yet."}
    current = rows[-1]
    app, title = current["app"], current["title"]
    vision = observation(current)
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
    corrected = False
    corroborating = []
    if ranked:
        best = max(score for score, _ in ranked)
        winners = {name for score, name in ranked if score == best}
        if len(winners) == 1:
            activity = winners.pop()
            corrected = True
            why.append("You previously corrected a matching activity in this app.")
    projects = sorted(set(profiles) | {"mellow"}, key=lambda project: (-len(project), project))
    direct = [project for project in projects if contains(evidence, project)]
    prior = rows[:-1]
    if not ranked:
        categories = [name for name, pattern in CATEGORIES if re.search(pattern, evidence, re.I)]
        if len(direct) == 1:
            activity = direct[0]
        elif not direct and len(categories) == 1:
            activity = categories[0]
        elif not direct and not categories and not tokens(evidence) and app.casefold() in {"code", "codex", "chatgpt", "qoder"}:
            # Only bridge a generic work window across immediately preceding, corroborating scenes.
            tail = prior[-2:]
            if len({row["app"].casefold() for row in tail}) == 2:
                for project in projects:
                    if all(contains(scene_evidence(row), project) for row in tail):
                        activity = project
                        corroborating = tail
                        break
    setup = activity if activity in profiles else ""
    study = bool(re.search(r"\b(study|studying|college work|coursework)\b", activity))
    has_title = bool(title.strip())
    window = f"The {app} window" + (f" “{title}”" if has_title else "")
    if corrected:
        headline = f"My best read is {activity}, based on your saved correction."
        # Keep current evidence visible even when it disagrees with the learned label.
        headline += (" Visible now: " + vision if vision else
                     f" {window} is active, but I don’t have screen details.")
    elif vision:
        title_supports_task = activity and (contains(title, activity) or any(
            name == activity and re.search(pattern, title, re.I) for name, pattern in CATEGORIES))
        headline = (f"Looks like {activity} in “{title}”. {vision}"
                    if title_supports_task and tokens(title) - tokens(vision) else vision)
    elif corroborating:
        headline = (f"You may be continuing {activity} in {app}, based on the previous two windows; "
                    "I don’t have current screen details.")
    elif activity:
        headline = f"{window} suggests {activity}; I don’t have screen details."
    else:
        headline = f"{window} is active; I’m unsure of the task without screen details."
    answer = headline
    if action == "why":
        foreground = app + (f" · {title}" if has_title else "")
        why.append(f"Foreground: {foreground}.")
        if vision:
            why.append("I based that on what was visible on screen.")
        else:
            source = "the window title and app" if has_title else "the foreground app"
            why.append(f"Current evidence is limited to {source}; screen details are unavailable.")
        if activity and not corrected and not corroborating:
            why.append("Activity label derived from the current title or visible evidence.")
        observed_at = current.get("observed_at")
        if isinstance(observed_at, str) and observed_at.strip():
            why.append("Current capture: " + observed_at.strip() + ".")
        if activity and not corroborating:
            for row in reversed(prior):
                if row["app"].casefold() != app.casefold() and contains(scene_evidence(row), activity):
                    corroborating = [row]
                    break
        for row in corroborating:
            why.append(f"Earlier: {row['app']} · {row['title']}.")
        answer = "\n".join([headline, *why])
    return {"answer": answer, "activity": activity, "setup": setup, "study": study}


def main():
    try:
        data=json.loads(base64.b64decode(sys.stdin.buffer.readline(65537)))
        reply=evaluate(Path("data/history.db"),data)
    except (ValueError, KeyError, TypeError, OSError, sqlite3.Error):
        reply={"answer":"Local activity recognition is unavailable."}
    print(base64.b64encode(json.dumps(reply).encode()).decode())


if __name__=="__main__":
    main()
