"""Run fictional extraction checks against a local LM Studio model."""

import argparse
import json
import re
from time import perf_counter

from mellow.providers.lmstudio import LocalModelError, MemorySuggestion, extract_memories

CASES = {
    "fact_and_episode": "I prefer short answers. Yesterday I finished a robotics workshop.",
    "greeting": "Hey, what's up?",
    "hypothetical": "If I ever move to Tokyo, I'll try ramen every week.",
}


def check(case: str, memories: list[MemorySuggestion]) -> bool:
    """Apply a narrow rubric for these fixtures, with output available for review."""
    if case not in CASES:
        raise ValueError(f"Unknown benchmark case: {case}")
    if case != "fact_and_episode":
        return not memories
    if len(memories) != 2:
        return False
    by_kind = {memory.kind: memory.text.casefold() for memory in memories}
    fact = by_kind.get("fact", "")
    episode = by_kind.get("episode", "")
    return bool(
        re.search(r"\b(short|concise|brief|succinct)\b", fact)
        and re.search(r"\b(answers?|responses?|replies)\b", fact)
        and re.search(r"\b(finished|completed)\b", episode)
        and all(word in episode for word in ("robotics", "workshop", "yesterday"))
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("model", help="Loaded LM Studio model ID")
    parser.add_argument("--port", type=int, default=1234, metavar="PORT")
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")

    passed = 0
    for case, prompt in CASES.items():
        started = perf_counter()
        try:
            memories = extract_memories(args.model, prompt, port=args.port)
        except LocalModelError as error:
            print(f"{case}: ERROR ({perf_counter() - started:.2f}s) — {error}")
            continue
        ok = check(case, memories)
        passed += ok
        print(f"{case}: {'PASS' if ok else 'REVIEW'} ({perf_counter() - started:.2f}s)")
        print(json.dumps([{"kind": m.kind, "text": m.text} for m in memories]))
    print(f"{passed}/{len(CASES)} cases matched the rubric.")
    return 0 if passed == len(CASES) else 1


if __name__ == "__main__":
    raise SystemExit(main())
