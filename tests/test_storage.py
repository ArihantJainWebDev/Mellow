"""Exercise the CLI and reopen its SQLite history after shutdown."""

import io
from pathlib import Path
import sqlite3
import tempfile
import unittest
from contextlib import closing, redirect_stderr, redirect_stdout
from unittest.mock import patch

from mellow.__main__ import main
from mellow.config import Settings
from mellow.providers.groq import ProviderError
from mellow.storage import History


class HistoryTests(unittest.TestCase):
    def test_cli_records_outcomes_and_keeps_sessions_separate(self):
        def stream(settings, message, **options):
            yield "visible text"
            if message == "fail":
                raise ProviderError("Offline")
            if message == "cancel":
                raise KeyboardInterrupt

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "data" / "history.db"
            history = History(path)
            with (
                patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
                patch("mellow.__main__.History", return_value=history),
                patch("mellow.__main__.stream_reply", side_effect=stream),
                patch("builtins.input", side_effect=["hello", "fail", "cancel", "/clear", "new", "/quit", "restart", "/quit"]),
                redirect_stdout(io.StringIO()),
                redirect_stderr(io.StringIO()),
            ):
                self.assertEqual(main([]), 0)
                self.assertEqual(main([]), 0)
            reopened = History(path)
            session_id, _ = reopened.start_turn(None, "abrupt exit")
            with closing(sqlite3.connect(path)) as db:
                rows = db.execute(
                    "SELECT session_id, user_text, reply_text, status FROM turns ORDER BY id"
                ).fetchall()
            self.assertEqual(rows, [
                (1, "hello", "visible text", "completed"),
                (1, "fail", "visible text", "failed"),
                (1, "cancel", "visible text", "interrupted"),
                (2, "new", "visible text", "completed"),
                (3, "restart", "visible text", "completed"),
                (session_id, "abrupt exit", "", "pending"),
            ])

    def test_resume_reopens_existing_session_and_sends_only_completed_context(self):
        requests = []

        def stream(settings, message, **options):
            requests.append(options["context"])
            yield "new reply"

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "history.db"
            stored = History(path)
            session_id = None
            for index in range(5):
                session_id, turn_id = stored.start_turn(session_id, f"question {index}")
                stored.finish_turn(turn_id, "completed", f"reply {index}")
            _, turn_id = stored.start_turn(session_id, "failed question")
            stored.finish_turn(turn_id, "failed", "partial")
            stored.start_turn(session_id, "pending question")
            stdout = io.StringIO()
            with (
                patch("mellow.__main__.History", return_value=History(path)),
                patch("mellow.__main__.load_settings", return_value=Settings("fake", "test")),
                patch("mellow.__main__.stream_reply", side_effect=stream),
                patch("builtins.input", side_effect=[
                    "/sessions", f"/resume {session_id}", "/resume 999999999999999999999",
                    "/resume 999", "follow-up", "/quit",
                ]),
                redirect_stdout(stdout),
                redirect_stderr(io.StringIO()),
            ):
                self.assertEqual(main([]), 0)
            self.assertIn(f"{session_id} |", stdout.getvalue())
            self.assertIn(f"Resumed session {session_id}", stdout.getvalue())
            self.assertEqual(len(requests), 1)
            self.assertEqual([item["content"] for item in requests[0][1:]], [
                text for index in range(1, 5)
                for text in (f"question {index}", f"reply {index}")
            ])
            self.assertEqual(History(path).list_sessions()[0][2], 8)
            self.assertEqual(len(History(path).list_sessions()), 1)
