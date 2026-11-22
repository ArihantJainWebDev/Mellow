"""Launch the Windows desktop ghost. Close the terminal or press Ctrl+C to exit."""
from pathlib import Path
import subprocess
import sys


def main() -> int:
    if sys.platform != "win32":
        print("The desktop ghost currently requires Windows.", file=sys.stderr)
        return 1
    script = Path(__file__).with_name("desktop") / "avatar.ps1"
    child = subprocess.Popen(["powershell.exe", "-NoProfile", "-STA", "-ExecutionPolicy",
                              "Bypass", "-File", str(script), *sys.argv[1:]])
    try:
        return child.wait()
    except KeyboardInterrupt:
        child.terminate()
        child.wait()
        return 0


if __name__ == "__main__":
    raise SystemExit(main())

