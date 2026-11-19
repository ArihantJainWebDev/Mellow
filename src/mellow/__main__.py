"""CLI entry point: check configuration or stream one reply from Groq."""

import argparse
from contextlib import closing
from pathlib import Path
import sys

from mellow.config import ConfigurationError, load_settings
from mellow.providers.groq import ProviderError, stream_reply


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Mellow: stream one reply from Groq.")
    parser.add_argument(
        "--env-file", type=Path, default=Path(".env"),
        help="Configuration file (default: .env in the current working directory).",
    )
    parser.add_argument("--check-config", action="store_true", help="Check settings without contacting Groq.")
    arguments = parser.parse_args(argv)
    try:
        settings = load_settings(arguments.env_file)
    except ConfigurationError as error:
        print(f"Configuration error: {error}", file=sys.stderr)
        return 2

    if arguments.check_config:
        print("Required configuration is present. No API request was made.")
        return 0

    try:
        print("One message will be sent to Groq; no conversation is saved.")
        message = input("You: ").strip()
        if not message:
            print("Enter a non-empty message.", file=sys.stderr)
            return 2
        print("Mellow: ", end="", flush=True)
        with closing(stream_reply(settings, message)) as chunks:
            for text in chunks:
                print(text, end="", flush=True)
        print()
    except (KeyboardInterrupt, EOFError):
        print("\n[Cancelled; any displayed reply is incomplete.]")
        return 130
    except ProviderError as error:
        print("\n[Reply failed; any displayed text is incomplete.]")
        print(f"Provider error: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
