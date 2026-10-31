# Boards usability native evidence — 31 October 2026

Actual production `Boards/app/Program.cs` and mounted `Boards.cui`, Linux x64 X11/Skia, private Xvfb :93 with xcompmgr client composition. SDK 10.0.401, Release, repository low-memory guard and serial SDK lock. These are unmodified screenshots of the running application. No owner profile or installed application was changed. All private test applications were closed normally through Exit.

## Final source and outcomes

Final implementation: `a0f37f4fa5961d26ec832bf624e549bb1bcd2660` on `main`. `native-conversion-build.json` records exact assembly hashes; the final source was built and exercised immediately before committing, so informational versions still name the preceding `115834b9` commit. `verification.json` records full Boards 469/469, focused adaptive5/5, and shared CUI 754 passed/12 known failures/766, including failure names and TRX hashes. This is engineering evidence, not full specification acceptance.

- `native-conversion-focus.png`: final source, 1440×900 application window; the third paragraph was converted to a bullet list through the toolbar, then accepted new keyboard text at its preserved caret without clicking the row again.
- `native-conversion-result.json`: saved row text after that interaction, with the unchanged first/second paragraphs and one persisted ink stroke.
- `native-final-narrow.png`: same final running application resized to 900×700; long list text wraps and fixed actions stay visible while adaptive controls collapse.
- `native-final-reopen.png`: exact `115834b9` native build reopening the renamed board, text/bold/font size and real ink.
- `native-style-keyboard.png`: native style selector reopened with Space after Escape, demonstrating restored keyboard focus.
- `native-list-feedback.png`: unavailable list typography explains itself on hover over the enabled parent group.
- `native-build.json`: assembly hashes for the exact `115834b9` build.

## Earlier verified interactions

The earlier `final` run preceded the list-capability and selector-Escape follow-up; its paragraph layout remains the same.

- `native-wide.png`: 1440×900 application window, default bright theme; board-name menu trigger, save status, direct history, adaptive paragraph toolbar and fixed Add/Drawing/AI actions.
- `native-narrow.png`: same live application at 900×700.
- `native-file-menu.png`: actual board-name menu with applicable existing operations and left-aligned frame. Recovery choices appear only when relevant.
- `native-history.json`: persisted font size after native Undo and Redo clicks, 0 then 22.

Full desktop captures include the surrounding Xvfb background. Only the default bright theme was checked natively. Raw runs, private board files, logs and additional screenshots remain at `/workspace/scratch/boards-native-tools/runs/{overhaul1,overhaul2,final,verified,focusfix}`. The final disposable board is `/workspace/scratch/boards-native-tools/overhaul-final.9to1board`.

Launch route: `/workspace/.tools/dotnet/dotnet '/workspace/9to1/9to1 Workspace/Boards/app/bin/Release/net10.0/CakeOS.Apps.Boards.App.dll' [board file]`, with explicit private HAVEN_DATA_DIR/XDG variables. The cloud helper `/workspace/scratch/boards-native-tools/native.py` records PID, arguments, profile and X11 window for each run. This evidence does not establish owner-PC/ARM acceptance or completion of the remaining Boards specification.
