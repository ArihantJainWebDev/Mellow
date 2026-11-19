import io
from contextlib import redirect_stdout
from pathlib import Path
import tempfile
import sqlite3
from contextlib import closing
from threading import Event
import unittest
from unittest.mock import patch

from mellow.__main__ import main
from mellow.config import Settings
from mellow.providers.lmstudio import LocalModelError, MemorySuggestion
from mellow.storage import History
from mellow.memory_worker import run_worker


class MemoryWorkerTests(unittest.TestCase):
    def test_reopens_retries_and_discards_stale_results(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "history.db"
            history = History(path)
            _, turn = history.start_turn(None, "A preference")
            self.assertTrue(history.finish_turn(turn, "completed", "OK", enqueue=True))
            reopened = History(path)
            with patch("mellow.storage.time.time", return_value=1000):
                first = reopened.claim_memory_job()
                self.assertIsNone(history.claim_memory_job())
            with patch("mellow.storage.time.time", return_value=1201):
                second = history.claim_memory_job()
                self.assertEqual(second[0], first[0])
                reopened.finish_memory_job(first[0], first[1], first[3], "[]")
                self.assertEqual(history.memory_job_counts(), {"running": 1})
                history.finish_memory_job(second[0], second[1], second[3], None)
                self.assertIsNone(history.claim_memory_job())
            with patch("mellow.storage.time.time", return_value=1212):
                third = reopened.claim_memory_job()
                reopened.finish_memory_job(third[0], third[1], third[3], None)
                self.assertEqual(history.memory_job_counts(), {"failed": 1})
                self.assertIsNone(history.claim_memory_job())

    def test_chat_continues_while_extractor_is_waiting(self):
        started = Event()
        release = Event()
        second_reply = Event()

        def extract(model, message):
            if message == "first":
                started.set()
                if not release.wait(5):
                    raise AssertionError("Chat did not reach the second reply")
            return [MemorySuggestion("fact", "Sample candidate")]

        def stream(settings, message, **options):
            if message == "second":
                second_reply.set()
            yield "reply"

        def user_input(prompt):
            match next(messages):
                case "first":
                    return "first"
                case "second":
                    self.assertTrue(started.wait(5))
                    return "second"
                case _:
                    self.assertTrue(second_reply.is_set())
                    release.set()
                    return "/quit"

        messages = iter(["first", "second", "/quit"])
        with tempfile.TemporaryDirectory() as directory:
            history = History(Path(directory) / "history.db")
            with (
                patch("mellow.__main__.History", return_value=history),
                patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
                patch("mellow.__main__.stream_reply", side_effect=stream),
                patch("mellow.memory_worker.extract_memories", side_effect=extract),
                patch("builtins.input", side_effect=user_input),
                redirect_stdout(io.StringIO()),
            ):
                try:
                    self.assertEqual(main(["--memory-model", "fake-local"]), 0)
                finally:
                    release.set()
            self.assertGreaterEqual(history.memory_job_counts().get("completed", 0), 1)
            self.assertEqual(history.list_sessions()[0][2], 2)

    def test_migrates_existing_history_and_requeues_offline_job(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "history.db"
            original = History(path)
            session_id, turn = original.start_turn(None, "stored message")
            original.finish_turn(turn, "completed", "stored reply")
            with closing(sqlite3.connect(path)) as db, db:
                db.execute("DROP TABLE memory_candidates")
                db.execute("DROP TABLE memory_jobs")
                db.execute("PRAGMA user_version = 1")
            migrated = History(path)
            self.assertEqual(migrated.recent_completed(session_id, 4), [
                ("stored message", "stored reply"),
            ])
            self.assertEqual(migrated.memory_job_counts(), {})
            _, turn = migrated.start_turn(session_id, "new message")
            migrated.finish_turn(turn, "completed", "new reply", enqueue=True)
            stop = Event()

            def offline(*args):
                stop.set()
                raise LocalModelError("Local server is offline")

            with patch("mellow.memory_worker.extract_memories", side_effect=offline):
                run_worker(migrated, "fake-local", stop)
            self.assertEqual(History(path).memory_job_counts(), {"queued": 1})
