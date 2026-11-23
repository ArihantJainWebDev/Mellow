"""Local named workspaces. The original two-column desktop protocol still works."""
import base64
from contextlib import closing
from pathlib import Path
import re
import sqlite3
import sys
from urllib.parse import urlsplit

from mellow.storage import History


def profile_name(value):
    name = " ".join(value.split()).casefold()
    if not re.fullmatch(r"[\w][\w .-]{0,47}", name):
        raise ValueError("Invalid workspace name")
    return name


def link_url(value):
    url = urlsplit(value)
    if (len(value) > 1800 or url.scheme != "https" or not url.hostname or
            url.username is not None or url.password is not None or
            any(c.isspace() or ord(c) < 32 or c in '\\"' for c in value)):
        raise ValueError("Use a complete HTTPS address")
    return value


def transfer(action, path, names, profile="default"):
    name = profile_name(profile)
    history = History(path)
    with closing(sqlite3.connect(history.path)) as db, db:
        db.execute("BEGIN IMMEDIATE" if action in {"save", "pin", "unpin", "forget"} else "BEGIN")
        if action == "save":
            if not 1 <= len(names) <= 3 or any(
                not app or app.startswith("@") or not title or len(app) > 256 or len(title) > 200
                for app, title in names
            ):
                raise ValueError("Invalid window set")
            db.execute("DELETE FROM workspace_windows WHERE profile=?", (name,))
            db.executemany("INSERT INTO workspace_windows(profile,position,app,title) VALUES(?,?,?,?)",
                           ((name, i, app, title) for i, (app, title) in enumerate(names)))
        elif action in {"pin", "unpin"}:
            if len(names) != 1:
                raise ValueError("Expected one link")
            url = link_url(names[0][1])
            browser = names[0][0].removeprefix("@link:") if names[0][0].startswith("@link:") else ""
            if browser not in {"", "chrome", "msedge", "opera", "firefox", "brave", "vivaldi"}:
                raise ValueError("Unsupported browser")
            if action == "pin":
                db.execute("""INSERT INTO workspace_links(profile,url,browser) VALUES(?,?,?)
                    ON CONFLICT(profile,url) DO UPDATE SET browser=excluded.browser""", (name, url, browser))
                if db.execute("SELECT count(*) FROM workspace_links WHERE profile=?", (name,)).fetchone()[0] > 8:
                    raise ValueError("Workspace has eight links")
            else:
                db.execute("DELETE FROM workspace_links WHERE profile=? AND url=?", (name, url))
        elif action == "forget":
            db.execute("DELETE FROM workspace_windows WHERE profile=?", (name,))
            db.execute("DELETE FROM workspace_links WHERE profile=?", (name,))
        elif action == "list":
            return [("@profile", row[0]) for row in db.execute(
                "SELECT profile FROM workspace_windows UNION SELECT profile FROM workspace_links ORDER BY profile")]
        elif action != "load":
            raise ValueError("Unknown operation")
        windows = db.execute("SELECT app,title FROM workspace_windows WHERE profile=? ORDER BY position",
                             (name,)).fetchall()
        links = db.execute("SELECT url,browser FROM workspace_links WHERE profile=? ORDER BY rowid", (name,))
        return windows + [("@link:" + row[1] if row[1] else "@link", row[0]) for row in links]


def main():
    try:
        action = sys.argv[1]
        profile = base64.b64decode(sys.argv[2], validate=True).decode() if len(sys.argv) == 3 else "default"
        pairs = []
        for line in sys.stdin:
            app, title = line.split()
            pairs.append(tuple(base64.b64decode(x, validate=True).decode() for x in (app, title)))
        for pair in transfer(action, Path("data/history.db"), pairs, profile):
            print(" ".join(base64.b64encode(s.encode()).decode() for s in pair))
    except (IndexError, ValueError, OSError, sqlite3.Error):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
