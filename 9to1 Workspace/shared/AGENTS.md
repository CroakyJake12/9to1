# Editing 9to1 shared source

The current [9to1 Development Specification](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit), amended by explicit current user instructions, is the product authority. Existing source, legacy Haven rules, model plans, migration/completion notes and historical machine paths do not supply alternative product scope. Consult applicable source sections before implementation; unresolved OPEN product decisions must remain explicit.

Project Astra la Vista uses exactly four explicitly configured GPT-6.1 Sol implementation workers without nested delegation. The parent coordinates allocation, integration and verification. Current path ownership and evidence live in `../../../docs/ASTRA-WORKING-LEDGER.md`. Do not introduce competing release plans. Respect the single implementation owner of every shared file and request cross-owner edits.

## Shared architecture

Preserve useful implementation and reuse the owning system before creating a parallel service:

- `src/Haven.Core` owns stable domain entities, identifiers, value objects and contracts.
- `src/Haven.Application` owns use cases, routing, app/agent orchestration and domain policy; depend on contracts rather than platform/database details.
- `src/Haven.Infrastructure` owns persistence, filesystem, provider, platform and external integration adapters.
- `src/Haven.Desktop` owns the existing desktop host, presentation, navigation and accessibility. App-specific canonical source remains in the owning Workspace app; CUI is the specification's application UI format.
- Shared CUI runtime, semantic AI context/actions and controls live under `framework/CUI/`. Consume Home-managed shared services rather than starting private substitutes.

Legacy Haven-named technical documentation remains useful implementation context only where it agrees with the current specification. Read the relevant architecture/system documentation and inspect reusable code before substantial changes; correct stale technical descriptions in the same pass. Preserve stable IDs and explicit persisted schema versions. Consumer-data legacy compatibility is not mandatory for this development-only product, but development work and existing cakemods.com content must be preserved.

## Integrity and permissions

Use the existing canonical entities and revision/conflict contracts. Mutations must be atomic at their declared user-visible boundary, report partial success explicitly, carry cancellation through I/O and reject stale results. Prefer recoverable deletion where the product does not specify permanent deletion.

Keep secrets out of source and reports. Respect caller identity and permission filtering. Reuse Home's permission broker, contextual AI Read-only/Write modes and typed app APIs. Data AI mutations require approval for each action; live-database mutations additionally require meaningful preview, validation and recoverable backup before execution. Computer Use requires explicit invocation and must obey game exclusions. Preserve OSM attribution/provider terms and all third-party licences.

For development actions, explicit user authorisation in the session governs routine reversible work. Obtain specific approval before production DNS changes, live-site replacement, WordPress retirement or purchases. Do not force-push, discard development changes or merge the primary branch without applicable authorisation.

## Validation and evidence

Review changed code line by line, run meaningful closest tests and integrated checks, and verify real runtime behavior, security, accessibility, performance and platform/package acceptance appropriate to each source requirement. Compilation and a passing subset do not prove whole-product completion. Keep exact commands/results and distinguish IMPLEMENTED, VERIFIED, BLOCKED and UNVERIFIED in the single working ledger.

Preserve unrelated user changes, source, provenance and useful technical documentation. Obsolete model guidance may be deleted/replaced under the current launch authorisation, with recoverability preserved first. Before interruption, preserve changes and a concise continuation record; do not claim continued execution after the task stops.
