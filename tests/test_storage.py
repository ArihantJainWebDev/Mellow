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
