import base64
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from mellow.storage import History


class WorkspaceMemoryTests(unittest.TestCase):
    def test_desktop_bridge_restarts_replaces_exports_and_resets(self):
        with tempfile.TemporaryDirectory() as folder:
            def run(action, rows=(), check=True):
                wire = b"".join(b" ".join(base64.b64encode(field.encode()) for field in row)
                                + b"\n" for row in rows)
                return subprocess.run([sys.executable, "-B", "-m", "mellow.workspace_memory", action],
                                      input=wire, cwd=folder, capture_output=True, timeout=10, check=check)

            original = [("opera", "Mellow · research"), ("Code", "Mellow — editor")]
            first = run("save", original).stdout
            self.assertEqual(run("load").stdout, first)
            self.assertNotEqual(run("save", [("Code", "")], check=False).returncode, 0)
            self.assertEqual(run("load").stdout, first)
            replacement = [("Code", "Demo — revised")]
            self.assertEqual(run("save", replacement).stdout, run("load").stdout)
            history = History(Path(folder) / "data/history.db")
            export = Path(folder) / "export.json"
            history.export(export)
            self.assertEqual(json.loads(export.read_text(encoding="utf-8"))["workspace_windows"],
                             [{"profile": "default", "position": 0, "app": "Code", "title": "Demo — revised"}])
            history.reset()
            self.assertEqual(run("load").stdout, b"")

    def test_named_setups_pins_and_v8_migration(self):
        from mellow.workspace_memory import transfer
        import sqlite3
        from contextlib import closing
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "history.db"
            History(path)
            with closing(sqlite3.connect(path)) as db, db:
                db.execute("DROP TABLE workspace_windows")
                db.execute("CREATE TABLE workspace_windows(position INTEGER PRIMARY KEY,app TEXT,title TEXT)")
                db.execute("INSERT INTO workspace_windows VALUES(0,'Code','old project')")
                db.execute("PRAGMA user_version=8")
            self.assertEqual(transfer("load",path,[]), [("Code","old project")])
            transfer("save",path,[("opera","lecture")],"Study")
            entry = [("@link","https://example.com/notes")]
            self.assertEqual(transfer("pin",path,entry,"study"), [("opera","lecture"), *entry])
            self.assertEqual(transfer("pin",path,entry,"Study"), transfer("load",path,[],"study"))
            with self.assertRaises(ValueError):
                transfer("pin",path,[("@link","file:///notes.txt")],"study")
            self.assertEqual(transfer("unpin",path,entry,"study"), [("opera","lecture")])
            transfer("forget",path,[],"study")
            self.assertEqual(transfer("list",path,[]), [("@profile","default")])
