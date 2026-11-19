# Goal 1 implementation and learning plan

Status: agreed phase outline; block details remain adjustable. B01 implemented and software-verified; C01 learning assessment pending. Workflow updated 2026-11-10: assistant implements and tests; owner learns file responsibilities, important functions, and system connections through concise walkthroughs.

## Current checkpoint

B01 is implemented; see [FOUNDATION.md](FOUNDATION.md) for file/function map and evidence. B02 is now implemented and offline-tested; see [GROQ_REQUEST.md](GROQ_REQUEST.md). Local key setup and live verification are next. No live Groq call has been made.

## Phase 1 — foundation

- B01: Agree starter structure; verify Python/Git and configuration strategy. Read legacy `config/settings.py`, `requirements.txt`, and `setup.py`. Stop with a minimal runnable project and safe configuration example.
- B02: Trace `core/brain.py` (`__init__`, `_think_with_groq`) and relevant call site in `core/conversation.py`. Implement one input → Groq → response. Check valid request, missing key, and failed request. Explain HTTP headers, payload, response, and timeout.

## Phase 2 — conversation and CLI

- B03: Streaming and terminal rendering; cancellation and partial response behavior. Compare legacy non-streamed chat. Stop with readable streamed output and a usable prompt after cancellation.
- B04: Recent context and explicit personality; inspect `_build_messages`, `config/prompts.py`, and prompt assembly in `core/personality.py`. Stop with a multi-turn fictional test and bounded requests.
- B05: CLI commands and user-facing error/status behavior. Exact command names/library are selected here. Stop with consistent help, quit, and session/context controls.

## Phase 3 — durable history

- B06: SQLite schema/migrations, source IDs, timestamps, completed/interrupted/failed messages. Read `_get_connection`, `_init_database`, and conversation storage in `core/memory.py`.
- B07: Session resume and restart checks. Define durable-history deletion separately from clearing active context. Stop with persistence/restart and transactional failure checks passing.

## Phase 4 — local processing

- B08: Verify LM Studio endpoint, benchmark a small quantized model, and implement validated structured output. Compare misleading legacy `use_local` routing with explicit provider choice.
- B09: Durable bounded job queue, one worker, retry/cancellation/restart policy. Read `_extract_facts_async` in `core/conversation.py` as a reference for what needs improvement. Stop when chat continues with local inference offline and jobs recover without duplicates.

## Phase 5 — controllable memory

- B10: Explicit personal memories and episodes with provenance and uncertainty; inspect legacy fact/episode methods. Stop with deterministic save/inspect/edit behavior before relying on model extraction.
- B11: Correction, conflicts, deletion, source suppression, private mode, and deletion-versus-worker races. Compare `forget_about` and `clear_memory` in the legacy project. Stop with invariants passing across restart.

## Phase 6 — retrieval

- B12: Understand `core/embedder.py`; implement and verify embeddings with explicit model metadata and failure handling.
- B13: Understand `semantic_search_facts`, `semantic_search_episodes`, and personality context assembly. Add bounded relevant retrieval and compare it with keyword search. Stop with labeled relevant/irrelevant cases and measured retrieval overhead.

## Phase 7 — automatic memory and summaries

- B14: Combined local fact/episode extraction, eligibility rules, semantic validation, duplicate/conflict reconciliation, and visible review policy. Read legacy extraction methods in `core/brain.py` and their orchestration. Stop with ambiguous/negated/fictional examples handled correctly.
- B15: Bounded summaries and context compaction with provenance. Read `_run_daily_summary` as a cautionary reference for truncation and timezone behavior. Stop with source-linked summaries and correct deletion invalidation.

## Phase 8 — release

- B16: Integrate CLI status, memory controls, privacy explanation, backlog handling, and graceful lifecycle. Measure baseline versus memory/background modes.
- B17: Complete meaningful tests, fictional demo, clean setup, dependency/configuration documentation, licensing/artifact checks, and release evidence. Owner explains the architecture. Publish `v0.1` only after the gate passes.

## Session format

Keep sessions short and practical. Briefly explain the next block, then the assistant implements and verifies it. Give a compact walkthrough of each changed file, its key functions, callers/dependencies, and data flow. Finish with one or two understanding questions when useful and update the records. Several blocks can share a session; 45–60 minutes is an optional guideline, not a required lesson length. Deep dives are driven by questions or demonstrated gaps.

For every completed block record: purpose, legacy functions consulted, files created/changed, important functions/connections, software verification result, learning evidence (if assessed), unresolved issue, and next stopping point. The assistant performs implementation edits under the updated working agreement. Track chapter understanding independently in [REPORT_CARD.md](REPORT_CARD.md); a block may be implemented while its learning assessment remains pending.

## Git and release workflow

Use local Git commits during Goal 1 once initialized. Do not silently overwrite existing remotes or create repositories/commits before their block. Publish the new codebase on completion of Goal 1; remote name/visibility remain pending. Tag the verified release `v0.1`. Subsequent substantial goals use `codex/goal-2-<scope>` or another owner-chosen branch, a reviewable PR, verification, merge to `main`, and a new version tag.

No fixed completion date is promised. Avoid unnecessary abstractions and repeated low-value exercises; progress is driven by working behavior and understanding.
