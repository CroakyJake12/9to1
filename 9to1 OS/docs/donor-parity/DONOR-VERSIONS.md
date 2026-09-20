# Donor versions (ground truth)

Worktree: `C:\Users\Jacob\AppData\Local\Temp\opencode\cakeos-donor-audit`
Branch: `repair/donor-parity-audit` (base `4d30846d3afd9aead93e4b26ef4ea4b971d4438c`, tracks `origin/emergency/windows-apps-book4edge`)
Audit date (UTC): 2026-09-16. Method: read local pin files + `Cargo.lock` / `pubspec.lock` + live `git ls-remote` + detached checkout of both donors under `C:\Users\Jacob\AppData\Local\Temp\opencode\_donor-src` (outside the worktree, audit scratch only).

## 1. Rnote (Canvas engine donor)

- Repository: `https://github.com/flxzt/rnote`
- Pinned rev: `8cac558fa73ee63d958cec05124ea2fdbd7d608d`
- Pinned packages: `rnote-engine v0.14.2`, `rnote-compose v0.14.2` (default-features = false, no `ui` feature, no GTK)
- Commit subject at rev (verified by checkout): `chore: bump version to v0.14.2`
- `git ls-remote https://github.com/flxzt/rnote 8cac558fa73ee63d958cec05124ea2fdbd7d608d` resolves (network OK at audit time).

Evidence paths (in worktree):

- `apps/canvas/rnote-poc/Cargo.toml:16-17` — both deps pinned `git = "https://github.com/flxzt/rnote", rev = "8cac558fa73ee63d958cec05124ea2fdbd7d608d"`.
- `apps/canvas/rnote-poc/Cargo.lock:3016-3018` (`rnote-compose 0.14.2`, `source = "git+https://github.com/flxzt/rnote?rev=8cac558...#8cac558..."`).
- `apps/canvas/rnote-poc/Cargo.lock:3045-3047` (`rnote-engine 0.14.2`, same source).
- `apps/canvas/rnote-poc/PROVENANCE.md:5-7` — states `v0.14.2` + rev + `default-features=false`.
- `apps/canvas/rnote-poc/README.md` — boundary statement (HUI owns windowing/input; crate owns coordinate/tool/style/SVG/persistence/camera mapping).

Donor source inspected at this rev (scratch checkout, NOT vendored):

- `crates/rnote-ui/src/*` — `app/`, `appwindow/` (`actions.rs`, `appsettings.rs`, `imp.rs`, `mod.rs`), `canvas/`, `colorpicker/`, `dialogs/` (`export.rs`, `import.rs`, `mod.rs`), `groupediconpicker/`, `penssidebar/` (`mod.rs`, `brushpage.rs`, `shaperpage.rs`, `typewriterpage.rs`, `eraserpage.rs`, `selectorpage.rs`, `toolspage.rs`), `settingspanel/` (`mod.rs`, `penshortcutmodels.rs`, `penshortcutrow.rs`), `strokewidthpicker/`, `workspacebrowser/` (`filerow/`, `workspacesbar/`, `workspaceactions/`), plus `appmenu.rs`, `canvasmenu.rs`, `mainheader.rs`, `penpicker.rs`, `sidebar.rs`, `overlays.rs`, `canvaswrapper.rs`, `contextmenu.rs`, `filetype.rs`, `strokecontentpaintable.rs`, `strokecontentpreview.rs`, `unitentry.rs`, `iconpicker.rs`.
- `crates/rnote-engine/src/pens/*` — `mod.rs` (`PenStyle`: Brush/Shaper/Typewriter/Eraser/Selector/Tools), `penholder.rs`, `penmode.rs`, `shortcuts.rs`, `pensconfig/` (`brushconfig.rs`, `shaperconfig.rs`, `typewriterconfig.rs`, `eraserconfig.rs`, `selectorconfig.rs`, `toolsconfig.rs`), `selector/`, `typewriter/`, `tools/` (`laser.rs`, `offsetcamera.rs`, `verticalspace.rs`, `zoom.rs`), `brush.rs`, `shaper.rs`, `eraser.rs`, `typewriter/mod.rs`.
- `crates/rnote-compose/src/builders/mod.rs:55-96` — `ShapeBuilderType` (13 variants).
- `crates/rnote-engine/src/document/` — `background.rs` (`PatternStyle`: None/Lines/Grid/Dots/IsometricGrid/IsometricDots), `layout.rs` (FixedSize/ContinuousVertical/SemiInfinite/Infinite), `format.rs`, `config.rs`.
- `crates/rnote-engine/src/engine/` — `export.rs` (`DocExportFormat` Svg/Pdf/Xopp; `DocPagesExportFormat` Svg/Png/Jpeg; `SelectionExportFormat` Svg/Png/Jpeg + prefs), `import.rs` (PDF/XOPP prefs), `mod.rs`, `snapshot.rs`, `rendering.rs`, `strokecontent.rs`.
- `crates/rnote-engine/src/camera.rs:76-78` — `ZOOM_MIN 0.2`, `ZOOM_MAX 6.0`, `ZOOM_DEFAULT 1.0`.
- `crates/rnote-ui/src/appwindow/actions.rs:38-177` (action registration) and `:1108-1149` (accelerators) — full `win.*` action + shortcut inventory used for the parity matrix.

## 2. AppFlowy Board (Boards structured-board donor)

- Repository: `https://github.com/AppFlowy-IO/appflowy-board`
- Pinned rev: `804d7898ac0becabf73e45527baf5d5c573cd6bb`
- Pinned package version: `0.1.2` (upstream `pubspec.yaml` at that rev; CakeOS `pubspec.lock` records `0.1.2`)
- Commit subject at rev (verified by checkout): `Merge pull request #52 from AppFlowy-IO/load_cards_on_demand`
- `git ls-remote https://github.com/AppFlowy-IO/appflowy-board HEAD` returns `804d7898... HEAD` (network OK; pin is upstream HEAD-line at audit time).

Evidence paths (in worktree):

- `apps/Boards/README.md:14-18` — repo URL + exact commit + MPL-2.0 selection note.
- `apps/Boards/THIRD_PARTY.md:6-12` — project/repo/commit/version/licence + boundary rules.
- `apps/Boards/appflowy_poc/pubspec.yaml:15-18` — `appflowy_board: git: url ... ref: 804d7898...`.
- `apps/Boards/appflowy_poc/pubspec.lock:4-12` — `ref` + `resolved-ref` both `804d7898...`, `version: "0.1.2"`.

Donor source inspected at this rev (scratch checkout, NOT vendored):

- `lib/appflowy_board.dart` — public exports (group_data, group builders, board_data, styled widgets, board).
- `lib/src/widgets/board.dart` — `AppFlowyBoard`, `AppFlowyBoardConfig`, `AppFlowyBoardScrollController`, `_AppFlowyBoardContentState`, `AppFlowyBoardState`.
- `lib/src/widgets/board_data.dart` — `AppFlowyBoardController` + `OnMoveGroup` / `OnMoveGroupItem` / `OnMoveGroupItemToGroup` / `OnStartDraggingCard` typedefs.
- `lib/src/widgets/board_group/group_data.dart` — `AppFlowyGroupItem`, `AppFlowyGroupController`, `AppFlowyGroupData`, `AppFlowyGroupHeaderData`.
- `lib/src/widgets/board_group/group.dart`, `reorder_flex/*` (`reorder_flex.dart`, `reorder_mixin.dart`, `drag_state.dart`, `drag_target.dart`, `drag_target_interceptor.dart`, `drag_auto_scroller.dart`), `reorder_phantom/*` (`phantom_controller.dart`, `phantom_state.dart`), `styled_widgets/*` (`card.dart`, `header.dart`, `footer.dart`, `widgets.dart`), `transitions.dart`, `utils/log.dart`, `example/`, `test/`.

## 3. HUI donor (CakeAI Haven.UI)

- Repository: `CroakyJake12/CakeAI` (per lock; full URL `https://github.com/CroakyJake12/CakeAI`)
- Pinned rev: `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb`
- Source path: `src/Haven.UI`

Evidence paths (in worktree):

- `havenos.lock:29-37` — `components.HUI.migrationDonor = { repository: "CroakyJake12/CakeAI", revision: "7c021082...", sourcePath: "src/Haven.UI" }`.
- `HUI/vendor/.donor-revision` — file content is exactly `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb`.
- `apps/canvas/rnote-poc/PROVENANCE.md:3` — restates the same HUI donor pin + boundary.
- `apps/Data/README.md:39,42,63` — restates the same pin and the staging contract (CI stages exact platform commit, checks donor revision, runs preview smoke).
- `HUI/vendor/Haven.UI/*` — staged donor tree (Components: Button/Container/Canvas/Text/Popup/Tabs/Slider/Select/Toggle/Page/etc.; Scene/Layout/Input/Rendering/Tokens/...). This audit did NOT re-verify the CakeAI remote itself (no fetch of CakeAI was performed); the pin is verified as *internally consistent* across the four evidence paths above, not as *remote-confirmed*.

## 4. What was NOT pinned / NOT verified here

- No Firefox, LibreOffice Writer, EDS/libical, PTY/libvterm, VSCodium, WinBoat, Wine-version, GStreamer/GES, or glycin pins exist in this checkout (`havenos.lock` only pins gnome-shell, mutter, HUI donor, HavenAI donor, llama.cpp). See `other-donors-audit.md`.
- `apps/Data` (Calc+DuckDB) and `apps/present-engine` (Impress) document their own distro/runtime versions in their READMEs (LibreOffice 26.2.5.2 / DuckDB 1.5.5 in CI lanes); those are integration-runtime versions, not UI-migration donors, and are classified in `other-donors-audit.md`.
- The `_donor-src` scratch checkouts used for source citation were deleted-or-not-tracked outside the worktree and are intentionally NOT part of this commit. Only `docs/donor-parity/*` is committed by this audit.
