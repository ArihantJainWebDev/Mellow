# Mellow project roadmap

**Next priority, November 23.** Build general desktop awareness ahead of mail integration. The existing baseline includes paged chat, local assignments, periodic primary-screen sampling and explicit desktop commands. [Current implementation](DESKTOP_WORKFLOW.md).

1. Combine foreground app, visible content and background app presence into a local, timestamped account of the current activity. Acceptance example: recognize an AI chat about Mellow, separately identify a background game, and update the answer after an app switch. Where evidence is ambiguous, say what is uncertain.
2. Connect that short-lived context to optional longer-lived preferences. Verify that correcting a mistaken inference changes subsequent behavior and that forgetting a preference removes its influence.
3. Demonstrate initiative through study music and a learned project setup. Offer the recurring Opera links, ChatGPT, VS Code and music together, then restore the setup after one confirmation. Recognize studying, start YouTube Music, and change tracks without individual commands. Verify that manual pause wins and that one skipped track does not permanently redefine taste.
4. Extend to additional browser and app actions, including WhatsApp, as their individual integrations are verified. Voice, webcam, avatar polish and mail connectors are deferred.

Agreed product baseline • 22 November 2026

Mellow is a Windows companion that shares one ongoing conversation with its owner. It learns from daily life, offers help on its own, and performs permitted work. Each person running the open-source project owns their own data. The legacy prototype is a read-only reference.

## How to use this plan

Use the five goals to group releases. Follow the owner-reviewed priority sequence in the [product design](PRODUCT_DESIGN.md); the current step is design review. Before each block, identify the requirement, current behavior and stopping test. If either of us proposes work outside the active goal, call it out, say which milestone it would displace, and return to the active block unless the owner explicitly revises the plan. Record approved changes and rationale in [Decisions](DECISIONS.md) before implementation. Keep observed results in [Progress](PROGRESS.md). Walk through changed files, key functions and data flow after each batch. Assess understanding separately in the private report card.

## 1. Everyday continuity

**Starting point.** The current CLI streams Groq replies, stores SQLite sessions, resumes by command, and builds a bounded extractive recap. Local Qwen extraction runs in a durable queue. Candidates require approval. Approved memories support keyword and optional Nomic semantic retrieval. The last recorded run passed 44 offline tests. Combined live recap and recovery acceptance remains pending.

1. Save model choices once. Make ordinary startup continue the active conversation across restarts. Preserve existing history, explicit fresh starts, manual resume, and private isolation.
2. With automatic memory enabled, retain clear first-person facts, preferences and dated episodes with their source. Keep questions, guesses and ambiguous or sensitive candidates out of automatic recall. Offer review as an optional control. Handle corrections through ordinary speech and distinguish previous from current facts.
3. Implement source-aware forgetting, export, history deletion and reset. Cover recaps, embeddings, pending jobs, retries and old source turns so forgotten information cannot leak back into a reply.
4. Finish lifecycle and release checks in the [Goal 1 specification](SRS.md). Measure time to first token, response completion, retrieval quality, outage behavior, background contention, token usage and cost on the owner's laptop. Set numerical targets from measurements. Keep local extraction local and disclose context sent to Groq.

**Release demonstration:** after initial configuration, run with zero flags. Say you own a red bicycle, restart, ask what you ride, sell it, restart, ask whether you still own it, then forget it. Inspect the actual outgoing context for stale details. Test private mode, local-model outage, queued-job recovery and a clean install with fictional data.

## 2. A living Windows desktop presence

1. Build a rigged pearl-lilac ghost from the [visual baseline](visuals/README.md). Preview the model on light and dark desktops at everyday size. Review idle, walk, turn, listen, think, point, offer, speak and rest animations.
2. Implement a transparent, non-focus-stealing avatar surface, taskbar walking, click-through during work, dragging, sizing, quick summon, quiet/hide, full exit and optional launch at sign-in.
3. Connect the CLI and avatar to one conversation service. Drive poses and transient thought/speech bubbles from real application events. Use synthetic events to preview future sensing states.

**Release demonstration:** keep typing in an editor while Mellow walks and offers a reminder. Verify focus, click targets, placement and dismissal. Compare screenshots and motion clips with the stored artwork before accepting visual changes. Respect applications that prohibit overlays.

## 3. Voice, vision and initiative

1. Add microphone input, speech recognition, spoken replies and interruption. Support push-to-talk and opt-in ambient listening.
2. Add user-selected screen/window/region and webcam input, each with its own indicator, permission and immediate pause. Prefer local processing and no raw recordings by default. Measure local model capacity and delay with chat running. Document supported password-field detection and fail closed where protection cannot be guaranteed.
3. Connect perception events to gaze and posture. Offer help when context warrants it. Treat expressions as cues for a question. Support dismissal, quiet hours and interruption preferences. Hearing bystanders grants no automatic permission to remember them.

**Release demonstration:** verbally ask about a selected window, interrupt the answer, and pause each input independently. Verify capture has actually stopped before showing the privacy pose. Demonstrate a timely offer and a dismissal that sticks. Record false offers and missed cues on consented scenarios.

## 4. Life, commitments and work

1. Build read-only authorized mail and college portal connectors. Discover the actual portal and supported authentication before choosing an integration. Track assignments, quizzes, deadlines and follow-ups with source, last successful check and change history.
2. Unify tasks and completion, goals, dreams, journal entries, project files, decisions and checkpoints. Link revised deadlines to existing work. Surface source conflicts and unavailable accounts. Adapt tone and habits from permitted context and owner corrections.
3. Add an inspectable dashboard for tasks, source evidence, memories, connected accounts, sensors and job health. Trial opt-in reflections and proactive reminders with the owner.

**Release demonstration:** a fictional professor changes a quiz date. Ask what changed and what remains today. Verify one updated task, its source, timezone, and honest status if a connector goes offline. Restore a project's last checkpoint.

## 5. Creation, laptop actions and phone control

1. Draft Reddit, LinkedIn and X posts in the owner's voice from verified project work. Preview audience-specific drafts and obtain separate authorization to publish.
2. Add bounded browser research, comparison, coding assistance and laptop actions. Begin with opening a project or app. Add routines, visible outcomes and cancellation. Scope account access and require confirmation for consequential submissions.
3. Pair a phone with revocable access. Route spoken requests through the same permission and execution service as the desktop. Return actual results, handle offline laptops, and prevent repeat execution on retry.

**Release demonstration:** draft a post from a fictional milestone, review it, then request a supported laptop routine from a paired phone. Verify audit history, denied actions, disconnection, revocation and duplicate-request handling.

## Release discipline

Use meaningful milestone commits on the existing GitHub repository. After Goal 1, branch per goal, verify, review, merge and tag a new version. Before public releases verify setup, example configuration, dependency and asset licensing, fictional demo, regression results and data exclusions. Document known failures. Claim only demonstrated capabilities in README and résumé. No fine-tuning without an evaluation showing a specific unmet need. Budget hosted calls from measured usage. Model IDs, rendering technology, connector details and performance thresholds are implementation decisions to validate, not release claims.
