# Law Board import and study-section completion — 3 November 2026

P0 for 4 November 2026: build a real, saved, reopenable **Law** Board in the existing Boards
app, import the owner's Law archive into it, and complete the study sections of the Criminal
Law workpacks without answering a single student question. This file records what was built,
the decisions behind it and what is still open. The testing evidence is separated into
[the testing report](law-board-testing-2026-11-03.md) and the unanswered knowledge into
[the gap report](law-board-gap-report-2026-11-03.md).

## What is on disk

| | |
| --- | --- |
| Board file | `/home/jacob/Documents/9to1/Boards/Law.9to1board` |
| Size / identity | 19,696,822 bytes, SHA-256 `602784bab563a5373a53d93bf86d343ae08560d37d39984c18580cf4ab86deed` |
| Document id / schema | `7385c54b-81ee-4c12-9181-998b03571103`, `.9to1board` schema 5, board version 1 |
| Shape | 6 sections, 445 pages, 7,427 blocks, 522 tables (1,527 rows), 432 lists, 2,530,066 characters |
| Largest single page | 13,463 characters (`Criminal Law · 27/28`) |
| Source archive | `Law (Old)-20261103T193832Z-1-001.zip`, 1,379,560,661 bytes, SHA-256 `f2f1444892e7567bcb08a8c24eba3caee6f7f96bba03b001a53177e3999170e6` |
| Documents read | 275, indexed into 36,685 snippets with section and slide attribution |

The board was not hand-written JSON. A private harness
(`/tmp/opencode/law/gen/Program.cs`) builds it through the real
`CakeOS.Apps.Boards.Contract` model, runs the application's own
`HavenRichNotesValidator`, serialises with the store's settings and then reads the finished
file back through `HavenBoardDocumentCodec` before it is accepted — so the file the app opens
is the file the validator admits. The harness is deliberately outside the repository: it is
import tooling, not application behaviour.

## Structure: the owner's tree, not mine

The owner supplied an AppFlowy sketch of their Law workspace on 3 November, and that sketch is
the top level of the board:

`Summary Sheets | Workpacks | Retrieval Tasks | Practice Papers & Exam Questions | Assessments | Other`

Two surfaces the work order asked for — the source index (`Source Library`) and the
outstanding-work list (`Outstanding Work and Gaps`) — are kept as **pages inside Other**
rather than as two more top-level sections, so the tree on screen is the tree the owner drew.
That is one edit in `build_board.py` to reverse if they would rather see them as sections.

Every imported document gets a parent page carrying its provenance (the archive path and, where
one exists, the Google Drive link) and its parts nest underneath as
`<short name> · 3/7`. The short name exists because of a measurement: the sidebar truncates a
page title to roughly twenty characters, so `Actus Reus - Workpack (2025) (part 3 of 7)` and
`(part 4 of 7)` both read as `Actus Reus - W…` — the part number, the only thing telling those
rows apart, was exactly what got cut off. On screen the tree now reads
`Actus Reus · 1/7 … 7/7`.

Page budget: 70 blocks and 9,000 characters per page, with long tables split across page parts
and their header row repeated. Both limits come from measurements rather than taste — Boards
renders a page's blocks without virtualisation (a recorded NOT-IMPLEMENTED row in the Boards
coverage ledger), and the first build put the whole Source Library index into one list block,
which made a single page carry 52,741 characters that no splitter could break.

## How the workpacks were completed, and what was refused

Thirteen Criminal Law workpack copies already carried a study-notes block from the earlier
pass. The knowledge grids inside them are now filled with **179** cells, and each fill is one
paragraph quoted verbatim from the owner's own material with its file recorded:

| where the answer came from | cells |
| --- | --- |
| `Revision Resources/Summary Sheets/Criminal Law.docx` | 123 |
| the teaching-notes block that pack already carries | 54 |
| the pack's own category label (for a case's "area" column) | 2 |

Table classification, per pack: 76 knowledge grids completed, 209 student-response grids left
alone, 18 GIL/administration grids skipped, 33 mixed grids completed only where a note matched
the row exactly. **249** response spaces are preserved blank, and **106** knowledge cells are
reported as gaps rather than filled — see the gap report for every one of them.

Sources deliberately never used for answers: the workpack bodies themselves, assessments, exam
papers, mark-scheme material, feedback decks, the specification PDF, and any lesson file that is
a copy of a workpack. Retrieval questions, scenarios, problem questions, essays, conclusion and
exam-technique rows, empty rows in a grid and GIL trackers are never written into.

Five wrong behaviours were found by reviewing the output of my own earlier passes and then
fixed, and they are recorded here because they are the interesting part of the work:

1. **A bullet fragment quoted as a definition.** Slide decks split one sentence across bullets,
   so "Property:" followed by "there must simply be an intention to do so." produced nonsense in
   a definition cell. Quotation is now restricted to a complete labelled statement.
2. **The same short label answered from the wrong offence.** The summary sheet reuses "Actus
   Reus" and "Mens Rea" under several offence headings, so the Murder pack was first answered
   from the Burglary section. Evidence is now scoped: the pack's own summary-sheet section first,
   then that sheet, and unrelated documents only when neither states the point.
3. **A paragraph that merely mentions the term.** A Tort duty paragraph landed in a Criminal Law
   "aims of the law" row because the label appeared late inside it. A label must now be reached
   near the start of the paragraph.
4. **Repeated content — the fault the order asked to correct.** Facts, Ratio and Area of one case
   row were all filled with the owner's single combined sentence. Each passage is now classified
   from its own wording into facts or ratio and written only into the column it answers; the other
   column becomes a gap that says *"the notes state this case as one combined proposition"*, which
   is the truth about the source instead of a second invented sentence.
5. **One case answering another.** "DPP v Smith (2006), hair-cutting ABH" answered the causation
   grid's "R v Smith". Case names are now matched as a contiguous run of words at the head of a
   note line, which still lets the sheet's "Gibbins and Proctor" answer the pack's
   "Gibbons and Proctor" but cannot let one case answer a different one.

Images inside the source documents are **not** embedded. Where a figure exists, the page carries
an explicit note — `[Figure in the source: 2 images here. The original file is listed in Source
Library.]` — and 1,040 empty paragraph blocks preserve the writing space of the originals.
Embedding the images is open work, listed in the board's own Outstanding Work page.

## How to reproduce

Everything is scripted; nothing about the board was typed by hand into the app.

```
# 1. corpus and knowledge filling
python3 -B /tmp/opencode/law/corpus.py                      # 36,685 snippets from 275 documents
python3 -B /tmp/opencode/law/fill8.py                       # 13 packs -> parsed_wp/*.json + *.audit.json

# 2. recipe (sections, nested pages, blocks) and Board generation
python3 -B /tmp/opencode/law/build_board.py                 # out/law_recipe.json, out/tally.json
python3 -B "$SDK" exec gen/bin/Release/net10.0/LawBoardGen.dll build out/law_recipe.json out/Law.9to1board
python3 -B "$SDK" exec gen/bin/Release/net10.0/LawBoardGen.dll check out/Law.9to1board
cp out/Law.9to1board "/home/jacob/Documents/9to1/Boards/Law.9to1board"

# 3. print route (output must be under $HOME: the LibreOffice snap here cannot read /tmp)
python3 -B eng/development/boards_export_html.py \
    "/home/jacob/Documents/9to1/Boards/Law.9to1board" \
    --out ~/Documents/9to1/export/law_whole_board.html --pdf
```

The Boards application itself was rebuilt on 3 November under the memory guard
(`eng/development/guarded_lowmem_build.py --floor-mib 1100`, pinned SDK 10.0.301,
`-m:1 -nodereuse:false`): result OK, exit code 0, 0 errors, 7.1 s, minimum 3,349 MiB available,
peak process-tree RSS 105 MiB. SDK work stayed serial under
`flock -n /tmp/opencode/9to1-sdk.lock`.

## Decisions taken during this work

| decision | why | reversing it |
| --- | --- | --- |
| Owner's six-item AppFlowy tree as the top level | explicit owner instruction on 3 November | n/a |
| Source Library and Outstanding Work as pages inside Other | keeps the drawn tree honest while still delivering both support surfaces | one edit in `build_board.py` |
| Answer keys and question task sheets indexed, not pasted | completing a workpack must not mean copying an answer into a blank | per-file change in `section_other()` |
| Gap cells left blank and listed, not filled with a note | the order forbids inventing; a note in the cell would destroy the writing space | already both: blank in the grid, listed under the pack |
| 70 blocks / 9,000 characters per page | page-open settle measured 1.66–4.22 s for both 40-block and 70-block pages, so the smaller budget only doubled the tree (424→717 pages) without making it quicker | constants in `layout.py` |
| Private tooling kept in `/tmp/opencode/law`, only the exporter committed | the harness is import tooling for one archive, not application behaviour | move it under `eng/development` if the owner wants repeat imports |

## What is still open

- Figures from the source documents are named but not embedded.
- The page-open settle of 1.66–4.22 s on a 445-page board, and the 26 s close, are application
  behaviour, not board content: recorded in the testing report as defects D2 and D3.
- Re-import after a profile draft exists opens an empty board (defect D1) — it has a workaround
  (delete the profile draft for that document id) and it must be fixed properly.
- The embedded Haven route for this board is unverified on this PC:
  `Haven.Desktop.Tests` targets `net10.0-windows` and cannot build on Linux.
- Full independent acceptance stays paused under the owner's instruction; nothing in these three
  files claims acceptance.
