# Write parity and acceptance

Current reconciliation: 11 October 2026. Status: **CHECKPOINT — not RCVP, Full or independently VERIFIED.**

The earlier September record incorrectly described the app as having no CUI resource or browser adapter. The actual browser entry is `apps/Web/Write/`; it uses `NotesDocument / WriteDocumentEditor` and the maintained retained document surface. Source presence and earlier focused tests do not establish donor-complete acceptance.

The actual native `9to1 Workspace/Write/Program.cs` launches `Haven.Desktop.App.RunOriginalDesktop`. An isolated `WriteApplication` or test host is not proof of the shipping native route. Native chrome still requires canonical CUI conformance and current packaged runtime evidence.

The LibreOfficeKit donor integration remains a mandatory acceptance gap. Its separate native engine/probe directory is not evidence that the normal native or web entry point uses it. The existing document model must not be described as a substitute satisfying complete donor parity.

The current specification requires APP-AC, LAUNCH-001–006, PARITY-001, CUI-001–002, DESIGN-001, STAGING-001, ACCEPT-001–006 and GOV-001–002 (and WRITE-AC for Write). Acceptance requires a version-pinned exhaustive donor feature inventory, approved stage assignments/exceptions, independent start/editor composition, real retained-session transitions, complete packaged workflows and format fidelity, privacy/accessibility, platform/performance evidence and independent review. None is waived by an attractive shell or a passing focused test.

## Recovered historical worker record

The report below is preserved from the old OpenCode checkpoint `d66d27a1fe0a10238f83d2d318352f39c0fa1a00` during worktree consolidation. Its implementation labels, test totals and environment observations describe that historical worker report only; they are **not current independent acceptance evidence**. Preserve the useful behavioral detail while validating the current integrated source afresh.

---

# Write Parity Record

Audit date: 2026-09-20
Re-audited: 2026-10-09 against the native host, browser adapter and CUI surface
delivered on `opencode/later-apps-20261009`.

## Sources inspected

| Role | Source |
| --- | --- |
| Donor sketch | `reference/cakeos/phase2b-worker2-app-sketches/apps/Write/App/WriteEngineContracts.cs`, `WriteEngineImpl.cs`, and `Hui/WriteHuiScene.cs` |
| Donor integration notes | `9to1 Workspace/shared/docs/architecture/write-libreoffice-engine.md` |
| Native host | `9to1 Workspace/Write/` (`HavenOS.Write.csproj`, `Program.cs`, `WriteAppHost.cs`, `WriteApplication.cs`, `WriteAppSurface.cs`, `Tests/`) |
| Editor | `9to1 Workspace/shared/src/Haven.Application/Write/WriteDocumentEditor*.cs` |
| Desktop surface | `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Write/` (`WritePage*`, `WordWriteHavenScene*`, `WriteDocumentSurface*`) |
| Browser surface | `apps/Web/Write/` (`WriteBrowserSession.cs`, `WriteBrowserSurface.cs`, `WriteBrowserFeature.cs`, `Write.cui`, `Scene/`, `Tests/`) |
| Compatibility proof of concept | `9to1 Workspace/Write/write-libreoffice-poc/` (`lok_probe.cxx`, `lok_semantic_probe.cxx`, `haven_write_engine_poc.cxx`, and client/verifier scripts) |

## Exact status

| Capability | Donor sketch | Current source | Status | Evidence / blocker |
| --- | --- | --- | --- | --- |
| App identity | `write` / `Write` constants | Same constants in `WriteAppSurface` and the `app.write` browser route | Preserved | Static source match. |
| Desktop editor surface | Donor defines a separate LOK-backed app and HUI sketch | `HavenOS.Write.csproj` launches the original Desktop App with the maintained Notes repository and `WritePage` | Implemented | Release build 0 warnings 0 errors. `HavenOS.Write.Tests` 6/6. One-process OS-profile lease is documented in `9to1 Workspace/Write/README.md`. |
| Writer open, render, edit, save, reopen | Broad `IWriteEngine` contract; donor HUI controller leaves several actions as comments | `WriteDocumentEditor` plus `NotesRepository`/`WriteNativeDocumentPackageStore` drive native, Desktop and browser surfaces from one model | Implemented | `WriteBrowser.Persistence.Tests` seed 23 / verify 40, `WriteBrowser.Package.Tests` 10, `WriteBrowser.UnknownCommit.Tests` 40, `BrowserWriteSpace.Tests` 15 checks, all against the real repository and codec. |
| Formatting | Declared by donor contract | `WriteCharacterFormat` (Bold, Italic, Underline, StrikeThrough) with selection-scoped run splitting, plus `SetFontFamily`, `SetFontSize`, `SetForeground`, `SetBackground` and `SetLink` over the same range | Implemented in the editor model; all four character toggles, font family, size, text colour, highlight and hyperlink implemented in browser CUI | Toolbar/picker journeys assert Bold changes only the selected structured run and survives a canonical save/reopen. The toolbar journey also asserts that Italic, Underline and Strikethrough decorate only that same run, that font size, family, text colour and highlight write through to the owning run and read back through their two-way bindings, that a size below the editor's 4 point floor and a non-hex colour are rejected for validation without reaching the run, that the full character and font formatting reaches the canonical document on save, and that the hyperlink input reads back the selected run's link, writes a URL through to that run, removes it again when emptied, leaves the run boundaries intact because the selection sits exactly on them, and reaches the canonical document on save. |
| Alignment, indentation, spacing, lists, tables | Declared by donor contract | `WriteDocumentEditor.SetAlignment`, `SetLineSpacing`, `SetLeftIndent`, `SetFirstLineIndent`, `SetSpaceAfter`, list and table operations. Alignment is exposed on the browser surface as `AlignLeft`/`AlignCenter`/`AlignRight`/`AlignJustify`; line spacing, left indent, first-line indent and space after are exposed as two-way number inputs through `WriteBrowserSession.TrySetValue`; structural block insertion is exposed as `InsertParagraph`/`InsertHeading`/`InsertQuote`/`InsertCode`/`InsertBullets`/`InsertNumbering`/`InsertChecklist`/`InsertPageBreak`/`InsertDivider`/`InsertTable`, with `TableRows`/`TableColumns` as two-way insert dimensions; list item editing is exposed as a `Repeat` over `SelectedListItems` whose text, checklist state and nesting level write back through `WriteListItemRow` into `WriteBrowserSession.TryEditListItem`, plus an `AddListItem` action; table cell editing and shape changes are exposed as a nested `Repeat` over `SelectedTableRows` whose cell text writes back through `WriteTableCellRow` into `WriteBrowserSession.TryEditTableCell`, plus `AddTableRow`/`AddTableColumn`/`RemoveTableRow`/`RemoveTableColumn`; an existing list's kind is changed through `SetListBulleted`/`SetListNumbered`/`SetListChecklist` | Implemented in the editor model; alignment, paragraph measurements, block insertion, list item editing and table editing implemented in browser CUI | Toolbar journey asserts center alignment changes only the selected paragraph, that the four number inputs write through to the owning editor, that an out-of-range value is rejected for validation without reaching the document, that each insert command creates the intended block structure (including the `heading-1` style and the page-break flag) as the selected block, that a four-by-two table request produces exactly that shape on every row while out-of-range dimensions are rejected at the input, that list item text, checklist state and nesting level write through to the owning item while a level beyond the editor's 0-8 range is rejected, that `Add list item` appends the editor's authored item, that changing an existing list's kind cycles the same list through numbered and checklist while keeping its items, that every cell of the inserted table is rendered and its text writes through while row and column additions and removals change the live shape, and that the whole paragraph format and every inserted block persist through a canonical save and repository reload. |
| Page layout | Declared by donor contract/HUI sketch | `WriteDocumentEditor.SetPagePreset`, `SetOrientation`, `SetMargins`, `SetPageNumbers` and `SetLayout` over `NotesPageSetup` and `NotesLayoutMode`; the Desktop `WordWriteHavenScene` ribbon already drives A4, Letter, Portrait, Landscape, margins, page numbers and layout mode | Implemented in the editor model and Desktop ribbon; the same controls added to browser CUI | The toolbar journey asserts the authored 72 point margin and page-number setting read back, that Letter yields 612 by 792 and A4 yields 595 by 842, that landscape swaps the axes and portrait restores them, that the size readout tracks the requested page live, that a margin beyond 1000 points is rejected for validation without reaching the page setup, that the page-number checkbox writes through, that the layout mode binding reports the authored Continuous mode and both layout commands switch it, and that the whole page setup reaches the canonical document on save. |
| Find and replace | Declared by donor contract/HUI sketch | `WriteDocumentEditor.Find` and `ReplaceAll` search list items, table cells, equations and media text as well as run text; the Desktop `WordWriteHavenScene` ribbon already drives `Write.Review.Find`, `Write.Review.Replace`, `Write.Review.FindPrevious`, `Write.Review.FindNext` and `Write.Review.ReplaceAll` | Implemented in the editor model and Desktop ribbon; the same controls added to browser CUI | The toolbar journey asserts the session prompts before searching, that a case-insensitive query counts every match in the authored paragraph, that `Find next` and `Find previous` walk the position forward and backward, that a genuinely empty result set and an empty query are refused with their own typed failures without mutating the document, that the live status label renders the position, and that `Replace all` writes through to the owner in one admitted edit, reports its replacement count and leaves its target with no matches before a canonical save. Navigation only selects a match whose offset addresses run text, so a list or table match counts and advances the position instead of silently selecting an unrelated range. |
| Block move and delete | Declared by donor contract/HUI sketch | `WriteDocumentEditor.MoveSelected` and `DeleteSelected` reorder and remove the selected block as ordinary mutations; the Desktop `WordWriteHavenScene` ribbon drives `Write.Home.Up`, `Write.Home.Down` and `Write.Home.Delete`, and the contextual pill drives the same operations | Implemented in the editor model and Desktop ribbon; the same controls added to browser CUI | The toolbar journey asserts an inserted heading moves above a typed paragraph without losing either block's content, that moving the first block up is refused without reordering anything, that moving it down restores the order, that deleting the selected block leaves the other block's text intact, that the delete control hides once one block remains, and that the editor itself refuses to delete a document's last block while the surface reports the call as successful. |
| Run and table cell structure | Declared by donor contract/HUI sketch | `WriteDocumentEditor.SplitRunAtCaret`, `MergeRunWithPrevious`, `MergeTableCellRight` and `SplitTableCell` refine the selected run and cell as ordinary mutations; the Desktop ribbon drives `Write.Home.SplitRun` and `Write.Home.MergeRun`, and the contextual pill drives `Write.Table.MergeRight` and `Write.Table.SplitCell` | Implemented in the editor model and Desktop ribbon; the same controls added to browser CUI | The toolbar journey asserts that writing a table cell selects it for the structural commands, that splitting a run at a caret inside it divides the run in place while carrying its formatting and hyperlink over and leaves the paragraph's text unchanged, that merging the split runs back restores the authored run and its link, that merging a cell right spans its neighbour into it while the other rows keep their shape, and that splitting the spanning cell restores the original four by two table before a canonical save. |
| Paragraph style | Declared by donor contract/HUI sketch | `WriteDocumentEditor.ApplyStyle` resolves a style by id from `Document.Styles` and `WriteNamedStyleOperations.Apply` writes the style's id, kind, paragraph format and character format onto the selected block; the Desktop `WordWriteHavenScene` ribbon drives `Write.Home.Style` | Implemented in the editor model and Desktop ribbon; the same picker added to browser CUI as a `Repeat` over the document's own style catalogue | The toolbar journey asserts the catalogue reads back every named style the document declares with a distinct control label, that applying Heading 1 switches the selected block to a heading, that Quote switches it to a quote while its text survives untouched, that the readout binding tracks the authored style, and that returning to Normal restores a plain paragraph before a canonical save. |
| Equation authoring | Donor sketch declares equation support; Desktop `WordWriteHavenScene` drives `Write.Insert.Equation` | `InsertEquation` inserts the owner's authored equation block as an ordinary admitted edit, and two-way `EquationSource` and `EquationAlternative` bindings write the selected equation's LaTeX source and accessible alternative through `WriteDocumentEditor.UpdateEquation`, each writing its typed value and reading its untouched sibling from the block | Implemented | The toolbar journey inserts the equation, asserts the property bindings report the inserted equation's own authored source and alternative, types a new source and alternative through their inputs and asserts both reach the selected equation, then asserts the canonical save commits the typed equation alongside the image. |
| Table cell background | Desktop `WordWriteHavenScene` drives `Write.Table.CellBackground` | A two-way `CellBackground` binding writes the selected cell's colour through `WriteDocumentEditor.SetTableCellBackground`, gated on a selected table cell with an editable document | Implemented | The toolbar journey asserts the binding reports the selected cell's own authored transparent background once a cell is written, that the typed hex colour reaches that cell, and that a non-hex value is rejected for validation without reaching the cell. |
| Image insertion and editing | Donor sketch declares `IWriteEngine` image support; Desktop `WritePage.WordProcessor` picks a file and imports it through the Notes attachment store | `InsertImage` opens the platform image chooser through an injected `IWriteBrowserImageSource`, stages the picked file and imports it through the injected owner `INotesAttachmentStore`, so the attachment identity, stored copy and digest stay with the owner; the browser reports `AttachmentStoreMissing` or `ImageSourceMissing` instead of accepting an image no save could carry. Alt text, caption and wrapping write through `WriteDocumentEditor.UpdateMedia`, size through `ResizeSelectedMedia`, rotation through `RotateSelectedMedia` and the four crop fractions through `SetSelectedMediaCrop`, all exposed as two-way bindings in `Write.cui` | Implemented | The scene journey inserts a picked image and asserts the attachment owner assigned the identity and stored path with the exact picked bytes, digest and size; types alt text, caption and wrapping through their two-way inputs and asserts each reaches the selected image's own properties; resizes, rotates and crops through their inputs; asserts every media control disables once the selection leaves the image; then saves and asserts the canonical document carries the image and every edited property. The persistence suite proves a fresh process addresses the same media block and canonical attachment, that the stored attachment bytes still match the recorded digest, that the native package carries the media block and its canonical attachment reference, that a read-only document refuses both the insert and a property write without changing the durable revision or storing a new attachment, and that undo and redo remove and restore the media insert as admitted edits |
| File picker | Declared by donor contract/HUI sketch | Desktop `WritePage` reaches `TopLevel.StorageProvider` for image insertion only. Browser surface lists repository documents in-page and has no OS file picker | Partial | No donor parity is claimed for the browser surface. |
| Print UI | Declared by donor contract/HUI sketch | `WriteApplication` binds Ctrl+P to `WriteAppHost.PrintAsync`, which hands the open document to the injected `INotesImportExportService.PrintAsync` (writes a PDF and opens the platform handler on Windows). It refuses with an explicit failure when no document is open, and reports a refused or failed print in a dialog without tearing down the editor holding the draft | Implemented in the native host | `HavenOS.Write.Tests` 6/6, including print handoff of the open document and the no-document refusal. The donor sketch itself leaves print as a placeholder controller path; the browser target is WASM and has no print path, and the Desktop `WritePage` ribbon still exposes no print action. |
| LibreOffice donor engine | LOK-backed render/edit/save | `write-libreoffice-poc/` probes exist and are Linux-only; the production path remains `NotesRepository`/`NotesDocument` | Not integrated | Windows workspace has no LibreOfficeKit runtime. Per `write-libreoffice-engine.md`, donor integration is a later, separately gated step. |
| CUI parity | No `.cui` artifact | `apps/Web/Write/Write.cui` (185 lines) drives the browser surface; the scene journey lowers it through the real retained registry and captures rendered frames | Implemented and validated | Scene journeys: native-input 177, toolbar 639, picker 28, navigation 19 assertions, including the action/binding catalogue checks added in `25973af0`. |

## CUI surface

`Write.cui` declares fifty-three dispatchable commands: the fifteen core
document commands `CreateDocument`, `SaveDocument`, `BoldSelection`, `Undo`,
`Redo`, `CloseDocument`, `RefreshDocuments`, `InspectSavedState`,
`ImportPackage`, `ExportPackage`, `OpenDocument` (reached through the
`OpenSavedDocument` action reference), `AlignLeft`, `AlignCenter`,
`AlignRight` and `AlignJustify`, the character toggles `ItalicSelection`,
`UnderlineSelection` and `StrikeSelection`, plus `InsertParagraph`,
`InsertHeading`, `InsertQuote`, `InsertCode`, `InsertBullets`,
`InsertNumbering`, `InsertChecklist`, `InsertPageBreak`, `InsertDivider`,
`InsertTable`, `AddListItem`, `SetListBulleted`, `SetListNumbered`,
`SetListChecklist`, `AddTableRow`, `AddTableColumn`, `RemoveTableRow` and
`RemoveTableColumn`, the page-layout commands `SetPageA4`, `SetPageLetter`,
`SetPagePortrait`, `SetPageLandscape`, `SetLayoutPaginated` and
`SetLayoutContinuous`, the review commands `FindNext`, `FindPrevious` and
`ReplaceAll`, the block commands `MoveBlockUp`, `MoveBlockDown` and
`DeleteBlock`, the run and table cell commands `SplitRun`, `MergeRun`,
`MergeCellRight` and `SplitCell`, and `ApplyStyle`, which the paragraph style
picker reaches through the same `Actions` catalogue as `OpenDocument`, so each
rendered style control carries its own row as the dispatch parameter.
The scene journey resolves every component action through the document `Actions`
catalogue, asserts each command is dispatchable by
`WriteBrowserSession.DispatchableCommands`, asserts each root-scope binding
resolves against the live session, and asserts every dispatchable command is
still declared by the markup. `DispatchAsync` throws when a catalogue entry has
no dispatch case, so markup/dispatch drift fails acceptance instead of
rendering a control that silently reports `UnsupportedFeature`.
`WriteBrowserSession` also implements `ICuiWritableBindingContext`, so the
line spacing, left indent, first-line indent and space after inputs write back
into the selected paragraph with the editor's own bounds; a value outside the
editor's range is rejected and flagged as an input validation error rather
than silently clamped. The `Repeat` subtrees over `SelectedListItems` and
`SelectedTableRows` resolve and write back through their own row contexts, so
list items and table cells edit in place instead of through detached copies.
`FontFamily`, `FontSize`, `TextColor` and `TextHighlight` read and write the
run a character command would change: the editor targets the runs covered by
the selection, so the readout starts at the selection's first character rather
than at the editor's internal active run, which may still sit on the run before
a boundary. A font size outside the editor's 4 to 300 point bounds and a colour
that is not a hex value are rejected for validation, matching how an
out-of-range paragraph measurement behaves instead of silently leaving the run
with something the writer did not type. `Link` reads and writes that same run:
an empty value clears the link instead of being rejected, because removing a
hyperlink is a real authoring action and the editor stores none for an empty
value. `PageMargins` and `ShowPageNumbers`
read and write `NotesPageSetup` the same way, `PageMargins` rejecting anything
outside the editor's 0 to 1000 point bounds because the editor clamps there,
`PageSizeLabel` reports the current page in points, `PageLayoutMode` reports
the authored `NotesLayoutMode`, and `WordCount` reaches a read-only label so
the writer can see the count the session already tracks. `FindText` and
`ReplaceText` are session state rather than authored content, so they accept a
write while a document is open and are accepted even on a read-only document;
changing `FindText` resets the walk position and the replacement status.
`FindMatchCount` and `FindStatus` re-run the editor's own search on every
refresh, so the readout cannot drift from the document, and `FindStatus` reports
`Enter text to find.` and `No matches.` for the two empty cases instead of a
blank label. `CanFind` follows `FindNext`, which needs only an open document
because searching never mutates, while `CanReplace` follows `ReplaceAll`, which
additionally needs an editable document; `ReplaceAll` refuses an empty query
before it can reach the editor's replace loop, which would otherwise never
advance past an empty search. `CanMoveBlocks` follows `MoveBlockUp`, which
needs only an editable open document because the editor itself declines a move
that would leave the block where it already is, while `CanDeleteBlock`
additionally hides once only one block remains so the surface never offers a
delete the editor can only ever refuse. `CanSplitRun` and `CanMergeRun` follow
their own commands and need only an editable open document, because the editor
itself declines a split whose caret is not strictly inside a run and a merge of
the first run, while `CanMergeCell` and `CanSplitCell` additionally require a
selected block that holds a table, so the cell structure controls never appear
beside an ordinary paragraph. `Styles` reads the open document's own
`NotesNamedStyle` catalogue and falls back to the built-in set while no
document is open, so the surface keeps the same rows and only their enabled
state changes when a document arrives; the array identity is held while the
document's style ids are unchanged so a refresh does not rebuild every style
control. Each row carries a `Set ...` label derived from the style name, which
keeps every rendered control label unique against the surface's existing
`Set bullets`, `Set numbering` and `Set checklist` controls, and is handed back
as the `ApplyStyle` dispatch parameter. `CanApplyStyle` follows `ApplyStyle`,
which needs only an editable open document, while `SelectedStyle` reports the
selected block's style by name so the writer can read back what a click did.

## Remaining gaps

- Print is implemented in the native Write host only: the browser target is
  WASM with no print path, and the Desktop `WritePage` ribbon exposes no print
  action.
- No OS-level file picker on the browser surface; Desktop uses `StorageProvider`
  only for image insertion.
- Lists and tables can be inserted and edited from the browser surface,
  including changing an existing list's kind. Reordering items inside a list is
  not implemented in the shared editor at all, so it is not a browser gap.
- The control stack above the document canvas is a non-scrolling `StackPanel`,
  so a window shorter than the stack leaves its lowest controls out of reach. The
  saved-document list now renders above the formatting controls, which keeps the
  library itself reachable on the scene window's 800 pixel height, while the
  stack now measures about 860 pixels, so the scene suite renders at 920 pixels.
- LibreOffice donor engine is not integrated; `write-libreoffice-poc/` remains a
  Linux-only proof of concept.
- No automated cross-process writer locking or actor ACL proof; those remain
  owner-backend gates as documented in `apps/Web/Write/README.md`.

## Validation

- Native: `9to1 Workspace/Write/Tests/HavenOS.Write.Tests.csproj` - 6/6.
- Format fidelity gate: `apps/Web/Write/Tests/Write.OriginalFormats.Tests.csproj` - 7/7.
- Browser suites: `WriteBrowser.Package.Tests` 10, `WriteBrowser.Scene.Tests` 199
  (native-input), 681 (toolbar), 28 (picker) and 19 (navigation),
  `WriteBrowser.UnknownCommit.Tests` 40, `WriteBrowser.Persistence.Tests` seed 47
  and verify 57, `BrowserWriteSpace.Tests` 15 checks,
  `BrowserActionAvailability.Tests` 26 checks.
- Every suite runs against the unchanged production `NotesRepository`,
  `WriteNativeDocumentPackageStore`, `NotesAttachmentStore` and editor, with an
  isolated `HAVEN_DATA_DIR` and per-project build outputs.
- The proof scripts under `write-libreoffice-poc/` require Linux,
  `libreofficekit-dev`, a LibreOfficeKit runtime and a C++20 compiler; those
  prerequisites are unavailable in this Windows workspace.
