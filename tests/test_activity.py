import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from mellow.activity import evaluate


class ActivityTests(unittest.TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name) / "history.db"

    def read(self, *pairs, **opts):
        return evaluate(self.path, dict(scenes=[dict(app=a, title=t) for a, t in pairs], **opts))

    def test_handoff_needs_corroboration_and_generic_destination(self):
        prior = [("opera", "Mellow"), ("codex", "Mellow discussion")]
        self.assertEqual(self.read(*prior, ("code", "Welcome"))["activity"], "mellow")
        self.assertFalse(self.read(*prior, ("code", "Payroll"))["activity"])
        self.assertFalse(self.read(prior[0], ("code", "Welcome"))["activity"])

    def test_correction_persists_is_scoped_and_can_be_forgotten(self):
        scene = ("opera", "Calculus homework")
        self.read(scene, action="correct", label="college work")
        self.assertTrue(self.read(scene)["study"])
        self.assertFalse(self.read(("opera", "New tab"))["activity"])
        self.assertFalse(self.read(("code", scene[1]))["activity"])
        self.read(scene, action="forget", label="college work")
        self.assertFalse(self.read(scene)["activity"])

    def test_ordinary_and_ambiguous_activities(self):
        self.assertEqual(self.read(("opera", "Shopping cart"))["activity"], "shopping")
        self.assertFalse(self.read(("opera", "shopping cart and lecture"))["activity"])
