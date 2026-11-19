"""CLI entry point: check configuration or chat with Groq."""

import argparse
from contextlib import closing
from pathlib import Path
import sqlite3
import sys

from mellow.conversation import Conversation
from mellow.config import ConfigurationError, Settings, load_settings
from mellow.providers.groq import ProviderError, stream_reply
from mellow.storage import History


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Mellow: chat with Groq.")
    parser.add_argument(
        "--env-file", type=Path, default=Path(".env"),
        help="Configuration file (default: .env in the current working directory).",
    )
    parser.add_argument("--check-config", action="store_true", help="Check settings without contacting Groq.")
    parser.add_argument("--once", action="store_true", help="Send one message and exit.")
    parser.add_argument("--private", action="store_true", help="Keep this chat off disk.")
    arguments = parser.parse_args(argv)
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
        return _chat(settings, None if arguments.private else History(Path("data/history.db")))
    except (OSError, sqlite3.Error):
        print("Could not save local history. Check access to data/history.db or restart with --private.",
              file=sys.stderr)
        return 1


def _chat(settings: Settings, history: History | None) -> int:
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
            command = message.casefold()
            if command == "/quit":
                return 0
            if command == "/help":
                print("/help  Show commands\n/clear Clear recent conversation\n/quit  Exit Mellow")
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
            history.finish_turn(turn_id, outcome, answer)
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
