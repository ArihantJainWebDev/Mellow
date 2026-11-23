# Decision log

Baseline date: 2026-10-27. Accepted decisions come from the conversation; proposed details require review in their implementation blocks.

| ID | State | Decision and reason | Consequence |
|---|---|---|---|
| D001 | Accepted | Preserve legacy project; new implementation in Mellow | Old code remains a reference; no automatic data migration |
| D002 | Superseded by D013 (2026-11-10) | Original agreement: owner implements code with block-by-block explanation | Preserved as decision history; no longer governs implementation |
| D003 | Accepted | Goal 1 is a substantial memory-aware CLI release | Integrations and memory are internal phases rather than separate release goals |
| D004 | Accepted | Groq conversation + LM Studio local background processing | Explicit provider boundaries; no hosted fallback for local memory work |
| D005 | Accepted direction | Foreground must not await memory extraction | Eventual memory consistency; measure retrieval overhead and resource contention |
| D006 | Accepted direction | Prompt engineering and memory RAG; no initial fine-tuning | Revisit training only against a concrete evaluated deficiency |
| D007 | Accepted | GitHub release after Goal 1; branches/PRs for later goals | Local version control during Goal 1; exact remote/visibility pending |
| D008 | Proposed | Python, SQLite, one worker thread with durable bounded jobs | Low operational complexity; lifecycle/idempotency/deletion races need tests |
| D009 | Proposed | Local embeddings with SQLite-backed similarity scan | Avoid a vector service initially; benchmark against keyword search |
| D010 | Proposed | Begin local-model comparison around 3–4B, 4-bit, modest context | Hardware fit and extraction quality must be measured; no model chosen yet |
| D011 | Accepted direction | Facts, episodes, history, and summaries have distinct roles | Provenance and deletion must cover derived data and jobs |
| D012 | Accepted | Honest verified release/resume claims only | Track evidence and limitations throughout implementation |
| D013 | Accepted 2026-11-10 | Assistant writes and verifies code; owner learns through concise walkthroughs of files, functions, and connections | Supersedes D002; keep block-by-block progress without mandatory owner coding or deep dives |
| D014 | Accepted 2026-11-10 | Maintain a chapter report card using observed explanations, conversation evidence, and short quizzes/scenario tests | No invented grades; distinguish exposure, assessed understanding, and software verification; retain attempts and reassess gaps |

| D015 | Implemented 2026-11-10 | Use src package layout, setuptools/pip editable install, and project-local venv; tested Python 3.14.7 | Declared >=3.11 is provisional; only 3.14.7 tested; revisit with embedding/inference compatibility |
| D016 | Implemented 2026-11-10 | Explicit dotenv file + environment precedence, frozen settings, no import-time I/O/network | python-dotenv 1.2.4 installed; dependency range >=1.2,<2, release lock pending; no default model until B02 |

| D017 | Implemented 2026-11-12 | B02 uses urllib.request for one explicit Groq HTTP request; no SDK dependency or retries yet | 30-second socket timeout is not a strict whole-operation deadline; revisit client for streaming/cancellation in B03 |
| D018 | Provisional 2026-11-12 | Example model llama-3.1-8b-instant remains configurable; 256 maximum completion tokens | Official model docs checked; actual account availability/quality and live response unverified |

## Open decisions

- Final supported Python range, release dependency locking, and future inference-library compatibility (initial venv/pip/setuptools workflow selected in D015).
- CLI library and streaming/cancellation behavior.
- Groq model, budget, rate-limit handling, and HTTP/SDK choice.
- Exact local model, quantization/context, runtime version, and quality baseline.
- Embedding model/revision, artifact provenance/license, thresholds.
- Runtime data location, default retention, export format, and backup behavior.
- Automatic extraction default, inferred-memory review, and correction semantics.
- Queue capacity, retry/lease/deadline values, and recovery policy.
- Minimal suppression metadata and exact deletion guarantees.
- Measured release targets for relevance, latency, quality, and cost.
- Repository destination/visibility, release license, and contribution/CI details.

## New decision template

ID / date / status / problem / options considered / choice / rationale / disadvantages / evidence / revisit trigger / affected requirements and blocks.

Do not mark a proposal accepted merely because it appears in an architecture document. Do not change accepted scope silently; record the reason and update linked documents.

## D019 — correction to D018 — 2026-11-12

The user questioned the initial model's availability. Verified Groq's deprecation notice: llama-3.1-8b-instant retired for free/developer tiers on 2026-08-16, enterprise exempt. D018's example selection is superseded by openai/gpt-oss-20b per Groq's migration recommendation. Corrected .env.example and B02 guide; no personal credentials/configuration touched. Live verification remains pending. Source: https://console.groq.com/docs/deprecations .


## D020 — streaming — 2026-11-19

Implemented B03 using urllib SSE events and a closing generator, retaining the existing HTTP approach without new dependencies. Normal completion requires an explicit stop finish reason and [DONE]; unexpected EOF, truncation, bad events, and read errors report failure rather than masquerading as successful text. CLI flushes each content chunk and explicitly marks partial output on interruption. Retain non-streamed reply() for existing contract tests temporarily; consolidate shared request/error handling if both paths remain supported. Timeout remains per socket operation, not an overall deadline; remote billing cancellation cannot be guaranteed.

## 2026-11-22 product baseline

Adopt the five-goal [roadmap](ROADMAP.md) and [visual references](visuals/README.md). Automatic continuity and opt-in automatic memory supersede manual resume and mandatory review as target defaults. Preserve optional controls. Flag scope drift before implementation and record explicit plan revisions here.

## 2026-11-22 release

Use MIT for project code. Direct ordinary statements can be saved locally. Ambiguous material stays in review. Local model classification is fallible. Retain exportable source text on recall suppression and provide separate physical record deletion controls.


## November 23, 2026 · Desktop usefulness

A single composer serves chat, local assignments and explicit desktop commands. Reply pages advance at the reader's pace. The local assignment parser performs deterministic SQLite operations, including notice replay detection. The screen watcher samples on a timer with one in-flight local request to limit laptop load. Image interpretation cannot issue an OS command. External actions originate from the user's composer or menu. Voice, webcam and avatar artwork are deferred.
