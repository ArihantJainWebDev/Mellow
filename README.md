# Mellow — rebuild workspace

Mellow is a personal companion intended to live alongside its owner in their operating system, understand their preferences and experiences, and eventually support voice, visual context, and useful actions.

This folder starts a new implementation. The original Mellow-Gyrlick project remains a read-only learning reference. B01 and the B02 one-message Groq path are implemented. Fourteen offline tests pass on Python 3.14.7 / Windows. One real Groq response is confirmed by a user-provided terminal transcript on 2026-11-12; model identity, latency, and token usage were not independently measured. B03 streaming is implemented and live-verified (2026-11-19); B04 adds a personality prompt and recent session context. Persistent history and memory remain planned. See [streaming walkthrough](docs/STREAMING.md).

## Run the foundation

In PowerShell, from this folder:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install --editable .
.\.venv\Scripts\python.exe -m mellow --help
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

The environment/install steps are already completed on this laptop. Use the explicit environment Python path; activation is optional. The `py` launcher reported no installations here, so setup uses the working `python` command.

Configuration is read from `.env` in the current working directory, or an explicit `--env-file PATH`; the loader does not search parent directories. Existing environment variables override file values, including intentionally empty values. Variable interpolation is disabled. Copy `.env.example` to `.env` and enter your own key and a selected Groq model when Block B02 begins. Do not paste keys into chat or commit `.env`.

```powershell
.\.venv\Scripts\python.exe -m mellow --check-config
```

Without settings, this prints an actionable configuration error and exits with code 2. With both required values, it reports configuration present and exits 0. The `--check-config` option is not an API connectivity or credential-validity check. Without that option, Mellow streams replies in a continuous session. Groq receives the personality prompt, up to four completed exchanges, and the current message. Interactive sessions save to the ignored local `data/history.db` file, relative to the working directory. Use `--private` to chat without saving locally. Each launch begins a fresh session. Use `/sessions` to list saved chats, then `/resume ID` to load the latest four completed exchanges and continue saving to that session. Groq still receives chat requests in private mode. Ctrl+C during an answer cancels that turn. At the input prompt, Ctrl+C or EOF exits. In interactive mode, use `/help` to list commands, `/clear` to reset recent context, or `/quit` to exit. Commands stay local. Use `--once` for the original single-request mode. See [the B02 walkthrough](docs/GROQ_REQUEST.md) for local setup and live verification. No `.env` containing real credentials was created during B01.

After environment activation, the installed `mellow` command invokes the same entry point. A fully pinned release environment remains future work; the current dependency range installed python-dotenv 1.2.4. Python >=3.11 is declared; only 3.14.7 has been verified so far, and future inference dependencies may change the supported range.

## Start here

1. [Project charter](docs/PROJECT_CHARTER.md): purpose, scope, and learning agreement.
2. [Software requirements](docs/SRS.md): Goal 1 behavior and acceptance criteria.
3. [Architecture](docs/ARCHITECTURE.md): proposed components, data flow, and boundaries.
4. [Goal 1 roadmap](docs/GOAL_1_PLAN.md): phases, learning blocks, and stopping points.
5. [Verification and release](docs/VERIFICATION.md): evidence required before publication.
6. [Progress tracker](docs/PROGRESS.md): current state and next session.
7. [Decision log](docs/DECISIONS.md): accepted decisions and unresolved choices.
8. Local learning report card (kept outside Git): chapter coverage, demonstrated understanding, quiz evidence, and revisit points.

## Documentation rules

- Requirements describe intended behavior; they do not claim implementation.
- Architecture details marked proposed must be reviewed during their block.
- Mark work verified only with recorded evidence.
- When scope changes, update the requirement, decision log, and roadmap together.
- Do not put credentials, personal conversations, recordings, or memory exports in these documents.

Planning baseline: 2026-10-27. Learning agreement updated: 2026-11-10 (Asia/Kolkata). The assistant implements and verifies code; the owner learns through concise walkthroughs and brief assessments. The next implementation session is not scheduled.

## Check local memory extraction

Start LM Studio's server bound to 127.0.0.1, load a chat model that supports
structured output, and run `python -m mellow.providers.lmstudio MODEL_ID` in the
activated environment. The command sends a fictional example to localhost port
1234 and prints validated fact and episode candidates. Use `--port PORT` for a
different local port. This standalone check expects local API authentication to
be off. Schema validation checks structure and bounds. It cannot verify that
the model's claims follow from the input.

Run the three fictional memory checks with
`python -m mellow.memory_benchmark MODEL_ID` after loading the model in LM Studio.
The runner prints the candidates and elapsed time for each prompt. Its keyword
rubric is a first pass. Inspect the candidates for meaning and flag valid
paraphrases that the rubric misses. For the hypothetical, the expected result
is no saved candidate. See the progress log for live results.

### Background memory extraction

With LM Studio running on localhost port 1234 and a chat model loaded, run
`python -m mellow --memory-model qwen3-vl-4b-instruct` (use your loaded model ID).
This opts the saved session into background extraction. Use `/jobs` to inspect
local queue counts. A job sends the user's message from a completed turn to
the local model. Candidates are kept in SQLite for later review and retrieval.
Private and single-request modes do not enqueue jobs.

The queue admits up to 100 active jobs. A full queue leaves chat history intact
and reports that the turn was skipped for extraction. Failed jobs retry up to
three total attempts. Pending work can resume on a later run with
`--memory-model`. Interrupted claims can be recovered after their two-minute
lease expires. Exiting stops the worker from taking more jobs and waits up
to one second for it to finish. A request already sent to LM Studio can keep
running independently of Mellow.
