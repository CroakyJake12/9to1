# Handoff — 30 October 2026, end of day (donor work)

Owner instruction at wrap-up: finish the unit in flight, commit, write this handoff, stop. No dev instance
was launched for the grip-menu retest ("leave it for the moment"), so **no on-screen native evidence was
produced today** for any bug.

## Source identity

- Branch `main`, `origin/main` = **`0f34ce792621`**. Working tree clean, nothing stashed.
- Commits this session, oldest first: donor pins `91086b00` → licence audit `ef13052c` → Unsloth
  measurement `96454af6` → research pack + 3 self-corrections `af2af0fb` → per-app pins in the gate
  `1b96f038` → llama.cpp resolved to EMBED `f5125a17` → Avalonia fork record `ac377794` → plans `36409dce`
  → coverage audit `63a99754` → audit fix `1d7700e9` → CI ratchet `911719d1` → Avalonia framework
  declaration `0b73daf0` → DuckDB wheel pin `6fc2e981` → declarations + aspirational + bugfixes `b0a84753`
  → committed the untracked provenance record `0f34ce79`.
- Donor forks (not in this repo): `CroakyJake12/Avalonia` branch `9to1/cui-framework-20261030` tip
  **`1c465a47b097e849250f3928a8ea8c475a02ec32`**; `CroakyJake12/Rnote` branch
  `9to1/canvas-engine-20261030` tip **`44ff1089`**. Both pushed.

## Evidence level of every claim in this handoff

- **Test-measured here**: 53 donor-tool tests (`test_donor_policy` 18, `test_donor_coverage` 11,
  `test_check_donor_sources` 13, `test_donor_fork_equivalence` 6, `test_record_donor_audit_baselines` 6) and
  `Data/Tests/test_duckdb_dependency.py` 6 — all pass. Gate/audit/census/ratchet numbers below are from
  runs at these commits.
- **Compiled here**: `Avalonia.Controls.csproj` from the vendored tree — **0 errors, 0 warnings, 41.68 s**
  (guarded dotnet 10.0.301, `-m:1`, no shared compilation). Previously: Boards app, Data app, Boards tests.
- **NOT RUN**: every Rust claim (no toolchain installed), the submodule switch, the Files pin, DevTools,
  Gecko/Chromium engine route, wheelhouse packaging, all ARM packaging, and **all owner-visible bug retests**.
- **Owner-reported, unverified by me**: the live app (pid 572980) is not running; cause unrecorded.

## Measured audit state (reproducible from `0f34ce79`)

`python3 -B .github/scripts/record-donor-audit-baselines.py check` → gate **17** issues / 36 bindings,
census **0** violations / 29 rows / 6 findings, coverage **9** problems. Exit 0, no ratchet breach.

- Registry: **31 upstreams, 36 bindings**. 12 aspirational bindings print as `ASPIRATIONAL:` gate notes.
- The 9 remaining coverage problems are all owner-decision contradictions, deliberately **not** edited away:
  `code-oss`/`vscodium` bound to an app key "Dev" (spec names VS Code/VSCodium/Code-OSS **0 times**, and says
  Build is one application); `eds`/`libical` bound to Planner (spec 11897: "No external application donor is
  currently normative"); `glycin`/`loupe`/`gstreamer`/`ges`/`libvterm` have bindings with no designation
  sentence found.
- Two of my own bugs and their fixes are recorded in `b0a84753`; one honesty correction is in `0f34ce79`
  (the Avalonia gate pass was initially not reproducible from the commit because the provenance file was
  untracked).

## Next executable actions, in order

1. **Rust toolchain, then the Rnote consumption switch (approved item 1).** Toolchain tarballs already
   downloaded and extracted in `/tmp/opencode/rust` (rust/cargo/rust-std 1.99.1, 2026-10-15 channel).
   `install.sh` failed only on `--disable-ldpath` (unrecognized option) — retry without it:
   `sh <dir>/install.sh --prefix=/tmp/opencode/rust/toolchain --no-modify-path`. Then repoint
   `9to1 Workspace/Canvas/rnote-poc/Cargo.toml` + `Cargo.lock` to `44ff1089`, move the gitlink in the same
   commit, replace the ARM vendor-patch stage with a verification stage, run
   `.github/scripts/native16-arm64/rnote-native/test_rnote_native_stage.py` and `cargo check`.
   Census findings 1–4 (Rnote patches and fork tip absent from the consumed pin) close only when this lands.
2. **Avalonia submodule switch (approved item 2).** Authored files are already on the fork
   (`CakeOS.Cui.AvaloniaFork.csproj`, `build/upstream-resource-targets/*`, carried generated inputs with
   `-text` attributes, Inter OFL.txt). Enumerate blast radius first: nested `external/Avalonia.DBus` +
   `external/XamlX` gitlinks, the ARM donor-compiler stage (`build-native-arm64.py` line ~28 builds
   `src/Avalonia.Build.Tasks` from that path), `notices/source-license-inputs.json` (reads `licence.md`,
   `NOTICE.md`, `OFL.txt` **from inside** the tree with pinned digests — provenance files must stay),
   `native16-source-identity.json`, `fork-extra-source-manifest.json`, nine workflow files. Rebuild
   `Avalonia.Controls` afterwards (baseline 41.68 s, 0 errors).
3. **Files pin (approved item 3).** Submodule at `21d407b51dbc2b1bd2733ef615d727345d5dd16f`; base already
   proven by tree identity (`src d4a883424a2d…`, `tests be743d924b43…`). Record our 9-line `AGENTS.md` as an
   **intended** wrapper difference — it is already `action: "replace"` in
   `docs/specification/authority-reconciliation.json`, not drift (my earlier wording was wrong).
4. **Answer the owner's outstanding Unsloth question** (never answered): `studio/backend/hub` is 88 files /
   2.4 M importing only `hub.*`/`fastapi`/`httpx`/`huggingface_hub` with **zero** cross-package imports, so
   the model browser is separable from their model-host/auth/tunnel stack. Remaining objections are
   AGPL-3.0-only vs our GPL-3.0 repo, and "no support for PLE GGUFs" in their HF path while the live model
   is a PLE GGUF.
5. **DuckDB follow-through**: produce the wheelhouse, point `DuckDbDatabaseEngine` at a bundled interpreter
   instead of `pythonExecutable="python3"`, and write the failure-mode tests (missing dep, wrong version,
   wrong ABI, corrupt wheel, offline start, worker launch on x64 + ARM64).
6. **DevTools one-unified-tool requirement** (F12 identical on web and CUI surfaces) — RDP/BiDi basis;
   Mozilla removed CDP in Firefox 141.
7. **Held ARM64 deb**: still held on the owner's "Hold the whole deb until the menu works." The grip-menu
   fix has **no on-screen verification**, so nothing is cut until a dev instance is reloaded and the owner
   confirms.

## Blockers / open decisions for the owner

- **cakemods** (Build site-workspace primary donor, spec 7491): both candidate repos declare **no licence**
  → adoption blocked pending a rights position or an explicit 9to1-native replacement.
- **Strata** (spec 4515 requires Dulche to carry a maintained fork): no upstream identity or pin exists.
  The resident Strata host (~45 GiB, `--resident-budget-gib 41`) must **not** be stopped or investigated in
  a way that disturbs it.
- The four registry-vs-specification contradictions listed above.
- Whether to keep both Rnote pins diverged (Canvas `8cac558fa73e…` v0.14.2, Boards `1a728d6a85db…` v0.15
  line) — currently kept, with `commitReason` recorded.

## Machine state to carry forward

- Disk **542 G free** (was 13 G); receipts in `/tmp/opencode/storage-reclaim-20261030/`. Pre-fork freeze at
  `~/Documents/9to1backup`.
- Scratch: `/tmp/opencode/aval-fork` (348 M, the Avalonia clone used by the equivalence tool),
  `files-up`, `wheelcheck` (the two verified DuckDB wheels), `rust` (extracted, not installed).
- Resident model host untouched and running throughout; the owner's live instance was never signalled.
- Coordination with the parallel Astra worker stayed via repo files/commits only — no messages sent from my
  turns (prefill lag in the owner's app).
