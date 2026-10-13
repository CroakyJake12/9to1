# Preserved Chat interface checkpoint

Status: CHECKPOINT — current scoped owner tests and labelled fixture checks passed. This is not full Spaces or visual parity acceptance.

Base source is `0e5aa3b5` plus the preserved UI diff. Existing features, canonical IDs, routes and data owners remain in place. No demo values enter product composition.

## Changes under verification

- Shared CUI retains the original message action controls, revealing only the message under pointer or keyboard focus. The message container has a continuous transparent hit region, including the gap before its actions. Availability still comes from the original action owner.
- Header and sidebar use the canonical icon catalogue, including the approved filled Chat presentation. Conversation icons identify actual conversation rows; they imply no cloud, agent, unread or runtime state. Switcher/collapse controls keep 44-pixel targets.
- The shared conversation surface contains the exact approved footer disclaimer, with responsive wrapping and canonical text resources. Desktop padding consumes the former unused bottom space to preserve the composer position.
- The original controller alone marks a successfully settled initial read as idle. The browser hides only that marked current status; save errors, failed turns, unavailable models and meaningful action acknowledgements remain visible, including when a draft is retained.

## Historical evidence

Before the P0 pause, visual10 completed 68/68 desktop geometry and interaction checks, including body-to-gap-to-Copy trajectory and physical Tab focus. The six current owning product source hashes still match that run. Its images predate mandatory labelled report generation and remain historical internal test evidence only.

## Current verification boundary

The explicitly opted-in automated fixture enters through actual BrowserSpacesFeature, BrowserChatSession and ChatSpaceController, then the canonical shared CUI shell and original host action availability. Its deterministic backend supplies synthetic records only. Reports prominently state VISUAL TEST FIXTURE / NOT LIVE ACCOUNT OR MODEL OUTPUT outside unmodified images. Raw PNGs are internal comparison evidence.

Current owner suites passed: Browser 63/63 and shared Spaces 112/112. The source-linked Browser test host emitted 94 existing CA1416 platform warnings; no errors occurred. The isolated visual executable built with zero warnings and errors.

All eight explicitly opted-in cases passed: 1530×946 and 390×946 in Professional Dark, SuperBright, Bright and SuperDark. Desktop cases each passed 68 checks; compact cases each passed 67, for 540 scoped checks. Every case retained the exact host action availability, actual pointer trajectory, physical Tab reveal, repeated-message isolation and original draft. The disclaimer stayed below the composer, all required measured controls stayed within the viewport, and recorded geometry stayed identical across colour appearances. Contrast and pixel/source custody are recorded per case.

Use only the external-banner HTML reports under `/workspace/astra-audit/evidence/chat-ui-labelled-11/<appearance>-<width>/VISUAL-TEST-FIXTURE-NOT-LIVE.html`. `matrix.json` enumerates exact outputs. `chat-ui-current-manifest.json` preserves source, loaded-assembly and log hashes, contrast values and original PNG-byte verification. No raw fixture image was delivered inline. No further production source change was required during this continuation; the test harness alone gained the optional appearance argument.

## Remaining gates

Rendered fixture checks cannot establish authenticated accounts, persisted state, actual model responses, tools, approval or cross-device synchronization. Actual browser publication and signed-out smoke tests belong to the integration owner. Native Windows and physical touch-device validation remain separate.

Visual/feature gaps remain: correct work chronology and elapsed time, quick questions and answer ownership, reasoning and usage controls, Aa/emoji formatting, voice, sidebar configuration, full Group/Project/Assistant and other first-party Space workflows. The measured model control follows genuine supplied model text; no synthetic percentage or reasoning state is added to imitate the reference. Spaces remains unaccepted.

Applicable sources: current Spaces companion specification; shared HAVEN_UI_RULES.md; docs/ARCHITECTURE_RULES.md; docs/VALIDATION_RULES.md; docs/ui/building-ui.md; docs/development/testing.md.
