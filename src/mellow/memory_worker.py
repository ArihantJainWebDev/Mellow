"""Process local extraction jobs serially outside the chat loop."""

from dataclasses import asdict
import json
import re
import sqlite3
from threading import Event, Thread

from mellow.providers.lmstudio import LocalModelError, extract_memories
from mellow.storage import History


def run_worker(history: History, model: str, stop: Event) -> None:
    while not stop.is_set():
        try:
            job = history.claim_memory_job()
            if job is None:
                stop.wait(1)
                continue
            turn_id, lease, message, attempts = job
            error = None
            try:
                if re.fullmatch(
                    r"(?:do|does|did|am|is|are|was|were|can|could|should|would|will|"
                    r"has|have|what|which|when|where|who|why|how)\b[^.!?]*\?",
                    message.strip(), flags=re.IGNORECASE,
                ):
                    suggestions = []
                else:
                    context = history.extraction_context(turn_id)
                    suggestions = extract_memories(model, message, context=context)
                result = json.dumps([asdict(candidate) for candidate in suggestions])
            except LocalModelError as failure:
                result = None
                error = str(failure)
            history.finish_memory_job(turn_id, lease, attempts, result, error=error)
        except sqlite3.Error:
            # A claimed job can be recovered after its lease expires.
            stop.wait(1)


def start_worker(history: History, model: str) -> tuple[Event, Thread]:
    stop = Event()
    thread = Thread(target=run_worker, args=(history, model, stop), daemon=True)
    thread.start()
    return stop, thread
