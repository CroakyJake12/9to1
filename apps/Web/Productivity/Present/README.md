Current browser UI restoration (12 October 2026): the same original owner now
exposes table/chart insertion and Properties editing through all Insert menus.
`PresentBrowserSession.ContentUi.cs` holds only presentation bindings and dispatch;
`PresentEditor.Structured.cs` remains the mutation owner. Selection/generation
fences reject stale cell/data callbacks. Chart data retains independent series,
finite numeric values and real persisted categories. Focused native evidence lives
in `Tests/PresentUiAcceptanceChecks.cs`; integrated browser rendering and matched
Impress donor acceptance remain separate required gates. The current immediate
fill/stroke controls supersede the historical Apply-button description below.

Imported PPTX titles now remain original `PresentElement` text objects with the
existing extensible `title` role. Their geometry and uniform formatting inherit
from the actual title placeholder/layout/master/theme, with direct slide values
taking precedence. The element owns visible title content; `Slide.Title` stays
synchronized for navigation and legacy callers. The original editor's live edit,
undo, deletion and duplication operations preserve that relationship. Rendering
and export suppress the synthetic title only when a title object exists;
metadata-only documents retain their original title behavior without a schema
migration. Hidden titles keep their native hidden flag, and a slide without a
title placeholder keeps an empty metadata title instead of gaining a synthetic
numbered heading. Ordinary textboxes remain ordinary text even when named
"Title". This focused change does not claim general master/layout fidelity or
mixed-run text parity. `Tests/PresentTitleChecks.cs` covers the independent PPTX
fixture, owner identity/history, real repository and PPTX round trips, and native
physical title editing; browser and pinned Impress execution remain required.

The browser Present route consumes the original `PresentEditor` and an injected
`IPresentRepository`. The specialized CUI object compiles the original
`PresentSlideCanvas` source files and reuses the single original Haven scene
assembly, owning font assets and extracted resources already linked for Write.
It introduces no presentation document model, drawing engine, animation runtime,
permission broker or alternate repository identity.

The owning canvas's typed selection, movement, transform, vector and live-text
events call the owning editor operations. Selection changes are raised as a
`SelectionChanged` notification so the browser controls follow the native
selection without a rebuild, and the selection toolbar drives bold, italic,
grow, shrink and both rotations over the owning editor. Shift-click multi-select
drives the arrange toolbar, which aligns, distributes, groups, ungroups and
nudges the selected objects one step at a time. A session-scoped object
clipboard stores the editor's own copy payload, clears whenever a deck is
attached, and rebuilds that payload under a new identity through Paste, while
four nudge controls move the selection one 0.01 donor step left, right, up and
down. A two-way fill input applies a
colour to the selected shape, and the font size controls move a selected text
object's point size. Two-way stroke colour and stroke width inputs apply the
object's own outline through `Apply stroke` and `Apply stroke width`, which take
the owner's vector stroke path on a vector shape and the style path with a
transparent outline for a blank colour otherwise. Present runs a
`PresentPlaybackSession` over a private snapshot, keeps the canvas on the
running frame and offers previous, next and end controls until the presentation
ends. A slide can be hidden from the running presentation and shown again, which
sets the slide's own `Hidden` flag so the playback session leaves it out of its
frame list. Export writes the open deck through the owner `PresentPptxExportService`
and hands the bytes to the browser as a `.pptx` download, while import opens a
real browser file chooser, rebuilds the picked file through the owner
`PresentPptxImportService` and commits it as a new stored presentation; both
travel over the Present-owned transfer bridge in `present-indexeddb.js` and
both freeze while a presentation is running. Live-text preview remains explicitly
dirty; Save commits the owner's live editing transaction before snapshotting.
Native and retained input freeze while busy, read-only, uncertain or
presenting. This is
presentation enforcement, not backend ACL authority. The actual acknowledged
durable revision remains the CAS base through content undo/redo.

Registration requires actual durable storage. No default document is created by
opening the route. Production browser composition remains pending. Focused native retained CUI
checks passed 128 workflow and 14 staged-admission assertions; separate seed and
fresh-process verification passed 7 and 8 assertions using the original
filesystem repository/package codec. These results are not browser acceptance.
Exact source, commands, failures and oracles are recorded in the Team B
`continuation/b2/productivity-gates/handoff.json` evidence.

The reviewed browser-storage implementation must preserve current/previous
versions, conflict admission, corruption recovery and referenced assets. The
original private repository validation needs an owner-reviewed portable seam.
Native `.9to1p` import allocates an independent document ID, and its extracted
assets cannot be treated as durable merely because ephemeral codec staging
succeeds. Foreign formats, donor parity, full launch/authoring chrome, Actions,
components, notes identities/rich text, accessibility and complete canonical
Present requirements remain required and unverified by this focused adapter.
