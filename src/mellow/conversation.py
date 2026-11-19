"""Recent completed exchanges for this process only."""

from collections import deque

PERSONALITY = (
    "You are Mellow, a warm, curious AI companion. Be honest about what you "
    "know, keep replies concise by default, and ask when context is unclear."
)
RECENT_EXCHANGES = 4


class Conversation:
    def __init__(self) -> None:
        self._recent: deque[tuple[str, str]] = deque(maxlen=RECENT_EXCHANGES)

    def record(self, message: str, answer: str) -> None:
        self._recent.append((message, answer))

    def context(self) -> tuple[dict[str, str], ...]:
        messages = [{"role": "system", "content": PERSONALITY}]
        for message, answer in self._recent:
            messages.append({"role": "user", "content": message})
            messages.append({"role": "assistant", "content": answer})
        return tuple(messages)
