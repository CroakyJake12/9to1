# Qwen — integrate Astra, finish Boards, then repair themes

You are continuing 9to1 development in a fresh session. Implement and verify real code; do not stop after a plan. Keep the current model/settings unless the owner changes them. Work autonomously within this scope, with concise evidence-based progress updates and durable checkpoints.

## 1. First action: safely pull Astra's changes
Before application edits, locate the actual repository and read AGENTS.md plus its integration/worker rules. Inspect working-tree/index status, branch, upstream, remotes, recent commits and any active writer. Do not assume main, origin or a cloud branch name. Establish exclusive ownership at a safe boundary; do not race another worker or kill unrelated processes.

Fetch the verified remote. Identify the actual incoming Astra work from commit history/diffs/handoff, not from the previous chat's claims. Record local HEAD, upstream HEAD, incoming commit IDs and which changes were integrated. On the intended branch, with a clean non-diverged tree, use a fast-forward-only pull. If there are local uncommitted changes or diverged commits, preserve them and follow the existing repository's authorised integration procedure. Do not blindly stash, reset, clean, force-checkout, overwrite, rebase published work or force-push. Do not create a competing worktree/branch just to evade a conflict. Resolve only conflicts whose intended behaviour is established by current sources; preserve ambiguous conflicts and report the blocker.

If Astra has not actually pushed, authentication fails, or the upstream is not identifiable, state that exact fact. Do not invent a successful pull or continue implementing against a supposedly integrated baseline. Unblocked read-only investigation/checkpoint preparation is allowed while integration is blocked.

After integration, review Astra's actual diff and handoff. Compile and run focused regression checks before assuming its completion claims. Locate the real mounted Boards route and launcher. Check unfinished inherited edits, including BlockRenderer.cs if it is still relevant. Do not repeat already completed changes.

## 2. Load this updated contract without bloating context
Locate the attached/extracted 9to1_Qwen_Handoff_v3.1 pack; verify actual paths. Read OWNER_DECISIONS_v3.1.md, BOARDS_ACCEPTANCE.md and the focused reference extracts first. Use 9to1_Development_Specification_v3.1.docx and 9to1_Interface_Atlas_v3.1.html for the full baseline, revised Boards states and theme galleries. Use THEME_REFERENCE_INDEX.md and ThemeRefs/ for the next pass. MANIFEST.json records package hashes; these attest file identity, not product correctness.

Read the repository's complete Boards module, retained technical/boards-*.md chapters and specific dependency contracts as needed. The supplied DOCX does not contain every referenced technical source. Do not reconstruct missing chapters from headings or treat the focused extracts as a reduced product scope.

Apply current explicit owner directions and the v3.1 amendment narrowly over conflicting older UI wording. Keep compatible security, persistence, platform, donor and behaviour contracts. Record stable requirement IDs and exact evidence in the existing ledger. Preserve older decision history while marking superseded UI wording inactive. Keep the current spec/atlas and corresponding repository modules consistent as you implement; do not fork competing authorities or rewrite unrelated modules.

## 3. Finish Boards — not just the toolbar
Owner intent: the editor broadly works but is clunky. Improve and finish the real existing CUI application, not a new demonstration or an AppFlowy UI replacement.

Ensure the integrated implementation matches these decisions:
- Identity row: board icon, clickable board name opening the existing file menu, Undo and Redo. No separate File button. Preserve file operations, navigation and honest save state.
- Adaptive toolbar: one rounded container with dividers between Style/Font, Text Formatting, Paragraph, and Properties; a separate fixed Add / Drawing / AI pill.
- Style/Font has distinct visible Paragraph/heading style, actual font family, editable size with minus/plus and Aa casing controls. Bold/italic/underline/strikethrough/colour/highlight are in the next group, not merged into the font group.
- Alignment, list and spacing choices use compact menus, not a permanent row of every variant. Properties follows selection. Adaptive controls change by content type; fixed controls stay stable. Preserve commands in responsive overflow and keyboard access.
- Toolbar Add inserts current-page content. Sidebar footer Add creates pages/sections. Preserve distinct destinations and non-mutating cancellation.
- Repair real menu placement/dismissal, clipping, layout, search, focus, selection, Enter/split behaviour, ghost caret and contextual outlines. Verify that a reported bug still exists before editing; the blank style label was reported fixed.
- Retain CUI and reuse suitable AppFlowy Rust capabilities. Inspect existing APIs/bindings/licensing, preserve the canonical document model, and do not claim integration before exercising it.

Continue Qwen's remaining Boards specification and Astra's unfinished tasks from verified state. Cover local create/open/save/save-as/reopen/recovery; nested page operations; rich text, lists, tables/media; block operations; drawing/ink; layout manipulation; undo/redo; independent Edit/View, Locked and Snap; canonical embeds and applicable AI/collaborator/permission behaviour. Existing implementations must be verified, not automatically rewritten. No unrelated app work unless required by a demonstrated Boards dependency.

For each coherent patch: reproduce/identify the requirement, inspect the live path, add or strengthen regression coverage, implement, compile, run focused and affected integration tests, exercise the real app, then inspect the diff line by line and by affected subsystem. Never weaken tests, hide exceptions, fake persistence, or disable required features to turn the ledger green. Keep unsupported host/platform verification explicitly unverified.

## 4. Boards acceptance gate, then theme design
Use BOARDS_ACCEPTANCE.md and the full source acceptance clauses. Compilation and unit-test counts alone are insufficient. Verify real input/menu behaviour and saving followed by reopening in a fresh process on disposable fixtures. Record executable path/build identity, commit plus dirty state, commands/results, screenshots and interaction evidence.

Do not start the broad theme pass while known in-scope Boards defects or acceptance failures remain. If genuine source/permission/tool/host blockers prevent finishing, name them precisely, continue unblocked Boards tasks, and checkpoint when no safe progress remains. Do not relabel blocked work “complete” to unlock themes. A local-target pass does not certify other platforms.

After the Boards gate passes, inspect all 27 images across the 10 ThemeRefs folders and implement the next shared CUI theme-design pass. Use THEME_REFERENCE_INDEX.md; fix materials, control design, typography, spacing/elevation and interaction states, not merely recolouring the current UI. Professional and Glow include component sheets. Preserve the other theme references as supplied and honour the baseline annex restrictions.

Do not silently equate Frost with a renamed Frosted Glass, Industrial with Metallic, or add Synthwave/Snug as built-ins without explicit owner/repository authority. Record unresolved mappings and continue the unambiguous themes. Reference screenshots are design input, not production assets or instructions to copy a third-party app or its functions.

Apply shared theme fixes across relevant components while preserving layout topology, actions and authored colours. Verify supported light/dark, high contrast, reduced motion, disabled/focus/selected states and switching mid-edit. Re-run Boards tests after changes. Use an existing authorised image-capable reviewer or actual visual access where available; a text-only worker must not certify appearance from unseen screenshots or geometry alone. Do not launch paid/cloud reviewers or extra inference workers without authorisation.

## 5. Checkpoints, commits and pushes
Update the repository's existing status/requirement ledger after each bounded milestone: current branch/HEAD, dirty paths, incoming Astra commits, active requirement IDs, confirmed decisions, commands/results, failures, exact next action and source paths. Before compaction persist that checkpoint; after compaction re-read it and this amendment. New owner decisions get explicit revisions rather than being buried in chat. Avoid repeatedly summarising summaries.

Commit coherent verified in-scope changes and push at safe milestones to the existing authorised tracked branch, respecting repository policy and branch protection. Review exactly what is staged; never blindly commit unrelated changes, secrets, generated captures or the whole reference archive. Preserve inherited work. Verify the push and retain local commits if it fails. Do not force-push.

The earlier 2%-remaining cloud-usage cutoff belonged to Astra's temporary sprint. It does not apply to this local Qwen run. Do not invent a usage meter or stop because that old limit expired. Create no scheduled automation, self-restarting task or background service. Explicit owner stop/pause always wins.

Start with the safe Astra fetch/pull and baseline verification, then implement. Keep reports concise and factual. At a checkpoint or genuine blocker, report integrated commits, verified improvements, evidence, unresolved items, push status and the exact next task. Do not ask for a routine “continue”.
