import base64
import datetime as dt
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from mellow.storage import History
from mellow.tasks import task_command


class AssignmentTests(unittest.TestCase):
    def test_desktop_protocol_across_three_processes(self):
        with tempfile.TemporaryDirectory() as directory:
            env = dict(os.environ, GROQ_API_KEY="", GROQ_MODEL="",
                       MELLOW_MEMORY_MODEL="", MELLOW_EMBEDDING_MODEL="")
            def run(*lines):
                wire = b"".join(base64.b64encode(line.encode()) + b"\n" for line in (*lines, "/quit"))
                proc = subprocess.run([sys.executable, "-B", "-m", "mellow.desktop_chat"],
                                      cwd=directory, env=env, input=wire, capture_output=True, check=True, timeout=12)
                events = [line.split(b" ", 1) for line in proc.stdout.splitlines()]
                self.assertFalse(any(event in (b"think", b"chunk") for event, _ in events))
                return "".join(base64.b64decode(v).decode() for event, v in events if event == b"text")
            first = run("Add assignment Physics lab due 2026-12-03", "Move Physics lab to 2026-12-06",
                        "/notice Maths quiz due 2026-12-02\nEnglish essay due 2026-12-09")
            self.assertIn("Moved #1 Physics lab to 2026-12-06", first)
            self.assertIn("Added #3 English essay", first)
            second = run("What's due?", "mark #1 done")
            self.assertLess(second.index("#2 Maths quiz"), second.index("#1 Physics lab"))
            self.assertIn("Completed #1", second)
            self.assertIn("#1 Physics lab · 2026-12-06 · done", run("/tasks done"))
            history = History(Path(directory) / "data/history.db")
            self.assertFalse(history.list_sessions())
            path = Path(directory) / "export.json"
            history.export(path)
            self.assertEqual(len(json.loads(path.read_text())["assignments"]), 3)
            history.reset()
            self.assertEqual(run("/tasks all").count("#1 Physics lab"), 0)

    def test_notice_replay_dates_private_and_ambiguity(self):
        with tempfile.TemporaryDirectory() as folder:
            history = History(Path(folder) / "db")
            def ask(text):
                return task_command(text, history, dt.date(2026, 11, 23))
            notice = "/notice Subject: Robotics\nDeadline: tomorrow"
            self.assertIn("2026-11-24", ask(notice))
            self.assertIn("2026-11-27", ask("Move #1 to Friday"))
            self.assertIn("already imported", ask(notice))
            self.assertIn("Added #2", ask("Add assignment Robotics due today"))
            self.assertIn("No unique", ask("mark Robotics done"))
            self.assertIn("Invalid deadline", ask("Move #1 to 2026-02-30"))
            self.assertIn("#2", ask("what’s due today?"))
            self.assertNotIn("#1", ask("what's due today?"))
            self.assertIn("2026-12-09", ask("/notice Literature essay due 9 December 2026"))
            self.assertIn("Moved #3", ask("/notice Literature essay deadline extended to 11 Dec 2026"))
            self.assertIn("already imported", ask("/notice Literature essay deadline extended to 11 Dec 2026"))
            self.assertIn("2026-12-10", ask("change the deadline for Literature essay to 10 Dec 2026"))
            self.assertIn("skipped", ask("/notice Quiz sometime soon"))
            self.assertIsNone(ask("Why is the sky blue?"))
            self.assertIn("private", task_command(notice, None))
