# B03 — streaming and cancellation

Implemented and verified 2026-11-19. No learning score assigned.

## Files and flow

- `providers/groq.py`: `stream_reply()` sends stream=true and yields content deltas. `_events()` reads Server-Sent Events (SSE), joining multiline data and ignoring comment/heartbeat lines. Metadata/role/reasoning-only chunks are not printed. `reply()` remains the older tested non-streamed path; the CLI now uses only stream_reply().
- `__main__.py`: `main()` loops over yielded text, using print(end="", flush=True), so output is not held until completion. contextlib.closing releases the generator/HTTP response even on Ctrl+C or rendering failure.
- `tests/test_streaming.py`: verifies incremental output before completion, Unicode, multiline SSE, invalid events, HTTP errors, partial EOF, read timeout, generator closure, and CLI interrupted/failed output. Existing test_groq.py CLI fakes now supply closeable generators.

Flow: input -> stream_reply -> HTTPS stream -> SSE event -> JSON delta.content -> yield -> CLI print/flush -> next event. A generator suspends at yield and resumes when the caller requests another chunk. It is not itself a background thread.

## Completion and failure

Normal successful completion requires nonblank text, finish_reason=stop, and [DONE]. Token-limit termination reports incomplete output; unexpected EOF or malformed data cannot count as success. Role and usage events carry no visible text. Ctrl+C gives exit 130; provider failure gives exit 1. Already printed chunks stay visible, followed by an incomplete marker. The request is not retried automatically. Closing a local connection does not prove the remote server stopped generating or charging.

This block still sends one message per launch and stores no conversations. Continuous prompting and recent-context/personality belong to B04/B05. The 256-token cap and 30-second socket timeout remain unchanged; the cap can constrain reasoning-model answers. Streaming improves perceived responsiveness; actual model throughput/total latency is not guaranteed to improve. A blocking read can delay processing until interrupted or its timeout; no strict whole-request deadline is claimed.

## Run

From the activated Mellow environment:

```powershell
python -m mellow
```

Try a short question. For a manual cancellation check, ask for a longer answer and press Ctrl+C while output is arriving; expect an incomplete/cancelled message and return to PowerShell. Very short replies may finish before cancellation. `--check-config` remains offline.

## Evidence

23 offline unittest methods passed. A live fictional greeting request completed in four text chunks, first visible text at 0.744 seconds and total at 0.745 seconds. One observation, no benchmark claim, no tokens/cost recorded. Original test-fixture failures and corrections are recorded in PROGRESS.md. Live terminal Ctrl+C has not been manually verified; generator cleanup and partial-output labelling passed offline tests.

Source consulted: [Groq streaming documentation](https://console.groq.com/docs/text-chat#streaming-a-chat-completion). No real credentials were printed or copied to documentation.

## Understanding check — pending

Does streaming necessarily make the model generate faster, or does it primarily let you see text before the complete answer is ready? Which function yields chunks and which function prints them?
