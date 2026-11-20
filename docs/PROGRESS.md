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
| Local worker | B08–B09 | B08 and B09 verified | Qwen passed three extraction smoke cases. 36 offline tests passed. |
| Memory controls | B10–B11 | B10 review plus targeted edit, forget, and keyword recall verified | SQLite migration, approval/rejection, local CLI dispatch |
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

B08 adds a standalone loopback-only LM Studio request and validates fact and
episode candidates. It does not connect extraction to chat or storage yet.
The installed Gemma 4 12B QAT loaded in 26.07 seconds. Two fictional extraction
attempts did not return a usable answer within the socket timeout. The second
explicitly reported a timeout after about 30 seconds. The temporary server was
stopped. A successful live extraction and smaller-model benchmark remain open.
C08 learning assessment remains pending.

B08 adds a repeatable three-case smoke benchmark for a preference plus a timed
episode, a greeting, and a hypothetical. It uses fictional inputs and prints
candidate memories and per-case timing. The benchmark rubric passed offline
checks. Live Qwen3-VL-4B-Instruct Q4_K_M results are pending installation and load.

### B08 live Qwen check

Qwen3-VL-4B-Instruct Q4_K_M was already loaded at 16,384 context tokens
with four parallel slots at inspection. The run used that existing setup.
The first pass matched 1/3 cases in 14.02 s, 4.53 s, and 9.58 s. It missed
a preference and proposed a hypothetical as an episode. The extraction
prompt now checks both memory kinds and defines an episode as something
that happened. It excludes hypothetical and conditional statements.

The repeat passed all three cases in 4.67 s, 1.50 s, and 1.86 s. Output
inspection confirmed the two expected candidates and two empty lists.
These prompts informed the refinement, so this is a smoke check. A separate
evaluation set and controlled performance measurements remain future work.
All 33 offline tests passed. Next: B09 background processing.

### B09 background jobs

Added opt-in extraction and /jobs, with SQLite schema v2, atomic queuing,
bounded admission, retries, and expiring claims. Candidate results are linked
to their source turns. Offline checks covered history migration, chat while
extraction waits, offline retries, restart recovery, and stale results.
Live worker check passed in the user terminal. The queue started empty. After four replies, three jobs were completed, one was running, and none had failed. This verifies job processing alongside chat. Candidate contents were not inspected. Next: B10 memory controls.

### B10 candidate review

SQLite schema v3 backfills completed worker results into individual candidate
rows with turn provenance, review state, and review timestamp. New results
create candidate rows in the same transaction that completes the job.
The CLI exposes pending and approved items and changes review state locally.
37 offline tests passed. They cover backfill, provenance, review persistence,
and idempotent job completion. At the B10 checkpoint, editing, deletion, and retrieval were still open.

Live B10 check: the CLI listed extracted candidates with source turn IDs.
Approving one made it visible under /memories. Follow-up chat confirmed
the approved item was still outside Groq's prompt, as expected before
retrieval. Rejection and migration remain covered by offline tests.

### Memory controls and recall milestone

Approved memories can be edited or removed. The CLI ranks approved memories
by keyword overlap and includes up to three matches in saved chat requests.
Pending candidates and private sessions are excluded. Removal clears the
duplicated extraction result for that job, while keeping the original chat
and any other materialized candidates from the same job.

38 offline tests passed. A live test seeded a fictional candidate in a
temporary database, approved it through the CLI, restarted the process,
and verified that a fresh chat recalled the fictional preference via Groq.
Broader source deletion, conflict resolution, embeddings, and semantic
evaluation remain open. Bundle related work and verification into milestone
commits rather than separate progress-only commits.

### Semantic recall milestone

Added opt-in LM Studio Nomic embeddings with task prefixes, normalized cosine
ranking, an in-process cache keyed by text and model, a 100-record scan and
three-match limit, and keyword fallback during a local inference outage.
The 0.54 cutoff is provisional. In the live fixture, the intended paraphrase
scored 0.559 and the unrelated question scored at most 0.517. Live checks
covered recall, no-match, edits, deletion and fresh-session Groq recall.
Broader relevance evaluation remains open. All 41 offline tests passed.

The streaming completion budget is now 1,024 tokens. This includes the local
fix for the earlier blank response that hit the 256-token cap.

### Contextual extraction and corrections

B14 passes the two previous completed exchanges from the same session to the
local extractor. The prompt uses them only to resolve references. The worker
skips common standalone questions. Storage suppresses new duplicates based on
normalized wording, including rejected entries. Corrections remain pending
for explicit replacement of a selected approved memory.

42 offline tests passed. A live Qwen worker run on fictional data linked
a one-word bicycle type to its subject, suppressed a repeated fact, skipped
a question, and saved a correction candidate that replaced the old record
when confirmed. Paraphrase deduplication and automatic conflict detection
remain outside this implementation. Next: B15 summaries.
