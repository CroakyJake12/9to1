# Build Code — development environment — Build/Dev, Sites and Stacks — explicit developer workflows; Adaptive product, retained domains (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1686 · v2 lines 8728–8728

## 7. Build/Dev, Sites and Stacks — explicit developer workflows

### V2-B1687 · v2 lines 8730–8730

### UX27-BUILD-001 — Adaptive product, retained domains

### V2-B1688 · v2 lines 8732–8732

**O/B.** Build integrates Dev, Sites and Stacks functions while preserving each canonical project/domain model. The same project can move between code editing, visual site/CUI editing, source review, build/test and deployment without copied shadow projects. The navigation communicates the active project, environment and source revision, and changes its tool surfaces according to the current artifact type.

### V2-B1689 · v2 lines 8734–8734

Opening an existing repository consumes its real project files rather than requiring a proprietary replacement. New Project asks only applicable language/framework/runtime options, validates the destination and toolchain, and creates a genuinely buildable project. Missing tools get explicit supported configuration/install choices, not a simulated successful project.

### V2-B1690 · v2 lines 8736–8736

### UX27-BUILD-002 — Editor input, tabs and diagnostics

### V2-B1691 · v2 lines 8738–8738

**B/D.** Code editing preserves selection, indentation, undo, line endings and file encoding. Tab previews versus pinned editors are explicit; closing a dirty editor follows save/recovery. Language diagnostics target exact file/range/revision and clicking them navigates correctly. Stale results from an old build are labelled and must not overwrite current diagnostics silently.

### V2-B1692 · v2 lines 8740–8740

Completion, rename/refactor, find/replace and formatting show the actual change scope and remain undoable. AI edits are inspectable diffs against a pinned base, not opaque whole-file replacements that discard concurrent changes. Protected/frozen source boundaries remain enforced outside the UI as specified.

### V2-B1693 · v2 lines 8742–8742

### UX27-BUILD-003 — Run/build/test/debug pipeline

### V2-B1694 · v2 lines 8744–8744

**B/D.** Run selects an explicit configuration/environment, restores/builds as required, and reports actual stages/exit codes. A failed compile cannot show a successful preview from an unrelated old executable without labelling it stale. Stop targets the owned run/debug process, not all similarly named processes. Test navigation links failures to logs and source; skipped/platform-blocked tests remain distinct from passes.

### V2-B1695 · v2 lines 8746–8746

Debugging exposes breakpoints, pause/continue/step, stack, variables and console through the real debugger adapter. Editing while debugging follows a declared supported hot-reload/rebuild policy. Preview frame identity and source revision remain inspectable.

### V2-B1696 · v2 lines 8748–8748

### UX27-BUILD-004 — Visual editor and CUI authoring

### V2-B1697 · v2 lines 8750–8750

**B/D.** Visual selection, property edits, hierarchy changes and code/CUI edits update the same canonical source model. Drag handles, snapping, align/distribute, responsive preview and component insertion have actual undoable semantics. Generated ordinary UI is canonical CUI, not AXAML renamed after generation or a hidden legacy tree.

### V2-B1698 · v2 lines 8752–8752

A component palette item is available only when its registered contract exists. Invalid properties produce useful diagnostics rather than swallowed parser failures. Preview and shipping build must use compatible rendering/runtime paths; a designer-only visual is not evidence of app integration.

### V2-B1699 · v2 lines 8754–8754

### UX27-BUILD-005 — Source control and conflicts

### V2-B1700 · v2 lines 8756–8756

**B.** Preserve the detailed Stacks Branch/Twig/Leaf/Root/Freeze semantics and source ownership, including its explicit review choices for AI conflict resolutions. A conflict view shows exact files/hunks, base/local/incoming identities and the result of each choice. Accept without Merging and Accept and Merge remain distinct. Reject restores the proposed-resolution state without destroying the original work.

### V2-B1701 · v2 lines 8758–8758

The owner’s current request to consolidate 9to1 development onto main is a programme policy, not a deletion of branching capabilities from Build. Prune, force-push and protected-branch operations retain their elevated safeguards. Local commit, push, merge and published deployment are separate statuses.

### V2-B1702 · v2 lines 8760–8760

### UX27-BUILD-006 — Sites publishing and rollback

### V2-B1703 · v2 lines 8762–8762

**B/D.** Site editing, local preview, build artifact, staging and production deployment have explicit identities. Publish displays target/environment, relevant changes and required permissions; it never changes production merely because a preview is opened. A failed deployment preserves the previous live version and reports the actual failed stage.

### V2-B1704 · v2 lines 8764–8764

Verify routes, assets, forms/data bindings, responsive layouts and the deployed build—not just a local screenshot. Rollback selects a known valid deployment and records its result. Secret references stay protected in logs and exports, and a template must not contain the owner’s live credentials or private sample data.

### V2-B1705 · v2 lines 8766–8766

### UX27-BUILD-007 — Agentic development and review

### V2-B1706 · v2 lines 8768–8768

**B/D.** The Agent view identifies task scope, source base, current owned changes, execution mode, tools, blockers and review status. Steer/queue/stop map to the same durable runtime as Spaces. A draft instruction is not an admitted task; a completed tool command is not accepted product work. Keep permission prompts and unresolved ambiguity visible.
