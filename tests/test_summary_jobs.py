from contextlib import closing, redirect_stdout
from io import StringIO
from pathlib import Path
import sqlite3
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from mellow.__main__ import main
from mellow.config import Settings
from mellow.storage import History
from mellow.summary import session_summary


class SummaryAndJobsTests(unittest.TestCase):
    def test_resume_includes_bounded_recap_and_clear_drops_it(self):
        def reply(*args, **kwargs):
            yield "OK"

        with TemporaryDirectory() as folder:
            path = Path(folder) / "history.db"
            history = History(path)
            session_id = None
            for index in range(18):
                session_id, turn = history.start_turn(session_id, f"Unique source {index}." + "x" * 250)
                history.finish_turn(turn, "completed", "y" * 200)
            _, turn = history.start_turn(session_id, "Failed source")
            history.finish_turn(turn, "failed", "Partial reply")
            _, turn = history.start_turn(None, "Other session")
            history.finish_turn(turn, "completed", "Unrelated reply")

            with (
                patch("mellow.__main__.History", return_value=History(path)),
                patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
                patch("mellow.__main__.stream_reply", side_effect=reply) as send,
                patch("builtins.input", side_effect=[
                    f"/resume {session_id}", "/summary", "Continue", "/clear", "Fresh", "/quit",
                    "/summary", "Private", "/quit",
                ]),
                redirect_stdout(StringIO()) as output,
            ):
                self.assertEqual(main([]), 0)
                self.assertEqual(main(["--private"]), 0)
            resumed, cleared, private = [call.kwargs["context"] for call in send.call_args_list]
            recap = resumed[1]["content"]
            self.assertIn(recap, output.getvalue())
            self.assertIn('"turn": 3', recap)
            self.assertIn("Unique source 13.", recap)
            for omitted in ("Unique source 0.", "Unique source 14.", "Failed source", "Other session",
                            "x" * 160, "y" * 100):
                self.assertNotIn(omitted, recap)
            self.assertIn("Unique source 14.", resumed[2]["content"])
            self.assertEqual(len(cleared), 1)
            self.assertEqual(len(private), 1)
            with closing(sqlite3.connect(path)) as db, db:
                db.execute("DELETE FROM turns WHERE id = 3")
            self.assertNotIn("Unique source 2.", session_summary(history, session_id)["content"])

    def test_job_diagnostics_and_retry_migrate_existing_history(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / "history.db"
            history = History(path)
            session_id, turn = history.start_turn(None, "A saved turn")
            history.finish_turn(turn, "completed", "OK", enqueue=True)
            with closing(sqlite3.connect(path)) as db, db:
                db.execute("ALTER TABLE memory_jobs DROP COLUMN last_error")
                db.execute("PRAGMA user_version = 3")
            history = History(path)
            self.assertEqual(history.recent_completed(session_id, 1), [("A saved turn", "OK")])
            self.assertFalse(history.retry_memory_job(turn))
            for now in (100, 110, 120):
                with patch("mellow.storage.time.time", return_value=now):
                    claimed = history.claim_memory_job()
                    history.finish_memory_job(turn, claimed[1], claimed[3], None,
                                              error="LM Studio timed out.")
            self.assertEqual(history.failed_memory_jobs(), [(turn, 3, "LM Studio timed out.")])
            out = StringIO()
            with (
                patch("mellow.__main__.History", return_value=History(path)),
                patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
                patch("builtins.input", side_effect=["/jobs", f"/retry-job {turn}", "/quit"]),
                redirect_stdout(out),
            ):
                self.assertEqual(main([]), 0)
            self.assertIn("LM Studio timed out.", out.getvalue())
            self.assertIn(f"Requeued turn {turn}.", out.getvalue())
            self.assertEqual(history.memory_job_counts(), {"queued": 1})
            claimed = history.claim_memory_job()
            self.assertEqual(claimed[3], 1)
            history.finish_memory_job(turn, claimed[1], claimed[3], "[]")
            self.assertEqual(history.memory_job_counts(), {"completed": 1})
