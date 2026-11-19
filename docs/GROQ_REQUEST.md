# B02 — one message to Groq

2026-11-12. Implemented and offline-tested; live API response pending.

## Connections

`__main__.main()` -> `config.load_settings()` -> prompt for input -> `providers.groq.reply(settings, message)` -> HTTPS POST -> parse response text -> print and exit.

`config.py` is unchanged. `providers/__init__.py` identifies the new package. `providers/groq.py` owns the fixed Groq endpoint, request construction, response validation, and safe `ProviderError`. `__main__.py` owns user interaction and exit codes. `tests/test_groq.py` fakes HTTP to check the request contract and errors without spending tokens. Existing configuration tests now use `--check-config` to stay offline.

## Request and response

POST https://api.groq.com/openai/v1/chat/completions with Authorization: Bearer <local key> and Content-Type: application/json. JSON contains the configured model, one role=user message, max_completion_tokens=256, and stream=false. Parse choices[0].message.content and reject missing/empty/non-text content.

This is one turn: no personality prompt, prior turns, streaming, storage, extraction, or LM Studio. A cap can truncate long responses. Timeout is 30 seconds per blocking socket operation, not a strict total deadline. No automatic retry prevents hidden duplicate requests; richer deadlines/cancellation are future work. Provider response bodies and raw exceptions are not echoed. A 400/404/422 message suggests checking model/request validity, not a definitive diagnosis.

## Local setup

From C:/Users/agraw/Music/Mellow in PowerShell, if .env does not already exist:

```powershell
Copy-Item .env.example .env
```

Edit .env locally: supply GROQ_API_KEY, set GROQ_MODEL=openai/gpt-oss-20b. Never paste the key into chat. Existing environment values override .env. Do not overwrite an existing .env with the blank example.

```powershell
.\.venv\Scripts\python.exe -m mellow --check-config
.\.venv\Scripts\python.exe -m mellow
```

Use a fictional prompt such as `Reply with a short greeting.` One user message is sent to Groq; nothing is saved by Mellow. Hosted provider policies still apply. A successful response verifies that particular key/model/request, not the whole product.

Exit codes: 0 success/config check; 1 provider failure; 2 missing/invalid configuration or blank input; 130 cancelled input/request. An interrupted remote request may already have been processed; cancellation does not guarantee no token usage.

## Verification

14 unittest methods passed on Windows/Python 3.14.7 (8 foundation + 6 Groq/CLI). Fake HTTP covers payload, auth header, response, 400/401/403/404/422/429/5xx, timeout/network errors, malformed/empty output, blank input, cancellation, and secret-safe errors. pip check passes. Live request, first-response latency, token usage, and billing remain unverified.

Legacy reference: D:/HEHE/Mellow-Gyrlick/core/brain.py `_think_with_groq`; its generic error reply is replaced by a separate ProviderError path. Sources: [Groq API reference](https://console.groq.com/docs/api-reference), [model documentation](https://console.groq.com/docs/model/llama-3.1-8b-instant). No current price/free-usage guarantee is made.

## Understanding check (not answered)

Which function sends the HTTP request? What do the Authorization header and messages field carry? Why should an authentication error be handled separately from a generated reply?

## Model correction — 2026-11-12

The initial model recommendation missed the account-tier restriction. Groq deprecated llama-3.1-8b-instant for free/developer usage on 2026-08-16; committed enterprise customers are exempt. The example now selects Groq's recommended replacement openai/gpt-oss-20b. See https://console.groq.com/docs/deprecations . Existing user .env files and environment variables were not edited. Live access and output quality remain unverified. The current 256-token output cap may need adjustment for reasoning-model output; do not claim the replacement has passed a live test.

## Live smoke check — 2026-11-12

Passed based on user-provided terminal transcript: activated environment, python.exe -m mellow, input hi, greeting response displayed, normal return to shell. This verifies one real request under the user's configuration. Exact configured model, latency, token usage, and billing were not measured. Earlier pending-live notes describe the state before this check.
