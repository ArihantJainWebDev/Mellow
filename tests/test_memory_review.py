import io
from contextlib import closing, redirect_stderr, redirect_stdout
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

from mellow.__main__ import main
from mellow.config import Settings
from mellow.storage import History


class MemoryReviewTests(unittest.TestCase):
    def test_migration_and_review_keep_source_and_avoid_network(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "history.db"
            history = History(path)
            _, turn = history.start_turn(None, "Fictional input")
            history.finish_turn(turn, "completed", "OK", enqueue=True)
            job = history.claim_memory_job()
            history.finish_memory_job(turn, job[1], job[3],
                '[{"kind":"fact","text":"Prefers tea"},{"kind":"episode","text":"Went hiking"}]')
            with closing(sqlite3.connect(path)) as db, db:
                db.execute("DROP TABLE memory_candidates")
                db.execute("PRAGMA user_version = 2")
            history = History(path)
            first, second = history.list_memories("pending")
            out = io.StringIO()
            with (
                patch("mellow.__main__.History", return_value=history),
                patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
                patch("mellow.__main__.stream_reply") as network,
                patch("builtins.input", side_effect=[
                    "/candidates", f"/approve {first[0]}", f"/reject {second[0]}",
                    f"/reject {first[0]}", "/memories", "/quit",
                ]),
                redirect_stdout(out),
                redirect_stderr(io.StringIO()) as errors,
            ):
                self.assertEqual(main([]), 0)
            network.assert_not_called()
            self.assertIn(f"(turn {turn})", out.getvalue())
            self.assertIn("No pending candidate", errors.getvalue())
            reopened = History(path)
            self.assertEqual(reopened.list_memories("pending"), [])
            self.assertEqual(reopened.list_memories("approved"), [first])
            self.assertEqual(reopened.list_memories("rejected"), [second])

            _, turn = reopened.start_turn(None, "New input")
            reopened.finish_turn(turn, "completed", "OK", enqueue=True)
            job = reopened.claim_memory_job()
            for _ in range(2):
                reopened.finish_memory_job(turn, job[1], job[3],
                    '[{"kind":"fact","text":"Prefers coffee"}]')
            self.assertEqual(len(reopened.list_memories("pending")), 1)
