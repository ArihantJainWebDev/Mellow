"""Store the explicitly saved desktop setup in the normal exportable local database."""
import base64
from contextlib import closing
from pathlib import Path
import sqlite3
import sys

from mellow.storage import History


def transfer(action, path, names):
    history = History(path)
    with closing(sqlite3.connect(history.path)) as db, db:
        if action == "save":
            if not 1 <= len(names) <= 3 or any(
                not app or not title or len(app) > 256 or len(title) > 200 for app, title in names
            ):
                raise ValueError("Invalid window set")
            db.execute("DELETE FROM workspace_windows")
            db.executemany("INSERT INTO workspace_windows(position,app,title) VALUES(?,?,?)",
                           ((i, app, title) for i, (app, title) in enumerate(names)))
        elif action != "load":
            raise ValueError("Unknown operation")
        return db.execute("SELECT app,title FROM workspace_windows ORDER BY position").fetchall()


def main():
    try:
        action, = sys.argv[1:]
        pairs = []
        for line in sys.stdin:
            app, title = line.split()
            pairs.append(tuple(base64.b64decode(x, validate=True).decode() for x in (app, title)))
        for pair in transfer(action, Path("data/history.db"), pairs):
            print(" ".join(base64.b64encode(s.encode()).decode() for s in pair))
    except (ValueError, OSError, sqlite3.Error):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
