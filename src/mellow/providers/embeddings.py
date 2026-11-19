"""Validated text embeddings from LM Studio on loopback."""

from http.client import HTTPConnection, HTTPException
import json
import math


class EmbeddingError(RuntimeError):
    """Local embedding inference failed or returned invalid data."""


def embed(model: str, inputs: list[str]) -> list[tuple[float, ...]]:
    connection = HTTPConnection("127.0.0.1", 1234, timeout=3)
    try:
        connection.request("POST", "/v1/embeddings",
                           json.dumps({"model": model, "input": inputs}),
                           {"Content-Type": "application/json"})
        response = connection.getresponse()
        if response.status != 200:
            raise EmbeddingError("Local embeddings unavailable.")
        items = json.loads(response.read(8_000_001))["data"]
        if len(items) != len(inputs):
            raise ValueError
        indexed = {}
        for item in items:
            index, values = item["index"], item["embedding"]
            if type(index) is not int or index in indexed or not 0 <= index < len(inputs):
                raise ValueError
            if not isinstance(values, list) or not values or any(
                type(value) not in (int, float) or not math.isfinite(value) for value in values
            ):
                raise ValueError
            norm = math.hypot(*values)
            if not 0 < norm < math.inf:
                raise ValueError
            indexed[index] = tuple(value / norm for value in values)
        return [indexed[index] for index in range(len(inputs))]
    except (HTTPException, OSError, IndexError, KeyError, TypeError, ValueError, OverflowError):
        raise EmbeddingError("Local embeddings unavailable or invalid.") from None
    finally:
        connection.close()
