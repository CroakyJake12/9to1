# Acceptance defect handoff

| Field | Record |
| --- | --- |
| Candidate | Repository and exact revision, native runtime or artifact SHA-256, target platform, relevant donor and build receipts |
| Requirement | Current specification ID, Atlas route, interaction and control binding |
| Expected | Starting state, action and expected visible or persisted result |
| Observed | Actual state, reproduction steps with disposable data, exact first failed assertion and any failure to reproduce |
| Evidence | Environment, timestamp, command result, original capture or recording, and evidence paths and hashes |
| Impact | Affected workflow, scope, severity (MINOR or MAJOR) and owning subsystem |
| Retest | Repair revision, replacement build identity and result of the original failed interaction |

Preserve the original evidence alongside the retest. Name unavailable target checks explicitly. A minor defect returns to its owning implementation and affected acceptance checks. A major defect needs the coordinator to reassign the underlying flow and review the integration boundary. A receipt for the report identifies the receiver; a qualified repair also requires the candidate and retest evidence. [Current specification and Atlas](../../specification/owner-references/2026-11-22-atlas-v4/README.md)
