# Skills, Plugins, MCP and capability packages

Module ID: `plugins-mcp` · 9to1 Development Specification v3

## Prerequisites

- **Platform, app registry and host integration** — Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.

## Overview

Skills, Plugins and MCP integrations are registered, versioned capabilities with explicit trust, scope, transport and lifecycle. Availability in a picker is separate from permission to execute or disclose data.

## Layouts & visible states

- **V3-PLUGINS-MCP-LAY-01** — Expose installed/available/disabled/incompatible/failed states, version, source, owner, permissions and configuration. Clickable composer tiles configure the exact referenced capability.
- **V3-PLUGINS-MCP-LAY-02** — Show real MCP connection and authentication health, tool schema availability and actionable errors rather than generic connected badges.

## Features & behaviour

- **V3-PLUGINS-MCP-FEA-01** — Retain package manifests, signature/trust, compatibility, dependency and registry contracts. Imports validate packages before activation; installing text or assets cannot execute arbitrary unreviewed code.
- **V3-PLUGINS-MCP-FEA-02** — Use typed tools, stable identifiers and declared schemas with timeouts, cancellation, logs and controlled errors. Assign capabilities to accounts/Spaces/Assistants within current policy.
- **V3-PLUGINS-MCP-FEA-03** — Peer Review, Multiple Responses and Plan capabilities obey their source-defined semantics, scoped approvals and budgets; enabling a plugin does not grant a new authority.

## Canonical entities & persistence

- **V3-PLUGINS-MCP-ENT-01** — Store capability IDs/versions, package provenance, configuration, grants, bindings and connection state canonically. Space folders reference global packages rather than creating incompatible clones.
- **V3-PLUGINS-MCP-ENT-02** — Keep tool execution receipts linked to Task/Runs and action IDs.

## Permissions & recovery

- **V3-PLUGINS-MCP-PER-01** — Revoke assignments and credentials safely, stopping future access and exposing interrupted tasks. A stale cached schema must not call a changed tool blindly.
- **V3-PLUGINS-MCP-PER-02** — Recover failed installations, disconnected transports and expired auth without losing unrelated drafts or reporting false success.

## Acceptance checks

- **V3-PLUGINS-MCP-ACC-01** — Test install/validate/configure/assign/use/revoke, missing dependency, schema mismatch and transport timeout.
- **V3-PLUGINS-MCP-ACC-02** — Verify passive mentions only reference resources, while explicit invocation uses the documented command/action and approval flow.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Skills, Plugins, MCP and capability packages — MCP Support; Identity and lifecycle (part 01) — `technical/plugins-mcp-01.md` (1,836 words; V2-B2109, V2-B2110, V2-B2111…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
