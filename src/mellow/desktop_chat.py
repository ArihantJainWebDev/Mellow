"""UTF-8 line protocol for a single local desktop chat subprocess."""
import base64
from contextlib import redirect_stderr, redirect_stdout
import sys

from mellow.__main__ import main as chat


def emit(kind, value=""):
    encoded = base64.b64encode(value.encode("utf-8")).decode("ascii")
    sys.__stdout__.write(f"{kind} {encoded}\n")
    sys.__stdout__.flush()


class Output:
    def write(self, value):
        if value:
            emit("text", value)
        return len(value)

    def flush(self):
        pass


def read_message(prompt):
    emit("ready")
    line = sys.stdin.buffer.readline()
    if not line:
        raise EOFError
    return base64.b64decode(line.strip(), validate=True).decode("utf-8")


if __name__ == "__main__":
    output = Output()
    with redirect_stdout(output), redirect_stderr(output):
        code = chat(read_message=read_message, event=emit, local_fallback=True)
    emit("end", str(code))
    sys.exit(code)
