# Dulche Translate service

Module ID: `dulche-translate` · 9to1 Development Specification v3

## Prerequisites

- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.
- **Models, discovery and universal model routing** — Model discovery/lifecycle, stable provider capabilities, ordered routing and truthful usage/effort metadata.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Dulche Translate is the shared translation service behind Spaces Translate and other authorised consumers. It preserves source language/text, target language outputs and provenance through real model/runtime capabilities.

## Layouts & visible states

- **V3-DULCHE-TRANSLATE-LAY-01** — Owning surfaces present source/target languages, progress, one output per target language and genuine unsupported/failed states.
- **V3-DULCHE-TRANSLATE-LAY-02** — Keep translation service controls consistent with canonical model routing; do not create a conflicting per-app credential/model store.

## Features & behaviour

- **V3-DULCHE-TRANSLATE-FEA-01** — Use retained language detection, translation, voice/text and output contracts as supported. Explicit user target choices override a detected default where valid.
- **V3-DULCHE-TRANSLATE-FEA-02** — Preserve content boundaries, language identifiers and source provenance. A translated output is derived content, not a silent edit to the original source.
- **V3-DULCHE-TRANSLATE-FEA-03** — Multi-target requests share a TranslationSet with distinct sibling results and partial-failure state.

## Canonical entities & persistence

- **V3-DULCHE-TRANSLATE-ENT-01** — Persist request/TranslationSet/output IDs, source reference/revision, language codes, model/route provenance and result status.
- **V3-DULCHE-TRANSLATE-ENT-02** — Keep temporary runtime buffers distinct from retained translation history and exported output.

## Permissions & recovery

- **V3-DULCHE-TRANSLATE-PER-01** — Respect source disclosure and local/cloud policy. Unsupported language/voice capabilities cannot be implied by a visible selector.
- **V3-DULCHE-TRANSLATE-PER-02** — Retry only failed target output where safe, preserving successful siblings and the original source.

## Acceptance checks

- **V3-DULCHE-TRANSLATE-ACC-01** — Test language detection/explicit targets, multiple sibling outputs, partial failure/retry and source preservation.
- **V3-DULCHE-TRANSLATE-ACC-02** — Verify model capability/permissions and canonical use from Spaces Translate.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Dulche Translate service — Translate; Translation Sets and variants (part 01) — `technical/dulche-translate-01.md` (1,409 words; V2-B1826, V2-B1827, V2-B1828…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
