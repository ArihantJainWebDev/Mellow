"""Read and validate startup configuration without exposing secrets."""

from dataclasses import dataclass, field
import os
from pathlib import Path

from dotenv import dotenv_values


class ConfigurationError(ValueError):
    """A missing or invalid setting that the user can correct."""


@dataclass(frozen=True)
class Settings:
    groq_api_key: str = field(repr=False)
    groq_model: str
    memory_model: str | None = None
    embedding_model: str | None = None


def load_settings(env_file: Path = Path(".env")) -> Settings:
    """Read the selected file; existing environment values take precedence.

    A missing file is fine when configuration is supplied through the environment.
    No parent-directory search, environment mutation, or network probe is performed.
    """
    try:
        if env_file.exists():
            with env_file.open(encoding="utf-8-sig") as stream:
                file_values = dotenv_values(stream=stream, interpolate=False)
        else:
            file_values = {}
    except (OSError, UnicodeError):
        raise ConfigurationError(
            "Cannot read the configuration file. Check its path, permissions, and UTF-8 encoding."
        ) from None

    values = {**file_values, **os.environ}
    required = {}
    for name in ("GROQ_API_KEY", "GROQ_MODEL"):
        value = (values.get(name) or "").strip()
        if not value or value.lower() in {"your_api_key", "your_api_key_here", "your_model_here"}:
            raise ConfigurationError(
                f"Set {name} in your environment or configuration file. "
                "Use .env.example as a template."
            )
        if any(character.isspace() for character in value):
            raise ConfigurationError(f"{name} must not contain whitespace.")
        required[name] = value

    return Settings(
        groq_api_key=required["GROQ_API_KEY"],
        groq_model=required["GROQ_MODEL"],
        memory_model=(values.get("MELLOW_MEMORY_MODEL") or "").strip() or None,
        embedding_model=(values.get("MELLOW_EMBEDDING_MODEL") or "").strip() or None,
    )
