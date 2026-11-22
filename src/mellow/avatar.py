"""Launch the Windows desktop ghost. Close the terminal or press Ctrl+C to exit."""
from pathlib import Path
import ctypes
import os
import uuid
import subprocess
import sys


def main() -> int:
    if sys.platform != "win32":
        print("The desktop ghost currently requires Windows.", file=sys.stderr)
        return 1
    arguments = sys.argv[1:]
    if arguments[:1] == ['--home'] and len(arguments) >= 2:
        os.chdir(arguments[1])
        arguments = arguments[2:]
    arguments = [{'--hat-trick': '-HatTrick', '--classic': '-Classic'}.get(arg, arg) for arg in arguments]
    script = Path(__file__).with_name("desktop") / "avatar.ps1"
    event_name = "Local\\MellowClose-" + uuid.uuid4().hex
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateEventW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_wchar_p]
    kernel.CreateEventW.restype = ctypes.c_void_p
    kernel.SetEvent.argtypes = kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = kernel.CreateEventW(None, True, False, event_name)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    child = subprocess.Popen(["powershell.exe", "-NoProfile", "-STA", "-ExecutionPolicy",
                              "Bypass", "-File", str(script), *arguments],
                             env={**os.environ, 'MELLOW_CLOSE_EVENT': event_name, 'MELLOW_PYTHON': sys.executable.replace('pythonw.exe', 'python.exe')},
                             creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        return child.wait()
    except KeyboardInterrupt:
        kernel.SetEvent(handle)
        child.wait(timeout=12)
        return 0
    finally:
        kernel.CloseHandle(handle)


if __name__ == "__main__":
    raise SystemExit(main())

