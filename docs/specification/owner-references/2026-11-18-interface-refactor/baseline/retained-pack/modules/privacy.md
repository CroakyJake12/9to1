# Privacy, data boundaries and context isolation

Module ID: `privacy` · 9to1 Development Specification v3

## Prerequisites

- **Authority, scope and implementation reading map** — Current source precedence, narrow owner overrides, source accounting and the required module structure.

## Overview

Privacy applies to storage, model execution, context retrieval and every app handoff independently. Local storage does not imply local inference, and a connected resource does not imply permission to send its contents to an external model.

## Layouts & visible states

- **V3-PRIVACY-LAY-01** — Expose effective local/remote execution, storage location, capture indicators and relevant source scope where they influence a meaningful user decision.
- **V3-PRIVACY-LAY-02** — Offer explicit private/temporary and memory states with truthful limitations, not hidden privacy assumptions.

## Features & behaviour

- **V3-PRIVACY-FEA-01** — Keep app, conversation, personal, shared/team and organisation context boundaries. Retrieve only relevant authorised material, with source provenance and current grants.
- **V3-PRIVACY-FEA-02** — Temporary conversations are local-only until an explicit supported conversion; they do not create or reinforce durable memory or transfer to Mesh/Cloud automatically.
- **V3-PRIVACY-FEA-03** — Stop screen/camera/microphone capture at the actual runtime boundary when the user ends sharing. Hidden frames must not continue to reach models.
- **V3-PRIVACY-FEA-04** — Treat imported web, document, plugin and retrieved content as untrusted data; text within it cannot elevate permissions or rewrite platform policy.

## Canonical entities & persistence

- **V3-PRIVACY-ENT-01** — Track provenance, scope, consent, retention and revocation for stored/transferred context. Encryption protects data at rest/in transit; it is not a substitute for access control or an executable prompt-injection proof.
- **V3-PRIVACY-ENT-02** — Separate context snapshots from canonical source artifacts and preserve permission-aware deletion/tombstones in caches and indexes.

## Permissions & recovery

- **V3-PRIVACY-PER-01** — Recheck authorisation when rendering stored citations, opening cached files or continuing a background job. Revocation must promptly remove eligible access without leaking cached content.
- **V3-PRIVACY-PER-02** — Provide inspection, correction, deletion and export controls where retained policy permits, without treating frequency changes as deletion requests.

## Acceptance checks

- **V3-PRIVACY-ACC-01** — Test cross-app denied access, removed sources, temporary conversations, cloud-route refusal and capture shutdown using observable runtime receipts.
- **V3-PRIVACY-ACC-02** — Verify logs and UI traces expose useful high-level provenance without private internal chain-of-thought or unrelated user context.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

SPUI-195, SPUI-196, SPUI-197, SPUI-198

## Technical reading map

- Privacy, data boundaries and context isolation — Privacy and data-protection baseline; UCML APP PERMISSIONS, OUTPUT CONTROL, AND PROMPT-INJECTION DEFENCE (part 01) — `technical/privacy-01.md` (1,907 words; V2-B0112, V2-B0113, V2-B0114…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
