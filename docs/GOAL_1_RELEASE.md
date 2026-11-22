# Goal 1 release evidence

Windows, 22 November 2026. All project checks used fictional inputs and isolated databases. No personal history was used for testing.

| Area | Evidence |
|---|---|
| CHAT / continuity | Offline streaming, error, cancellation, resume and fresh-start tests. Actual configured Groq model openai/gpt-oss-20b answered five fictional requests in the live CLI demo. |
| MEM / retrieval | Qwen and Nomic loaded in LM Studio. A new conversation recalled the correct bicycle color. After a sale a fresh conversation answered “No.” Forgetting removed the memory and the demo checked recent and older context in every fixture session. A separate live worker automatically retained “I collect vintage fountain pens.” |
| Extraction quality | Combined fact/episode, greeting and conditional fixtures passed 3/3. The first run exposed a conditional incorrectly proposed as an episode. Leading hypothetical cues are now filtered before extraction. This is a small regression benchmark. |
| BG / reliability | Tests exercise full queues, lease recovery, retry limits, stale results, local provider failures and keyword fallback. Direct low-risk facts commit with the completed turn. Other local jobs execute asynchronously. |
| PRIV / storage | Tests inspect exports, deletion, private isolation, forgetting during extraction, per-session deletion, and reset. Forgetting is lexical and source-based. Read the data-control boundaries in the README. |
| OPS / measurements | Five live requests used 780 input and 610 output tokens total. First visible text from CLI invocation took 0.305–0.918 seconds in this sample. At the published uncached rates of $0.075 and $0.30 per million tokens this is about $0.00024 before any plan allowances. Actual billing was not inspected. [Groq model pricing](https://console.groq.com/docs/model/openai/gpt-oss-20b) |
| Distribution | Fresh Windows virtual environment, wheel installation, offline suite, GitHub Actions matrix. The only external Python runtime dependency is python-dotenv. Model files and LM Studio are separately installed. Project code uses MIT. |
 
## Walkthrough

Configuration selects providers. The CLI reads visible completed turns from SQLite and builds a request with a recap and retrieved memories. Groq streams text. A successful saved turn may produce a literal memory immediately or a durable local job. The worker validates model output and can run a second eligibility check before saving the original wording. History transactions own state changes. A lease prevents stale workers committing after deletion. The CLI can resume chat while local jobs are outstanding.

## Known limits

Automatic recognition and conflict resolution vary with phrasing. Corrections for arbitrary records use the explicit natural-language form in the README. Forgetting can suppress later unrelated context from an affected session. Synonyms outside the documented lexical matching can require additional removal. Previously exported copies or data already sent to a provider remain outside local deletion. Local model latency and hosted rates vary. The fixture run is not a general quality or latency guarantee. The owner's hands-on review and learning assessment are separate from software verification.
