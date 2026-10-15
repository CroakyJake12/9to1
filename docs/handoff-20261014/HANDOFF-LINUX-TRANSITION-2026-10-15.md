# Handoff — Spaces/Chat CUI merge, pass of 2026-10-14/15

Written by the outgoing agent immediately before being moved Windows → Linux. Everything below is
verified state as of `origin/main = 971e0ed9`, not recollection.

---

## 0. Read this first

This repository is large, partly vendored, and has **five git worktrees**. Two facts will save you
an hour of confusion:

1. **The branch you were working on is now `main`.** Work was fast-forwarded to `origin/main` at
   `971e0ed9`. Work on a fresh branch from `origin/main`; do not resurrect
   `opencode/spaces-handoff-20261014` (it is identical to `main` and will be confusing).
2. **Do not touch the other four worktrees or `stash@{0}`.** See §6.

---

## 1. What this pass accomplished

The governing directive is in `docs/handoff-20261014/MERGE-DIRECTIVE-2026-10-14.md`: **merge HUI
into CUI; CUI is the single UI authority; the spec takes precedence over framework behaviour.**

26 commits, fast-forwarded onto `main` (no history rewrite, no force-push). Net diff:
7,900 files, 97,596 insertions, 2,141,090 deletions — the deletions are almost entirely the
`docs/releases` cleanup, explained in §5.

### Stage status

| Stage | State | Evidence |
|---|---|---|
| **M1** — CUI is theme authority | **Done** | `UserPreferencesService.ApplyThemeChoice` resolves through `CuiThemeCatalog` and publishes `CuiSurfacePaletteCatalog.ActiveTheme`, which nothing had ever written before |
| **M2** — CUI owns the Spaces palette | **Done** | `SurfacePaletteCatalog.For` routes Chat/Go/Spaces to `CuiSurfacePaletteCatalog`; accent override threaded through the continuous overload |
| **M3** — HUI Prefab/DynamicUI → CUI | **Partial** | See §4 |
| S:L00014 Space rail + header | **Not done** | `TopRail` still renders at `Grid.Row="1"` (bottom) of `RowDefinitions="*,Auto"` |

### Framework capabilities added

- **Per-item template selection in `Repeat`** — `<Repeat Template="...">` resolves against the
  Repeat's own `Definition="true"` children. This unblocked M3: HUI's `CreateItem(template, …)`
  chose a template per item and CUI could not express a mixed list at all. New diagnostics
  `CUI047`, `CUIR053`, `CUIR054`. Covered by `CuiPerItemTemplateTests` (4/4).
  - **A latent bug surfaced here**: when a template changed under a stable key, the superseded row
    kept its scope subscribed *and stayed parented*, rendering alongside its replacement. Fixed by
    explicit retirement.
- **Exact binding-key resolution in `CuiViewModel`** — previously any dotted path resolved by its
  last segment alone, so `Conversation.Title` and `Status.Message` were the same lookup and
  whichever was published last answered both. Fixed additively (exact key first, last segment as
  fallback). `CuiViewModelPathResolutionTests` (5/5).

### Sources brought into reality

`ChatSpace.cui` had **47 compiler diagnostics** and `SpacesHome.cui` had **30**, and neither had
ever been compiled — nothing loaded them. `SpacesHome.cui` even carried a comment asserting every
element and attribute was canonical CUI. Both are now canonical and enforced by
`CuiCanonicalApplicationCompilationTests.Actual_spaces_and_chat_source_compiles_against_canonical_cui`.

`ChatSpaceCuiBindings` is the new CUI binding owner for the Chat Space, and
`ChatSpaceCuiRenderingTests` (4/4) renders the authored resource against real `ChatSpaceViewState`.

---

## 2. Linux transition — verify these before trusting anything

Everything in this section is a **difference that will bite you**, not speculation. I could not
verify any of it from Windows.

### 2.1 Line endings — highest risk

- `core.autocrlf` is **`true`** on this Windows checkout. It will be unset/false on Linux.
- Root `.gitattributes` only covers `*.sh text eol=lf` and `*.py text eol=lf`. **`.cs`, `.cui`,
  `.axaml`, `.xaml` are not covered.**
- Every commit this pass emitted an `LF will be replaced by CRLF` warning for exactly these files.
- **Action**: before your first commit on Linux, confirm the diff contains only intended lines. If
  `.cui`/`.cs` churn appears wholesale, the eol policy changed — consider adding explicit
  `text eol=lf` for source types rather than letting it drift per-platform.

### 2.2 Case sensitivity

Windows resolved paths case-insensitively; Linux will not. Live hazards:
- `HavenUI` vs `Haven.UI` — **both** spellings exist and are different directories.
- `9to1 Workspace` and `9to1 OS` contain a space. Quote every path in bash.
- The repo mixes `Repositories`/`repository`-style variants. If a build fails only on Linux,
  suspect casing before anything else.

### 2.3 Toolchain

- Windows used **`C:\dotnet-x64\dotnet.exe`** — an x64 SDK 10.0.401 installed side-by-side
  deliberately, because the default `dotnet` on that host was **arm64** and **cannot run the
  x64-pinned test assemblies**. The tests appeared broken until the right binary was used.
- On Linux: find out whether `dotnet` is x64 or arm64 **before** trusting a test run. A wrong
  architecture fails in ways that look like product bugs.
- **Submodules must be initialised or the build cannot proceed.** `XamlX` and `Avalonia.DBus` were
  initialised this pass (`864a052`, `009d481`) and are load-bearing. There are ~25 submodules;
  most are heavy vendored upstreams (Firefox, LibreOffice, Wine, GStreamer) that you should not
  need for CUI/Spaces work.

### 2.4 Shell

All prior commands were PowerShell. Two traps that carry over:

- **Never round-trip a source file through `Set-Content`/`Get-Content` or `sed`/`awk` in place.**
  It re-encoded the file and corrupted a `U+2022` character, which broke a test in a way that
  looked like a product defect. Use the editor tools.
- Prefer `rg`/grep with explicit paths; the tree is huge and most searches will otherwise crawl
  `vendor/`.

---

## 3. Test baselines — what "green" actually means

This suite has a large pre-existing failure count. **A single run cannot prove absence of
regression.**

| Suite | Baseline | Note |
|---|---|---|
| CUI Runtime | **349/349** | Fully green. This is the reliable signal. |
| Spaces (`HavenOS.Spaces.Tests`) | **78/78** | Fully green. |
| CUI Markup | 34/34 · Build.Tasks 25/25 | green |
| CUI Compiler | **7 failed / 43** | 5 are `apps/Web/**` files that **do not exist in this repo** (pre-existing); 2 are `MailWorkspace.cui` and `ModelPicker.cui` — out of scope, see §4 |
| **Native** (`Haven.Desktop.Tests`) | **110 failed / 1450 passed / 1674** | **Flaky band 110–113.** Same code produced 110, 111 and 113 on different runs. |

**How to verify a change:** isolated probes only. Never cite a single full-suite run as proof.
The known undiagnosed native root cause is a real `Haven.Application` concurrency-custody
mismatch — `TaskRunProcessProducerContext.DemandExternalJoin` throws
`"An actual Agent source callback cannot join its own process retirement."` while tests expect
refusal via a different mechanism. Characterised, not fixed.

---

## 4. M3 — what is genuinely left

Read `docs/handoff-20261014/M3-SCOPE-2026-10-14.md` first; §0 of it is the measured table.

**Done:** CUI capability (Repeat + per-item Template + Prefab parity), both Spaces/Chat `.cui`
sources canonical, the Chat binding projection, and rendering against real domain state.

**Not done:**

1. **Shell wiring — the largest item.** No Spaces or Chat surface has a native CUI host.
   `SpaceTaskWidgetPage.cs` and `AssistantsNativeCuiSurface.cs` are the reference patterns
   (`CuiSceneHost` + `ShowAsync(new CuiNativeScene(...))`). The live Chat is `ChatHavenScene`
   (HUI) and it carries threading, send queueing, dual-model selection and attachments that
   `ChatSpace.cui` does **not** model. **Do not remove `ChatHavenScene` until the CUI surface
   demonstrably covers that behaviour.**
2. 21 production `.hui` templates unmigrated. The two Chat message templates are a strict
   superset relationship and unify; `ChatToolActivity` is a structurally distinct Grid card and
   is exactly the per-item `Template` case.
3. 8 scene files still drive HUI DynamicUI imperatively (`CreateItem`/`DeleteItem`/`Clear`).
4. `HavenPrefabCatalog.FromAssembly` / `HavenDynamicUITemplateCatalog.FromAssembly` **must not be
   deleted** until CUI owns a verified successor — they are load-bearing.
5. `MailWorkspace.cui` (111 diagnostics) and `ModelPicker.cui` (7) — same defect class as the two
   fixed here, outside the Spaces/Chat delivery.
6. Native CUI capture is itself among the 110 baseline failures
   (`NativeAssistantCanonicalVisualCaptureTests…emit_opt_in_captures`, 4 appearances), so the
   route that would give native visual evidence for a CUI surface is **not currently green**.

### Three traps found by rendering that the compiler cannot catch

Binding a path that names a property which does not exist compiles cleanly and renders nothing
(`attachment.Name` vs `MessageAttachment.OriginalName`, `activity.Name` vs `ToolActivity.Title`).
`CUIR007` refuses two-way binding on `ComboBox`, so `Input Type="Select"` cannot bind its current
value. `ScrollView` has no canonical CUI equivalent — the transcript became a `Container` and
therefore **lost follow-at-end scrolling**, a real behavioural gap.

---

## 5. The `docs/releases` deletion — understand before you touch it

Commit `8b2cfa57` ("Remove outdated/time-sensitive documentation") deleted **8,102 files**: 8,036
under `docs/releases` plus 66 real docs (`AGENTS.md`, editing guides, architecture rules).

Verified before accepting it: no `.csproj` sits at or above a `docs/releases` tree so SDK globbing
never compiled any of the 1,610 deleted `.cs` files; nothing in the repo references a
`docs/releases` path; the whole build and test surface is green with them absent. They are release
archive — review probes, build logs, JSON artifacts from `sol-happy-20261003`.

However the sweep also took **268 verification records** (108 `.trx`, 105 `.md`, 55 `.stderr`),
including `compliance-safety-2026-08-20/TEST_AND_REPRODUCTION_REPORT.md` — an actual security
audit report. Commit `971e0ed9` restored exactly those 268 from `origin/main`.

**Open decision, deliberately left to you:** the 163 `.trx`/`.stderr` files are regenerable
machine output and are the weakest of the 268; the 163 `.md` reports and especially the compliance
report are human-written conclusions that do not regenerate. I defended restoring all 268 and would
defend keeping the `.md` ones. If they should go, do it as a normal follow-up commit — **never
force-push to `main`.**

---

## 6. Git state — do not disturb

```
origin/main                       971e0ed9  (in sync with the worktree you were in)
main                              2c4d151b  [behind 26]  checked out at C:/Github/9to1
integration/reconciliation-20261012 3364c9fa [ahead 6, behind 30]  at C:/Github/9to1-integration
codex/cake-id-auth-backup-20261002 7cc52df0  [origin: gone]
opencode/spaces-handoff-20261014  971e0ed9  identical to main
stash@{0}  On main: !!GitHub_Desktop<main>   UNTOUCHED — leave it
```

- `main` is **26 behind** because it was checked out in another worktree; it could not be
  fast-forwarded from the work I was in. Pull in that worktree when convenient.
- `integration/reconciliation-20261012` was ahead 6 / behind 4 before this pass and is now
  **ahead 6 / behind 30**. It needs a rebase before it can merge. Warn anyone who tries.

---

## 7. House rules that were enforced

- **Track native and browser evidence separately.** A recoloured legacy surface is **not** a
  completed migration — this is exactly how `ChatSpace.cui` passed as done for its whole life
  without compiling.
- **Do not claim visual acceptance.** No approved tolerance exists (O22). Frames are diagnostic
  only.
- **Do not weaken a test to match your output.** Prefer fixing the implementation. Where a legacy
  contract genuinely survived the merge, say so and show it passing on its own terms.
- **No blind global search-and-replace** when consolidating names — separate branding, internal
  symbols, persisted IDs, protocol contracts and provenance. (Naming consolidation is deferred:
  37,783 `Haven` / 3,071 `HUI` / 710 `HavenUI` occurrences across 9,302 files.)
- **Never round-trip source files through shell text tools** (§2.4).

---

## 8. Suggested next step

Start with **M3 shell wiring**, since it is the largest item and everything else in M3 is
downstream of knowing whether the CUI Chat surface can replace the HUI one. Concretely: build a
`ChatSpacePage` native host following `SpaceTaskWidgetPage`, **keep `ChatHavenScene` as the live
surface**, and get the delta in observable behaviour written down before proposing a swap. That
delta is the real work; the markup is already done.