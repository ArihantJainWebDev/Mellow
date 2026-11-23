# Desktop chat, assignments and screen observation

Launch `python -m mellow.avatar` from the project folder. Exit an older avatar through its tray menu before launching the updated one.

## Talk and read

Click Mellow or press **Ctrl+Alt+M**. The translucent composer appears at the top center of the primary work area and takes ordinary Windows keyboard focus. **Enter** sends, **Shift+Enter** inserts a newline and **Esc** hides the composer. On send or dismissal Mellow asks Windows to restore the previously focused app. The chat worker is shared with the CLI.

The cloud tracks the ghost and stays inside the primary work area. It measures pages with the actual WPF font and wrap width. Incoming text is revealed progressively. **Reveal** finishes the current page, Pages advance after a reading pause. **Next** and **Back** show the destination page immediately. Bold, italic and inline code use styled text. Speaking gestures settle while a completed page waits. **Copy** copies the reply received so far. **Close** dismisses the cloud, including later chunks from the same answer. Sending a new message replaces the cloud.

## Track assignments

These messages work in desktop chat and the saved CLI session. Try the sequence with a fictional task.

```text
Add assignment Physics lab due tomorrow
Move Physics lab to Friday
What's due this week?
Mark Physics lab done
/tasks done
```

Exit and relaunch from the same folder. Use `/tasks done` to see the saved completion and revised deadline. Use `/tasks` for open items and `/tasks all` for both states. If two open tasks share a title, use the displayed ID, e.g. `Move #2 to 2026-12-04`. Dates are calendar days on the local machine. A named weekday means its next occurrence including today. Weekly results run through Sunday and include overdue work.

Choose **Notice** in the composer, paste the following two lines, then send. The CLI accepts a single line after `/notice`.

```text
Algorithms worksheet due 27 November 2026
Electronics lab due 2026-12-04
```

A two-line subject and deadline also works. A notice such as “Electronics lab deadline extended to 7 December 2026” updates the single matching open task. Mellow reports saved, duplicate and skipped items. Use an explicit date for ambiguous notices. Times of day, arbitrary prose and account syncing are outside this importer. Tasks store title, due day, source and completion state locally. A fingerprint identifies previously imported lines. Reset and export include assignments. These commands bypass Groq, the transcript and memory extraction. Desktop assignments also work without Groq credentials. Private CLI sessions cannot access saved tasks.

## See and act

Start a vision-capable model in LM Studio and enable its local server on port 1234. The default desktop model ID is `qwen3-vl-4b-instruct`. To override it set the `MELLOW_VISION_MODEL` process environment variable before launch.

| In the composer | Effect |
| --- | --- |
| `/screen watch` | Shows a persistent Stop badge and starts sampling the primary display. One local model request can run at a time, on a 12-second timer. |
| `/screen` | Shows the latest description and its capture time or the current availability. |
| `/screen ask What error is visible?` | Requests a fresh local image answer while observation is on. |
| `/screen stop` | Stops new captures and clears the cached description. Late results are discarded. The badge also has a Stop button. |
| `/open https://example.com`, `/search compiler docs` | Opens an HTTPS address in the default browser or sends the search terms to Google. |
| `/browser back`, `/browser forward`, `/browser refresh` | Requests navigation from the foreground supported browser. Open the composer with the browser focused. |
| `/open notepad`, `/open calculator` | Launches the named Windows application. |
| `/point`, `/close-window` | Uses the existing hover-to-select flow. Window closure requires confirmation. |
| `/desktop` | Lists desktop commands. |

You can also type “Start watching my screen”, “Look at my screen: which error is visible?”, “Stop watching my screen”, “Open calculator”, “Search the web for compiler docs”, “Go back in my browser”, or “Refresh this tab”.

Screen observation starts off on every launch. The focus check pauses new captures for the composer and password fields or unknown focus reported by Windows accessibility. It cannot identify sensitive content elsewhere on the screen. Use **Stop** before opening confidential material. Locking the Windows session stops the watcher. Raw images travel in memory to the local model. Screen descriptions stay out of Groq chat, SQLite, and exports. The screen result cannot trigger actions. Navigation, searches and app launches require an explicit command. The browser and LM Studio have their own settings and histories.

## Repeat the checks

```powershell
python -m unittest discover -s tests -q
python -m mellow.avatar -VerifyInteraction
python -m mellow.avatar -VerifyScreen
```

The first command uses disposable data, including assignments shared across three desktop-worker processes. The second exercises production WPF text measurement and reply state offscreen. The last sends a generated quiz card to the installed local model. No personal screenshot is needed for any of these checks. For the final hands-on check, type in an editor, summon Mellow, submit a long reply, use Next and Back, and check that typing returns to the editor. Try browser navigation on two ordinary pages and use the badge to stop observation.
