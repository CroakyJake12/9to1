# Canvas documents and ink editing

Canvas belongs to Work. Its app surface uses a revisioned document and Files integration. The editor and host helpers must preserve the active document, ink object identity and save destination across changes to the viewport or tool.

The Rnote engine bridge provides drawing capabilities behind a renderer-neutral interface. Keep donor data and native artifact identity distinct from 9to1’s document envelope. Verify an ink or eraser action in the actual Canvas view, commit it, reopen the same document and check its persisted contents and undo behavior.

A bridge smoke test establishes the exercised engine operations. A complete Canvas interaction additionally needs the effective tool, viewport and actual object result.

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

[Canvas workspace](CanvasCuiWorkspace.cs) · [Native viewport](CanvasNativeViewport.cs) · [Rnote bridge](rnote-poc/README.md) · [Capability inventory](tools/README.md)
