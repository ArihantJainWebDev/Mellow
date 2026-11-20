from pathlib import Path
import tempfile
from threading import Event
import unittest
from unittest.mock import patch

from mellow.memory_worker import run_worker
from mellow.providers.lmstudio import MemorySuggestion
from mellow.storage import History


class ContextualExtractionTests(unittest.TestCase):
    def test_questions_deduplication_and_explicit_replacement(self):
        with tempfile.TemporaryDirectory() as folder:
            history = History(Path(folder) / "history.db")
            _, foreign = history.start_turn(None, "Unrelated session")
            history.finish_turn(foreign, "completed", "OK")
            session, first = history.start_turn(None, "I own a red bicycle")
            history.finish_turn(first, "completed", "What kind of bicycle is it?", enqueue=True)
            _, second = history.start_turn(session, "cruiser")
            history.finish_turn(second, "completed", "Nice", enqueue=True)
            _, question = history.start_turn(session, "Do I prefer short answers?")
            history.finish_turn(question, "completed", "Unsure", enqueue=True)
            _, correction = history.start_turn(session, "Actually, my bicycle is blue")
            history.finish_turn(correction, "completed", "Got it", enqueue=True)
            stop = Event()

            def extract(model, message, *, context):
                if message == "I own a red bicycle":
                    self.assertEqual(context, [])
                    return [MemorySuggestion("fact", message)]
                if message == "cruiser":
                    self.assertEqual(context, [
                        ("I own a red bicycle", "What kind of bicycle is it?"),
                    ])
                    return [MemorySuggestion("fact", "User owns a red bicycle."),
                            MemorySuggestion("fact", "The user's bicycle is a cruiser.")]
                self.assertEqual(message, "Actually, my bicycle is blue")
                self.assertEqual(context[0], ("cruiser", "Nice"))
                stop.set()
                return [MemorySuggestion("fact", "The user's bicycle is blue.")]

            with patch("mellow.memory_worker.extract_memories", side_effect=extract) as model:
                run_worker(history, "test", stop)
            self.assertEqual(model.call_count, 3)
            self.assertEqual(history.memory_job_counts(), {"completed": 4})
            old, cruiser, new = history.list_memories("pending")
            self.assertEqual([old[3], cruiser[3], new[3]], [first, second, correction])
            self.assertTrue(history.review_memory(old[0], approve=True))
            self.assertFalse(history.replace_memory(new[0], cruiser[0]))
            self.assertEqual(history.list_memories("approved"), [old])
            self.assertTrue(history.replace_memory(new[0], old[0]))
            self.assertEqual(history.list_memories("approved"), [new])
            self.assertEqual(history.list_memories("pending"), [cruiser])
