# Naming consolidation and documentation cleanup â€” plan, inventory and blockers

**Date:** 2026-10-14 Â· **Authority:** explicit owner instruction, 2026-10-14
**Status:** Step 1 (documentation) **DONE**. Step 2 (renaming) **NOT STARTED â€” see Â§4 blockers.**

The owner instruction resolves the open decision in
`DECISION-2026-10-14-CHAT-SURFACE-AUTHORITY.md`: the rule files were outdated, and HUI
(HavenUI) folds into CUI rather than continuing as a parallel UI authority. Gate B is
unblocked in principle. **Gate B remains NOT RUN** â€” no visual evidence exists yet (Â§11 of
`BASELINE-2026-10-14.md`: the native suite cannot execute on this ARM64 host).

---

## 1. Step 1 â€” outdated documentation removed (DONE)

The instruction was to remove time-sensitive or inaccurate `.md` files and keep only material
that is currently accurate and not time-bound. **171 of 397 `.md` files removed (43%).**
All deletions are `git rm`-tracked and fully recoverable from branch history.

### 1.1 Removed â€” conflicting authority and editing rules

These were the files that produced the conflict and the blocker:

| File | Why removed |
|---|---|
| `9to1 Workspace/shared/HAVEN_UI_RULES.md` | Contained the **Theme Modification Lock** that blocked the Professional reference correction. Outdated by the owner's ruling. |
| `9to1 Workspace/shared/AGENTS.md` | Agent authority file mandating the above; "Haven" throughout |
| `docs/AGENT_TOOL_EXECUTION_RULES.md`, `ARCHITECTURE_RULES.md`, `BACKGROUND_LEARNING_RULES.md`, `GENUI_RULES.md`, `PLATFORM_RULES.md`, `SECURITY_RULES.md`, `VALIDATION_RULES.md` (under `shared/docs`) | Rule set superseded by the specification |
| `9to1 Workspace/shared/Haven-App-Editing-Guide/` (10 files) | Duplicate editing guideline tree, mirrored by `shared/docs/App-Editing-Guide/` |

### 1.2 Removed â€” time-bound execution evidence

- `shared/docs/PASS-REPORT-*.md` (13) and `PASS9*.md` (3)
- `shared/docs/HAVEN-FULL-PROJECT-HANDOVER.md`, `HAVEN-MASTER-AUDIT-CHECKPOINTS.md`, `HAVEN-CONTINUATION-VALIDATION.md`
- `shared/docs/HAVENOS-QA-PROVENANCE-INTEGRATION-20260830{,-LIVE,-POSTFREEZE}.md`
- `shared/docs/{qa,releases,recovery,worker-handoffs,backlog,requirements}/`
- `shared/docs/ui/present-{editor-redesign,interaction-engine}.md` â€” Present-specific, excluded by the handoff scope
- `9to1 OS/docs/{MIGRATION.md,parity/STATUS.md,parity/DONOR-PARITY-STATUS.md,donor-parity/STATUS.md}`

### 1.3 Removed â€” programme status pinned to superseded revisions

- `docs/{9TO1_COMPLETION_STATE.md, MASTER-MIGRATION-STATUS.md, CONSOLIDATION.md, 9TO1_WORKER_DEPLOYMENT.md, SOURCE-CLASSIFICATION.md, donor-source-convergence-2026-09-24.md}`
- `docs/{history,proposals,releases}/` (98 release-archive files)
- `9to1 Workspace/Picture/IMPLEMENTATION-CHECKPOINT-20261007.md`, `9to1 Workspace/Files/CUI/IMPLEMENTATION-STATUS.md`

### 1.4 Over-deletion corrected (2026-10-14, owner review)

The original cleanup removed material that was **not** proven obsolete. Corrected:

- **The two editing-guide trees were not duplicates.** `Haven-App-Editing-Guide/` held three
  documents absent from `docs/App-Editing-Guide/`: `HOME-CALL-AND-PLAN.md`,
  `SURFACES-AND-VISUAL-SYSTEM.md`, `TEACH-AND-CHAT-GROUPS.md`. All three **restored** into
  `docs/App-Editing-Guide/`. The superseded duplicate tree is not reinstated.
- **Present requirements were wrongly discarded.** The Spaces handoff excluded
  *Present-specific shell work*; that does not authorise deleting current Present requirements.
  `present-editor-redesign.md` (60 lines) and `present-interaction-engine.md` (148 lines) are
  **restored** to `shared/docs/ui/`.
- **Useful current requirements were salvaged, not dropped.** The security, architecture/identity,
  platform-parity and validation obligations from the deleted `*_RULES.md` set are consolidated
  into `shared/docs/CURRENT-REQUIREMENTS.md`, with naming updated to 9to1 / CakeUI / CUI. The
  superseded appearance authority is **not** reinstated.

### 1.5 Kept - accurate, non-time-bound reference

`framework/CUI/Themes/README.md` (CUI theme/property/typography contract - matches the spec and `CUI-PREF-201`), `framework/CUI/Components/*.cui` (incl. `ThemeCreator.cui`), `framework/CUI/Runtime/Assets/Fonts/README.md`, `eng/cui/README.md`, `shared/docs/{architecture,development,ui,App-Editing-Guide}/`, `shared/docs/CURRENT-REQUIREMENTS.md`, `shared/docs/README.md`, root `README.md`, `THIRD_PARTY.md`, and this handoff's own folder.

**Verification after deletion â€” exact checks performed and their limits.**

Performed:
- `./eng/9to1.ps1 build -Component core -Configuration Release` â€” **0 errors / 0 warnings**
- `NineToOne.Cui.Markup.Tests` â€” **34/34**
- `HavenOS.Spaces.Tests` â€” **74/74**

An earlier revision claimed "No source depended on the removed files." **That claim was wrong
and is retracted.** A later reference scan found 47 surviving `.md` references and 16
code/config references to the deleted names.

What the scan found, and what was done:

- **Real regression, fixed:** `9to1 Workspace/shared/tools/release/Test-HavenRules.ps1` was a
  release gate requiring `AGENTS.md`, `HAVEN_UI_RULES.md` and four `docs/*_RULES.md` to exist,
  requiring `AGENTS.md` to reference them, and asserting the literal `Theme Modification Lock`
  phrase. Deleting those files broke it. Rewritten to require
  `docs/CURRENT-REQUIREMENTS.md` instead. It now runs clean.
- Its **Montserrat / SemiBold / no-Light-weight** checks are *current* obligations matching
  `CUI-PRO-202`, so they were **kept**; only the deleted-file, cross-reference and
  appearance-freeze assertions were removed. Two Android checks were already stale (they resolve
  under `shared/src/Haven.Android`, which does not exist â€” that project is under
  `9-1 OS (Android)`) and were dropped.
- **Not a regression:** the `AGENTS.md` hits in `ChatSessionService`, `AgentInstructions`,
  `CheckpointRepository` and `WorkspaceCheckpointRestore` are the runtime *agent instruction
  file* feature (`agent.md`/`AGENTS.md` discovery), unrelated to the deleted document.

Limits: `build -Component core` compiles only the CUI/Home/Spaces graph. It does **not** compile
`Haven.Desktop.Tests`, which could not build at that point and therefore could not have
detected a dangling reference there. No repository-wide markdown link checker was run, and no
Android/other-platform project was built.

---

## 2. Naming inventory (measured, not estimated)

Across **9,302** scannable source files (`.cs`, `.axaml`, `.cui`, `.hui`, `.csproj`, `.sln`,
`.json`, `.props`, `.targets`, `.py`, `.ps1`; excluding `obj/`, `bin/`, release archives):

| Token | Occurrences | Target per instruction |
|---|---|---|
| `Haven` | **37,783** | `9to1` |
| `HavenOS` | 3,226 | `9to1 OS` (already the product's own tree name) |
| `HUI` | 3,071 | `CUI` |
| `HavenUI` | 710 | `CakeUI` |
| `CakeOS` | 1,513 | â€” (already the CUI framework's own root: `CakeOS.Cui.*`) |
| `NineToOne` | 509 | `9to1` |
| `9to1` | 4,507 | current |

The intended end state is therefore **`9to1` / `CakeUI` / `CUI`**, with `CakeOS.Cui.*` retained
as the CUI framework's existing assembly root (it is already spec-correct and is what
`eng/cui/first-party-inventory.json` already records).

---

## 3. Legacy APIs with no spec successor (DO NOT REMOVE YET)

The instruction asks for the list of legacy APIs that are **outdated implementations of spec
features** and have **no successor**, to be retained. Those identified by inspection so far:

| # | Legacy API / surface | Location | Why no successor yet |
|---|---|---|---|
| L1 | `HavenIconCatalog` â€” 24x24 **stroke** geometry, "agents" only, no `assistants`/`specialists` key | `9to1 Workspace/shared/src/Haven.UI/Drawing/` | The reference requires prominent **filled** icons. No spec-conformant asset set exists in-repo. Needed by the Assistants/Specialists Space migration. |
| L2 | `ChatSpaceProjection` pre-chronology ordering (now fixed) â€” related legacy: `Haven.Desktop/Views/Pages/Chat/ChatProjection.cs` non-canonical projection | `shared/src/Haven.Desktop/.../ChatProjection.cs` | Two competing Chat projections exist (native `ChatHavenScene` vs Spaces `ChatSpaceController`). Collapsing them requires the CUI host contract. |
| L3 | `CakeTheme` + `9to1 OS/HUI/theme/tokens.json` teal vocabulary (`#0F1A1E` / accent `#3ED598`) with no theme selection | `9to1 OS/HUI/HuiRenderer/`, `9to1 OS/HUI/theme/tokens.json` | This is the **root cause of the visual mismatch**. It has a successor (CUI Professional) but cannot be deleted until HUI token resolution is redirected â€” deleting it first would remove all Chat chrome colour. |
| L4 | `CakeOS.Hui.Renderer` + `CakeOS.Hui.SharedComponents` (HUI renderer/component layer) | `9to1 OS/HUI/` | 30 `.hui` files across `shared/src` depend on it. No CUI equivalent of HavenUI's Prefab/DynamicUI runtime exists yet. |
| L5 | `CakeOS.Hui.SharedComponents` vendored `Haven.UI` component library | `9to1 OS/HUI/vendor/Haven.UI/` | Same as L4. |
| L6 | Dual Spaces CUI declarations with no runtime host: `ChatSpace.cui`, `SpacesHome.cui` | `9to1 Workspace/Spaces/UI/` | `ChatSpaceCuiDocument`/`SpacesHomeCuiDocument` are loaded only by tests; no app route renders them. Removing them loses spec-described surface without a host. |
| L7 | `SpacesDestinationKind.Agent` / `SpaceKind.Agent` / `SpaceScope.Agent` identifiers | `Haven.Application/Spaces/`, `9to1 Workspace/Spaces/` | Retained deliberately: numeric enum values must never be renumbered and persisted records key on them. Already migrated at display level only. |
| L8 | `Haven.Desktop.HavenUI.Tokens.HavenUiResourceApplier` + `SurfacePaletteCatalog` | `shared/src/Haven.Desktop/HavenUI/Tokens/` | Second palette authority parallel to CUI. Successor identified (CUI), removal blocked by L3/L4. |

**This list is a floor, not a ceiling.** A complete inventory requires the HUIâ†’CUI host/renderer
contract to exist first; until then the true set of successor-less legacy APIs cannot be
enumerated with confidence.

---

## 4. Why the rename has NOT been started

A mechanical find-and-replace across 37,783 `Haven` occurrences is **not safe to perform as a
single pass**, and doing it blindly would very likely destroy working product code. The
specific hazards found:

1. **Reflection-driven template discovery.** `HavenPrefabCatalog.FromAssembly(...)`,
   `HavenDynamicUITemplateCatalog.FromAssembly(...)` and `CuiControlTemplates` discover prefabs
   and templates by scanning assemblies. Renaming an assembly or namespace changes what those
   scans find â€” templates silently stop resolving rather than failing to compile.
2. **Assembly identity is baked in.** `Haven.Desktop`, `Haven.Core`, `Haven.Application`,
   `Haven.Infrastructure`, `Haven.UI`, `HavenOS.*` appear in `.deps.json`,
   `project.assets.json`, AXAML `clr-namespace` references and test assembly references.
3. **Persisted data.** Settings, drafts and checkpoints persist conversation/space records. The
   handoff's own identity invariant table forbids replacing stable IDs.
4. **The native suite cannot verify the result.** `Haven.Desktop.Tests` is x64-pinned and this
   host is ARM64-only (Â§11). A rename that silently broke prefab/template resolution or native
   UI could not be detected here at all.

### Required before renaming

1. **Install the x64 .NET runtime** on this ARM64 host, so the native suite can run. Without it
   the rename cannot be verified, and an unverified rename of this size is exactly the kind of
   change the handoff forbids ("A green build proves compilation, not that a route rendered").
2. Then rename in **staged, independently verifiable slices**, smallest first:
   - **S1** â€” file/directory names and `.csproj` `<AssemblyName>`/`<RootNamespace>` for leaf
     projects with no reflection consumers.
   - **S2** â€” `HavenUI` â†’ `CakeUI` and `HUI` â†’ `CUI` identifiers, verified by CUI inventory +
     native tests.
   - **S3** â€” `Haven` â†’ `9to1` (largest; touches assembly identity and reflection).
   - Build + run the native and AnyCPU suites **after each stage**, reverting on failure.

---

## 5. What is explicitly NOT established

- No renaming has been performed. Â§2 is inventory only.
- No legacy implementation has been removed from code. Only `.md` documentation was deleted.
- No visual evidence exists; the Chat/Professional correction remains unverified.
- `Haven.Desktop.Tests` has still never been executed on this host.
