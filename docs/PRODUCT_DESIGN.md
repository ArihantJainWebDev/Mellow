# Mellow as a desktop companion

Owner review draft · November 23, 2026

## What it should feel like

You open your laptop and carry on with your day. While you discuss Mellow in an AI chat, he can tell what you are working on from the visible exchange. He knows a game is open in the background. When you bring it forward, he changes his understanding of what you are doing. When you return to the project, the conversation and the work still have continuity. You should rarely need to narrate what is already apparent or administer a database of facts.

The first user is Arihant. An open-source installation should let someone else develop a different relationship with their own Mellow. The code, fictional demos and models can be shared independently of either person's history. The long-term ambition is a companion familiar with your thoughts, emotions, habits and dreams through what you share and how you interact. You can always tell him he has misunderstood.

## Everyday experiences and how to tell they work

| Experience | Target behavior | Acceptance scene |
| --- | --- | --- |
| Being understood | Connect visible evidence with conversation, project context and running apps. Keep track of what is current and how it was learned. | Switch from this chat to a background game mid-analysis. A late image result cannot cause him to describe the old chat as current. With the conversation hidden, he can say the project is uncertain. |
| One continuing relationship | Resume naturally after restarting. Recall relevant episodes, preferences and unfinished work. Learn clear facts without an approval queue becoming the everyday interface. | Tell him about a project, correct a preference a day later, then ask where you left off. Inspect the source, forget it and verify it stays out of future recall. |
| A character living on the desktop | Keep the small ghost, expressive eyes, soft pointed hands, tiny feet, animated cloth and removable hat. Give him physical contact with screen edges, windows and the taskbar. Use gaze and movement to express attention; clouds carry short, readable dialogue. | With bubbles muted, you can tell which object he noticed. Move or close his supporting window and he reacts. If the link he is pointing at disappears, he lowers his hand. Detailed art and behavior references live in [character direction](CHARACTER_BEHAVIOR.md) and [visuals](visuals/README.md). |
| Initiative that fits the moment | Choose between carrying on quietly, offering help and acting within an enabled routine. Adjust from corrections and repeated feedback. | While shopping, he can offer a relevant comparison. While you read, he can move aside. Dismiss an offer and he lets you continue. |
| Music for studying | Once the routine is enabled, recognize studying, play YouTube Music, and adapt selections to your tastes without a separate command for every song. | Use a study session and a visually similar casual browsing session. Check when music starts, whether an intentional pause holds, and whether repeated skips influence the next session. |
| Work and commitments | Connect assignments, quizzes, changed deadlines, outstanding work and project checkpoints. Accept pasted notices first, then authorized mail.com and Pod.ai updates. | A revised notice moves one quiz to Monday. Mellow can explain what changed and show its source after a restart. An unavailable account shows the age of its last successful check. |
| Acting across apps | Use the browser, WhatsApp and other desktop apps in service of a user goal. Draft Reddit, LinkedIn and X posts. Later, accept requests from a paired phone. | Trace the requested action to the right app and target. A WhatsApp draft can be inspected; sending has its own authorization. A retried phone request cannot send twice. |
| Voice, expression and presence | Eventually listen through the laptop mic, converse aloud and use optional camera cues to respond more considerately. | Pause each sensor independently. If an expression is ambiguous, Mellow asks how you feel. Privacy takes effect before the ghost performs his turn-away animation. |
| Seeing what he learned | Make a control center where you can inspect what he noticed, learned, revised or forgot, with a live 3D flow as the visual direction to prototype. | Follow a fictional preference from source through correction to forgetting. You can explain the change from the record and correct it yourself. |

## How the parts connect

1. Windows activity events provide app identity, focus changes and running-app presence. Local vision adds meaning when the screen changes. Each observation carries its source, time and the window it describes.
2. A short-lived activity record ties those observations together. Chat, gaze and routines consult the same record. Switching apps invalidates stale results. A running game alone is weak evidence that someone is playing.
3. Personal memory keeps selected facts and episodes with their origins. Ordinary-language corrections and forgetting apply to retrieval and learned routines. The lifetime of desktop observations is an owner decision below.
4. A routine decides whether to stay quiet, offer something or request an action. App-specific controls carry out allowed actions and check the outcome. Screen text is input to interpret; instructions seen inside a webpage cannot grant permissions.
5. Keep responsive hosted chat separate from background local processing. Start with Groq, LM Studio and SQLite. Use retrieval for relevant past context. Learning a music preference can be a local preference update. Consider fine-tuning only if measured examples reveal a behavior problem that simpler methods do not resolve.

## Build sequence and stopping points

The five release goals remain useful groupings. The order below reflects the owner's revised priorities; blocks can span goals. Calendar time is flexible.

| Block | Deliverable | Evidence before advancing |
| --- | --- | --- |
| 0. Agree the design | Review this document, resolve everyday autonomy and memory choices, record decisions. | Owner recognizes the scenarios and agrees with the first milestone. |
| 1. Understand the desktop | Make the AI-chat, project and background-game scenario work across app changes. Start with answers on request. | Run repeatable scenes with a game open, foregrounded, minimized and closed. Include ambiguous content, unavailable vision and a switch during inference. Record correct and stale answers, update delay and resource use. |
| 2. Learn from experience | Connect current context to correctable preferences and episodes. Give the owner an inspectable account of why behavior changed. | Verify learning, corrections, forgetting and restart on fictional sessions. |
| 3. Take useful initiative | Deliver study music and a learned project setup as the first two routines. Add a quiet response and a timely offer in other activities. | Measure unwanted starts and interruptions as well as useful actions. Verify manual override, playback failure handling and one confirmation per proposed project setup. |
| 4. Expand useful work | Extend verified actions to browser and apps, then mail, college updates, social drafting and WhatsApp. Grow the control center alongside the underlying records. | Demonstrate one complete user outcome per integration, including offline, misunderstanding and retry cases. |
| 5. Expand presence | Add voice, optional camera, phone access and further character work in owner-selected order. | Demonstrate visible sensor state, immediate pause, target accuracy, revocation and graceful degradation on the owner's hardware. |

## Evidence and release

The [desktop workflow](DESKTOP_WORKFLOW.md) describes the existing interaction, task commands, sampled screen vision and supported actions. The [progress tracker](PROGRESS.md) records verification. The capabilities above are design targets. Before a milestone release, compare realistic scenes on the 12 GB laptop and RTX 3050 with sensing on and off. Record chat first-token delay, local inference time, memory and GPU use, hosted tokens and cost. Keep the setup, configuration example, license, tests and reproducible fictional demo usable from a clean checkout. Publish claims backed by those results.

## Everyday behavior agreed with Arihant

**Starting the day.** After initial setup and the permissions required by Windows or a connected service, Mellow resumes permitted desktop awareness automatically. An explicit privacy pause must be respected. The everyday interface is the character and conversation. The long right-click list belongs in developer tools during testing. Keep pause and exit easy to reach.

**Remembering a session.** Preserve enough local activity detail to answer questions such as "Which track did I replay while working on Mellow?" and "Which tools and links did I use last time?" Associate track plays, skips, repeats, apps, project links and times with the activity where evidence is available. Distinguish an observed skip from a learned dislike. If a player did not expose the track, say so. Verify recall after restarting and verify that deleting an episode removes it from future answers and routine evidence.

**Recognizing a setup.** Across several project sessions, notice the recurring combination of particular Opera links, ChatGPT, VS Code and YouTube Music. On a later return to that project, offer the actual proposed setup in one question. One yes covers that setup on this occasion. Reuse open tabs and apps, open the missing items, and report any failure. If Arihant declines, leave the desktop alone and suppress repeat offers during that session. Test a similar casual browsing session to catch false offers.

**Showing attention.** While Arihant is comparing two items in Opera, Mellow's eyes follow the relevant item, his head follows his gaze, and he shifts aside when he covers it. If the page changes, he releases the old target. When Arihant reads quietly, Mellow can settle beside the content; a fixed front-facing stare throughout scrolling and switching tabs fails this scene. Test with speech and bubbles muted. A viewer should be able to identify what caught his attention without reading an explanation.

## Activity history, screenshots and credentials

Arihant wants broad recall across apps, including messaging and banking, and finds historical screenshots useful. The proposed default is a searchable, local activity timeline with occasional screenshots at meaningful changes. Deduplicate near-identical frames. Keep screenshot storage bounded by age and disk space; choose defaults after measuring a representative day on the laptop. Make it possible to pin a useful moment, delete a time range and pause observation. Deleting a moment should remove its frame, thumbnail, extracted text and derived search entries. Historical screenshots stay local. On a month-old query, distinguish surviving activity records from images that have expired.

Treat credential entry as a distinct data path. Mellow may remember that a sign-in happened. Recommend explicit saving through a local encrypted credential vault with OS-backed protection and user unlock. The local assistant may look up an entry by service and account alias. A separate vault component can fill an unlocked entry for its matching destination after user authorization. Plaintext passwords stay outside model prompts, chat history, SQLite activity memory, embeddings, logs and ordinary exports. An isolated process is useful only if its interface and access controls enforce this boundary.

Before screen history ships, test capture suppression for protected or uncertain sign-in surfaces. Skip the frame when protection cannot be established. Custom fields or secrets already visible elsewhere can escape detection; document coverage and retain immediate pause and deletion controls. Historical screenshots, OCR and screen summaries must have a local retrieval path independent of hosted chat.

Acceptance scenes: retrieve a fictional past work session with an available screenshot; show a timeline-only result after its frame expires; save and fill a dummy credential locally with the network unavailable; refuse a mismatched destination; verify the dummy secret never appears in stored screenshots of the test sign-in, activity search, exports or outbound requests. Vault implementation and screenshot retention limits remain design work.
