"""Offline end-to-end study demo with disposable data and an offscreen Windows UIA fixture."""
import argparse
import ctypes
import os
from datetime import date, timedelta
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from queue import Queue, Empty
from threading import Thread

from mellow.storage import History
from mellow.study import handle
from mellow.tasks import task_command


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", type=Path, help="Write the production dashboard to a PNG.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    if sys.platform != "win32":
        parser.error("This check exercises the Windows desktop.")
    cmd = ["powershell.exe", "-NoProfile", "-NonInteractive", "-STA", "-ExecutionPolicy", "Bypass", "-File"]
    with tempfile.TemporaryDirectory() as directory:
        db_path = Path(directory) / "data/history.db"
        history = History(db_path)
        due = (date.today() + timedelta(days=2)).isoformat()
        task_command("Add assignment Problem set 4 due " + due, history)
        now = int(time.time())
        def act(action, offset, **values):
            return handle(db_path, action, values, now + offset)
        act("add", -3600, label="Evening piano", url="https://music.youtube.com/watch?v=abcdefghijk")
        act("cue", -3500, app="opera", cue="Linear algebra")
        act("bind", -3400, source="fixture-player")
        session = act("start", -1526, topic="Linear algebra")["session"]
        act("track", -1300, session=session, source="fixture-player", title="Evening train", artist="Glass Arcade")
        act("moment-save", -1100, name="Eigenvectors", app="opera", title="Linear algebra · lecture notes",
            title_of_track="Evening train", artist="Glass Arcade", source="fixture-player", session=session)
        probe = subprocess.Popen([*cmd, str(root / "tools" / "fixture_toolbar.ps1")],
                                 cwd=directory, stdout=subprocess.PIPE, text=True, creationflags=subprocess.CREATE_NO_WINDOW)
        handle_value = 0
        try:
            lines = Queue()
            Thread(target=lambda: lines.put(probe.stdout.readline()), daemon=True).start()
            try:
                handle_value = int(lines.get(timeout=12))
            except (Empty, ValueError) as exc:
                raise RuntimeError("The test toolbar did not open") from exc
            verify = [*cmd, str(root/"src/mellow/desktop/avatar.ps1"), "-VerifyStudy"]
            if args.preview:
                verify += ["-Preview", str(args.preview.resolve())]
            subprocess.run(verify, cwd=directory, timeout=40, check=True,
                           env=dict(os.environ, MELLOW_PYTHON=sys.executable, MELLOW_FIXTURE_HWND=str(handle_value),
                                    PYTHONDONTWRITEBYTECODE="1"))
        finally:
            if handle_value and probe.poll() is None:
                ctypes.windll.user32.PostMessageW(ctypes.c_void_p(handle_value), 0x10, 0, 0)
            try:
                probe.wait(timeout=3)
            except subprocess.TimeoutExpired:
                probe.terminate()
                probe.wait()


if __name__ == "__main__":
    main()
