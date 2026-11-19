import unittest

from mellow.memory_benchmark import check
from mellow.providers.lmstudio import MemorySuggestion


class MemoryBenchmarkTests(unittest.TestCase):
    def test_preference_and_episode_require_timing(self):
        fact = MemorySuggestion("fact", "Prefers concise replies.")
        timed = MemorySuggestion("episode", "Completed a robotics workshop yesterday.")
        untimed = MemorySuggestion("episode", "Completed a robotics workshop.")
        self.assertTrue(check("fact_and_episode", [timed, fact]))
        self.assertFalse(check("fact_and_episode", [fact, untimed]))
        self.assertFalse(check("fact_and_episode", [fact, timed, fact]))

    def test_greetings_and_hypotheticals_do_not_become_memories(self):
        for case in ("greeting", "hypothetical"):
            with self.subTest(case=case):
                self.assertTrue(check(case, []))
                self.assertFalse(check(case, [MemorySuggestion("fact", "Lives in Tokyo.")]))
