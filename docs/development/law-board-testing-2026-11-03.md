# Law Board testing report — 3 November 2026

Companion to [the import record](law-board-import-2026-11-03.md) and
[the gap report](law-board-gap-report-2026-11-03.md).

Every row states how it was tested and what level of evidence exists. **On-screen** means a real
frame captured from the running application window on this PC; **file** means the physical
`.9to1board` on disk was inspected; **test** means an automated check in this repository;
**compile** means a guarded build outcome; **NOT RUN** means nobody has seen it work.

Nothing here is independent acceptance. Under the owner's standing instruction acceptance stays
paused, and no row below should be read as app, native or release acceptance.

## What ran

The standalone Boards app was rebuilt first (guard `--floor-mib 1100`, pinned SDK 10.0.301,
`-m:1 -nodereuse:false`): **result OK, exit 0, 0 errors, 7.1 s, minimum 3,349 MiB available, peak
process-tree RSS 105 MiB** — compile evidence only. It was then launched on X11 display `:1`
against `/home/jacob/Documents/9to1/Boards/Law.9to1board` with
`HAVEN_DATA_DIR=/home/jacob/.local/share/9to1-development/profile`, window id `0x04e0000f`
(2850×1781 device px). Input used the repository's own X11 tooling
(`eng/development/drive_window.py`), addressed by exact window id, raising and activating before
each action, and frames were decoded with the padded row stride like `eng/development/capture_window.py`.
No second Boards instance was running, and the owner's live instance was not touched.

| behaviour | how it was tested | evidence | result |
| --- | --- | --- | --- |
| The Law board opens from its file path | launch with the path, then capture | on-screen | **PASS** — window title `Law — Boards`, 21 page rows in the tree, content drawn |
| Start-up to first painted content | polling captures every second | on-screen | window mapped **5.3 s**, page content first drawn **6.6 s** (measured on the 19.7 MB 445-page build; the consolidated 18.6 MB / 149-page build was opened the same way and was on screen within the launch poll) |
| Consolidation holds the same content with fewer pages | block census of the on-disk file before and after | file | **PASS** — 445 pages → **149 pages**, characters 2,530,066 → 2,469,394 (the difference is page-break boilerplate), tables 522 → 510, blank answer paragraphs **1,040 → 1,040**, gaps/fills/kept counts unchanged at 179 / 106 / 249 |
| Section and page tree shows the imported structure | captured frames read page by page | on-screen | **PASS** — sections, parent pages, nested parts `Actus Reus · 1/7 … 7/7` |
| Opening a page by clicking it in the tree | 9 clicks on the 445-page build, plus a targeted open of the 41,000-character Actus Reus page on the consolidated build, each re-targeted from the current frame and timed to a settled picture | on-screen | **PASS** but slow: settle **1.66–4.22 s**; the large single page opened in **~2.0 s**, i.e. a 45,000-character page is no slower to open than a 9,000-character one |
| Scrolling a large page | 20 wheel clicks at two pointer positions, watching the scrollbar thumb; plus a thumb drag | on-screen | **PARTIAL** — on the 41,000-character page the wheel moved content at one pointer position (2.5% of the content area, thumb shifted 6 px) and did nothing at the other; on an 80,000-character test page the wheel did nothing at either position while **dragging the scrollbar thumb worked** (thumb travelled, content changed 12.7%). Reported as D5 |
| Tree disclosure (collapse/expand) | click on the disclosure chevron, frame diff | on-screen | **PASS** — 93 changed rows when a document's parts collapsed |
| Tree reveals the selected page | fixed-coordinate click series | on-screen | **PASS** — the tree re-scrolls to the selection, which is why row coordinates drift between clicks |
| "Search this Board" filters the tree | typed `Theft`, then `Murder` | on-screen | **PASS** — matching pages grouped under their sections (`Murder · 1/6 … 6/6`, plus hits in Retrieval Tasks and Other); settle 3.3 s |
| Scrolling a long page | 12 wheel clicks over the content card, settle, frame diff, scrollbar position | on-screen | **PASS** — content shifted and the scrollbar thumb moved to the bottom of the page |
| Keyboard paging (Page Down / Next) over the page | three key combos after placing focus in the page | on-screen | **no change observed** in the state tested (D4) |
| Edit / View mode switch | click the View pill, frame diff | on-screen | **PASS** — toolbar changed, block grips hidden in View |
| Provenance and Drive links render | captured page of `Burglary - Workpack (2026)` | on-screen | **PASS** — `Original: Workpacks/Criminal Law/…` and a real `https://drive.google.com/file/d/1PYKqpk…` line |
| Imported content is real, not filename placeholders | captured frames of a Murder part page, a Theft part page, the Actus Reus causation page | on-screen | **PASS** — teaching notes, case propositions, blank scenario rows and GIL grids all as in the source |
| Answer spaces survive | block census + export renderer | file, test | **PASS** — 1,040 empty Board paragraph blocks; the exporter renders each as a ruled writing line |
| Editing one page changes only that page | typed a marker into one block on a **scratch copy**, then diffed the physical JSON page by page | file | **PASS** — exactly one page changed (486→505 characters, one block), the other 423 pages untouched |
| Saving | `Ctrl+S`, then read the file | file | **PASS** — file rewritten, board version became 39. No autosave appeared within 3 s of typing (autosave timing NOT RUN beyond that) |
| Saving and reopening | close through the app's own window close, relaunch, re-read file and capture | file, on-screen | **PASS** — the marker was still in the saved page after close and reopen |
| Normal close | `WM_DELETE_WINDOW` to the app window, five times | on-screen | exits every time, **26.4 s** each (D3) |
| Reopening the same file a second time | launch with the path twice, with and without the profile draft present | on-screen | **FAIL** — see D1 |
| Reopening the board the way the owner would (start Boards, no file argument) | launched with no path, then captured | on-screen | **PASS** — the app came back up on the Law board by itself (`Law — Boards`, 21 tree rows, content drawn), so the ordinary route is not blocked by D1 |
| Delivered content survives the application's own save round-trip | thirteen named content checks against the on-disk file after several open/close cycles, re-run on the consolidated file | file | **PASS** — the completed Actus Reus definition, the Theft appropriation element, the Stone and Dobinson ratio, a pack's Gaps list, its "Spaces kept for you" note, a retrieval question, the archive Drive link, the `Actus Reus · 3/7` label and the figure placeholders are all still present; the file is byte-identical to the build that was installed (`602784ba…`, board version still 1) |
| Print / export route | `eng/development/boards_export_html.py` on the installed board, then LibreOffice PDF | file, on-screen, test | **PASS** — 149 Board pages → **587 A4 pages** (6.4 MB); one workpack (Murder) → **17 A4 pages**; a rendered PDF page was read and shows real content with tables and headings |
| Exporter behaviour | `eng/development/test_boards_export_html.py` | test | **7 tests, all passing**, including a real LibreOffice conversion under `$HOME` and the refusal for a path outside it |
| The owner's other boards are untouched | SHA-256 of every file in `~/Documents/9to1/Boards` before and after | file | **PASS** — all 13 pre-existing board files hash-identical; only `Law.9to1board` was added |
| Maths boards preserved | folder census of the PC | file | **nothing to preserve** — no Maths board exists on this machine, only untitled test boards |
| Board embedded in Haven | — | NOT RUN | `Haven.Desktop.Tests` targets `net10.0-windows` and cannot build on Linux |
| Independent acceptance of the Law content | — | NOT RUN | paused by owner instruction; the 106 knowledge gaps remain open by design |

## Defects found in the application (not in the Law content)

**D1 — reopening the same board file opens an empty board.** Reproduced twice: with the profile's
draft copy for that document id present, launching `Boards` with the path to `Law.9to1board` showed
an empty `Notes / Start here` board instead of the requested one. Removing that profile draft and
launching the identical file opened it correctly. Reproduction: open a `.9to1board` by path, close
the app normally, launch with the same path again. The application log records the path and gives no
reason on screen. Workaround used here: clear the profile draft for that document id. Needs a proper
fix in the import path.

**D2 — page changes take 1.66–4.22 s on this board.** Nine measured page opens. Halving the page
budget (70 blocks → 40 blocks, 9,000 → 5,500 characters) changed the settle time not at all
(1.9–3.6 s against 1.6–3.3 s) while doubling the page count from 424 to 717, so the cost is the
application rebuilding the page and the nav tree, not the size of a Board page. This is the same
family of lag the owner reported on 1 November.

**D3 — normal close takes 26.4 s.** Five closes, each 26.4 s through `WM_DELETE_WINDOW` on a 19.7 MB
board. The close path does run (it drains and saves), but it reads as a hang.

**D4 — Page Down / Next does not scroll the page** when focus is inside the page area on this build;
the mouse wheel sometimes does.

**D5 — wheel scrolling on a page is position-dependent, and absent on very large pages.** On the
41,000-character Actus Reus page the wheel worked under one pointer x and not under another; on an
80,000-character page it did nothing at two positions while the scrollbar thumb dragged normally.
Nothing about the page's *size* explains the difference in open cost, so this is an input/hit-test
problem, not a content problem, and it is the reason the board's pages should not be made smaller
to compensate.

Also still open from earlier reviews and re-noticed here: the page tree has no drag-to-nest, so pages
move through the row's right-click menu.

## Method notes worth keeping

The first navigation probe produced a wrong conclusion — it looked like clicking a page's label text
in the tree did nothing while clicking the empty part of the row worked. That was an artefact of
sampling the screen 0.9 s after a click, which is shorter than this board's settle time, combined with
the tree re-scrolling to the selection so the fixed coordinate was no longer the same row. Re-measured
with settle detection and re-targeted clicks, the label row is clickable normally. **No hit-area defect
is claimed.** The rule this established for future native passes: time to a
settled picture, and re-measure the target from the current frame every time.

A second method error of mine, also corrected rather than reported as a product defect: typing
`Actus Reus - Workpack` into the search field appeared to lose the hyphen, which would have been
a keyboard defect. It was my driver — python-Xlib's `XK.string_to_keysym("-")` returns 0 (the
names are `minus`, `comma`, `period`, `slash`), and my helper skipped any character without a
keysym. The same thing earlier made an edit marker land as `MARKER0311EDITTEST` instead of
`MARKER-0311-EDIT-TEST`. With names mapped explicitly, `A-B` types correctly and the search
matched the expected rows. **No hyphen or typing defect is claimed against the application**; the
edit/persistence conclusion is unaffected because the stored string was still unique. The lesson
for future passes: check the driver's key mapping before blaming the app for missing characters.
