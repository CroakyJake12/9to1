# HavenOS Canvas

This directory is the bounded standalone Canvas app surface for HavenOS.

## Functional journey

The first slice exposes a framework-neutral Canvas session that can create, open, and save native Canvas documents through the shared `INotesRepository`, add and move objects, connect objects, draw ink, pan/zoom the board, and undo/redo edits. The repository retains the canonical Notes document and its versioned persistence behavior; Canvas does not introduce a second storage format.

The app surface deliberately delegates creative behavior to the existing engine in `src/Haven.Application/Canvas` and the native Notes canvas data model in `src/Haven.Core/Notes`. It does not duplicate geometry, ink, connector, history, or document-storage rules.

After history operations, `CanvasAppSurface` synchronizes the controller's active board back into the canonical `NotesDocument`, so restored state remains the state that storage and later app surfaces observe.

## Focused validation

```text
dotnet build "9to1 Workspace/Canvas/HavenOS.Canvas.csproj" --configuration Release
dotnet test "9to1 Workspace/Canvas/Tests/HavenOS.Canvas.Tests.csproj" --configuration Release
```

The interaction test covers create -> add objects -> snapped move -> connector -> pen stroke -> pan/zoom -> undo -> redo, plus canonical document synchronization. Persistence tests cover repository-backed create -> edit -> save -> reopen and reject opening ordinary Notes documents as Canvas.

## Native controller save and reopen

The app-owned native controller and its managed controls live under
`9to1 OS/HUI/CanvasApp` and `9to1 OS/HUI/CanvasApp.Tests`. Its Rnote save uses a
new, exclusively created sibling staging file and publishes the active document
path only after promotion and size verification. Failures before that acknowledgement
retain the current document identity and dirty state, and cleanup touches only
that save's owned staging file. Status-observer faults preserve an already
acknowledged file result and remain visible alongside write or cleanup faults.
Opening a replacement validates its render bounds and prepares its
tool state before retiring the previous session. Failed replacement preparation
closes the candidate and retains both preparation and cleanup causes when needed.
If retiring the previous session itself throws, the failure remains explicit;
its retained identity does not establish that the partially retired session is usable.

The physical save/reopen controls use managed session fixtures. The preserved
native Rnote component results and DLL closure require their own exact source and
package lineage; these controls establish no installed GUI, account admission or
full application acceptance.
# Recovered native Home owning route

`Host/CanvasHomeWindowFactory` opens the compatible Rnote Canvas window inside the same genuine Windows Home process, using its actual profile, broker, Files owner and process lifetime. Root supplies the owning Home navigation/composition seam. Canvas joins its original input, render, CUI and approval work before releasing its local native document; it does not retire global Home or Files owners.

The production Files bridge retains Windows physical ancestors, single-link artifact handles and the original Files directory-binding record, then passes the actual privately claimed Home commit fence through canonical Files CAS. It preserves an actual provider acknowledgement before later audit/reopen work and never replays that mutation. Unconfigured standalone Canvas launch refuses honestly.

The compatible C22 engine imports 28 functions present in the preserved ABI-3 native kit. `tools/original-windows-native-kit.json` pins its 33-DLL closure; `tools/Stage-OriginalWindowsNativeKit.ps1` stages that original closure/licenses into a normal Home publish output. The later user-layer/filtered-render exports are unavailable. Source recovery, kit staging and component controls do not establish GUI, installed-domain, signing or clean-host acceptance; actual owning build and draw/save/normal-close/fresh-process-reopen evidence remains required.
