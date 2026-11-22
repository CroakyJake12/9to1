# Choose a Boards verification path

Start with the operation that changed. The typed tests cover document integrity, attachments, freeform objects, rich editing, history granularity, section icons and Trash. Use the corresponding native CUI/host checks when the change affects a visible affordance.

The PowerShell foundation check verifies the pinned AppFlowy boundary and provenance. The collaboration and generative checks cover separate contracts. A source-level gate cannot verify native input or restore a real document by itself.

For a user-facing edit, create a disposable local board, perform the action, undo or redo as appropriate, save and reopen. Keep donor compilation, native window evidence and distribution acceptance separate.

[Tests](CakeOS.Apps.Boards.Tests.csproj) · [Static foundation check](verify-boards-foundation.ps1) · [Editor](../app) · [Native preview](../../../docs/development/live-preview.md)
