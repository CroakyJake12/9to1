# UI report adoption — owner amendment, 25 October 2026

The owner reattached the 22 October UI report, asked for its additional design
requirements to enter the project specification, and explicitly clarified that
send-button stacking is a feature. The owner also warned that inspected UI and
other historical observations may now be dated. This amendment is additive to
the canonical specification and supersedes older single-action Send-slot and
blue-default selected-tab directions where explicitly stated below.

## Adopted owner design requirements

- UI-RPT-001 — Replace the single composer Send slot with a contextual horizontal
  overlapping icon shelf. The front action is fully visible on the left. Only a
  narrow right-hand sliver and tiny icon glimpse of the next action show behind
  it. The approved preview uses a 56 px primary and 14 px rear extension, total
  70 px: preserve these proportions when adapting production sizes.
- UI-RPT-002 — Either horizontal or vertical scrolling changes the front action
  without changing the horizontal layout. Position dots show the current place
  while navigating. Maximum five eligible actions; no filler, duplicates or
  invented rear preview when no next eligible action exists.
- UI-RPT-003 — Exact defined orders: idle chat with a draft, Send → Voice;
  running agent with a draft, Steer → Stop → Queue → Voice; acknowledged stopped
  mid-response, Resume → End Pass. Eligibility must come from actual runtime
  state and supported capabilities, not simulated timers or a UI label.
- UI-RPT-004 — Stop requests a resumable paused state. Show the actual pending
  stopping state until runtime acknowledgement; a failed request or terminal
  cancellation is not an acknowledged resumable pause. External tool processes
  retain their own real cancellation/completion constraints.
- UI-RPT-005 — Resume continues that preserved acknowledged pass. End Pass ends
  it terminally and remains distinct from Stop. Preserve draft and queued
  content; queue execution/deletion after End Pass is not specified and must
  not be inferred. Use the canonical task owner and genuine acknowledgements.
- UI-RPT-006 — Voice enters the existing same-session Voice & Vision workflow,
  retaining actual capture permissions and session ownership. Shelf navigation
  alone never starts capture. Send, Steer and Queue retain distinct domain
  semantics and the owning draft lifecycle.
- UI-RPT-007 — Selected tabs default to light grey or white with black text
  unless the user explicitly overrides the tab accent. A generic blue theme
  fallback is not an explicit user override. Removing an override restores the
  neutral default. Hover, unselected and keyboard focus remain distinct states;
  this rule does not turn every selected row or control white.
- UI-RPT-008 — Provide more depth, stronger accents and selective rounding:
  distinct workspace, lifted composer and floating-control surfaces; quieter
  secondary surfaces; radii appropriate to container size and purpose. Preserve
  the darker rail/lighter sidebar relationship and Professional versus Glow
  distinction. Do not copy sampled bitmap colours as universal theme tokens.
- UI-RPT-009 — Preserve later Haven browser chrome and global-control placement,
  the shared standalone/embedded app implementation, emoji, floating format
  toolbar, current refined plus treatment, genuine model/resource state and
  permission presentation. Mockup accounts, percentages and messages are
  illustrative and must never become fake product data.

## Recommendations requiring deliberate implementation choices

The report explicitly separates these from additional owner decisions. Use them
as implementation and testing guidance, not as proof that every detail was
approved: browsing versus activation separation; preserve draft and selection;
bind a pointer press to its original action and cancel on replacement; accessible
keyboard/touch navigation and truthful position/count; keep Stop discoverable;
keep text-field Enter governed by editor rules rather than the browsed shelf item.

Wrap versus no-wrap remains an open interaction choice. The report's proposed
idle-empty Voice-only and running-empty Stop → Voice orders are suggestions,
not the three exact owner-defined orders above. Retain real runtime eligibility
and record any implementation choice without inventing new owner approval.

## Historical observations and current-build rechecks

E038/E053/E073 and associated email observations describe older captures. They
are not evidence that the same issue still exists, nor proof of a later fix.
Recheck current source/rendering before reopening toolbar/model-recovery overlap,
popup backing, responsive composer gutters, typography, icon sizing, profile
menu wording, account wiring or app-route discoverability. Keep existing fixes.

Use intentional responsive minimum gutters and a maximum composer width; the
bitmap's roughly 84 px margins are not a mandatory fixed layout. Inspect the
actual loaded font and declared theme before diagnosing a font or palette fault.
Match current source, theme, display scale and state in basic UI smoke/captures.
Full independent acceptance remains paused by the latest owner instruction.

## Provenance and implementation status

[Original report](9to1-ui-reference-comparison-2026-10-22.html), 2,623,000 bytes,
SHA-256 `6b9d3e8db8b0061816c2dd0b0949291fe4a9b71e58074fa4756d5a00bd2556ca`.
The HTML contains historical screenshots and an explicitly simulated interactive
preview; preserving it is not application integration or runtime verification.

This amendment records requirements, not completion. Spaces owns the shelf and
canonical pause/resume/end semantics; Home/browser owns selected-tab styling;
shared CUI owns reusable input, layout and theme machinery. Exact-source basic
checks and later independent acceptance must establish the implemented result.
