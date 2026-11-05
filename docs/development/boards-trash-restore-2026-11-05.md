# Boards Trash and Restore — required flow, pinned before coding (5 November 2026)

Continuation of the AppFlowy porting direction the owner gave on 4 November 2026 ("port things over to
boards"). Studying the donor's sidebar item menu alongside the canonical specification showed Boards already
covers most of the tree's context actions - Rename, Icon, Duplicate, Move up/down, Add subpage, Nest under
previous, Move to section root - so this unit is the part that is genuinely missing.

## What is actually missing, measured against the current source

| Requirement | Authority | Boards today |
| --- | --- | --- |
| "Trash removes the selected entity from normal navigation but **retains recoverable content and parent/order metadata**" | UX27-BOARD-015 | `HavenRichBoardContract.RemovePage` (`contract/HavenRichBoardContract.cs:1135`) deletes the page and its blocks outright |
| "Deleting a container **shows the impact on descendants**" | UX27-BOARD-015 | no confirmation of any kind; the row menu says only "Delete" |
| "Restore returns to the original parent where possible or a clearly identified recovered location; **it does not silently orphan subpages**" | UX27-BOARD-015 | no restore path; child pages are re-parented to the deleted page's parent at deletion time (`RemovePage` line 1148), so the subtree is rewritten rather than preserved |
| "One successful move is one undo transaction" | UX27-BOARD-015 | applies to the same operation through the menu, so trashing and restoring must each be one document mutation |
| Rows contain "context actions available on hover/focus" | UX27-BOARD-012 | satisfied by the existing context menu |

So the gap is recoverability, not menu coverage. That is also the part where a wrong implementation destroys
the owner's Law content, which is why the flow is pinned before any code.

## The flow, in the order the owner meets it

1. **Entry point.** The page row's existing context menu gains one item: "Move to Trash…". The word "Delete"
   is not kept beside it: two destructive-sounding rows in the same menu invite the wrong click, and the
   recoverable route is the one the specification describes.
2. **Impact before action.** A confirmation popover names the page, and says what goes with it: subpage count
   and block count (for a leaf page it says so plainly rather than showing "0 subpages"). Two actions:
   Cancel, and Move to Trash. Nothing happens until the owner chooses, and Cancel leaves the tree untouched.
3. **Out of navigation, into the record.** The trashed page leaves the tree; the selection moves to a
   neighbouring page so the editor is never showing a page that no longer exists. The page object itself,
   with its blocks and its parent/order metadata, stays in the board file.
4. **Where it is seen.** The Board menu gains "Trash…", opening a panel listing trashed pages: the page's
   mark (its icon, which the same feature now stores), title, the section it came from, and when it was
   trashed. Each row offers Restore.
5. **Restore, with the outcome stated.** Restoring a page brings back the page and its subtree together.
   If the original parent section and parent page still exist, it returns to its recorded position. If the
   parent page is gone, it returns to the section root **and the status line says which happened** -
   "Restored Criminal Law to Workpacks (its parent page is no longer here)" rather than a bare "Restored".
   A section that was itself deleted is never guessed at; the page goes to the section root of the section
   it came from, and if that section no longer exists the operation is refused with a reason rather than
   inventing a destination.
6. **Undo.** Trashing and restoring are each one document mutation through the existing session route, so a
   single Ctrl+Z reverses one action.

## Deliberately not in this increment, with the reason

- **Empty Trash / permanent purge.** Not required by UX27-BOARD-015, and a purge control needs its own
  reviewed confirmation and retention policy. Until that exists, nothing in Boards destroys a page's content
  from this route - which is the outcome the requirement is protecting.
- **Drag-to-trash and the Move To popup.** UX27-BOARD-015 also asks for drag preview/insertion targets and a
  "Move To" popup with destination search. Those are a separate unit: they touch the tree's drag behaviour,
  which needs its own pinned flow and on-screen evidence. Recorded as open, not folded in here.
- **Trashed sections.** This unit covers pages. Sections have their own delete route; extending the same
  mechanism to them is the follow-up once the page path is proven.

## Data shape, kept additive

A trashed page is recorded as an entry holding the page object, its section id, its parent page id, its
position within that parent/section, and when it was trashed. This is additive on the document, with the same
rule the icon fields followed: a board saved before the field existed opens unchanged, and the field's absence
means an empty trash rather than a schema bump. Descendants of a trashed page are recorded as part of the same
subtree so a restore cannot orphan them.

## Evidence this unit must produce

| Claim | Evidence level required |
| --- | --- |
| A trashed page disappears from navigation and the file still contains it | test (contract + session round-trip) |
| The impact popover states the subpage and block counts | test on the rendered surface, plus on-screen capture |
| Restore returns the subtree to its recorded place, and says so when it cannot | test, plus on-screen capture of the status line |
| Reopening the board after a trash still offers the page for restore | on-screen native: trash, close through the app's own route, relaunch, restore |
| The owner's delivered Law board is unaffected | byte-identical hash before and after every native pass, scratch copies only |

Independent acceptance remains paused by owner instruction, so nothing here is described as accepted: the
levels above are test, headless render and on-screen native capture, stated per claim as usual.
