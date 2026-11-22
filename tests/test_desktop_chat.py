import base64
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from mellow.storage import History


class DesktopChatTests(unittest.TestCase):
    def test_desktop_uses_active_cli_session_and_local_commands(self):
        with tempfile.TemporaryDirectory() as tmp:
            history = History(Path(tmp) / "data/history.db")
            session = history.remember(None, "I study pottery")
            commands = ["/memories", "Remember that my project is Clover", "/quit"]
            wire = b"".join(base64.b64encode(x.encode()) + b"\n" for x in commands)
            env = dict(os.environ, GROQ_API_KEY="fake", GROQ_MODEL="demo",
                       MELLOW_MEMORY_MODEL="", MELLOW_EMBEDDING_MODEL="")
            result = subprocess.run([sys.executable, "-m", "mellow.desktop_chat"], input=wire,
                                    cwd=tmp, env=env, capture_output=True, timeout=15, check=True)
            events = []
            for line in result.stdout.splitlines():
                name, encoded = line.split(b" ", 1)
                events.append((name, base64.b64decode(encoded).decode()))
            self.assertEqual(events[-1], (b"end", "0"))
            self.assertIn("pottery", "".join(v for _, v in events))
            self.assertEqual(history.active_session(), session)
            self.assertEqual(history.list_memories("approved")[-1][2], "my project is Clover")
