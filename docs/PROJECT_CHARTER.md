# Project charter

Product baseline adopted 2026-11-22. The [roadmap](ROADMAP.md) defines scope, sequence and release gates.

## Purpose

Build Mellow as a deeply personal OS companion: a recognizable presence that learns about its owner, maintains continuity, and eventually hears, sees, and performs useful actions with permission. Curiosity and understanding the code are primary motivations. A useful open-source release and an honest interview story are desired outcomes.

The first intended user is the owner. Future contributors should be able to run their own companion without receiving the owner's personal data.

## Goal 1 — memory-aware CLI companion

Release a usable terminal application with responsive Groq conversation, recent context, persistent sessions, personal and episodic memory, semantic retrieval, and local background processing through LM Studio. Include personality, memory control, failure handling, and evidence of quality.

A goal is a substantial release. API integration, database setup, and retrieval are blocks inside a goal, not separate release goals.

## Future vision, outside Goal 1

Desktop presence; permitted OS/activity awareness; habit understanding; proactive conversations; microphone and speech output; camera and screen context; laptop actions; browser tools; authenticated phone access. See the five agreed goals in the roadmap.

## Working agreement

- Preserve `D:\HEHE\Mellow-Gyrlick` as the legacy reference.
- Build the new implementation in `C:\Users\agraw\Music\Mellow`.
- Updated agreement (2026-11-10): the assistant writes, edits, and verifies implementation code block by block. The owner focuses on understanding every file, important function, and connection. This supersedes the earlier owner-writes-code agreement.
- Before each block, briefly explain its purpose and design. Consult relevant legacy files/functions and identify what is being reused or redesigned. After implementation, explain new/changed files, key functions, and the end-to-end flow.
- Use coherent implementation blocks: explain -> implement -> verify -> walkthrough -> record. Use brief understanding questions and occasional scenario/debugging quizzes; coding exercises are optional.
- Aim for 45–60 minute sessions; combine easy blocks and adjust to pace. Do not assume Goal 1 requires months.
- Keep explanations concise; expand when requested or needed for understanding.
- Record progress, tradeoffs, bugs, and verification as work happens. Maintain [the chapter report card](REPORT_CARD.md) with evidence-based learning assessments; implementation passing tests does not establish owner understanding.
- README/resume claims must describe only implemented, verified capabilities.

## Environment baseline

Windows laptop; 12 GB DDR5 RAM; RTX 3050 with 6 GB VRAM, reported 95 W TGP. LM Studio preferred; Groq provides hosted conversation. Python 3.14.7 and Git 2.55.0.windows.5 are available. A project-local virtual environment and editable install work. See the progress log for dated live checks. Measure current availability and performance before release.

## Change process

When a new idea arises, record it as a candidate. Decide whether it is required for Goal 1, supports an existing requirement, or belongs in the future backlog. Record accepted scope changes before implementing them. This keeps exploration possible without silently expanding the release.
