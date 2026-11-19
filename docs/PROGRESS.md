# Progress tracker

Updated: 2026-11-19 (Asia/Kolkata).

## Current position

- Legacy source inspection: completed in the conversation; 27 Python files passed syntax parsing at that inspection. Runtime/integration behavior was not verified.
- New folder: confirmed readable and empty before this documentation pack.
- Planning baseline: documented; detailed technical selections remain proposed.
- Learning workflow updated: assistant implements/tests, owner learns through concise file/function/data-flow walkthroughs.
- Chapter report card active: C01/C02 have partial evidence; C03 walkthrough provided, assessment pending.
- New implementation: B01 verified; B02 request path offline-tested and live response confirmed by user terminal transcript on 2026-11-12. B03 streaming implemented, offline-tested, and live-verified on 2026-11-19. B04 adds session context and personality. B05 CLI commands implemented. B06 adds SQLite history and reply states. B07 adds saved session listing and resume.
- Environment: `.venv` created with Python 3.14.7; editable package installed with python-dotenv 1.2.4. No data migrated, inference models downloaded, Git repository initialized, or GitHub release created.
- Foundation and live Groq request/streaming paths verified; LM Studio and local-model readiness remain unverified. See [FOUNDATION.md](FOUNDATION.md) for commands and results.

## Phase status

| Phase | Blocks | Status | Evidence |
|---|---|---|---|
| Foundation | B01–B02 | B01 verified; B02 live smoke check passed | 14 offline tests; user terminal transcript 2026-11-12 |
| Conversation/CLI | B03–B05 | B03–B05 verified | 26 offline tests and live streaming/context smoke checks |
| History | B06–B07 | B06–B07 verified | Offline CLI, SQLite reopen, and resumed request checks |
| Local worker | B08–B09 | Not started | — |
| Memory controls | B10–B11 | Not started | — |
| Retrieval | B12–B13 | Not started | — |
| Automatic memory | B14–B15 | Not started | — |
| Release | B16–B17 | Not started | — |

## Next implementation session — date not fixed

B07 adds /sessions and /resume ID. Resume uses only the latest four completed exchanges and appends new turns to the selected session. Next: B08 local model and structured output. B06 stores history in the ignored local data/history.db file. Replies are marked pending before generation and completed, failed, or interrupted after it. A hard termination can leave pending rows. /clear starts a fresh context and session while retaining earlier records. --private and --once bypass storage. C06 assessment pending.

Stopping point: minimal understood request path with a valid-response check and a clear missing-configuration failure. No memory or background worker is required in this first block.

## B01 record — 2026-11-10

Implemented project packaging, isolated environment, entry point, explicit configuration loading/validation, and eight offline tests. Consulted legacy `config/settings.py`; avoided import-time directory creation and always-successful validation. Verified editable install, both launch paths, missing-config exit 2, configured offline exit 0 from another directory, secret-safe output, and pip consistency. Initial tests failed because environment isolation removed Windows temporary-path variables before temporary-directory creation; corrected setup order and reran successfully. No credential or API validity was checked. C01 was explained in chat; no user answers or scores yet.

## B02 record — 2026-11-12

Consulted legacy MellowBrain._think_with_groq and current official Groq API/model documentation. Added providers/groq.py with one non-streamed request, 256 output-token cap, 30-second socket timeout, no automatic retries, safe provider errors, and response validation. CLI now accepts one prompted message; --check-config preserves offline startup validation. Added six test methods (14 total including B01), all passing with fake HTTP responses; pip check clean. No new dependency. Live API response pending local credentials; no claims of real latency, account access, or model success. No new assessment score.

## B03 record — 2026-11-19

Added stream_reply() generator and SSE event parser in providers/groq.py; main() renders chunks immediately with flush=True and closes the generator on completion/error/Ctrl+C. Failures label partial text incomplete. Requires finish_reason=stop plus [DONE]; token-limit termination is a reported incomplete reply. B02 reply() retained as a tested non-streamed reference for now. No new dependencies.

23 offline tests passed. Initial new test fixtures had an incorrectly escaped multiline bytes literal and a list iterator without close(); both fixtures corrected before the successful run. Live public greeting prompt completed in four text chunks: first text 0.744 s, total 0.745 s, response Hi there! with smile emoji. One sample, not a benchmark or guaranteed latency; no token/cost measurement. No private messages or credentials printed. Live Ctrl+C remains a manual terminal check; cancellation cleanup tested offline. Learning scores unchanged.

## Session record template

Date and duration:
Block and purpose:
Legacy files/functions read:
Files changed by assistant / owner:
File responsibilities, key functions, and connections explained:
Chapter IDs and owner explanation / brief quiz evidence:
Verification and evidence:
Decisions and unresolved bugs:
Next step and stopping point:

## Documentation update — 2026-11-10

Recorded the owner-authorized switch to assistant implementation and concise learning. Added REPORT_CARD.md with chapter coverage, assessment rubric, evidence log, and reassessment rules. No implementation work or new technical assessment was performed during this update.

## Future backlog

Desktop overlay and lifecycle; optional OS awareness; habits and proactive behavior; voice and interruption; camera/screen sharing; bounded laptop/browser tools; authenticated phone connection; custom voice; possible fine-tuning experiment only after a measured need. These are candidate future capabilities, not Goal 1 commitments.

## B02 live evidence — 2026-11-12

User supplied terminal transcript from activated .venv at C:/Users/agraw/Music/Mellow: python.exe -m mellow; input hi; response Hello! followed by a greeting and an offer to help; process returned to PowerShell normally. Confirms one successful live input-to-response path for the user's configured provider/model. Model ID, token usage, latency, and exit-code value were not independently captured; do not infer them from the transcript. No credentials recorded. Next implementation block: B03 streaming, followed by continuous CLI conversation/context blocks. C02 technical understanding remains unassessed.

## Version control

Project code, tests, and development docs are tracked in the Mellow GitHub repository. Commit and push verified blocks there as development progresses. Keep credentials and the personal learning report card local.

## B04 — recent context and personality

`conversation.py` builds the prompt from a fixed personality and the latest four
successful user/assistant pairs. The CLI keeps one Conversation per run and only
records an exchange after the stream completes. A failed or cancelled turn
returns to the prompt. Groq receives the context with each message. B05 commands
and persistent history remain separate blocks. C04 assessment pending.

B04 verification: 25 offline tests passed. A live fictional two-turn prompt
introduced an imaginary pet named Velmora and the follow-up returned Velmora.

B05 verifies local command dispatch, clean exit, and clearing prior turns from the next request while retaining the personality prompt. C05 assessment pending.

B07 live verification from the user's PowerShell run: /sessions listed two saved
sessions. Resuming the newer session loaded one completed exchange and the model
could not identify the user's preferred name. Switching to the earlier session
loaded four completed exchanges, and a follow-up correctly recalled the name.
The CLI returned to PowerShell after /quit.
