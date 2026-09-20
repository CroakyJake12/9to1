# CakeOS Donor-Parity Status (desktop controller-owned)

Updated: 2026-09-16 ~15:55 UTC by desktop controller.
Remote-wins rule observed: this file is written on `repair/donor-parity-desktop-integration`
atop `origin/emergency/windows-apps-book4edge` @ `5431052e` (advanced past handover `4d30846d` during work).
Laptop integrator branch `origin/repair/donor-parity-integration` @ `8732b92b` owns `docs/parity/` verdicts;
this file does NOT override it — it queues desktop deliveries for laptop review and reconciles paths.

## Remote state

- `origin/main`: `ca48757c` (minimal, 79 files — NOT the work base)
- `origin/emergency/windows-apps-book4edge`: `5431052e` (was `4d30846d` at handover; +3 commits: `b2fe9c24` automation hooks, `127edeca` ABI v3 pen color/width, `45c25044` CanvasApp checkpoint, `5431052e` HUI-native shell checkpoint)
- `origin/repair/donor-parity-integration`: `8732b92b` (laptop integrator, `docs/parity/` verdicts)
- Local main checkout `1b44cd77` is stale/behind + dirty — left untouched per git safety.

## Donor versions (verified, not from memory)

- Rnote: `flxzt/rnote` rev `8cac558fa73ee63d958cec05124ea2fdbd7d608d` = v0.14.2 (`rnote-engine` + `rnote-compose`, `Cargo.toml`/`Cargo.lock`/`PROVENANCE.md`; W1 re-verified via live ls-remote + source inventory). Matches laptop `docs/parity/CANVAS-RNOTE-PARITY.md`.
- AppFlowy board: `AppFlowy-IO/appflowy-board` @ `804d7898ac0becabf73e45527baf5d5c573cd6bb` (pkg v0.1.2; `Boards/README.md`/`THIRD_PARTY.md`/`pubspec.lock`; W1 live checkout). Note: package is a Kanban widget — properties/filter/sort/board-settings live in AGPL full app (excluded); W3 scoped accordingly.
- HUI donor: `CroakyJake12/CakeAI@7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb:src/Haven.UI` (`havenos.lock`, `HUI/vendor/.donor-revision`).

## CANVAS / RNOTE

- Donor: Rnote 0.14.2 `8cac558`
- Audit: W1 `51e02085` (`docs/donor-parity/rnote-parity-matrix.md`, 52 rows: PASS 6 / PARTIAL 10 / BLOCKED-BY-BRIDGE 18 / BLOCKED-BY-HUI 12 / MISSING 6) + laptop `docs/parity/CANVAS-RNOTE-PARITY.md` (30 rows). RECONCILE PATHS before claiming single source of truth.
- Bridge: pre-existing ABI v2 preserved; W2 `a44bdf2` extends to ABI 3 (40+ fns, 13 shapes, brush/shaper/eraser/selector/tools styles, layout/pattern/format, doc export SVG/PDF/XOPP + selection); remote `127edeca` independently landed ABI v3 pen color/width — MERGE CONFLICT RISK, must 3-way review.
- Controller: W2 adds `CanvasController` + header/menus/status wiring (existing HUI components only); remote `45c25044`/`5431052e` adds `HUI/CanvasApp` + `HuiRenderer` — OVERLAP, needs ownership decision (laptop integrator decides).
- HUI: W4 `b7aee98` provides shared components (consumable); W2 used existing-only + filed NEEDS-FROM-W4 (colour picker, dialog host, clipboard, numeric, pages-bitmap UI).
- Functional parity: PARTIAL (W2 honest gaps: shaper highlight/rough-fill UI, typewriter in-canvas editing, selector transforms, pages PNG/JPEG UI, dialogs as status interim, ARM64 headed smoke pending; `cargo test` not runnable on X64 box — needs toolchain lane).
- Visual parity: MISSING (no side-by-side donor/HUI screenshots yet).
- Blockers: native ABI-3 cdylib rebuild + ARM64 headed smoke; dialog/clipboard hosts from W4; merge with remote CanvasApp/HuiRenderer.
- Latest branch/SHA: `repair/canvas-rnote-parity` @ `a44bdf2` (based at `4d30846d`, needs forward-port to `5431052e`).

## BOARDS / APPFLOWY

- Donor: `appflowy-board` `804d7898` (widget scope)
- Audit: W1 `appflowy-parity-matrix.md` (34 rows: PASS 18 / PARTIAL 10 / MISSING 6, zero bridge/HUI blocks).
- Bridge/contracts: W3 `1a98a88` adds `CreateGroup/RemoveGroup/RenameCard/RemoveCard` + adapter + allowlists; stable-ID validation; orphan-safe hierarchy. No fake properties/filter/sort (correctly out of scope).
- Controller/model: HUI scenes now inline rename + delete + add-group toolbar, typed commands → persist → reopen restores; Flutter PoC rename/add/delete + counter fix (format unchanged).
- HUI: keyboard/non-drag equivalents working + persisted; pointer DnD + virtualization filed as NEEDS-FROM-W4.
- Functional parity: PARTIAL (donor-widget scope: CRUD/move/reorder/persist PASS; DnD pointer + virtualization pending; full-app properties/filter/sort need product decision).
- Visual parity: MISSING (no donor/HUI screenshot comparison).
- Blockers: W4 DnD primitive + virtualised lane; `flutter test` needs approved VM (no SDK here).
- Latest branch/SHA: `repair/boards-appflowy-parity` @ `1a98a88c` (based at `4d30846d`).
- Tests: contract 44/44, HUI 34/34, 4 static gates pass (W3 repaired 3 stale baseline gates).

## SHARED HUI

- Current state: vendor inventory done; new `HUI/shared/CakeOS.Hui.SharedComponents.csproj` BESIDE `vendor/` (survives `stage-donor.ps1` wipe): HeaderBar, Toolbar, ActionGroup, ToggleButton, SplitButton, Popover, Dialog, Tooltip, Menu/MenuButton, ColourPicker (+alias), NumericInput, Sidebar, Panel, StatusBar, PropertySurface. Preview-surface hex resolution only.
- Tests: `HUI/shared.Tests` 22/22 pass; Boards hui-tests 28/28 still pass; hosts build 0 warn.
- Immediate missing: renderer hex beyond preview surfaces; Wrap virtualization (>~50 swatches); no product controls built (correct).
- Latest branch/SHA: `repair/hui-donor-parity-components` @ `b7aee985`.

## OTHER DONORS

- W1 `other-donors-audit.md`: Data + Present = correct B-pattern (no violation); Write + 8 others = BLOCKED (nothing in checkout to judge); Canvas MEDIUM-contained / Boards LOW–MEDIUM. No rewrites started. Priority stays 1 Canvas, 2 Boards, rest by severity after inventories.

## Deliveries queued for laptop integrator (NOT merged to integration)

| Branch | SHA | Scope | Tests | Needs laptop |
|---|---|---|---|---|
| `repair/donor-parity-audit` | `51e02085` | `docs/donor-parity/` ×4 only | static (pins + source inventory) | reconcile `docs/donor-parity/` vs `docs/parity/` paths; accept or rename |
| `repair/canvas-rnote-parity` | `a44bdf2` | ABI v3 + Canvas HUI (existing components only) | `dotnet build` Win+Linux green; `cargo test`/smoke NOT run (no toolchain) | 3-way merge vs `127edeca`/`45c25044`/`5431052e`; rebuild cdylib; ARM64 headed smoke; screenshots |
| `repair/boards-appflowy-parity` | `1a98a88c` | contracts + HUI CRUD + PoC | 44/44 + 34/34 + 4 gates | `flutter test` on approved VM; DnD/visual QA |
| `repair/hui-donor-parity-components` | `b7aee985` | `HUI/shared/` + tests | 22/22 + 28/28 regression | confirm `HUI/shared/` placement vs `HUI/CanvasApp`/`HuiRenderer` direction |

## Next (controller)

1. Forward-port W2/W3 onto `5431052e` in review worktrees; 3-way merge ABI v3 + CanvasApp/HuiRenderer overlap; resolve `docs/parity/` vs `docs/donor-parity/` with laptop integrator (propose: keep laptop `docs/parity/` verdicts canonical, W1 matrices as evidence annex).
2. Wire W4 `HUI/shared/` into W2/W3 (colour picker, dialog host, numeric) as follow-up commits.
3. Capture donor reference screenshots + equivalent HUI screenshots; fill visual-compare section per parity gate.
4. Push frequently; never to `main`; no resets/force-pushes.
