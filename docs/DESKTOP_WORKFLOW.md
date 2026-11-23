# Desktop chat, assignments and screen observation

Launch `python -m mellow.avatar` from the project folder. Exit an older avatar through its tray menu before launching the updated one.

## Talk and read

Click Mellow or press **Ctrl+Alt+M**. The translucent composer appears at the top center of the primary work area and takes ordinary Windows keyboard focus. **Enter** sends, **Shift+Enter** inserts a newline and **Esc** hides the composer. The composer stays open during ordinary chat. Dismissing it returns focus to the previous app. The chat worker is shared with the CLI.

The cloud tracks the ghost and stays inside the primary work area. It measures pages with the actual WPF font and wrap width. Incoming text is revealed progressively. **Reveal** finishes the current page. Pages advance after a reading pause. **Next** and **Back** show the destination page immediately. Bold, italic and inline code use styled text. Speaking gestures settle while a completed page waits. **Copy** copies the reply received so far. **Close** dismisses the cloud, including later chunks from the same answer. Sending a new message replaces the cloud.

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
| `/screen watch` | Shows the Recent/Stop badge. Window changes are checked every second. After a window settles, its visible screen area can be sampled locally; subsequent checks run at roughly 12-second intervals, with one inference at a time. |
| `/screen` | Shows the latest description and its capture time or the current availability. |
| `/screen ask What error is visible?` | Requests a fresh local image answer while observation is on. |
| `/screen stop` | Stops new captures and clears the cached description. Late results are discarded. The badge also has a Stop button. |
| `/open https://example.com`, `/search compiler docs` | Opens an HTTPS address in the default browser or sends the search terms to Google. |
| `/browser back`, `/browser forward`, `/browser refresh` | Requests navigation from the foreground supported browser. Open the composer with the browser focused. |
| `/open notepad`, `/open calculator` | Launches the named Windows application. |
| `/point`, `/close-window` | Uses the existing hover-to-select flow. Window closure requires confirmation. |
| `/desktop` | Lists desktop commands. |

You can also type “Start watching my screen”, “Look at my screen: which error is visible?”, “Stop watching my screen”, “Open calculator”, “Search the web for compiler docs”, “Go back in my browser”, or “Refresh this tab”.

Awareness starts off on first use. After enabling it once, it resumes on ordinary relaunch. Stop persists across restarts and clears the temporary activity trail. The focus check pauses new captures for the composer and password fields or unknown focus reported by Windows accessibility. It cannot identify sensitive content elsewhere on the screen. Use **Stop** before opening confidential material. Locking the Windows session stops the watcher. Raw images travel in memory to the local model. Screen descriptions stay out of Groq chat, SQLite, and exports. The screen result cannot trigger actions. Navigation, searches and app launches require an explicit command. The browser and LM Studio have their own settings and histories.

## A project workspace that remembers its windows

Right-click the ghost and choose **Enable desktop awareness** once. While idle, Mellow glances toward the active window or a recently moved pointer inside it. The **Recent** badge button shows the last five observed windows. The local trail contains at most 48 transitions and is cleared on Stop or exit.

1. Spend a couple of seconds in each of three ordinary windows, for example Opera, VS Code and a chat. Press Ctrl+Alt+M over the last one and ask **What am I doing?** The foreground caption and other open apps are labeled separately. Local vision adds a dated description when available.
2. Say **remember my setup**. This explicitly saves up to three recently visited, still-open windows by application and title. Exact titles are personal data in the local SQLite export. A new save replaces the previous one; database reset removes it.
3. Exit and restart Mellow from the same folder. Minimize one of those windows, then say **restore my setup**. Review the list and choose **Restore setup** or **Skip**. Only uniquely matching live windows are eligible. Closed windows, changed tab titles and ambiguous matches are reported. This version does not relaunch missing apps or reconstruct URLs.
4. For an untrained example, visit windows A, B, C, A, B, C, A with stable titles, dwelling long enough to be observed. While Mellow is idle he can offer that setup. If a reply is already on screen, the badge shows **Workspace?** so you can inspect the offer without interrupting it. No window moves until you confirm. This narrow recognition rule operates within the current observation session.

**Take me back** returns to the preceding eligible window. **Take me back to VS Code** selects the most recent matching editor window. Mellow rechecks its identity before asking Windows to focus it. **Where was I?** brings back the recent trail without sending titles to Groq. A minimized app can appear under other open apps; its presence alone does not establish that it was used. Available titles and password-field detection depend on Windows accessibility. Use Stop when a window must be excluded.

## Repeat the checks

```powershell
python -m unittest discover -s tests -q
python -m mellow.avatar -VerifyInteraction
python -m mellow.avatar -VerifyScreen
```

The first command uses disposable data, including assignments shared across three desktop-worker processes. The second exercises production WPF text measurement and reply state offscreen. The last sends a generated quiz card to the installed local model. No personal screenshot is needed for any of these checks. For the final hands-on check, type in an editor, summon Mellow, submit a long reply, use Next and Back, and check that typing returns to the editor. Try browser navigation on two ordinary pages and use the badge to stop observation.

### Activity recognition

With desktop awareness enabled, ask “What am I doing?” or “Why do you think that?” Mellow uses foreground titles, recent windows and available local screen descriptions. Ambiguous evidence can yield an uncertain answer. Teach a label with “Actually, that was college work” and remove it with “Forget activity label college work”. Labels and matching terms stay in the local history database. A recognized saved setup or study activity can produce a badge offer after a short dwell. Skip dismisses it. Study starts silently unless you choose With music. Similar offers are limited to once per 30 minutes while Mellow is running.
