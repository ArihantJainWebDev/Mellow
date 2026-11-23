from contextlib import closing
from pathlib import Path
import sqlite3
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from mellow.activity import evaluate


class ActivityTests(unittest.TestCase):
    def setUp(self):
        directory = TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name) / "history.db"

    def read(self, *pairs, **opts):
        scenes = [dict(pair) if isinstance(pair, dict) else dict(app=pair[0], title=pair[1]) for pair in pairs]
        return evaluate(self.path, dict(scenes=scenes, **opts))

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

    def test_visual_task_sentence_is_the_grounded_read_answer(self):
        vision = "Debugging a visible missing-module error."
        result = self.read(dict(app="qoder", title="Qoder", vision=vision))
        self.assertEqual(result["answer"], vision)
        self.assertEqual(result["activity"], "")
        self.assertEqual(result["setup"], "")
        self.assertFalse(result["study"])
        self.assertEqual(set(result), {"answer", "activity", "setup", "study"})

    def test_visual_task_preserves_named_setup_and_study_derivation(self):
        self.read(("qoder", "Qoder"))
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute("INSERT INTO workspace_windows(profile,position,app,title) VALUES(?,?,?,?)",
                       ("fixture-work", 0, "code", "Welcome"))
        vision = "Discussing the visible fixture-work project."
        result = self.read(dict(app="qoder", title="Qoder", vision=vision))
        self.assertEqual(result["answer"], vision)
        self.assertEqual(result["activity"], "fixture-work")
        self.assertEqual(result["setup"], "fixture-work")
        self.assertFalse(result["study"])
        vision = "Reviewing a visible coursework problem."
        result = self.read(dict(app="opera", title="New tab", vision=vision))
        self.assertEqual(result["answer"], vision)
        self.assertEqual(result["activity"], "studying")
        self.assertTrue(result["study"])

    def test_task_specific_title_adds_context_to_shallow_vision(self):
        result = self.read(dict(app="editor", title="Linear algebra quiz", vision="A quiz question is visible."))
        self.assertEqual(result["activity"], "studying")
        self.assertIn("Linear algebra quiz", result["answer"])
        self.assertIn("A quiz question is visible.", result["answer"])

    def test_correction_precedes_but_does_not_erase_contradictory_vision(self):
        scene = dict(app="opera", title="Calculus homework")
        self.read(scene, action="correct", label="college work")
        scene["vision"] = "Comparing headphones in a visible shopping cart."
        result = self.read(scene)
        self.assertEqual(result["activity"], "college work")
        self.assertTrue(result["study"])
        self.assertIn("college work", result["answer"])
        self.assertIn("saved correction", result["answer"])
        self.assertIn(scene["vision"], result["answer"])
        why = self.read(scene, action="why")["answer"]
        self.assertTrue(why.startswith(result["answer"] + "\n"))
        self.assertIn("previously corrected", why)
        self.assertIn("what was visible on screen", why)

    def test_correction_without_vision_retains_title_evidence(self):
        scene = ("opera", "Lecture assignment")
        self.read(scene, action="correct", label="shopping")
        result = self.read(scene)
        self.assertEqual(result["activity"], "shopping")
        self.assertIn("Lecture assignment", result["answer"])
        self.assertIn("screen details", result["answer"])

    def test_qoder_handoff_requires_two_distinct_immediate_corroborations(self):
        prior = [("opera", "Mellow"), ("codex", "Mellow discussion")]
        destination = ("Qoder", "Qoder - New chat")
        result = self.read(*prior, destination)
        self.assertEqual(result["activity"], "mellow")
        self.assertIn("may be continuing", result["answer"])
        self.assertNotIn("Earlier:", result["answer"])
        why = self.read(*prior, destination, action="why")["answer"]
        self.assertIn("Earlier: opera · Mellow", why)
        self.assertIn("Earlier: codex · Mellow discussion", why)
        self.assertFalse(self.read(prior[0], destination)["activity"])
        self.assertFalse(self.read(prior[0], ("opera", "Mellow discussion"), destination)["activity"])
        self.assertFalse(self.read(*prior, ("opera", "New tab"), destination)["activity"])
        self.assertFalse(self.read(*prior, ("qoder", "Payroll"))["activity"])
        self.assertFalse(self.read(*prior, dict(app="qoder", title="Qoder", vision="Reading a visible recipe."))["activity"])

    def test_generic_qoder_title_does_not_invent_or_learn_a_task(self):
        result = self.read(("qoder", "Qoder - New chat"))
        self.assertEqual(result["activity"], "")
        self.assertIn("Qoder - New chat", result["answer"])
        self.assertIn("unsure", result["answer"])
        self.assertIn("without screen details", result["answer"])
        self.assertNotIn("mellow", result["answer"].casefold())
        correction = self.read(("qoder", "Qoder - New chat"), action="correct", label="mellow")
        self.assertIn("distinctive window title", correction["answer"])
        self.assertFalse(self.read(("qoder", "Qoder - New chat"))["activity"])

    def test_ambiguous_title_gives_useful_title_only_answer(self):
        title = "shopping cart and lecture"
        result = self.read(("opera", title))
        self.assertEqual(result["activity"], "")
        self.assertIn(title, result["answer"])
        self.assertIn("opera", result["answer"])
        self.assertIn("unsure", result["answer"])
        self.assertIn("screen details", result["answer"])
        self.assertNotIn("Foreground:", result["answer"])

    def test_ambiguous_corrections_do_not_override_visible_sentence(self):
        self.read(("opera", "algebra equations"), action="correct", label="college work")
        self.read(("opera", "algebra geometry"), action="correct", label="research")
        vision = "Reviewing visible algebra equations."
        result = self.read(dict(app="opera", title="algebra equations geometry", vision=vision))
        self.assertEqual(result["activity"], "")
        self.assertEqual(result["answer"], vision)

    def test_unavailable_vision_uses_app_and_title_without_guessing(self):
        scenes = [dict(app="qoder", title="Budget draft"),
                  dict(app="qoder", title="Budget draft", vision=""),
                  dict(app="qoder", title="Budget draft", vision=None),
                  dict(app="qoder", title="Budget draft", vision="  ")]
        for scene in scenes:
            with self.subTest(scene=scene):
                result = self.read(scene)
                self.assertEqual(result["activity"], "")
                self.assertIn("Budget draft", result["answer"])
                self.assertIn("qoder", result["answer"])
                self.assertIn("without screen details", result["answer"])
                why = self.read(scene, action="why")["answer"]
                self.assertIn("window title and app", why)
                self.assertNotIn("local vision observation", why)

    def test_missing_title_and_vision_yield_an_app_only_answer(self):
        result = self.read(("qoder", ""))
        self.assertEqual(result["activity"], "")
        self.assertIn("qoder window is active", result["answer"])
        self.assertNotIn("“”", result["answer"])
        self.assertIn("without screen details", result["answer"])
        self.assertIn("limited to the foreground app", self.read(("qoder", ""), action="why")["answer"])

    def test_why_adds_current_capture_provenance_and_only_relevant_prior(self):
        vision = "Debugging a visible import error in Mellow."
        scene = dict(app="qoder", title="Qoder", vision=vision, observed_at="2026-11-14T10:20:30Z")
        prior = [("opera", "Mellow discussion"), ("code", "Unrelated payroll")]
        result = self.read(*prior, scene)
        self.assertEqual(result["answer"], vision)
        why = self.read(*prior, scene, action="why")["answer"]
        self.assertTrue(why.startswith(result["answer"] + "\n"))
        self.assertIn("Foreground: qoder · Qoder", why)
        self.assertIn("what was visible on screen", why)
        self.assertIn("Current capture: " + scene["observed_at"], why)
        self.assertIn("Earlier: opera · Mellow discussion", why)
        self.assertNotIn("Unrelated payroll", why)
        self.assertNotIn(scene["observed_at"], result["answer"])

    def test_why_does_not_invent_a_capture_time(self):
        for observed_at in (None, "", 123):
            with self.subTest(observed_at=observed_at):
                why = self.read(dict(app="qoder", title="Qoder", observed_at=observed_at), action="why")["answer"]
                self.assertNotIn("Current capture:", why)

    @patch("mellow.providers.lmstudio._request_content")
    def test_reads_neither_call_a_model_nor_persist_vision_or_inference(self, model):
        self.read(("opera", "Calculus homework"), action="correct", label="college work")
        with closing(sqlite3.connect(self.path)) as db:
            before = list(db.iterdump())
        scenes = [dict(app="opera", title="Calculus homework", vision="Comparing a visible shopping cart."),
                  dict(app="qoder", title="Qoder", vision="Debugging a visible Mellow error.")]
        for scene in scenes:
            for action in ("read", "why"):
                self.read(scene, action=action)
        with closing(sqlite3.connect(self.path)) as db:
            self.assertEqual(list(db.iterdump()), before)
        model.assert_not_called()
