9-1 Boards local Windows editor

Run CakeOS.Apps.Boards.App.exe to open the default local board, or pass one
quoted .9to1board path to open/create that board. Use File > New board for a
new user-chosen path. Keep the whole extracted package together, including
Boards.cui, managed libraries, fonts and native runtime libraries.

This executable keeps local physical .9to1board files. The default is
Documents\9to1 Boards\Board.9to1board, falling back to LocalAppData when the
Documents directory is unavailable. Close waits for an actual save. A failed
save cancels close and leaves the editor available for retry. Save As changes
the current target only after its physical store acknowledges the new file.

Large attachments use a sibling .files directory. Move/copy it together with
the board. Cross-directory Save As sidecar portability is not established by
this package; use the original location until that separate workflow is proven.

This is the existing free local editor. Account login, collaboration and sync
are separate workflows. The Flutter AppFlowy preview and Canvas/Rnote engine
are separate and are not bundled here.

Avalonia licence and third-party notices are in licenses.
