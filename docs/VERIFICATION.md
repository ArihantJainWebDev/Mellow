# Verification and release evidence

Status: planned checks; none executed against a new implementation.

## Test strategy

Use deterministic provider fakes for routine tests. Run explicitly opted-in real-provider smoke checks using fictional data; distinguish live results from mocks. Evaluate model behavior separately from software correctness. Record model identifier/revision, prompt/config version, test data version, hardware, and date.

| Area | Required cases |
|---|---|
| Chat | Multi-turn continuity, streaming completion/cancellation, timeout, rate limit, bad credentials, malformed response |
| Storage | New schema/migration, restart, failed transaction, message status, concurrent worker access |
| Worker | Local offline, bounded backlog, retry exhaustion, restart recovery, idempotency, shutdown during work |
| Memory | Explicit save, relevant episode, ambiguity, negation, hypothetical text, third-party facts, changing preferences, unsupported assistant claims |
| Retrieval | Correct relevant result, irrelevant query abstention, stale/corrected/deleted memory excluded, embedding failure, prompt cap |
| Privacy | No private-session payload on disk/logs; local extraction never falls back remotely; no secrets logged |
| Deletion | Single memory, source conversation, full reset, dependent summary/embedding cleanup, pending/running worker race, reprocessing suppression |

## Fictional evaluation set

Create short synthetic conversations with labeled expected memories and source messages, plus queries with expected relevant results and queries where no memory is warranted. Include corrections and deletion timelines. Keep prompt-development examples separate from held-out evaluation examples.

Record extraction precision/recall, retrieval precision/recall at a chosen k, unsupported personal claims, and correction/deletion failures. Use counts and actual examples alongside percentages. Small datasets support limited claims, not universal accuracy.

## Performance and usage

Measure time to first token, total generation time, retrieval/storage overhead, local job duration, queue depth, and RAM/VRAM utilization. Compare Groq-only chat, retrieval-enabled chat, and chat while the local worker is active. Report median and tail latency with sample count; disclose network/model variation.

Starting prompt budgets only: roughly 1,000–2,500 Groq input tokens and 100–300 output tokens per reply; 500–1,500 local extraction input and 100–400 output tokens; periodic summaries roughly 2,000–4,000 input and 200–400 output tokens. These are estimates, not measured usage, prices, or guaranteed fit. Record actual usage fields where available, otherwise label estimates. Set explicit latency/quality/cost targets after the first baseline and before release evaluation.

## Release gate

- [ ] CHAT/MEM/BG/PRIV/OPS requirements mapped to recorded evidence.
- [ ] Remaining failures, limitations, and hardware assumptions documented.
- [ ] Setup works in a fresh environment using declared dependencies and chosen Python version.
- [ ] Example configuration contains placeholders only and explains which services receive data.
- [ ] Fictional demo covers restart/recall, correction/deletion, and local-model outage.
- [ ] No personal runtime data, secrets, or private exports included; inspect tracked files before publishing.
- [ ] License decision recorded; bundled/downloaded model and dependency obligations checked.
- [ ] README documents actual launch command, configuration, troubleshooting, architecture, and verified features.
- [ ] Tests and chosen automated CI checks pass.
- [ ] Owner can explain modules, data flow, eventual consistency, deletion races, and tradeoffs.
- [ ] Publication destination/visibility confirmed; release tag and notes match evidence.

## Evidence record template

Date / block / requirement IDs / implementation revision / command or manual steps / fictional fixture version / provider and model / expected result / actual result / measurements / limitations / pass-fail.

## 2026-11-22 release

See [Goal 1 release evidence](GOAL_1_RELEASE.md) for the executed software checks and remaining owner assessment.
