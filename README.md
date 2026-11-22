# Mellow

A Windows-first personal companion. The current release is a streaming, memory-aware CLI. The [roadmap](docs/ROADMAP.md) covers the future desktop ghost, voice, vision and assistant integrations.

## Start

Requires Python 3.11 or newer, a Groq API key and a Groq chat model available to your account. Tested locally on Windows with Python 3.14. From the checkout in PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
Copy-Item .env.example .env
```

Enter your credentials locally. Run `.\.venv\Scripts\python.exe -m mellow` from the same folder on each launch. Mellow continues your saved conversation. Use `/new` for a fresh chat, `/resume ID` to switch, or `--private` for a session without local persistence or personal memory. Groq receives chat requests in all three modes. Recent exchanges, a bounded older recap and relevant saved memories can be included in saved mode.

## Personal memory

Set MELLOW_MEMORY_MODEL to your Qwen model ID to enable extraction and direct fact saving. Set MELLOW_EMBEDDING_MODEL to your Nomic model ID for semantic recall. Load the models and enable the loopback API in LM Studio. Flags override environment variables, which override .env. Extraction jobs retry locally. Embedding outages fall back to keyword matching after a bounded request timeout.

Straightforward supported statements such as “I own a red bicycle”, “I sold my red bicycle”, “I prefer short answers” and “My favorite book is Dune” are recorded directly from your words when a reply completes. The narrow automatic rules live in [memory_policy.py](src/mellow/memory_policy.py). Other statements go through background local extraction. For a single eligible first-person statement, a second local classification can save the original sentence automatically. Uncertain suggestions remain available for review. Model classifications can be wrong. “Remember that I study pottery” explicitly saves a custom fact locally. For an arbitrary saved fact, say “Change my memory from OLD to NEW” using its original wording. Review uncertain suggestions with /candidates. Use /memories to inspect records and source turn IDs.

## Data controls

- “Forget the bike” uses literal terms with a bike/bicycle alias. It suppresses matching exchanges and later turns in affected sessions from future conversation context, removes matching memories and context-dependent suggestions from affected turns, and invalidates their queued work. Later unrelated conversation context in those sessions can be suppressed too. It cannot identify every paraphrase or reference in another chat. Inspect /memories for additional records.
- /forget ID removes an individual memory and suppresses its source and following context. Source transcripts are still available in exports. /delete-session ID CONFIRM deletes the entire selected transcript and linked data. /reset CONFIRM deletes all application records in the current database.
- /export PATH creates a local JSON snapshot including raw conversation history. Choose a private destination outside the repository. Export files, backups, provider copies and already-displayed terminal output have independent lifetimes. SQLite record deletion does not guarantee forensic erasure.
- /jobs, /retry-job TURN_ID and /summary expose diagnostics. /help lists commands. SQLite is stored in data/history.db relative to the working directory. Keep this directory and .env private.

## Verification

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
.\.venv\Scripts\python.exe tools/demo_goal1.py
.\.venv\Scripts\python.exe -m mellow.memory_benchmark qwen3-vl-4b-instruct
```

The demo uses a temporary fictional database and your configured Groq and local models. It incurs hosted token usage. See [release evidence](docs/GOAL_1_RELEASE.md), [architecture](docs/ARCHITECTURE.md), and [visual references](docs/visuals/README.md).

Project code uses the [MIT license](LICENSE). Model weights, LM Studio, Groq and installed dependencies are separately distributed under their own terms.

## Desktop ghost preview (Windows)

Run `python -m mellow.avatar` in a dedicated terminal. The click-through 3D ghost rests above the primary taskbar, blinks and walks after eight seconds. Use Ctrl+C in the launching terminal to exit. Windows PowerShell loads the bundled WPF mesh and animation code. This prototype runs separately from chat. [Actual desktop-size render](docs/visuals/prototype-idle.png).
