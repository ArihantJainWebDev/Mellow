"""Fictional live acceptance. Run with python tools/demo_goal1.py from an installed checkout."""
from pathlib import Path
import io
import os
import tempfile
from contextlib import redirect_stdout
from time import perf_counter
from unittest.mock import patch

from mellow.__main__ import main
from mellow.providers import groq


def demo():
    env = Path(".env").resolve()
    original = groq._events
    timings = []
    usage = []
    first_tokens = []
    import json

    request_start = [0.0]

    def measured(response):
        first = None
        for event in original(response):
            if event != "[DONE]":
                packet = json.loads(event)
                counts = (packet.get("x_groq") or {}).get("usage") or packet.get("usage")
                if counts:
                    usage.append((counts["prompt_tokens"], counts["completion_tokens"]))
                for choice in packet.get("choices", []):
                    if first is None and (choice.get("delta") or {}).get("content"):
                        first = perf_counter()
                        first_tokens.append(round(first - request_start[0], 3))
            yield event

    def chat(lines, extra=()):
        output = io.StringIO()
        t = perf_counter()
        request_start[0] = t
        with patch("builtins.input", side_effect=[*lines, "/quit"]), redirect_stdout(output):
            status = main(["--env-file", str(env), *extra])
        timings.append(round(perf_counter() - t, 2))
        result = output.getvalue()
        print(result.encode("ascii", "backslashreplace").decode("ascii"))
        assert status == 0 and "[Reply failed" not in result
        return result

    cwd = Path.cwd()
    with tempfile.TemporaryDirectory() as tmp, patch.object(groq, "_events", measured):
        os.chdir(tmp)
        try:
            chat(["I own a red bicycle"])
            recalled = chat(["/new", "What color is my bicycle? Answer in one word."])
            assert "red" in recalled.partition("Mellow: ")[2].lower()
            chat(["I sold my red bicycle"])
            sold = chat(["/new", "Do I still own my bicycle? Answer yes or no."])
            assert "no" in sold.partition("Mellow: ")[2].lower()
            chat(["Forget the bike"])
            from mellow.storage import History
            h = History(Path("data/history.db"))
            assert not h.list_memories("approved")
            for sid, *_ in h.list_sessions():
                assert not h.recent_completed(sid, 4) and not h.summary_exchanges(sid, 4)
            chat(["Reply with OK"], ["--private"])
        finally:
            os.chdir(cwd)
    print("Session wall times (seconds):", timings)
    print("First visible text from launch (seconds):", first_tokens)
    print("Provider token usage (input, output):", usage)


if __name__ == "__main__":
    demo()
