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
            self.assertEqual(json.loads(export.read_text())["workspace_windows"],
                             [{"position": 0, "app": "Code", "title": "Demo — revised"}])
            history.reset()
            self.assertEqual(run("load").stdout, b"")
