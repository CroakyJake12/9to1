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

## 4. Avalonia framework fork (fork branch CREATED and pushed; switching the vendored tree needs approval)

- **DONE under the forking authorisation:** created the controlled fork
  `https://github.com/CroakyJake12/Avalonia` (it did not exist: `CroakyJake12/avalonia` returned
  "Repository not found" when measured) and branch `9to1/cui-framework-20261030`, tip
  `b5bdd8cb9ada162d0282ecb55d4f63409532eadd`, on the declared upstream base tag `12.0.1`
  (`fe2741d6`). Our modification is split into three reviewable commits: framework build routing through
  CUI instead of the Avalonia meta-package (14 files), the popup/text/ref seams CUI needs (6 files), and
  a headless-session startup race fix (1 file).
- **Verified:** all 21 files on the fork branch are byte-identical to the files in this directory
  (21/21 by git blob identity), and `framework/CUI/vendor/Avalonia/FORK-MANIFEST.txt` records both blob
  identities per file. Also verified the 21 are real content changes, not line-ending noise: 21 with
  content differences, 0 line-ending-only.

- **MEASURED (one filter, stated so it is reproducible):** files under
  `framework/CUI/vendor/Avalonia` whose path has no `external` component: **5,274**, of which **2,245**
  are build outputs that `git ls-files` shows are **not tracked** (clutter, not provenance). Comparing
  the rest against upstream tag `12.0.1` (`fe2741d6`) by git blob identity: **2,872 byte-identical**,
  **20 modified C#/build-source files + `NuGet.Config` = the 21 on the fork branch**, and **21 files that
  exist only in our tree** once build outputs are excluded.
- The 21 modifications include `Avalonia.Controls/ToolTip.cs`, `ContextMenu.cs`, `TopLevel.cs`,
  `Primitives/Popup.cs`, `Presenters/TextPresenter.cs`, `Avalonia.Base/Utilities/Ref.cs`, several
  `.csproj`, `build/BuildTargets.targets`, `NuGet.Config` and three `packages/Avalonia/*.props|targets`.
  Our own 21 additions include `CakeOS.Cui.AvaloniaFork.csproj`,
  `build/upstream-resource-targets/*.props|targets`, generated interop
  (`src/Avalonia.Native/Interop.Generated.cs`, the four Win32 `*.Generated.cs`) and the browser
  `staticwebassets` JS. Spot-verified sample of why the modifications are real fork changes and not
  noise: `Popup.cs` gains a `BeforeShow` event raising the real `PopupRoot` destination so authored
  presentation policy can request per-pixel alpha before first show;
  `TextPresenter.CreateTextLayoutInternal` becomes `protected virtual` and gains
  `RefreshCaretBounds()`; `Avalonia.Base.csproj` comments out the `Avalonia.Build.Tasks` project
  reference in favour of the CUI compiler.
- **Correction to my own earlier reading:** I first reported that `SOURCE-MANIFEST.txt`'s 20 blobs did
  **not** match upstream 12.0.1. That was a bug in my comparison script, not a finding. Direct check:
  for `src/Avalonia.Base/Metadata/AmbientAttribute.cs` the manifest blob, the upstream blob and the
  on-disk file hash are all `1c85a67641fb…`; the manifest covers **20/20** files exactly.
- **The real gap, stated accurately:** `SOURCE.md` says the CUI fork "stages the complete
  `src/Avalonia.Base/Metadata` directory" (20 files) and calls the source "actual, unmodified Avalonia
  source", while `SOURCE-MANIFEST.txt` covers only those 20. Measured reality is a near-full Avalonia
  checkout carrying our 21 modifications and 21 additions. So the provenance record understates both the
  vendored scope and our own modification surface — corrected in `SOURCE.md` on 30 October 2026.
- **Fork action: DONE** (see the block at the top of this item). What remains is the *switch*: vendor
  this directory as a pinned submodule of the fork revision instead of a loose copy, which changes how
  the framework builds and so needs the verification below plus owner approval.
- **Open question raised by the measurement:** the fork branch carries **only the 21 modifications** —
  verified as 21 `M` entries and no additions. Our own 21 additions (`CakeOS.Cui.AvaloniaFork.csproj`,
  `build/upstream-resource-targets/*.props|targets`, generated interop such as
  `src/Avalonia.Native/Interop.Generated.cs` and the four Win32 `*.Generated.cs`, and the browser
  `staticwebassets`) are **not** on it. Before the vendored directory can become a submodule checkout of
  the fork, that has to be decided: put the authored additions (`CakeOS.Cui.AvaloniaFork.csproj`, the
  `upstream-resource-targets` props/targets) on the fork branch, and treat the generated interop and
  browser assets as build output regenerated by the build rather than donor source. My earlier note in
  this item wrongly implied `CakeOS.Cui.AvaloniaFork.csproj` was already on the branch — it is not, and
  the check that should have added it failed with a pathspec error that I then did not act on.
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

## 6. llama.cpp → Dulche/Thunder (resolved by measurement: EMBED, not a fork)

- **MEASURED 30 October 2026**, replacing the earlier unresolved label: `Home/Source/Dulche/llamacpp`
  contains **no upstream C/C++ at all** — 26 `.py`, 5 `.json`, 4 `.md`, 1 `.sh`, 1 `cpp-LICENSE` — and its
  own README states the directory "holds 9-1 integration/broker code, **not** upstream llama.cpp".
- Upstream `ggml-org/llama.cpp` and the controlled fork `CroakyJake12/llama.cpp` are pinned to
  `53ed051ce5e8193652e449f43216ca3859454f49` in `Dulche/Source/DONOR-PROVENANCE.md`; CI job
  `build-pinned-llamacpp-cpu` produces the binary, and
  `shared/tests/Haven.Core.Tests/LlamaCppRuntimeTests.cs` launches an executable path.
- So the engine is consumed unmodified behind our broker boundary: **no fork required**. Evidence limits
  recorded in that README are not upgraded here — GPU backends are unbuilt/unbenchmarked, and inference is
  not runtime-proven until an approved GGUF is actually loaded and prompted. This PC's live model runs on
  the resident Strata host, a separate runtime from this broker path.

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

## 8. The 17 declared-but-never-vendored donors (decision needed, gate is red because of them)

- **MEASURED:** `eng/check-donor-sources.py` reports **17 issues across 23 bindings**, all of the form
  `no app-local source tree`, for LibreOffice (Write, Present, Data), VSCode/VSCodium (Dev),
  GStreamer/GES (Wave, Motion), glycin/loupe (Picture), libvterm (Terminal),
  evolution-data-server/libical (Planner), llama.cpp (Dulche), Wine/WinBoat and gnome-shell/mutter (OS).
  These pre-date this session: my own two pin failures were fixed, taking the count from 19 to 17.
- The registry declares them, the gate demands their source, and nothing vendors them. Both positions
  cannot stay as they are: either vendor at a pinned revision, or make the declaration explicitly
  aspirational so the gate stops demanding source for intent that no build consumes yet.
- Under the owner's rule the two groups are: **fork when used** (LibreOffice, VSCode/VSCodium, Wine,
  WinBoat, gnome-shell, mutter) and **embed prebuilt** (GStreamer, GES, glycin, loupe, libvterm,
  evolution-data-server, libical, llama.cpp).
- Cost measured on this PC: cloning LibreOffice or VS Code is roughly 1–2 G each against 542 G free, so
  disk is not the obstacle; the obstacle is that unconsumed multi-gigabyte trees would ride into every
  guarded export unless they are pinned-but-not-checked-out, which is why §4 rule 6 exists.

## 9. Enforcement so none of this rots again

- Run both the donor gate and `eng/development/donor_policy.py census` in CI and before each ARM export.
  Today **neither is wired into CI** (measured: no workflow references `check-donor-sources`).
- The census answers "is this artifact modified, and does the build fetch the revision we pinned"; the
  gate answers "is the declared donor source materialised and pinned". They are complementary; the
  proposal is to run both, not to merge them, so each question keeps a separate answer.



- `eng/development/donor_policy.py census --repo .` (28 donor rows) already reports: pin-vs-build
  revision divergence, recorded modifications absent from the pin, unclassified changes inside a donor
  tree, donor edits inside scopes declared pristine, and loose vendored trees with no pin. **MEASURED**
  first run produced 5 findings, all real. 13 tests run on real tiny git fixtures; they caught two bugs
  in my own first draft.
- Proposed: run the census as an owning check in CI and before each ARM export, so "declared" can never
  again be mistaken for "integrated".
