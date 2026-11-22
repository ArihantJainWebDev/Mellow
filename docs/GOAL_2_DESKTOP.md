# Goal 2 desktop acceptance

Run `python -m mellow.avatar` from the same working folder as the CLI. Windows PowerShell, WPF and the project's installed Python environment are required. The first launch compiles the bundled C# files. The ghost rests on the primary monitor. Find **Mellow desktop** in the Windows notification area (possibly under the hidden-icons arrow).

| Check | Expected behavior |
|---|---|
| Keep typing in another app | The ghost blinks, walks and changes pose without activating a window. Ordinary mouse input passes through it. |
| Ctrl+Alt+M, or double-click the tray icon | Open the intentionally focusable chat. Type a question and press Enter. The ghost attends while you type, shows a thought cloud while preparing a reply, and displays a short speech caption as Groq streams. Full text stays in chat. |
| Continuity | Send a fictional code word from desktop chat. Exit Mellow from the tray, run the CLI in the same folder, and ask for the word. Both interfaces use the same active session and database. |
| Tray → Move ghost | Drag the ghost once to place it. The move ends on release and input passes through again. Choose a size and restart to check placement. |
| Quiet and hide | Quiet suppresses captions. Hide removes the avatar surface. Use Exit Mellow for full shutdown. |
| Preview a pose | Preview listen, think, point, offer, speak, rest and privacy without activating any sensors. A preview caption identifies a demonstration. |
| Launch at sign-in | Explicitly toggle it in the tray. It registers the current interpreter and working folder for the current Windows user. Toggle again to remove it. |
| Close chat | The chat panel hides and the ghost stays present. Exit from the tray to stop the desktop and its chat worker. |

## Implementation

Python launches the renderer. Ghost.cs owns the approved mesh, joints, gaze and walk cycle. Desktop.cs owns controls, placement, caption layout and the chat panel. Each desktop instance has a local child process running the same CLI conversation loop. Base64-framed UTF-8 lines on anonymous pipes carry user input, terminal output and turn events. There is no chat server or second history store. Input attention means typing; audio, capture, reminders and camera perception are later goals. Fixed-size previews truncate captions while preserving full replies in chat. The shortcut can be unavailable if another program owns it; the tray still opens chat.

Review the [actual motion capture](visuals/goal2-motion.gif) alongside the [approved visual references](visuals/README.md). The capture compares light and dark backgrounds using synthetic poses. The smoke check verifies Windows passive-window styles and samples foreground-window identity. The explicit chat check sends fictional input to Groq from a temporary directory. Personal assessment of movement, timing, dragging and appearance remains with the owner.

## Recorded checks

A clean install built the 0.2.0rc1 wheel and passed 49 tests. A Windows smoke run reported 56 frames, passive window styles and preserved foreground focus. A separate real Groq reply streamed through the desktop chat into a temporary SQLite session and reopened through the CLI history layer. The visual comparison capture contains 60 frames. Physical dragging, keyboard shortcut availability, startup at actual sign-in, and personal visual acceptance remain manual checks.
