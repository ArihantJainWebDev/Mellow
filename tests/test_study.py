from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import tempfile
import unittest

from mellow.study import handle, music_link
from mellow.storage import History

FIRST = "https://music.youtube.com/watch?v=abcdefghijk"
SECOND = "https://music.youtube.com/playlist?list=PL12345678"


class StudyTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.path = Path(folder.name) / "data" / "history.db"
        self.now = 10000

    def ask(self, action, **fields):
        self.now += 1
        return handle(self.path, action, fields, self.now)

    def test_canonical_share_links_and_preference_rotation(self):
        self.assertEqual(music_link("https://youtu.be/abcdefghijk?si=ignored"), FIRST)
        for url in ("https://music.youtube.com.evil.test/watch?v=abcdefghijk",
                    "file:///sample.mp3", "https://user@music.youtube.com/watch?v=abcdefghijk",
                    "https://music.youtube.com/watch?v=x;other"):
            with self.assertRaises(ValueError):
                music_link(url)
        self.ask("add", label="Piano", url=FIRST)
        self.ask("add", label="Rain", url=SECOND)
        self.assertEqual(self.ask("start")["launch"], FIRST)
        self.assertFalse(self.ask("start")["launch"])
        self.assertEqual(self.ask("next")["launch"], SECOND)
        self.ask("like")
        self.ask("like")
        self.ask("end")
        self.assertEqual(self.ask("start")["launch"], SECOND)
        self.ask("remove", label="Piano")
        self.assertFalse(self.ask("next")["launch"])
        self.ask("add", label="Piano", url=FIRST)
        self.assertEqual(self.ask("next")["launch"], FIRST)
        self.assertEqual(self.ask("open")["launch"], FIRST)

    def test_concurrent_starts_only_one_launch_and_cue_change(self):
        self.ask("add", label="Piano", url=FIRST)
        self.ask("cue", app="opera", cue="Physics")
        with ThreadPoolExecutor(max_workers=4) as pool:
            results = list(pool.map(lambda _: handle(self.path, "auto-start",
                                {"app": "opera", "cue": "Physics"}, 10500), range(4)))
        self.assertEqual(sum(bool(r["launch"]) for r in results), 1)
        self.assertEqual(len({r["session"] for r in results}), 1)
        self.ask("end")
        self.ask("uncue")
        with self.assertRaises(ValueError):
            self.ask("auto-start", app="opera", cue="Physics")

    def test_media_attribution_and_local_moments(self):
        self.ask("bind", source="fixture-player")
        session = self.ask("start")["session"]
        track = dict(source="fixture-player", session=session, title="Moon", artist="Weeknight")
        self.ask("track", **track)
        self.ask("track", **track)
        self.ask("track", **dict(track, source="other-player"))
        self.assertEqual([t["title"] for t in self.ask("history")["tracks"]], ["Moon"])
        moment = dict(name="RAG tutorial", app="opera", title="Retrieval basics", description="Vector search",
                      session=session, source="fixture-player", title_of_track="Moon", artist="Weeknight")
        self.ask("moment-save", **moment)
        found = self.ask("moment-search", query="moon tutorial")["moments"]
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0]["artist"], "Weeknight")
        self.ask("unbind")
        self.ask("moment-save", **dict(moment, name="After unlinking"))
        self.assertFalse(self.ask("moment-search", query="unlinking")["moments"][0]["track"])
        self.ask("moment-delete", id=found[0]["id"])
        self.assertFalse(self.ask("moment-search", query="moon")["moments"])
        self.ask("end")
        history = History(self.path)
        export = self.path.parent / "sample.json"
        history.export(export)
        self.assertEqual(json.loads(export.read_text())["study_tracks"][0]["title"], "Moon")
        history.reset()
        empty = self.ask("status")
        self.assertFalse(any(empty[k] for k in ("active", "mixes", "moments", "tracks", "source", "cue")))
