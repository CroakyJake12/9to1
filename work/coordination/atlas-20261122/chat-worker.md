# Spaces Chat Options
Base 2a9c4d980. The Spaces Chat worker owns the shared composer, its Chat provider adapters and focused tests. Astra/Flash retain the shared shell and themes.

## Pinned flow
1. Open Options from the composer in the current conversation. Keep the live text input, selected draft, chips, mounted messages and transcript position.
2. Change response style through the existing conversation owner. Stage allowed actions locally until Save permissions. Editing or cancelling sends no request and grants no capabilities.
3. Save permissions applies only a changed allowed-actions choice through the original owner. Cancel, outside dismissal and Escape discard staged changes. Return keyboard focus to Options.
4. Keep compatible task actions available and separate Temporary from response settings. The generic visual/text preference has no Natural Conversation routing authority.

Atlas v4 spaces-chat / spaces-permissions. APP-SPACES-002, SPC-010, NCONV-010/012. Current source shows an inline composer settings row plus a second always-visible thread strip on ChatSpacePage. Implementation and native checks in progress.

## 17:55 UTC
The CUI Options body, native popup transport and owner-bound staged action selection are in the working tree. The second inline controls strip has been removed. The coordinator confirmed ownership of the ChatHavenScene.cs SetResponseState projection so both actual chat hosts can show their current values. A guarded compile/test is running. Additional SDK launches are held during the hardware validation window. The new headless interaction test exercises the actual compiled composer, its original draft/selection/attachment, scroll and close/save controls.


The first SDK check stopped after 35.3 seconds when available memory reached 1098 MiB (1100 MiB floor). See the retained chat-unit1.log.guard.json receipt in the pass workspace. No compilation or test success is claimed. Offline XML checks passed for both UI documents, including original command parameters and removal of the inline duplicate. Source inspection now covers source identity changes, avoiding a duplicate write and preserving the original settings projection. Guarded rerun is pending coordinator release.

## Source review candidate

Eight owned paths are included in chat-candidate.diff. SHA-256 7514715560b848cf4954fae51f3dafc1a69c3137bbb3f8c68c3f2ec8e22e8a14. Each command still dispatches to its original conversation owner. Current evidence is the line review, XML structure checks and a clean git diff --check. The initial guard-stopped check did not verify the build.

- 9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui
- 9to1 Workspace/Spaces/UI/Chat/ChatSpace.cui
- 9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatComposerCuiView.cs
- 9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.Composer.cs
- 9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.cs
- 9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatSpaceComposer.cs
- 9to1 Workspace/Spaces/Tests/ChatComposerCompiledTransportTests.cs
- 9to1 Workspace/Browse/Host.Tests/ChatComposerEmojiPickerViewportTests.cs

The existing action-mode owner on NewChatPage and the ChatSpaceController thread settings set the current conversation’s action filter in memory. Save applies that filter to the original owner, whose normal permission checks still govern execution. This bounded patch adds neither a durable grant record nor cross-restart preference storage. The production response control retains visual/text bias for future responses. Atlas Natural Conversation Automatic/Chat/Agentic Chat/Worker/Design routing and contextual Study activities are separate product work.

## Flash Decision 2 revision

Saving is tracked by edit generation. Older acknowledgements cannot clear the newer save guard or close the newer panel. Opening waits for the original settings hook before revealing the panel. Added focused coverage for an out-of-order save, keyboard Tab cycle, return focus after Save, narrow-window panel sizing and closing the composer with an unsaved panel open. The stage/commit contract test now cites the v4 spaces-permissions route. Rechecked every touched section and refreshed the candidate. SDK validation remains on hold.

## Guarded checks
Spaces producer build passed, compiling the current authored CUI bundle. All 26 focused compiled composer / ChatSpace rendering tests passed at a minimum 2060 MiB headroom. The receipt is chat-spaces-serial.log.guard.json in the pass workspace. The combined desktop test and two desktop compilations hit the 1792 MiB host-memory floor before publishing an assembly. Their receipts and logs are retained. The next desktop check is aligned with the coordinator’s real training window.

Desktop producer passed, 0 warnings and 0 errors. The original Browse.Host.Tests harness then reported five out-of-scope UnifiedWorkspaceHost constructor callsites still missing the windowLifetime argument. A separate fixture owner is repairing those sites. Both focused native cases passed via a scratch copy of the original headless owner configuration, unchanged Chat test source and the actual compiled application assemblies. chat-native-rebased.log.guard.json records a 1862 MiB minimum. A second scratch-only capture hook produced chat-options-native-panel.png from the live 440×410 authored popup; visual inspection confirmed both selections, action grouping, focus outline and footer labels. Full managed-window inspection is owned by the coordinator.

## Integrated checkpoint

The complete canonical Browse.Host.Tests project built after the five test-fixture repairs by the separate fixture owner. Its test-assembly build used current dependencies and disabled test-time analyzers for the reserved memory floor. The production desktop build completed with normal analyzers, zero warnings and zero errors. Direct execution of the canonical test assembly passed all 13 selected cases (two Options interactions and 11 floating formatting-toolbar regressions). Receipts in the pass workspace are chat-host-tests-build.log.guard.json (2539 MiB minimum), chat-host-tests-run.log.guard.json (2706 MiB minimum) and chat-host-tests-run.log. With 26 Spaces checks this gives 39 distinct passing focused checks. The SDK lock is free.

The coordinator integrated the eight-path change as 50799b146. The path-limited baseline-to-HEAD delta matches chat-candidate.diff byte for byte, SHA-256 7514715560b848cf4954fae51f3dafc1a69c3137bbb3f8c68c3f2ec8e22e8a14, and git diff --check passed. Managed desktop inspection is with the coordinator. No external publication or GPU operation was performed by this lane.

## Follow-up on Build Code tabs
The isolated saved-project fixture created a real Home/Files root under /tmp and activated the real native workbench. It approved and opened seed.txt through the original Home/Files reader. The page reported one stable source tab, but its Open source documents row rendered just Pin (expected 2 tab buttons, observed 0). chat-dev-render.log records that failure. The SourceTabItem record does not expose its CUI Repeat item fields; App-level CUI restricts traversal to explicit binding contracts or dictionaries.

Astra assigned exclusive ownership of DeveloperProjectWorkbenchPage.cs for the typed SourceTabItem binding fix. Flow: create and admit an isolated existing project, open each real file, pin tabs to retain multiple documents, observe the selection and close controls, measure their center in the region before Pin, and verify narrow layout using the real compiled page. Atlas build-code / build-code-diagnostics, UI-006, APP-BUILD-001/002. No owner project or Files registration is modified.

The assigned follow-up now includes the durable native regression in shared/tests/Haven.Desktop.Tests/DeveloperWorkbenchSearchPublicationTests.cs. SourceTabItem exposes exactly its six existing CUI fields; other repeat and permission paths are unchanged. The two-file candidate is preserved as work/dev-tab-capture/typed-tabs.diff in the pass workspace. The genuine admitted baseline failure is in chat-dev-render.log. One CPU and a 1792 MiB floor still constrained compilation: 384 and 640 MiB managed-heap ceilings kept the system above the guard but caused Roslyn OOM. The 640 MiB receipt started 19:14:51 UTC, minimum 1895 MiB. Astra owns the next resource decision; no working-result geometry has yet been asserted.

## Develop tabs follow-up evidence
The original control binding defect was reproduced on the genuine manual-page fixture: one Home-approved disk file produced a saved source tab in the model and zero tab controls next to Pin. The six existing fields on SourceTabItem now implement the runtime's read-only item contract. A viewport capture then revealed that the original workbench root lacked vertical scrolling. Astra assigned the one-line root CUI change and the existing shared DeveloperWorkbenchSearchPublicationTests regression in addition to the native page.

The original Dev producer built successfully (chat-dev-scroll-package.log). A guarded friend test linked all 23 unchanged-or-edited production workbench partial files by path. Its Linux Home/Files graph used a real saved project registered wholly under a temporary directory. The new Dev assembly was hash-verified, and the real compiled CUI page opened seed.txt, Layout.cs and Colors.cs through individual Home read decisions. At 1,250/540 DIP window widths all three selectable labels, associated close buttons and trailing Pin were visible after genuine vertical scrolling. The one/two/three-tab centering error was 0.5/0/0 DIP at desktop width. At the narrow width, tabs wrapped cleanly over two rows, remained centered and left 11 DIP clearance before Pin. Real Tab-key traversal reached all six tab controls and then Pin. Pointer Unpin, a new authorised read when selecting another tab, and closing the intended tab without deleting its disk source all passed. The final run guard minimum was 2302 MiB. Full-page captures (page-3-wide.png and page-3-narrow.png), strip captures, geometry.json, and the complete scratch fixture are under the pass workspace's work/dev-tab-capture. The normal full Haven assembly/reload was resource-blocked; this evidence is exact production-source component rendering, not an installed app/landing-route acceptance claim.

The final three-file candidate was checked line by line. git diff --check passed. Pass workspace work/dev-tab-capture/typed-tabs.diff SHA-256 cb9dc82355abd23d88b1397bf70960a228d20b31d6f0f5de60c912425dceafe4. Astra owns normal full-build integration and any next managed reload.
