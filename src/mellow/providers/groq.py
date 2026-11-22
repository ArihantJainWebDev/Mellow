"""Groq text requests, including streamed deltas; no history or retries."""

from collections.abc import Generator, Iterator
from http.client import HTTPException
import json
import socket
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from mellow.config import Settings

ENDPOINT = "https://api.groq.com/openai/v1/chat/completions"


class ProviderError(RuntimeError):
    """Safe, user-facing failure; never contains the request or API key."""


def reply(settings: Settings, message: str) -> str:
    """Send one message and return validated text, or raise ProviderError."""
    if not message.strip():
        raise ProviderError("Enter a non-empty message.")

    payload = {
        "model": settings.groq_model,
        "messages": [{"role": "user", "content": message}],
        "max_completion_tokens": 256,
        "stream": False,
    }
    request = Request(
        ENDPOINT,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {settings.groq_api_key}",
            "Content-Type": "application/json",
            "User-Agent": "Mellow/0.1.0",
        },
        method="POST",
    )
    try:
        with urlopen(request, timeout=30) as response:
            data = json.load(response)
    except HTTPError as error:
        status = error.code
        error.close()
        if status == 401:
            explanation = "Groq rejected the API key. Check GROQ_API_KEY locally."
        elif status == 403:
            explanation = "Groq denied access. Check account and model permissions."
        elif status == 429:
            explanation = "Groq rate limit or quota reached. Wait and check your account limits."
        elif status in (400, 404, 422):
            explanation = "Groq rejected the request. Check GROQ_MODEL and its availability."
        elif status >= 500:
            explanation = "Groq is temporarily unavailable. Try again later."
        else:
            explanation = f"Groq returned HTTP {status}."
        raise ProviderError(explanation) from None
    except (TimeoutError, socket.timeout):
        raise ProviderError("Groq request timed out. Try again later.") from None
    except (URLError, OSError):
        raise ProviderError("Cannot reach Groq. Check your network connection.") from None
    except (ValueError, UnicodeError):
        raise ProviderError("Groq returned an unreadable response.") from None

    try:
        text = data["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError):
        raise ProviderError("Groq returned no usable text response.") from None
    if not isinstance(text, str) or not text.strip():
        raise ProviderError("Groq returned no usable text response.")
    return text.strip()


def _events(response) -> Iterator[str]:
    """Read SSE events, preserving multiline data and ignoring comments."""
    data = []
    for raw_line in response:
        line = raw_line.decode("utf-8").rstrip("\r\n")
        if not line:
            if data:
                yield "\n".join(data)
                data = []
        elif line.startswith("data:"):
            value = line[5:]
            data.append(value[1:] if value.startswith(" ") else value)
    if data:
        yield "\n".join(data)


def stream_reply(
    settings: Settings, message: str, *, context: tuple[dict[str, str], ...] = (),
) -> Generator[str, None, None]:
    """Yield text as it arrives; require explicit successful stream completion.

    Closing the generator releases the HTTP response, including after Ctrl+C.
    Text already yielded must be treated as partial if an error follows.
    """
    if not message.strip():
        raise ProviderError("Enter a non-empty message.")
    request = Request(
        ENDPOINT,
        data=json.dumps({
            "model": settings.groq_model,
            "messages": [*context, {"role": "user", "content": message}],
            "max_completion_tokens": 1024,
            "stream": True,
        }).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {settings.groq_api_key}",
            "Content-Type": "application/json",
            "Accept": "text/event-stream",
            "User-Agent": "Mellow/0.1.0",
        },
        method="POST",
    )
    finish_reason = None
    has_text = False
    try:
        with urlopen(request, timeout=30) as response:
            for event in _events(response):
                if event == "[DONE]":
                    if finish_reason == "length":
                        raise ProviderError("Reply reached the output-token limit; displayed text is incomplete.")
                    if finish_reason != "stop":
                        raise ProviderError("Groq did not confirm a normal text completion.")
                    if not has_text:
                        raise ProviderError("Groq returned no usable text response.")
                    return
                item = json.loads(event)
                if not isinstance(item, dict) or "error" in item:
                    raise ProviderError("Groq returned an invalid or failed stream event.")
                choices = item.get("choices")
                if choices == []:
                    continue  # Usage-only event; never display metadata as text.
                if not isinstance(choices, list) or not choices or not isinstance(choices[0], dict):
                    raise ProviderError("Groq returned an invalid stream event.")
                choice = choices[0]
                delta = choice.get("delta")
                if not isinstance(delta, dict):
                    raise ProviderError("Groq returned an invalid text delta.")
                text = delta.get("content")
                if text is not None:
                    if not isinstance(text, str):
                        raise ProviderError("Groq returned an invalid text delta.")
                    if text:
                        has_text = has_text or bool(text.strip())
                        yield text
                if choice.get("finish_reason") is not None:
                    finish_reason = choice["finish_reason"]
            raise ProviderError("Groq stream ended unexpectedly; displayed text may be incomplete.")
    except HTTPError as error:
        status = error.code
        error.close()
        messages = {
            401: "Groq rejected the API key. Check GROQ_API_KEY locally.",
            403: "Groq denied access. Check account and model permissions.",
            429: "Groq rate limit or quota reached. Wait and check your account limits.",
        }
        explanation = messages.get(status)
        if explanation is None:
            if status in (400, 404, 422):
                explanation = "Groq rejected the request. Check GROQ_MODEL and its availability."
            elif status >= 500:
                explanation = "Groq is temporarily unavailable. Try again later."
            else:
                explanation = f"Groq returned HTTP {status}."
        raise ProviderError(explanation) from None
    except (TimeoutError, socket.timeout):
        raise ProviderError("Groq stream timed out; displayed text may be incomplete.") from None
    except (URLError, OSError, HTTPException):
        raise ProviderError("Groq connection failed; displayed text may be incomplete.") from None
    except (ValueError, UnicodeError):
        raise ProviderError("Groq returned an unreadable stream event.") from None
