# Software requirements specification — Goal 1

Version: planning baseline 0.1, 2026-10-27. Status: intended behavior, not delivered functionality.

## Product and terminology

Mellow is a single-user CLI companion with a configurable personality and controllable personal memory.

- Context: recent messages and a bounded summary used for the current reply.
- Personal memory: lasting user facts, preferences, goals, or instructions.
- Episode: an experience or event with source context and available time information.
- Summary: a derived, potentially imperfect compression of conversation.
- RAG: retrieving relevant stored information and providing it to the conversational model.
- Background job: local extraction or summarization work that is not awaited by the interactive chat path.

## Core scenarios

1. Discuss a goal, restart the application, and resume the discussion.
2. Explicitly save a preference and later receive a relevant answer using it.
3. Recall a past experience without inventing details or confusing dates.
4. Correct a preference, inspect the change, and stop using the old value.
5. Delete history or memory and prevent queued extraction from recreating it.
6. Continue chatting when the local model is offline.

## Functional requirements

| ID | Requirement | Acceptance evidence |
|---|---|---|
| CHAT-01 | Stream Groq responses through a usable CLI | Interactive demo with first-token and completion measurements |
| CHAT-02 | Include bounded recent context and configurable personality | Multi-turn test and inspection of assembled request |
| CHAT-03 | Support sessions, restart/resume, and durable message status | Restart check; interrupted reply remains distinguishable from completed reply |
| CHAT-04 | Handle cancellation, timeout, authentication, and rate-limit failures clearly | Simulated failures leave CLI usable and avoid duplicate successful messages |
| MEM-01 | Support explicit remembering and configurable automatic extraction | Save command and automatic-memory-disabled test |
| MEM-02 | Store personal memories and episodes with source links, recorded time, and uncertainty | Inspect stored records and their source exchanges |
| MEM-03 | Distinguish user statements, model inferences, and user-confirmed information | Fictional extraction cases; no automatic promotion of guesses to confirmed facts |
| MEM-04 | Retrieve relevant memories with a threshold and prompt budget | Labeled retrieval cases including queries with no relevant memory |
| MEM-05 | Detect duplicates and handle changed/conflicting information without broad destructive replacement | Correction and multi-entity test cases |
| MEM-06 | Inspect, edit, and delete individual memories | CLI checks and subsequent retrieval checks |
| MEM-07 | Summarize older conversation without treating summaries as authoritative originals | Summary source linkage and bounded-context checks |
| BG-01 | Use LM Studio for validated structured memory extraction/summaries | Invalid structure and semantically incorrect output rejected or flagged |
| BG-02 | Persist bounded queued work, process one local job at a time, and recover across restart | Offline/restart/backlog tests; no chat wait on local inference |
| BG-03 | Bound retries and make final failures visible | Retry exhaustion test and job status inspection |
| PRIV-01 | Private sessions write no conversation, derived memory, or extraction payload to durable storage | Storage/log checks after private session and restart |
| PRIV-02 | Explain that Groq receives selected context and retrieved memories | Startup/help documentation and request inspection using fictional data |
| PRIV-03 | Local memory processing must not silently fall back to hosted processing | Local-outage test with no external extraction call |
| PRIV-04 | Delete selected conversations and their derived data/jobs; full reset covers all owned data | Referential deletion, queued/running-job race, restart, and retrieval checks |
| PRIV-05 | Deleting a memory also suppresses re-extraction from its retained source unless explicitly re-enabled | Reprocessing regression test; minimal suppression metadata inspected |
| OPS-01 | Validate configuration and expose useful backend/job status | Missing configuration and local-unavailable startup cases |
| OPS-02 | Record operational metrics without recording personal payloads or secrets | Logging tests and manual inspection with fictional data |

## Data behavior

Application data belongs to the user. Public source and demo data must be separate from personal runtime data. Conversation retention defaults and an export format remain open decisions. After the owner enables automatic memory, clear eligible user statements can enter recall without per-fact approval. Keep ambiguous or sensitive items out of automatic recall. Ordinary startup continues the prior conversation. Natural-language correction and forgetting are release requirements. See the [acceptance scenario](ROADMAP.md).

Deleting history must remove source-linked derived memories and dependent summaries, invalidate embeddings/cache, and cancel or invalidate affected jobs. Deleting only a memory while retaining its source requires a minimal suppression mechanism; metadata and its privacy implications must be documented. Never use a prompt-level 'forget' instruction as the deletion mechanism.

Private mode prevents durable retention by Mellow; it does not imply the hosted provider retains nothing. Local deletion cannot retract data already sent to a provider or remove copies in user-managed backups. Forensic secure erasure of SQLite/WAL/storage media is not promised; the implementation must document its exact deletion guarantees.

## Quality requirements

- Local generation must not be awaited by chat. Retrieval/storage overhead and hardware contention are measured separately.
- Use bounded queues, request/context budgets, timeouts, retries, and explicit shutdown behavior.
- Preserve source provenance and do not present inferred emotions or memories as certain.
- No microphone, camera, clipboard, OS monitoring, browser automation, phone endpoint, or autonomous machine actions in Goal 1.
- Fine-tuning is outside Goal 1 unless a new decision and measured requirement justify changing scope.
- Packaging must support a reproducible clean setup with testable configuration examples.
- Exact latency, recall, and cost targets will be set after baseline measurements; estimates are not acceptance results.

## Release acceptance

All applicable requirements above have recorded checks. A fresh setup can run the fictional demo. The owner can explain every implemented component, the dual-model data flow, privacy boundary, memory consistency model, and major tradeoffs. Known limitations and failed evaluations are documented before release.
