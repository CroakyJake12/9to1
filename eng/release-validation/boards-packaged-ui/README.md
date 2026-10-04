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

The `rich-history` scenario adds a separate native workflow. It types one
paragraph through the keyboard, focuses that real paragraph and sends Ctrl+B.
The named native Bold button must expose an On TogglePattern state, and the
actual file must contain a bold canonical text run with the same paragraph
text and block/document identities. The native Undo and Redo buttons are invoked
through their process-bound InvokePattern; each must restore the expected Bold
state in the current rebuilt native toolbar and actual durable file. This tests
the application's canonical history command, rather than interpreting a text
box's local Ctrl+Z history as document history. It covers whole-paragraph
formatting and selection, not substring or multi-run selection semantics.

After Redo, actual save/normal close/reopen must restore the typed paragraph,
its native Bold toggle state and canonical rich data. Each current automation
peer is reacquired after rebuild; absent, ambiguous or unsupported native
controls fail. Only this new scenario is dispatched by the continuation
workflow. The original `edit-save` scenario, successful 67-control run
`37210601635` at `099d7ee01ca2f530c5c16acde7fc10e7f2e8ecd9`, and its original
public evidence index remain preserved. Common custody, PID/focus, extraction
and bounded stream-drain guards are reused unchanged.

The OS/desktop/UIAutomation/input admission is a real gate, not an assumed
capability. Lack of an interactive desktop or native automation/focus remains
`FAILED_OR_BLOCKED_UNACCEPTED`. No app/source/provider/Home bootstrap is changed.
The hosted runner contains SDKs; no clean-PC, installed Home tuple, signing,
donor breadth, all themes/devices or full-app acceptance is claimed. C6 review
and root invocation are required before executing this new workflow.
