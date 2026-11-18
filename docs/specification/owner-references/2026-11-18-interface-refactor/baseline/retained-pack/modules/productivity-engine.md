# Shared productivity engine and canonical embeds

Module ID: `productivity-engine` · 9to1 Development Specification v3

## Prerequisites

- **CUI, themes and shared interaction system** — Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Boards, Write, Present, Canvas, Data and other productivity surfaces reuse real document/object engines and canonical embedded app surfaces. They must retain substantive editing power while sharing a coherent CUI shell.

## Layouts & visible states

- **V3-PRODUCTIVITY-ENGINE-LAY-01** — Embedding an app presents its genuine editor/viewer, with capability-appropriate compact controls and an Open in App route to the same artifact.
- **V3-PRODUCTIVITY-ENGINE-LAY-02** — Selection, toolbar and inspector context follow the actual focused object, not a synthetic card representation.

## Features & behaviour

- **V3-PRODUCTIVITY-ENGINE-FEA-01** — Use semantic object operations for blocks, pages, text ranges, strokes, shapes, cells, tables and media. Preserve donor-engine features and format compatibility identified in the retained technical clauses.
- **V3-PRODUCTIVITY-ENGINE-FEA-02** — Contextual AI produces inspectable typed changes against current revisions. Manual editing remains available without AI.
- **V3-PRODUCTIVITY-ENGINE-FEA-03** — Clipboard, drag/drop, keyboard editing, comments, history, search and accessible selection follow shared contracts without flattening each app’s model.

## Canonical entities & persistence

- **V3-PRODUCTIVITY-ENGINE-ENT-01** — Keep object IDs stable across embedded/native views, revisions and export where possible. Embed records reference canonical owning artifacts and scoped view settings.
- **V3-PRODUCTIVITY-ENGINE-ENT-02** — Preserve undo boundaries and dirty state across app transitions; storing a reference must not clone or silently move the source.

## Permissions & recovery

- **V3-PRODUCTIVITY-ENGINE-PER-01** — Respect artifact and embedded resource permissions independently. Revoked or missing sources show an honest placeholder and recovery path.
- **V3-PRODUCTIVITY-ENGINE-PER-02** — Read-only embed state must prevent both direct editing and AI mutation while retaining permitted viewing/navigation.

## Acceptance checks

- **V3-PRODUCTIVITY-ENGINE-ACC-01** — Edit a canonical embed, open in its owner, and verify the same revision and history. Switch focus between nested objects and verify correct tool routing.
- **V3-PRODUCTIVITY-ENGINE-ACC-02** — Exercise copy/paste, undo/redo, source revocation, format round-trip and local recovery.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Shared productivity engine and canonical embeds — Shared Productivity Engine; Canonical object identity, schemas and style registry (part 01) — `technical/productivity-engine-01.md` (787 words; V2-B0474, V2-B0475, V2-B0476…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
