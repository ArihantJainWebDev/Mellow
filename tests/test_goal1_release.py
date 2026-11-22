import json
import tempfile
import unittest
from pathlib import Path

from mellow.memory_policy import statement
from mellow.storage import History


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.folder = Path(folder.name)
        self.history = History(self.folder / "chat.db")

    def turn(self, message, session=None):
        session, turn = self.history.start_turn(session, message)
        self.history.finish_turn(turn, "completed", "OK", enqueue=True, automatic=True)
        return session

    def test_natural_update_and_sensitive_boundary(self):
        for message in ["Do I own a bike?", "If I owned a bike", "My bank PIN is 0000",
                        "I prefer tea and coffee", "I like my medication"]:
            self.assertIsNone(statement(message))
        session = self.turn("I own a red bicycle")
        self.turn("I sold it", session)
        memories = self.history.list_memories("approved")
        self.assertEqual([row[2] for row in memories], ["I sold a red bicycle"])
        self.assertIn("I own a red bicycle", self.history.recent_completed(session, 2)[0])

    def test_forget_while_worker_holds_lease(self):
        session = self.turn("My old bicycle had an unusual part")
        job = self.history.claim_memory_job()
        self.turn("I like tea")
        self.assertEqual(self.history.forget_topic("the bike"), 1)
        self.history.finish_memory_job(job[0], job[1], job[3],
            '[{"kind":"fact","text":"owns an unusual bicycle"}]')
        self.assertFalse(self.history.list_memories("pending"))
        self.assertFalse(self.history.recent_completed(session, 4))
        self.assertEqual(self.history.list_memories("approved")[0][2], "I like tea")

    def test_export_delete_and_reset(self):
        session = self.turn("I prefer short answers")
        second = self.history.remember(None, "I study pottery")
        export = self.folder / "export.json"
        self.history.export(export)
        self.assertEqual(len(json.loads(export.read_text())["turns"]), 2)
        with self.assertRaises(FileExistsError):
            self.history.export(export)
        self.assertTrue(self.history.delete_session(second))
        self.assertFalse(self.history.active_session())
        self.assertEqual(self.history.list_sessions()[0][0], session)
        self.history.reset()
        self.assertFalse(self.history.list_sessions())
        self.assertFalse(self.history.list_memories("approved"))
        self.assertFalse(self.history.memory_job_counts())
