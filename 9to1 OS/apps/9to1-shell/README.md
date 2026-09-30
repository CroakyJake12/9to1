# 9to1 OS session shell

This executable is the designated central native Home host for an OS session. It uses the actual shared `AddHavenInfrastructure` graph and holds `HomeNativeSessionLease` for its lifetime before starting Home services. A second host fails closed rather than composing a second authority. Child apps must attach through Home's authenticated transport; this executable is not an app-private fallback Home service.

The authored surface is `UI/Shell.cui`, rendered through the canonical `CuiNativeHost`/`CuiSceneHost`. The scene is mounted only after the real compatible `home.core`, `home.state` and `apps.installed` services are ready and the OS-bound Home actor is verified.

The current surface supports:

- Home-owned, profile-bound, device-local versioned shell configuration; no separate app settings database.
- One to five taskbar layers with stable identities, names/order, discrete adjacent navigation and an accessible position indicator. Typed items retain references to their canonical owner.
- Distinct Desktop Spaces with duplicate, rename, reorder, delete, navigation and portable copy/paste import/export through the real platform clipboard; these are not Virtual Desktops, Desktop Pages or LLM Spaces. Import remaps internal identities while retaining canonical owner references and requires Preview/Keep before persistence.
- Desktop Pages with validated non-overlapping grid placement, typed canonical item references, page navigation/add/rename/reorder/remove and global layout with per-Desktop-Space overrides. Supported legacy configuration projects into schema 2 without a write until Keep. Only installed-app items currently have an actionable owner bridge; unsupported item kinds stay disabled.
- Preview/Keep/Revert, automatic preview expiry, previous-configuration recovery, taskbar reset and safe defaults. Preview never writes canonical state. Keep checks the profile identity and record revision, authorizes current ownership and commits atomically. Invalid/newer data is preserved for recovery.
- Go All Apps/search through typed incremental providers and pin/unpin to the active layer. One failed/slow provider does not block another; query deadlines still complete when a provider ignores cancellation. Go retains canonical references and owner actions, not canonical application copies. Pinned app launch refreshes the canonical app identity/revision before authorization.
- Native Linux XDG desktop-entry discovery feeding the shared Home installed-app registry. User overrides mask system entries; hidden/non-app/ambiguous entries are excluded. Manual launch re-resolves the current canonical ID/revision and ownership, then delegates desktop activation to `gio` using an argument list.

This is an executable CUI session-host increment, not a completed OS compositor. It does not replace Mutter/GNOME, implement window/task management or snapping, virtual desktops, tray/status, widgets, notifications, quick settings, scope-aware full appearance/input settings, or all required Go providers/AI mode. Taskbar edge/icon/grouping/floating schema fields and foreign item kinds are not yet fully rendered/actionable. Desktop Space file import/export, sync and triggers remain unimplemented. Actual isolated Xvfb window rendering and pointer/keyboard Preview/Keep acceptance are recorded separately; they do not establish compositor, physical-device or full OS acceptance.

Installed app observation is discovery, not a verified Computer Use classification. Unknown application operability remains Unknown. The manual Go launch path does not expose a Dulche/Automation invocation or replace the shared Home confirmation/permission framework. Full automated actions require canonical owner registration through that framework.

The XDG adapter uses upstream desktop activation rather than reproducing `Exec` parsing. Installed-entry mutation between the last inventory check and upstream activation remains subject to the host's normal desktop-entry filesystem authority; this increment is not a sandbox for untrusted native programs.

The central host publishes a private leased Home discovery endpoint after actual readiness. Unsigned native peers are denied. The installed identity verifier requires a separately trusted controlled-launch authority in addition to signed complete-package/runtime evidence; this environment explicitly supplies the unavailable authority and does not grant installed peer/session-host identity. See `Authority/README.md` for the boundary and unprovisioned positive installation recipe.
