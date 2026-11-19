# B01 — foundation walkthrough and evidence

Date: 2026-11-10. Software checks passed; owner learning assessment pending.

## File and function map

| File | Responsibility / connection |
|---|---|
| `pyproject.toml` | Package name/version, Python constraint, dependency, setuptools src discovery, and `mellow` command pointing to `mellow.__main__:main` |
| `.gitignore` | Excludes `.env`, virtual environment, generated metadata, and anticipated personal runtime data; it is not encryption or a publication audit |
| `.env.example` | Blank Groq key/model template; not a live credentials file |
| `src/mellow/__init__.py` | Identifies/imports the package without startup side effects |
| `src/mellow/__main__.py` | `main()` parses `--env-file`, calls `load_settings()`, displays safe status, and returns 0 or 2; `SystemExit` returns that code to the shell |
| `src/mellow/config.py` | `load_settings()` reads the selected dotenv file and environment, validates key/model presence; `Settings` holds immutable values and hides the key from its generated repr; `ConfigurationError` reports a correctable problem |
| `tests/test_foundation.py` | `FoundationTests` uses fictional data to check precedence, isolation, missing settings, invalid encoding/whitespace, CLI statuses, and no secret output |
| `.venv/` (generated) | Separate Python environment for this project; not source code or a copied model |

`repr=False` reduces accidental exposure in the generated object representation; it is not encryption and does not prevent deliberate access. Configuration validation checks presence/format, not whether the key or model is accepted by Groq.

## Flow

```text
python -m mellow OR installed mellow command
    -> __main__.main()
    -> parse arguments
    -> config.load_settings(selected file)
    -> file values merged with environment values (environment wins)
    -> validate -> immutable Settings
    -> safe success status and exit 0
       OR ConfigurationError -> stderr and exit 2
```

No Groq client, chat loop, history, retrieval, or worker exists yet. The next block will use the Settings returned here. Default `.env` is relative to the command's working directory; use `--env-file` to be explicit elsewhere. Missing file is allowed when the environment supplies both settings. Blank environment values take precedence and fail validation rather than silently using a different file credential. The parser reads UTF-8/BOM and disables variable interpolation; it does not modify the process environment.

Legacy reference: `D:/HEHE/Mellow-Gyrlick/config/settings.py` loads dotenv and creates directories on import, while `validate_config()` always reports success. The new loader runs explicitly from the entry point and fails usefully without creating runtime storage.

## Verification

Verified Windows / Python 3.14.7; Git version available: 2.55.0.windows.5; python-dotenv installed: 1.2.4. No Git initialization, real .env creation, API request, or model download performed.

From project root:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
.\.venv\Scripts\python.exe -m pip check
.\.venv\Scripts\python.exe -m mellow --help
.\.venv\Scripts\mellow.exe --help
```

Result: 8 tests passed; dependency check clean; both help entry points work. Separate subprocess checks passed with isolated fictional environment: missing key -> exit 2; configured startup from another directory -> exit 0; fictional key absent from stdout/stderr. These smoke checks used no network. Live Groq access remains unverified.

Initial test run failed because clearing all process variables before creating temporary files removed Windows temporary-directory discovery settings. Resolving the temporary directory before isolating test configuration fixed the setup; all eight tests were rerun. This was a test fixture issue, not a provider failure.

Sources consulted for implementation: [python-dotenv](https://pypi.org/project/python-dotenv/), [setuptools packaging configuration](https://setuptools.pypa.io/en/latest/userguide/pyproject_config.html). Full dependency locking and clean-machine release validation remain future work.

## Short understanding check — unanswered

1. Trace how `python -m mellow` reaches configuration validation: which files/functions participate, and what does each do?
2. Why keep the key in an ignored `.env` rather than tracked code? If `.env` and the environment have different values, which is used? What happens if the required value is missing?

Record answers in the report card only after they are supplied. Do not assign scores from assistant-generated explanations or passing software tests.
