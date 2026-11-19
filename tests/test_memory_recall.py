import io
from contextlib import closing, redirect_stdout
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

from mellow.__main__ import main
from mellow.config import Settings
from mellow.storage import History


class MemoryRecallTests(unittest.TestCase):
    def test_fresh_chat_recalls_only_relevant_approved_memory(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "history.db"
            history = History(path)
            _, turn = history.start_turn(None, "Fictional source")
            history.finish_turn(turn, "completed", "OK", enqueue=True)
            job = history.claim_memory_job()
            history.finish_memory_job(turn, job[1], job[3],
                '[{"kind":"fact","text":"User prefers short answers"},'
                '{"kind":"fact","text":"User prefers hiking"},'
                '{"kind":"fact","text":"User owns a bicycle"}]')
            first, pending, unrelated = history.list_memories("pending")
            history.review_memory(first[0], approve=True)
            history.review_memory(unrelated[0], approve=True)
            with (
                patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
                patch("mellow.__main__.History", side_effect=lambda _: History(path)),
                patch("mellow.__main__.stream_reply", side_effect=lambda *a, **kw: iter_reply()) as send,
                patch("builtins.input", side_effect=[
                    "Do I prefer short answers?", f"/edit-memory {first[0]} I prefer Long Answers",
                    "/clear", "Do I prefer long answers?", f"/forget {first[0]}",
                    "/clear", "Do I prefer long answers?", "/quit",
                    "Do I own a bicycle?", "/quit",
                ]),
                redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(main([]), 0)
                self.assertEqual(main(["--private"]), 0)

            contexts = [call.kwargs["context"] for call in send.call_args_list]
            self.assertEqual([len(items) for items in contexts], [2, 2, 1, 1])
            self.assertIn("User prefers short answers", contexts[0][1]["content"])
            self.assertNotIn("hiking", contexts[0][1]["content"])
            self.assertNotIn("bicycle", contexts[0][1]["content"])
            self.assertIn("I prefer Long Answers", contexts[1][1]["content"])
            self.assertEqual(History(path).list_memories("approved"), [unrelated])
            self.assertEqual(History(path).list_memories("pending"), [pending])
            with closing(sqlite3.connect(path)) as db:
                self.assertIsNone(db.execute("SELECT result FROM memory_jobs WHERE turn_id = ?", (turn,)).fetchone()[0])
                self.assertEqual(db.execute("SELECT user_text FROM turns WHERE id = ?", (turn,)).fetchone()[0],
                                 "Fictional source")


def iter_reply():
    yield "OK"
