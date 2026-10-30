# Donor integration plans — for owner approval, 30 October 2026

Each item is independently approvable. Nothing below is implemented yet. Forking of *sources* is
already authorised and has been carried out where marked DONE; using a donor inside an application is
**not** started, per the owner's instruction.

Evidence labels: **MEASURED** = verified on this PC today; **NOT RUN** = no execution evidence;
**BLOCKED** = cannot be evidenced here.

---

## 1. Rnote engine → Canvas (fork DONE, consumption switch pending approval)

- **MEASURED / DONE:** our engine fork branch `9to1/canvas-engine-20261030` (tip `44ff1089`) exists on
  the controlled remote `CroakyJake12/rnote`, as two commits on the consumed v0.14.2 pin. Both patches
  reproduced the after-hashes our own `source-patches.json` already recorded (`c9297ad60653`,
  `46c0e665915f`), so the fork is verified without a compiler.
- **MEASURED today:** `rnote-poc/Cargo.toml` + `Cargo.lock` still fetch **upstream** `flxzt/rnote` at
  `8cac558`, and `.github/scripts/native16-arm64/rnote-native/rnote_source_patches.py` re-applies both
  patches to the vendor copy at build time, verifying every pre/post hash.
- **Proposed switch:** repoint `Cargo.toml` + `Cargo.lock` at `CroakyJake12/rnote?rev=44ff1089`, then
  replace the patch-application stage with a **verification stage** that asserts the fetched revision
  already contains the reviewed changes (same hashes, no rewriting). The donor gitlink pin and the
  build's consumed revision move to `44ff1089` **together**, because `eng/development/donor_policy.py`
  now treats pin-vs-build-revision divergence as drift.
- **Verification available:** the stage's own tests (`test_rnote_native_stage.py`, 235 lines, pure
  Python) run on this PC. The Rust build itself is **BLOCKED here — no cargo/rustc on this machine**
  (measured; the rustup install produced no usable toolchain), so `cargo check` evidence must come from
  the ARM producer host. I will not claim a compiled bridge.
- **Deliberately excluded:** our `lossless-serialization` work (tip `53981d09`) is written against the
  v0.15 glam API and conflicts with v0.14.2's nalgebra types; **nothing in this repo requests that
  feature** (measured). It stays as unused branch work, not smuggled into the pin.

## 2. Files (WinUI Files app) → 9to1 Files (fork decision made; source pin proposed)

- **MEASURED:** our loose copy of 2,351 files is byte-identical for **2,350** of them to our controlled
  remote `CroakyJake12/Files` at `21d407b5`; exactly one file differs — `AGENTS.md` — and 28 upstream
  files were not vendored. So today this is an *unmodified* vendored copy with a recorded base, not yet
  a modified fork.
- **Owner's rule applied:** 9to1 Files extends the donor's features and replaces its UI with CUI ⇒
  **fork**. The fork's value here is feature/behaviour lineage, not compiled binaries: WinUI/C# cannot be
  9to1 application source under the CUI rule.
- **Proposed steps:** (a) convert the loose copy into a pinned submodule at `21d407b5` so the base
  commit is real rather than implied; (b) resolve the single `AGENTS.md` divergence (restore the donor's
  file or move ours out of the donor tree — I propose moving ours out, since editing a donor file
  silently makes the whole tree "modified"); (c) mine the donor for behaviour to reproduce in CUI:
  folder navigation and sorting/grouping, tag/label model, archive handling, drive/volume listing,
  recycle-bin semantics, and the three-root model (Cloud / 9to1 Local Workspace / This Machine) with the
  storage rules from `docs/development/research/2026-10-30-six-handoffs/README.md` §E; (d) for each
  behaviour copied or adapted, record the donor file and revision in a parity manifest, exactly like
  `Boards/DonorParityManifest.md`.
- **Open sub-decision (item 4 of the earlier set, answered):** Linux "This Machine" roots show plain
  folder names (Documents, Downloads, Pictures, Music), with volumes appearing under their real mount
  points when the user opens the volume view.

## 3. Firefox + Gecko → Haven (fork the application, embed the engine)

- **MEASURED:** the pinned `Browse/Source/FirefoxGecko` checkout is **clean upstream** (`5836a062…`, a
  Mozilla changeset), with no patch, no mozconfig and no carried modification anywhere under Browse, and
  **nothing consumes it today** — `/usr/bin/chromium` and `/usr/bin/chromium-browser` are both absent on
  this PC, so Browse currently has no engine here at all.
- **Owner's rule applied:** fork **Firefox** (application: chrome, tabs, preferences, extension
  surfaces, per-browser features we extend) while the **Gecko engine stays byte-pristine** and ships
  **prebuilt** from a signed upstream build. The census now carries 17 engine scopes
  (`gfx, layout, js, netwerk, dom, xpcom, widget, ipc, storage, hal, modules, intl, memory, mfbt,
  xpfe, toolkit/library`) declared pristine; a modification inside them is reported as unresolved rather
  than allowed to pass as an app-layer change.
- **Proposed steps:** (a) add a versioned **engine artifact manifest** — exact Firefox release/channel,
  candidate build number, architecture (`linux-aarch64` for the ARM target, x64 for this PC), locale,
  artifact path, source changeset, publisher signature identity, verified digest — and verify signatures
  before recording anything; (b) remove or label-dev-only the `/usr/bin/chromium` lookup; (c) drive the
  unmodified engine **out-of-process** and host its surface in our CUI shell, which is where the real
  risk sits: resizing, DPI transforms, wheel/trackpad input, text/IME, focus, popups, accessibility,
  profile isolation, crash recovery and packaging each need a narrow prototype, on this PC **and** on the
  ARM target; (d) unified DevTools per the research pack: Chromium adapter over CDP, Gecko adapter over
  **RDP** (Mozilla removed CDP in Firefox 141), capability-tested per operation, one F12 shell over the
  neutral node/style contract; (e) Firefox **branding/trademark** handled separately from licence
  compliance, since we ship a 9to1-branded browser.
- **Honest limits:** no Gecko build, embedding or ARM artifact verification has been done here
  (**NOT RUN**), and Chrome for Testing publishes **no Linux ARM64** build, so the ARM route is
  Gecko-first.

## 4. Avalonia framework fork (fork action proposed; needs a build to verify)

- **MEASURED:** `framework/CUI/vendor/Avalonia` is a loose tree of 5,273 non-`external` files against
  upstream tag `12.0.1` (`fe2741d6`): 21 files **modified** by us (including
  `Avalonia.Controls/ToolTip.cs`, `ContextMenu.cs`, `TopLevel.cs`, `Primitives/Popup.cs`,
  `Presenters/TextPresenter.cs`, `Avalonia.Base/Utilities/Ref.cs`, several `.csproj`,
  `build/BuildTargets.targets`, `NuGet.Config`, three `packages/Avalonia/*.props|targets`), **19 files
  added** by us (e.g. `CakeOS.Cui.AvaloniaFork.csproj`, generated interop such as
  `src/Avalonia.Native/Interop.Generated.cs`, `src/Windows/Avalonia.Win32/**.Generated.cs`,
  `src/Browser/Avalonia.Browser/staticwebassets/*`), 2,948 verified byte-identical, plus 2,245
  **untracked** build outputs sitting inside the vendor tree (`git ls-files` shows 0 tracked bin/obj, so
  they are clutter, not provenance). Spot-verified sample of why those are real fork changes, not noise:
  `Popup.cs` gains a `BeforeShow` event raising the real `PopupRoot` destination so authored presentation
  policy can request per-pixel alpha before first show; `TextPresenter.CreateTextLayoutInternal` becomes
  `protected virtual` and gains `RefreshCaretBounds()`; `Avalonia.Base.csproj` comments out the
  `Avalonia.Build.Tasks` project reference in favour of the CUI compiler.
- **Correction to my own earlier reading:** I first reported that `SOURCE-MANIFEST.txt`'s 20 blobs did
  **not** match upstream 12.0.1. That was a bug in my comparison script, not a finding. Direct check:
  for `src/Avalonia.Base/Metadata/AmbientAttribute.cs` the manifest blob, the upstream blob and the
  on-disk file hash are all `1c85a67641fb…`; the manifest covers **20/20** files exactly.
- **The real gap, stated accurately:** `SOURCE.md` says the CUI fork "stages the complete
  `src/Avalonia.Base/Metadata` directory" (20 files) and calls the source "actual, unmodified Avalonia
  source", but the vendored tree is a near-full Avalonia checkout — 5,273 files with 21 modifications and
  19 additions — while `SOURCE-MANIFEST.txt` only covers those 20. So the provenance record understates
  both the vendored scope and our own modification surface.
- **Proposed fork action:** create our own branch on a controlled Avalonia remote from `fe2741d6`,
  commit exactly those 21 modifications + 19 additions as visible commits with reasons, then vendor it
  as a pinned submodule at that fork revision. The existing
  `CakeOS.Cui.AvaloniaFork.csproj` keeps compiling from the same path.
- **Verification required before switching:** build the CUI framework on this PC after the switch
  (dotnet only, no new toolchain), because this is the one donor whose tree our build actually
  compiles. Until then the loose tree stays, so nothing regresses.
- **Licence:** upstream MIT, notices already at `framework/CUI/../LICENSES/Avalonia-MIT.md`; our
  modifications keep the MIT notice and must be listed in the package copyright file.

## 5. DuckDB → Data (embed, prebuilt wheel)

- **MEASURED:** `Data/workers/duckdb_worker.py` requires exactly `1.5.5`; the vendored C++ pin
  (`d8cdaa33fd`, tag v1.5.5) is provenance only — Data imports the **Python wheel**, and that dependency
  is pinned nowhere in the repo.
- **Proposed:** add `Data/workers/requirements.in` declaring `duckdb==1.5.5`, resolved into
  `Data/workers/locks/<platform>-<python>.txt` as **hash-locked** wheels (pip hash-checking mode,
  `--no-index` at build time), with a check that the lock agrees with `REQUIRED_DUCKDB_VERSION`; package
  an isolated wheelhouse/runtime into the ARM build instead of ever importing the user's global Python.
  Upstream publishes a cp312 manylinux **aarch64** wheel for 1.5.5 (research pack; **NOT** downloaded or
  hash-verified here).
- **Tests to add:** offline startup, missing dependency, wrong version, wrong ABI, corrupt wheel bytes,
  no leakage from the system Python, and worker launch on x64 + ARM64.

## 6. llama.cpp → Dulche/Thunder (classification deliberately unresolved)

- **MEASURED:** the controlled remote exists (`CroakyJake12/llama.cpp`, 805 branches) but no gitlink and
  no vendored source; the census records `UNRESOLVED` on purpose.
- **Proposed first step (measurement, not code):** inventory what Thunder changes — CUDA/host-manager
  work, resident-budget behaviour, PLE handling — against the upstream revision we build from. If a
  change lands in llama.cpp source ⇒ fork; if everything stays in our host layer ⇒ embed. Either way the
  answer must come with the file list, not from the phrase "reviewed enhancements".

## 7. Unsloth Studio → Spaces model browser (conditional review, not adoption)

- **MEASURED:** `studio/` is AGPL-3.0-only (5,659 SPDX headers), the model browser is
  `studio/backend/hub` (88 AGPL headers), the UI is SvelteKit + Tauri/Rust (must be CUI regardless), it
  ships its own llama.cpp model host + router + tunnel/auth, and its Hugging Face path states **no
  support for PLE GGUFs** while this PC's live model is a PLE GGUF.
- **Proposed:** keep it as a **behaviour parity checklist** (the MB-01…MB-13 rows in the research pack)
  unless a specific review shows `hub`'s catalogue/download subsystem can be separated from their
  model-host/auth/tunnel stack and consumed across a process boundary at AGPL's terms. Rewriting their
  feature list in CUI is compatible with the owner's "keep or replace every non-proprietary feature"
  instruction and carries no licence exposure.
- **Hard invariant either way:** the resident Strata host is never stopped, replaced or commandeered by
  donor code.

## 8. Enforcement so this cannot rot

- `eng/development/donor_policy.py census --repo .` (28 donor rows) already reports: pin-vs-build
  revision divergence, recorded modifications absent from the pin, unclassified changes inside a donor
  tree, donor edits inside scopes declared pristine, and loose vendored trees with no pin. **MEASURED**
  first run produced 5 findings, all real. 13 tests run on real tiny git fixtures; they caught two bugs
  in my own first draft.
- Proposed: run the census as an owning check in CI and before each ARM export, so "declared" can never
  again be mistaken for "integrated".
