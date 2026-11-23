# Goal 2 desktop acceptance

Run `python -m mellow.avatar` from the same working folder as the CLI. Windows PowerShell, WPF and the project's installed Python environment are required. The first launch compiles the bundled C# files. The ghost rests on the primary monitor. Find **Mellow desktop** in the Windows notification area (possibly under the hidden-icons arrow).

| Check | Expected behavior |
|---|---|
| Keep typing in another app | The ghost blinks, walks and changes pose without activating a window. The painted ghost accepts clicks and pauses its walk when hovered. Click once for chat or right-click for controls. Full click-through is optional and can be reversed from the tray. |
| Ctrl+Alt+M, or double-click the tray icon | Open the top-center composer. Enter sends and Esc dismisses. Read progressive pages in the cloud, use Next and Back, or copy the received reply. |
| Continuity | Send a fictional code word from desktop chat. Exit Mellow from the tray, run the CLI in the same folder, and ask for the word. Both interfaces use the same active session and database. |
| Tray → Move ghost | Drag the ghost once to place it. The move ends on release and the prior click mode is restored. Choose a size and restart to check placement. |
| Quiet and hide | Quiet suppresses captions. Hide removes the avatar surface. Use Exit Mellow for full shutdown. |
| Preview a pose | Preview listen, think, point, offer, speak, rest and privacy without activating any sensors. A preview caption identifies a demonstration. |
| Launch at sign-in | Explicitly toggle it in the tray. It registers the current interpreter and working folder for the current Windows user. Toggle again to remove it. |
| Close chat | The chat panel hides and the ghost stays present. Exit from the tray to stop the desktop and its chat worker. |

## Implementation

Python launches the renderer. Ghost.cs owns the approved mesh, joints, gaze and walk cycle. Desktop.cs owns controls, placement, caption layout and the chat panel. Each desktop instance has a local child process running the same CLI conversation loop. Base64-framed UTF-8 lines on anonymous pipes carry user input, terminal output and turn events. There is no chat server or second history store. Input attention uses typing cues. The optional local screen watcher and assignment commands are documented in the [desktop workflow](DESKTOP_WORKFLOW.md). The reply reader measures each page and preserves all received text. The shortcut can be unavailable if another program owns it; the tray still opens chat.

Review the [actual motion capture](visuals/goal2-motion.gif) alongside the [approved visual references](visuals/README.md). The capture compares light and dark backgrounds using synthetic poses. The smoke check verifies Windows passive-window styles and samples foreground-window identity. The explicit chat check sends fictional input to Groq from a temporary directory. Personal assessment of movement, timing, dragging and appearance remains with the owner.

## Recorded checks

A clean install built the 0.2.0rc1 wheel and passed 49 tests. A Windows smoke run reported 56 frames, passive window styles and preserved foreground focus. A separate real Groq reply streamed through the desktop chat into a temporary SQLite session and reopened through the CLI history layer. The visual comparison capture contains 60 frames. Physical dragging, keyboard shortcut availability, startup at actual sign-in, and personal visual acceptance remain manual checks.

## Screen movement and attention

Right-click **Climb to the top edge**. Mellow walks to the nearer side of the primary screen. His mittens alternate fixed holds as his body rises. At the corner one hand keeps the wall while the other hooks over the top. He takes his weight on one arm, reaches back up, travels sideways along the rim, then opens his umbrella and returns to the floor. **Leave ledge** releases the grip early. The regular roaming route uses the same sequence. [Watch the desktop renderer perform the climb](visuals/edge-climb.gif). Its head, shoulder angles and mouth blend toward each pose over several frames. Activity outside chat uses a Windows last-input timestamp and a stationary pointer as an approximate typing cue. It reads no characters. To point, hover over an accessible button or hyperlink and press Ctrl+Alt+P, or use the delayed menu action. Only the target type and bounds are used locally. Unexposed controls cannot be identified this way.

Use the [character behavior brief](CHARACTER_BEHAVIOR.md) to review timing, body language and how Mellow interacts with the desktop.

Right-click **Walk along the floor** to replay a short walk. On level travel each foot alternately stays on a fixed desktop point while the other swings forward. The hem rises over the swinging shoe and Mellow settles the current step when hovering or typing pauses a walk. A guided journey with a height change separates horizontal travel from the elevation change. The umbrella descent keeps its own motion. [Watch the rendered walk in both directions](visuals/grounded-walk.gif).

The Windows renderer check exercises left and right climbs at 82, 116 and 156 display units. It temporarily colors each mitten separately to measure visible contact on the real overlay, checks whether a planted grip slides and follows the route through landing. To reproduce from the project root, run `powershell -NoProfile -STA -ExecutionPolicy Bypass -File src/mellow/desktop/avatar.ps1 -Verify`. The optional `-Preview PATH-climb.gif` records the sequence. The check briefly displays the avatar and disposable test windows.

### Offscreen visual review

[Desktop poses and captions](visuals/cloth-poses.png) are rendered from the same camera and model as the overlay. The hat ghost's two sleeves share their shoulder boundary positions with the animated sheet. The sleeves taper into the hands in the same mesh during everyday gestures, prop handling and climbing. Speech animates the smile, gaze follows the selected gesture, and the open umbrella pivots around the hand on its shaft.

Run these checks from the project directory. Both write to the supplied preview path without opening a desktop avatar. The pose sheet checks shoulder seams, finite mesh positions, prop contact and caption layout at the supported model sizes. The climb recording checks contact throughout both directions of travel. The interactive verification above also covers real Windows focus and window behavior.

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File src/mellow/desktop/avatar.ps1 -PoseSheet -Preview poses.png
powershell -NoProfile -STA -ExecutionPolicy Bypass -File src/mellow/desktop/avatar.ps1 -Offscreen -Preview sample-climb.gif
```

## Desktop signals and one-shot vision

The desktop reads primary-display taskbar and selected window rectangles. Right-click **Perch on a window**, then hover over a restored application with room above its title bar. Mellow approaches a side from the floor, reaches the lower frame, climbs the side, pulls across the corner and walks along the title bar. He follows a moved ledge. Closing, minimizing or covering the support, switching to another application window, or choosing **Leave ledge** starts a descent to the current floor. Typing or hovering during this recovery still lets him land. Revealing the bottom taskbar while the ghost stands there triggers a spring reaction. A local familiarity counter softens later encounters. Top-edge travel can lead to a hang and an umbrella descent. The accessibility guide refreshes a supported button or hyperlink's bounds while pointing. Hiding the control releases the target.

**Inspect selected region locally** opens a drag selector for an error, product comparison or visible checklist. Esc cancels. One screenshot of the chosen region goes directly to LM Studio on loopback. The application keeps the image in memory. The model returns a short description and optional approximate coordinates. Results describe the moment of selection. Start LM Studio and load Qwen3-VL first. Set the process environment variable MELLOW_VISION_MODEL to choose another installed vision model. Chat remains separately hosted on Groq.

**Open project checkpoint** reads the first unfinished Markdown checkbox in a file you select. **Watch a progress bar** uses an accessible numeric range; reaching 100% alone cannot establish a successful build. **Close a window** works only for a confirmed target exposing a supported Close button. Mellow requests normal OS closure after reaching the target and leaves save dialogs to the owner. **Dismiss / stop watching** clears attention to the current target.

### Verification and remaining dependencies

The Windows renderer and deterministic climb check ran successfully. The offline suite passed 51 tests. A real loopback Qwen3-VL request identified “missing file demo.py” in a generated error screenshot. This verifies content reading on that fixture; the reported center coordinates do not demonstrate word-level localization. Windows verification exercises both frame edges with disposable windows, moves one, closes it, covers another, and minimizes one mid-climb. The screen selector, taskbar timing, window-close interaction, progress watching and behavior in individual applications still benefit from hands-on checks.

The behavior brief remains a creative target. Ambient mic/camera, automatic semantic monitoring, mail and portal accounts, reliable build success/failure events, project-wide checkpoint inference, persistent learned rituals, physically solved contact and collision, and fully authored falls, sitting, recovery and umbrella folding are outstanding. The current gestures and timing are procedural. The preview privacy pose is a gesture; the selector captures only on explicit release.
