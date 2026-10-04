# Immutable packaged Boards native controls

This provisional workflow reuses the original successful Windows package from
run `37208129079`, source `fd5b989bcaebbd44261f0d5e4d02f72d7381b4b8`.
Binary wrapper artifact `11305252552` and original observation artifact
`11305292433` are separate. The reviewed catalogue pins both metadata records,
the inner package SHA256 and original full manifest SHA256. The harness verifies
every extracted original file before launching the existing packaged executable.
It does not rebuild the SDK/application or repeat the 54 normal app tests.

Windows PowerShell 5.1 loads standard Windows UIAutomation. Real `SendInput`
keyboard text and Ctrl+S run only with the exact launched process in the
foreground and the exact named native editor focused. The application creates
a real board at an absent explicit path inside the exclusively owned runtime
fixture directory. The harness reads that actual file to obtain generated IDs;
it never writes app state, manufactures IDs or calls a session/model API.

Controls check accessible Edit names, enabled/visible/focusable state and native
ValuePattern; type title and paragraph through the actual keyboard; observe
durable content; close normally; launch the packaged executable again on the
same physical file; and observe native display, stable identity and byte
preservation before the second close. Screenshots crop the actual foreground
app window. App stdout/stderr, observations and failures are retained. Only the
two exact processes started by this harness may be cleaned up. Forced cleanup
never establishes success. Ctrl+S delivery and durability are observed; existing
autosave may also contribute, so the test does not isolate shortcut causality.

The OS/desktop/UIAutomation/input admission is a real gate, not an assumed
capability. Lack of an interactive desktop or native automation/focus remains
`FAILED_OR_BLOCKED_UNACCEPTED`. No app/source/provider/Home bootstrap is changed.
The hosted runner contains SDKs; no clean-PC, installed Home tuple, signing,
donor breadth, all themes/devices or full-app acceptance is claimed. C6 review
and root invocation are required before executing this new workflow.
