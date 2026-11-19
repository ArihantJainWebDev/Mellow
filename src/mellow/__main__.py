"""CLI entry point: check configuration or chat with Groq."""

import argparse
from contextlib import closing
from pathlib import Path
import sqlite3
import sys

from mellow.conversation import RECENT_EXCHANGES, Conversation
from mellow.config import ConfigurationError, Settings, load_settings
from mellow.providers.groq import ProviderError, stream_reply
from mellow.storage import History
from mellow.memory_worker import start_worker


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Mellow: chat with Groq.")
    parser.add_argument(
        "--env-file", type=Path, default=Path(".env"),
        help="Configuration file (default: .env in the current working directory).",
    )
    parser.add_argument("--check-config", action="store_true", help="Check settings without contacting Groq.")
    parser.add_argument("--once", action="store_true", help="Send one message and exit.")
    parser.add_argument("--private", action="store_true", help="Keep this chat off disk.")
    parser.add_argument("--memory-model", metavar="MODEL_ID",
                        help="Enable background extraction using this local LM Studio model.")
    arguments = parser.parse_args(argv)
    if arguments.memory_model and (arguments.private or arguments.once or arguments.check_config):
        parser.error("--memory-model requires a saved interactive session")
    try:
        settings = load_settings(arguments.env_file)
    except ConfigurationError as error:
        print(f"Configuration error: {error}", file=sys.stderr)
        return 2

    if arguments.check_config:
        print("Required configuration is present. No API request was made.")
        return 0

    if arguments.once:
        return _one_message(settings)

    try:
        history = None if arguments.private else History(Path("data/history.db"))
        if history is None or not arguments.memory_model:
            return _chat(settings, history)
        stop, worker = start_worker(history, arguments.memory_model)
        try:
            return _chat(settings, history, extract_memory=True)
        finally:
            stop.set()
            worker.join(timeout=1)
    except (OSError, sqlite3.Error):
        print("Could not save local history. Check access to data/history.db or restart with --private.",
              file=sys.stderr)
        return 1


def _chat(settings: Settings, history: History | None, *, extract_memory: bool = False) -> int:
    conversation = Conversation()
    session_id = None
    print("Messages and recent exchanges are sent to Groq.")
    if history is None:
        print("This session is kept off disk.")
    else:
        print("Chat is saved locally to data/history.db. Use --private to keep a session off disk.")
    print("Type /help for commands or /quit to leave.")
    while True:
        try:
            message = input("You: ").strip()
        except EOFError:
            print()
            return 0
        except KeyboardInterrupt:
            print()
            return 130
        if not message:
            continue
        if message.startswith("/"):
            fields = message.casefold().split(maxsplit=1)
            command = fields[0] if len(fields) == 1 or fields[0] == "/resume" else message.casefold()
            if command == "/quit":
                return 0
            if command == "/help":
                print("/help  Show commands\n/clear Clear recent conversation\n/quit  Exit Mellow")
                if history is not None:
                    print("/sessions  List saved sessions\n/resume ID  Resume a saved session")
                    print("/jobs  Show local memory job counts")
            elif command == "/jobs":
                if history is None:
                    print("Memory jobs are unavailable in private mode.")
                else:
                    counts = history.memory_job_counts()
                    print("Memory jobs: " + ", ".join(
                        f"{state}={counts.get(state, 0)}"
                        for state in ("queued", "running", "completed", "failed")
                    ))
            elif command in ("/sessions", "/resume"):
                if history is None:
                    print("Saved sessions are unavailable in private mode.")
                elif command == "/sessions":
                    sessions = history.list_sessions()
                    if not sessions:
                        print("No saved sessions.")
                    for saved_id, created_at, count in sessions:
                        active = " (active)" if saved_id == session_id else ""
                        print(f"{saved_id} | {created_at} UTC | {count} turn(s){active}")
                else:
                    try:
                        saved_id = int(fields[1]) if len(fields) == 2 else 0
                    except ValueError:
                        saved_id = 0
                    if not 1 <= saved_id <= 2**63 - 1:
                        print("Usage: /resume ID. Use /sessions to find an ID.", file=sys.stderr)
                        continue
                    exchanges = history.recent_completed(saved_id, RECENT_EXCHANGES)
                    if exchanges is None:
                        print("Session not found. Use /sessions to find an ID.", file=sys.stderr)
                        continue
                    conversation.clear()
                    for user_text, reply_text in exchanges:
                        conversation.record(user_text, reply_text)
                    session_id = saved_id
                    print(f"Resumed session {saved_id}. Loaded {len(exchanges)} completed exchange(s).")
            elif command == "/clear":
                conversation.clear()
                session_id = None
                print("Conversation cleared. Previously saved history is retained.")
            else:
                print("Unknown command. Type /help for available commands.", file=sys.stderr)
            continue
        if history is not None:
            session_id, turn_id = history.start_turn(session_id, message)
        status, answer = _render(settings, message, context=conversation.context())
        if history is not None:
            outcome = {0: "completed", 1: "failed", 130: "interrupted"}[status]
            queued = history.finish_turn(turn_id, outcome, answer, enqueue=extract_memory)
            if extract_memory and status == 0 and not queued:
                print("Memory queue is full. This turn was saved without an extraction job.", file=sys.stderr)
        if status == 0:
            conversation.record(message, answer)


def _render(settings: Settings, message: str, **options) -> tuple[int, str]:
    parts = []
    try:
        print("Mellow: ", end="", flush=True)
        with closing(stream_reply(settings, message, **options)) as chunks:
            for text in chunks:
                print(text, end="", flush=True)
                parts.append(text)
        print()
        return 0, "".join(parts)
    except KeyboardInterrupt:
        print("\n[Cancelled; any displayed reply is incomplete.]")
        return 130, "".join(parts)
    except ProviderError as error:
        print("\n[Reply failed; any displayed text is incomplete.]")
        print(f"Provider error: {error}", file=sys.stderr)
        return 1, "".join(parts)


def _one_message(settings: Settings) -> int:
    print("One message will be sent to Groq; no conversation is saved.")
    try:
        message = input("You: ").strip()
    except (KeyboardInterrupt, EOFError):
        print("\n[Cancelled.]")
        return 130
    if not message:
        print("Enter a non-empty message.", file=sys.stderr)
        return 2
    status, _ = _render(settings, message)
    return status


if __name__ == "__main__":
    raise SystemExit(main())
