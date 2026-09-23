# HavenOS Canvas

This directory is the bounded standalone Canvas app surface for HavenOS.

## Functional journey

The first slice exposes a framework-neutral Canvas session that can create, open, and save native Canvas documents through the shared `INotesRepository`, add and move objects, connect objects, draw ink, pan/zoom the board, and undo/redo edits. The repository retains the canonical Notes document and its versioned persistence behavior; Canvas does not introduce a second storage format.

The app surface deliberately delegates creative behavior to the existing engine in `src/Haven.Application/Canvas` and the native Notes canvas data model in `src/Haven.Core/Notes`. It does not duplicate geometry, ink, connector, history, or document-storage rules.

After history operations, `CanvasAppSurface` synchronizes the controller's active board back into the canonical `NotesDocument`, so restored state remains the state that storage and later app surfaces observe.

## Focused validation

```text
dotnet build "HavenOS Apps/Canvas/HavenOS.Canvas.csproj" --configuration Release
dotnet test "HavenOS Apps/Canvas/Tests/HavenOS.Canvas.Tests.csproj" --configuration Release
```

The interaction test covers create -> add objects -> snapped move -> connector -> pen stroke -> pan/zoom -> undo -> redo, plus canonical document synchronization. Persistence tests cover repository-backed create -> edit -> save -> reopen and reject opening ordinary Notes documents as Canvas.
