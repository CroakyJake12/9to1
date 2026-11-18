# Cloud services, infrastructure and billing contracts

Module ID: `cloud-services` · 9to1 Development Specification v3

## Prerequisites

- **Account, identity and resource entitlements** — Canonical CAKE account identity, secure sessions, server entitlements and resource allocation state.
- **Privacy, data boundaries and context isolation** — Privacy scopes, consent, local/remote boundaries, source provenance, retention and revocation.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

The cloud/backend module retains the supplied service architecture, resource accounting, storage, authentication and billing contracts. Current UI changes do not remove infrastructure requirements or authorise live deployment.

## Layouts & visible states

- **V3-CLOUD-SERVICES-LAY-01** — Display actual service/connection, allocation, quota and billing state through owning apps. Avoid infrastructure controls in ordinary app flows unless the user must make a concrete decision.
- **V3-CLOUD-SERVICES-LAY-02** — Show pending/confirmed/failed server actions and explain actionable limits without invented allocations.

## Features & behaviour

- **V3-CLOUD-SERVICES-FEA-01** — Retain the specified Cloudflare frontend/Workers/D1/Durable Objects/queue and B2 storage responsibilities, secure authentication/PKCE and service boundaries.
- **V3-CLOUD-SERVICES-FEA-02** — Preserve Stripe checkout/webhook verification, idempotency, allocation/resource accounting, reserve/consume/settle and organisation pool/ceiling contracts.
- **V3-CLOUD-SERVICES-FEA-03** — Keep publishing, custom domains, storage and execution resources independently authorised. A mock-up publish button is not evidence of a deployed service.

## Canonical entities & persistence

- **V3-CLOUD-SERVICES-ENT-01** — Use server-authoritative account/resource ledgers, idempotency keys, object manifests, encrypted storage metadata and audit events.
- **V3-CLOUD-SERVICES-ENT-02** — Maintain migration/version compatibility and recovery semantics across queued events and partial service failure.

## Permissions & recovery

- **V3-CLOUD-SERVICES-PER-01** — Infrastructure deployment, billing changes and external publication need the actual operation’s authorisation. Historical delivery instructions in the source are not new permission.
- **V3-CLOUD-SERVICES-PER-02** — Fail closed for auth/resource checks, preserve retryability and avoid double-charging or duplicate external effects.

## Acceptance checks

- **V3-CLOUD-SERVICES-ACC-01** — Validate billing/webhook replay, concurrent reservations, partial failures, revocation and storage quota enforcement against real service tests.
- **V3-CLOUD-SERVICES-ACC-02** — Keep design review distinct from production readiness or deployment acceptance.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

SH-01, SH-02, SH-03, SH-04, SH-05, SH-06, SH-07, SH-08, SH-09, SH-10, SH-11, SH-12, SH-13

## Technical reading map

- Cloud services, infrastructure and billing contracts — Baseline, authority and deployment permissions; Production architecture and ownership (part 01) — `technical/cloud-services-01.md` (604 words; V2-B2171, V2-B2172).
- Cloud services, infrastructure and billing contracts — WordPress-to-Cloudflare website migration; CAKE ID and account services online (part 02) — `technical/cloud-services-02.md` (2,357 words; V2-B2173).
- Cloud services, infrastructure and billing contracts — Fresh-device onboarding; Web/desktop continuity (part 03) — `technical/cloud-services-03.md` (543 words; V2-B2174, V2-B2175).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
