# Boards acceptance before the theme pass
This is a checklist to execute, not a pre-passed test report. Retain the repository's complete acceptance requirements and source IDs. Use disposable fixtures, never destructive tests on the owner's real boards.

| Gate | Required evidence |
|---|---|
| B31-01 Integrated baseline | Verified remote/upstream, actual Astra commit IDs, local HEAD and preserved dirty state. Build/launcher maps to the mounted Boards route. |
| B31-02 Header and files | Icon/name/Undo/Redo; name click and keyboard open the actual file menu; New/Open/Save/Save As/Rename and supported operations work; truthful save/error status. |
| B31-03 Toolbar | Separate visible style/font controls, formatting group, compact paragraph menus, contextual Properties, fixed Add/Drawing/AI; actual/mixed selection values; stable commands under overflow. |
| B31-04 Menu lifecycle | Trigger anchoring at viewport edges; nested menus; keyboard navigation; Escape/outside dismissal; focus/range restored; no clipping/overlap at supported sizes. |
| B31-05 Add destinations | Footer creates one page/section at stated destination; toolbar/inline/slash insert one appropriate content item; open/cancel creates nothing. |
| B31-06 Editing | Typing and range formatting, Enter split, Shift+Enter soft break, IME handling, merge/delete boundaries, lists, tables/media, caret and selection through autosave/menus. |
| B31-07 Tree and undo | Nested add/rename/reorder/move/duplicate/remove; cycle rejection; same/cross-parent moves; exact LIFO undo preserving IDs/order/content. |
| B31-08 Ink and placement | Same page surface; pointer capture/cancel; zoom/scroll transforms; pen/highlight/erase/select; authored colours; stroke undo; ink and objects persist. |
| B31-09 Independent modes | Edit/View × Locked/Unlocked × Snap on/off; AI Read-only/Write separately enforced. View blocks mutation; locking does not accidentally disable allowed ink/text. |
| B31-10 Durable round trip | Create/edit/save/save-as; close and reopen in a new process; exact board/page/block/stroke IDs, hierarchy, content, layout, styles and embeds preserved; no required cloud/account/AI. |
| B31-11 Save failure/concurrency | Delayed/duplicate save completion, newer edits, retries, copy, interruption recovery and cancelled close; no data loss, stale overwrites or false Saved state. |
| B31-12 Donors/embeds/AI | Real applicable donor integration, canonical embeds and detach semantics; stale revision rejection, ownership and permission checks; no fake working controls. |
| B31-13 Accessibility/layout | Focus visibility, labelled controls, keyboard operation, responsive drawer/overflow, supported scale factors, optional-tabs default, no unintended hover panels. |
| B31-14 Visual and regressions | Actual application captures/reviewer evidence plus relevant shared consumers. Unit tests do not certify appearance. Test-host screenshots do not prove the owner's launcher is fixed. |

For every result retain requirement/source ID, reproduction, expected/observed outcome, exact commands and test IDs, executable path/build identity, commit/dirty state and evidence paths. Statuses: UNCHECKED, IMPLEMENTED, VERIFIED-LOCAL, BLOCKED, NEEDS-VISUAL-REVIEW, NEEDS-OTHER-PLATFORM. Never collapse blocked or untested into passed.

Theme pass starts only after the applicable Boards requirements and this gate are satisfied. If the remaining work is genuinely blocked, report it and preserve a resumable state rather than silently reducing the gate or switching to unrelated theme work.
