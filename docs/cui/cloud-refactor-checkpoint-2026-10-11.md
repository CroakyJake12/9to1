# Cloud refactor engineering checkpoint — 11 October 2026

This is an implementation checkpoint, not RCVP, Full, donor-parity or whole-app acceptance.

The approved portable refactor was verified before extraction (8,641,096 bytes; SHA256 `ad59c5adda54c3a4ffc739768356d580f52287c5ff4ced9d95bc307946da2f0a`). All 531 refactor and 12 recovered-main before/after records were checked. Three-way reconciliation retained newer main changes through `1dbb2bf57b7f0bef7a021ae96307624b2e9ed9b0`, including the resource bundle/path/collision worker. Conflict-marker candidates were never installed as shipping source. Execution, builds and Chromium tests ran in Linux cloud infrastructure without the original PC.

## Changes and behavior

- Preserve the canonical CUI migration, modern visible branding and existing service, assembly, resource, permission, persistence and IPC identities.
- Project absolute coordinates against actual reference bounds, with independent inherited axes. Pixel coordinates use the centre origin and positive Y upward.
- Keep retained scrolling/reordering stable; postpone responsive subtree mutations until native attachment completes. The original Home → Picture → Home attachment/index and duplicate-parent failures were reproduced before repair.
- Isolate theme observers and retry failed observers without blocking healthy ones. Publish complete palettes atomically, batch global palette notifications while retaining dictionary/brush/merged-owner identities, and reconcile interaction overrides without transient base-style flashes. Focus rings no longer resize controls.
- Finish initial Home local metadata before exposing clickable launch targets. Loading status is an overlay and does not resize the application viewport.
- Provide native popup layers for canonical menus. Preserve the original owner commands while placing Write/Present tools in responsive inspectors.
- Normalize a collapsed Write caret anchor so successive typed characters do not replace the first character.
- Wrap Write and Present toolbars at compact widths so Properties stays directly reachable without horizontal scrolling. Keep viewport observation alive while responsive inspectors detach their editor. Widening restores the original editor instead of leaving it hidden.
- Keep Present canvas geometry stable during object selection. Selection does not move authored objects. Read pending transition fields from the selected slide, accept actual typed duration values, and reject invalid values.
- Render Present text and thumbnail text using document colors/fonts rather than the surrounding dark interface palette.
- Give Present distinct start and workspace compositions using its existing canonical session. New/Open/Import enter the workspace; start/resume retains dirty content, selection and undo history. Inactive editing input and commands are denied. Recent entries come from the real repository.
- Exclude the nested Build.Tasks test project from the compiler production source glob. Retain resource-worker behavior and byte-preservation assertions; distinguish the actual Windows sharing error from Unix permission denial in portability tests.

## Executed checks

| Check | Observed result | Boundary |
| --- | --- | --- |
| CUI Runtime | 220 passed, 0 failed/skipped | Native headless runtime, including geometry, focus, themes, lifecycle and canonical boundaries |
| CUI Compiler | 43 passed | Static compiler; all six current browser CUI applications compile |
| CUI Build.Tasks | 25 passed | Actual resources/path/collision/staging preservation checks |
| Home account/dashboard | 34 passed | Managed composition and owner tests |
| Write native input | 255 assertions passed | Actual raw native input and owner persistence, before later viewport-only changes |
| Write start/resize | 263 assertions passed | Native retained editor, original owner/repository, inactive input and responsive restoration |
| Present native workflow | 265 assertions passed | Actual pointer/keyboard input, start/resume, arrangement, transitions, playback, import/export, durability and conflicts |
| Native Boards selected classes | 9 passed | BoardsAppTests and CanonicalContextTests on cloud headless; not native packaged acceptance |
| Chromium navigation | Three consecutive Home → Picture → Home round trips passed on publish13 | Actual published WASM artifact; stable 1280×900 viewport; no JS/managed exceptions |
| Chromium persistence | Write exact text saved and reopened in a fresh process; Present two-slide shape/notes artifact reopened | Real browser IndexedDB and owning model, stable identities |
| Chromium responsive editing | Write and Present Properties at 390px, then 1280px | Publish13: Properties is directly reachable; editors detach at compact width and restore on widening |

Browser evidence is tied to published artifact manifests kept with the private continuation handoff. Publish13's 380-file manifest SHA256 is `48c177478485297a218918e0da2a4d65245631c3895d3511b60507a71ccda7a5`. Its prepublication source manifest covers 19,065 files and hashes to `e715fb586078e753ff1bb7c0aa2c5d47ce4e3cfde9df8be7d7c036b0d573e5b1`. Later checkpoint-document edits are not part of the browser artifact.

The global-palette native regression reproduced 99 resource notifications and 41 partial observations (306.85 ms); batching produces four notifications and no partial observations (1.79 ms), with focus, selection, resource identities and unrelated resources retained. On actual publish12 Chromium, click-to-confirmed-theme plus two paint frames measured Glow 697 ms, Playful 2745 ms, Bubble 1870 ms, Cinematic 916 ms and Professional 577 ms in one sequence. Bubble previously took approximately 6.5 seconds on publish11. These observations show improvement, not an approved performance budget or statistically controlled benchmark.

Existing trimming warnings remain, particularly reflection-based owner serialization and TagLibSharp. Tested save/reopen paths worked; that does not establish all serialization or format paths.

## Open gates

Keep the full four-part plan and every CUI/PARITY/DESIGN/LAUNCH/ACCEPT/GOV gate. No donor exception is inferred. The current Notes-based Write editor does not satisfy mandatory LibreOfficeKit donor parity; a native bridge/ABI probe is not shipping donor execution. Complete native CUI migration, donor inventories and real donor execution, all required launch capabilities, approved performance/platform budgets, accessibility (including native menu/editor semantics), real cross-app/provider/permission/recovery workflows, native packaging and independent whole-app acceptance remain open. Global per-control retheme cost still needs profiling beyond the repaired palette/observer/state defects.

No Windows/Android packaged run or sign-in/cloud-provider success is claimed. No held worker or unrelated schedule was changed.

Architecture references consulted: `docs/architecture/cui.md`, the shared `AGENTS.md`, `HAVEN_UI_RULES.md`, architecture/validation rules and the pinned authoritative specification. The explicit canonical-CUI and centre/Y-up owner decisions govern this continuation.
