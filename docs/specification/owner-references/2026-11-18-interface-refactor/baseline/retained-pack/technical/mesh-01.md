# Mesh devices, replication and remote capabilities — Mesh; Mini Computer, Mesh and remote sessions (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0460 · v2 lines 2360–2360

Mesh

### V2-B0461 · v2 lines 2363–2363

Mesh is a Home feature, not a standalone app. It connects authorised user/team devices so selected 9to1 context, projects, Agents and capabilities can operate across devices without requiring every workflow to live entirely in remote cloud infrastructure.

### V2-B0462 · v2 lines 2366–2366

Every Mesh device MUST have a stable DeviceID bound to an authenticated device identity/key rather than a mutable device name. Pairing MUST require explicit user/account authorisation and establish an authenticated encrypted relationship. A device advertises supported 9to1 capabilities/services and versions; peers MUST NOT assume capabilities that were not advertised.

### V2-B0463 · v2 lines 2369–2369

Mesh data sharing/sync MUST be namespace/object scoped. Personal Mesh may sync selected personal Den/chat/context/project state; Team/Organisation modes may sync explicitly shared team/org state while preserving personal/private namespaces. Joining Mesh MUST NOT automatically share all local files, secrets, model context or app data.

### V2-B0464 · v2 lines 2372–2372

Remote capability invocation MUST use typed APIs and pass both caller-side and target-device permission/policy checks. Mesh MUST NOT become an implicit generic remote-desktop, mouse/keyboard or arbitrary-code-execution channel.

### V2-B0465 · v2 lines 2375–2375

Lost/revoked devices MUST be removable from Home. Revocation invalidates future Mesh authentication/session material for that device and prevents further sync/remote calls. Offline devices queue only operations whose owning service explicitly supports safe offline replay/conflict handling.

### V2-B2333 · v2 lines 13499–13499

### UX27-OTHER-017 — Mini Computer, Mesh and remote sessions

### V2-B2334 · v2 lines 13501–13501

**B/D.** A remote/session surface identifies the actual machine/runtime, connection state, input ownership and active permissions. View-only and control modes are distinct and visibly indicated. Reconnect must not create a new machine/session while pretending to resume the old one. Pending consequential operations are reconciled before replay.

### V2-B2335 · v2 lines 13503–13503

Clipboard/files/credentials crossing the boundary require their real authorised contracts. Closing a viewer is not automatically shutdown of the remote session. A failed Mesh/remote launch remains a scoped actionable error, not a fabricated screen or an unconditional reason to disable local editors.
