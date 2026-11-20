"""CLI entry point: check configuration or chat with Groq."""

import argparse
from contextlib import closing
from pathlib import Path
import sqlite3
import sys

from mellow.conversation import RECENT_EXCHANGES, Conversation
from mellow.config import ConfigurationError, Settings, load_settings
from mellow.providers.groq import ProviderError, stream_reply
from mellow.retrieval import SemanticRecall, memory_message
from mellow.storage import History
from mellow.summary import session_summary
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
    parser.add_argument("--embedding-model", metavar="MODEL_ID",
                        help="Use a local Nomic embedding model for semantic memory recall.")
    arguments = parser.parse_args(argv)
    if arguments.embedding_model and (arguments.private or arguments.once or arguments.check_config):
        parser.error("--embedding-model requires a saved interactive session")
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
        semantic = SemanticRecall(arguments.embedding_model) if arguments.embedding_model else None
        if history is None or not arguments.memory_model:
            return _chat(settings, history, semantic=semantic)
        stop, worker = start_worker(history, arguments.memory_model)
        try:
            return _chat(settings, history, extract_memory=True, semantic=semantic)
        finally:
            stop.set()
            worker.join(timeout=1)
    except (OSError, sqlite3.Error):
        print("Could not save local history. Check access to data/history.db or restart with --private.",
              file=sys.stderr)
        return 1


def _chat(settings: Settings, history: History | None, *, extract_memory: bool = False,
          semantic: SemanticRecall | None = None) -> int:
    conversation = Conversation()
    session_id = None
    print("Messages and recent exchanges are sent to Groq.")
    if history is None:
        print("This session is kept off disk.")
    else:
        print("Chat is saved locally to data/history.db. Older session excerpts and relevant approved memories are sent to Groq.")
        print("Use --private to skip saved history and memories.")
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
            fields = message.split(maxsplit=1)
            fields[0] = fields[0].casefold()
            command = fields[0] if len(fields) == 1 or fields[0] in (
                "/resume", "/approve", "/reject", "/edit-memory", "/forget", "/replace-memory", "/retry-job",
            ) else message.casefold()
            if command == "/quit":
                return 0
            if command == "/help":
                print("/help  Show commands\n/clear Clear recent conversation\n/quit  Exit Mellow")
                if history is not None:
                    print("/sessions  List saved sessions\n/resume ID  Resume a saved session")
                    print("/summary  Show older context for this session")
                    print("/jobs  Show memory job counts and recent failures")
                    print("/retry-job TURN_ID  Requeue a failed extraction")
                    print("/candidates  List pending memory suggestions\n/memories  List approved memories")
                    print("/approve ID  Keep a suggestion\n/reject ID  Dismiss a suggestion")
                    print("/edit-memory ID TEXT  Edit an approved memory\n/forget ID  Remove a memory")
                    print("/replace-memory CANDIDATE_ID MEMORY_ID  Approve a correction and replace the old memory")
            elif command in ("/candidates", "/memories", "/approve", "/reject", "/edit-memory",
                             "/forget", "/replace-memory"):
                if history is None:
                    print("Saved memories are unavailable in private mode.")
                else:
                    _review_command(history, command, fields)
            elif command == "/summary":
                recap = session_summary(history, session_id) if history is not None and session_id is not None else None
                print(recap["content"] if recap else "No older saved exchanges in this session.")
            elif command == "/retry-job":
                if history is None:
                    print("Memory jobs are unavailable in private mode.")
                    continue
                try:
                    turn_id = int(fields[1]) if len(fields) == 2 else 0
                except ValueError:
                    turn_id = 0
                if not 1 <= turn_id <= 2**63 - 1:
                    print("Usage: /retry-job TURN_ID", file=sys.stderr)
                elif history.retry_memory_job(turn_id):
                    print(f"Requeued turn {turn_id}.")
                    if not extract_memory:
                        print("Start Mellow with --memory-model to process it.")
                else:
                    print("Turn has no failed job or the queue is full.", file=sys.stderr)
            elif command == "/jobs":
                if history is None:
                    print("Memory jobs are unavailable in private mode.")
                else:
                    counts = history.memory_job_counts()
                    print("Memory jobs: " + ", ".join(
                        f"{state}={counts.get(state, 0)}"
                        for state in ("queued", "running", "completed", "failed")
                    ))
                    for failed_id, attempts, reason in history.failed_memory_jobs():
                        print(f"Turn {failed_id} failed after {attempts} attempt(s). "
                              f"{reason or 'No error reason was recorded.'}")
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
                print("Conversation cleared. Previously saved history is retained. Approved memories are still available.")
            else:
                print("Unknown command. Type /help for available commands.", file=sys.stderr)
            continue
        if history is not None:
            session_id, turn_id = history.start_turn(session_id, message)
        context = conversation.context()
        if history is not None:
            recap = session_summary(history, session_id)
            if recap is not None:
                context = (context[0], recap, *context[1:])
            approved = history.list_memories("approved")
            memory = semantic.find(message, approved) if semantic else memory_message(message, approved)
            if memory is not None:
                context = (context[0], memory, *context[1:])
        status, answer = _render(settings, message, context=context)
        if history is not None:
            outcome = {0: "completed", 1: "failed", 130: "interrupted"}[status]
            queued = history.finish_turn(turn_id, outcome, answer, enqueue=extract_memory)
            if extract_memory and status == 0 and not queued:
                print("Memory queue is full. This turn was saved without an extraction job.", file=sys.stderr)
        if status == 0:
            conversation.record(message, answer)


def _review_command(history: History, command: str, fields: list[str]) -> None:
    if command in ("/candidates", "/memories"):
        status = "pending" if command == "/candidates" else "approved"
        memories = history.list_memories(status)
        if not memories:
            print(f"No {status} memories.")
        for candidate_id, kind, text, turn_id in memories:
            # Escape terminal control characters in model output.
            safe_text = "".join(char if char.isprintable() else " " for char in text)
            print(f"{candidate_id} [{kind}] {safe_text} (turn {turn_id})")
        return
    if command == "/replace-memory":
        try:
            candidate_id, memory_id = map(int, fields[1].split())
            if not all(1 <= value <= 2**63 - 1 for value in (candidate_id, memory_id)):
                raise ValueError
        except (IndexError, ValueError):
            print("Usage: /replace-memory CANDIDATE_ID MEMORY_ID", file=sys.stderr)
            return
        if history.replace_memory(candidate_id, memory_id):
            print(f"Memory {memory_id} replaced by approved candidate {candidate_id}.")
        else:
            print("Provide a pending candidate and an approved memory of the same kind.", file=sys.stderr)
        return
    arguments = fields[1].split(maxsplit=1) if len(fields) == 2 else []
    expected = 2 if command == "/edit-memory" else 1
    try:
        candidate_id = int(arguments[0]) if len(arguments) == expected else 0
    except ValueError:
        candidate_id = 0
    if not 1 <= candidate_id <= 2**63 - 1:
        suffix = " TEXT" if command == "/edit-memory" else ""
        print(f"Usage: {command} ID{suffix}. Use /candidates or /memories to find an ID.", file=sys.stderr)
        return
    if command == "/edit-memory":
        text = arguments[1].strip()
        if not text or len(text) > 300:
            print("Memory text must be 1 to 300 characters.", file=sys.stderr)
        elif history.edit_memory(candidate_id, text):
            print(f"Memory {candidate_id} updated.")
        else:
            print("No approved memory with that ID.", file=sys.stderr)
        return
    if command == "/forget":
        if history.forget_memory(candidate_id):
            print(f"Memory {candidate_id} removed. Source chat is retained.")
        else:
            print("No memory with that ID.", file=sys.stderr)
        return
    approve = command == "/approve"
    if history.review_memory(candidate_id, approve=approve):
        print(f"Memory {candidate_id} {'approved' if approve else 'rejected'}.")
    else:
        print("No pending candidate with that ID.", file=sys.stderr)


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
