"""Exercise the Windows media bridge only against a uniquely named silent test player."""
import base64
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import uuid
import wave


def main():
    root = Path(__file__).resolve().parents[1]
    cmd = ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"]
    script = root / "src/mellow/desktop/media.ps1"
    def media(action, source="", expected=""):
        fields = [base64.b64encode(x.encode()).decode() for x in (source,expected)]
        probe = subprocess.run([*cmd, str(script), "-Action", action], input="\n".join(fields)+"\n",
                               capture_output=True, text=True, check=True, timeout=16)
        return json.loads(base64.b64decode(probe.stdout.strip()))
    existing = media("current")
    if existing.get("available"):
        print("Skipped the playback fixture because another media session is active.")
        return 2
    title = "Mellow fixture " + uuid.uuid4().hex
    with tempfile.TemporaryDirectory() as tmp:
        track = Path(tmp) / "fixture.wav"
        with wave.open(str(track), "wb") as stream:
            stream.setparams((1,2,22050,0,"NONE","not compressed"))
            stream.writeframes(bytes(22050*2*4))
        stop_file = Path(tmp) / "stop.signal"
        player = subprocess.Popen([*cmd, str(root/"tools/fixture_player.ps1"), "-Wave", str(track), "-Track", title, "-StopFile", str(stop_file)],
                                  stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,
                                  text=True,creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            result = {}
            for _ in range(7):
                if player.poll() is not None:
                    raise RuntimeError("Silent Windows fixture did not start: "+player.stderr.read()[:500])
                result = media("current")
                if result.get("title")==title and result.get("playing"):
                    break
                time.sleep(.3)
            else:
                print("Windows did not publish the synthetic media session.")
                return 2
            source = result["source"]
            if not media("pause",source,title).get("changed"):
                raise RuntimeError("Fixture declined pause")
            for _ in range(5):
                check = media("inspect",source,title)
                if check.get("available") and not check.get("playing"):
                    break
                time.sleep(.3)
            else:
                raise RuntimeError("Fixture still playing after pause")
            if not media("play",source,title).get("changed"):
                raise RuntimeError("Fixture declined play")
            for _ in range(5):
                if media("inspect",source,title).get("playing"):
                    break
                time.sleep(.3)
            else:
                raise RuntimeError("Fixture did not resume")
            print("Windows reported the synthetic track and confirmed pause and resume.")
        finally:
            stop_file.touch()
            try:
                player.communicate("\n", timeout=3)
            except subprocess.TimeoutExpired:
                player.terminate()
                player.communicate()
    return 0


if __name__ == "__main__":
    sys.exit(main())
