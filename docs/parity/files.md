# Files implementation and acceptance

Updated 16 October 2026 against the current V2 specification. Files is a **Part 1 FULL** product (V2 line 111); this report does not reduce that scope. The historical audit below predates the active Files CUI projects. Its "no ACTIVE implementation" conclusion is superseded by the current inventory. **Full Files acceptance remains incomplete.**

## Current implementation

The Files requirements occupy lines 2528 onward of the V2 text fetched on 16 October. Relevant obligations include stable identities, declared provider capabilities, native navigation/view parity, recoverable deletion (2713–2719), search/current permission checks (2740), properties (2788), and native/web platform behavior. Shared APP-AC and CUI obligations apply independently.

| Active area | Actual responsibility |
| --- | --- |
| `9to1 Workspace/Files/CUI` | Stable hosted contracts, durable local metadata/content provider, journals, cursors, materialization metadata and artifact boundaries. |
| `9to1 Workspace/Files/NativeHost` | Existing Home/profile/store composition, authenticated canonical reads, original-source ownership, reviewed mutations and typed cross-app selection. |
| `9to1 Workspace/Files/NativeUI` | Canonical `FilesBrowser.cui` scene and native input/controller lifetime. |
| `apps/Web/Files` | Provider-backed CUI route/controller; authenticated provider composition remains the host's responsibility. |
| Files CUI and Web test projects | Executable checks against real durable Files state; component checks do not establish native/deployed browser acceptance. |

Native Files lists/searches canonical children, opens folders, navigates up/back/forward/root, pages results, sorts the displayed page and exposes item metadata. Creating and renaming uses the same Home permission broker, source-issued page/row, expected store/item/parent revisions and final commit fence. Registered application roots remain protected. A retained actual operation result survives terminal Home audit failure; Retry audit never repeats the mutation.

Native Files now supports same-store Move: retain the actual original selected row, browse the destination, choose Move here, review in Home and Apply. Both source and destination pages and parent revisions are retained; the final metadata transaction preserves the item/subtree IDs and refuses cycles, stale state and name conflicts. The UI now keeps human properties prominent, exposes stable IDs/revisions through an explicit Technical details control, and shows complete sort labels. The Move flow, guarded provider transaction and actual CUI controls pass the focused Debug and Release component suites.

The current native Trash change adds a separate source-issued Trash view and original-store tombstone reads. Ordinary Get/List still hide deleted items and descendants. Delete and Restore require their own explicit Home decisions; Restore uses the deleted revision and original parent, refuses missing/deleted parents and name conflicts, and keeps original IDs. Deleted containers retain descendants and content. No purge action is exposed. Cursor identity distinguishes live and Trash views. Human details show type, size, availability, owner, privacy and timestamps; stable IDs and revisions are available through Technical details; teardown clears private search/name fields and terminal history.

Web Files preserves stable-ID create/rename, revision conflicts, lost-reply/idempotent retry, private-route teardown and bounded pagination. It now also provides root/back/forward navigation, folder-scoped provider search, clear search, query restoration on revisiting a folder, and sorting of loaded items. Search is capability-gated. History contains canonical IDs and queries, re-reads current metadata and is bounded to 128 visits. Refresh/search/move cannot leave an off-page selection active, and retired rendered rows cannot select current items. Private search/history clears on denial and disposal.

## Current verification

| Check | Result | Boundary |
| --- | --- | --- |
| Web Files Debug build | PASS, 0 warnings/errors | Normal production project references; serial build. |
| Web Files Release build | PASS, 0 warnings/errors | Normal production project references; serial build. |
| Web Files Debug executable | PASS, 22/22 | Actual DurableDriveProvider persistence; controlled faults; no browser/HTTP. |
| Web Files Release executable | PASS, 22/22 | Same cases in Release. |
| Files CUI domain Debug and Release executable | PASS | Existing maintained suite plus 3 real durable Trash/recovery checks and guarded Move/cycle/conflict/restart checks. |
| Native Files Debug component tests | PASS, 20/20; 0 skipped | Maintained Home/broker/Files fixture and exact address parser linked into a portable test project; actual CUI controls included. |
| Native Files Release component tests | PASS, 20/20; 0 skipped | Actual Skia CUI current-folder, Trash and Move destination captures were inspected at 720×640 in the separately hashed preceding Move baseline. |
| Canvas Files artifact bridge Debug and Release | PASS, 1/1 each; 0 skipped | Actual durable artifact create/save/reopen, rename/move identity and stale-revision refusal; no standalone launch admission. |
| Installed native render/input/accessibility | NOT RUN | Exact installed route and required platforms remain necessary. |
| Authenticated deployed web/HTTP/cloud sync | NOT RUN | Local provider checks do not establish production cloud behavior. |

The 22 web checks retain all 17 existing cases and add canonical-history reopening after rename, folder-scoped query/history behavior, sorting without identity changes, retired/moved row refusal, and search capability/privacy boundaries. The first build attempt was cancelled because `dotnet run -m:1` spawned parallel restore nodes; successful commands used explicit serial `dotnet build`, then executed the DLL. No failed assertion was deleted or weakened.

The native component project `9to1 Workspace/Files/NativeUI.Tests` references the actual production projects and links the existing maintained `FilesNativeBrowserMutationTests.cs`; it does not copy production sources or replace the canonical Home/Files graph. The original x64 Desktop suite remains unchanged and cannot execute on this ARM64 host without an x64 execution facility. Portable component evidence is Linux-only evidence. Initial harness compilation exposed missing explicit test access/DI references; those were added without making internal runtime APIs public. Domain compilation exposed a pre-existing span crossing an await in a byte-preservation assertion; awaiting the same bytes first preserves the assertion.

Move verification retained the first failed native run: its new conflict assertion incorrectly included configured App-root creation events. The corrected assertion compares the complete event count before and after refusal, and verifies both conflicting identities remain. The production source compiled without errors. Final evidence is in the task workspace `work/files-acceptance`: native `native-debug-4.log` / `native-release-2.log` (8/8), domain `domain-debug-build-3.log` / `domain-debug-tests-2.log` and `domain-release-build-2.log` / `domain-release-tests-2.log`, web `debug-build-4.log` / `debug-tests-4.log` and `release-build-3.log` / `release-tests-3.log` (22/22), and `canvas-files-bridge-{debug,release}-1.log` (1/1 each). Actual captures are under `native-move-captures/`. These Linux component results do not establish installed Windows or deployed web acceptance.

The exact pre-adapter Move baseline is bound to `work/files-acceptance/files-move-baseline-manifest.json`: base Git HEAD, 132 source/test hashes, the serial command runner hash, twelve passing result/build log hashes and three inspected capture hashes. Subsequent native app adapter changes are separate work and do not inherit these earlier passes.

## Unified host adapter work in progress

The Files-owned adapter in shared Desktop `Services/FilesBrowseAppContent*.cs` creates a distinct original Files route and surface per presentation, borrowing the actual Home graph and lifetimes. It has no nested application window or shell and creates no native Control during factory allocation. It accepts `9to1://files`, `9to1://files/store/<StoreID>` and an optional `/folder/<FolderID>`; the requested profile is checked against the actual admitted Home actor. Explicit store identity is preserved, file identities cannot be used as folders, and private browser sessions are refused until Files owns a corresponding privacy lifecycle.

Initialization, preparation and close dispatch asynchronously to the native UI thread and retain their original tasks. Draft names, pending Home reviews and active work veto a normal close without discarding state. Actual view publication requires successful initialization; a failed drain cannot produce a successful retirement receipt. The expanded Files component suite passes **20/20 in Debug and 20/20 in Release**, zero skipped (`native-adapter-debug-1.log`, `native-adapter-release-1.log`). These add four actual surface controls for distinct presentations over the same store/folder, draft and review preservation, foreign-store refusal and file-as-folder refusal, plus eight address grammar cases. The shared Desktop adapter now compiles in the actual Debug production graph (`work/host-acceptance/host-debug-3.log`). The generic native registry passes all 25 lifecycle/identity cases in that run; these do not replace the three genuine Windows Home adapter tests, which remain authored but unexecuted. That broader Host run is not accepted as a whole: 83/97 passed, with 12 Home layout failures and two workspace final-save failures under active repair. Release compilation and Windows adapter test compilation remain pending. The existing same-process Windows admission requirement is preserved; independent installed standalone admission remains unvalidated.

## V2 acceptance matrix

References below are exact lines in the authoritative V2 text fetched on 16 October. **FAIL** means mandatory implementation remains absent or incomplete; **UNVALIDATED** means the stated end-to-end/platform claim has not been demonstrated. Passing a local subcase does not mark the broader product requirement passed.

| V2 line | Required acceptance | Status and current evidence |
| --- | --- | --- |
| 2865 | Complete applicable donor CUI workflows/layout/QoL | FAIL — manifest accounts for gaps; tabs, views and donor workflows remain incomplete. |
| 2866 | First-class Drive; same hosted FileID on web and desktop | UNVALIDATED — stable IDs pass local component tests; authenticated hosted cross-client route not exercised. |
| 2867 | Live cross-client create/rename/move | FAIL — local change feed exists; production connected sync path/UI subscription incomplete. |
| 2868 | Open owning-app document survives move/rename | UNVALIDATED — actual Canvas artifact bridge save/reopen after rename/move passes; installed embedded-to-standalone journey remains absent. |
| 2869 | Sync now flushes journal/deltas and reports conflicts | FAIL — complete sync service/visible workflow absent. |
| 2870 | Hydration, offline use, durable pinning | FAIL — metadata/contracts exist; required working transfer/cache lifecycle absent. |
| 2871 | Safe free-space eviction | FAIL — no complete canonical cloud verification/eviction workflow. |
| 2872 | Offline operations survive restart and reconcile | FAIL — local persistence tested; remote offline queue/reconciliation absent. |
| 2873 | Conflicting raw edits preserve revisions | UNVALIDATED — durable artifact/revision support exists; required concurrent raw-client journey not run. |
| 2874 | Concurrent reorganisation conflicts deterministically | UNVALIDATED — local stale store/item checks pass; production multi-client workflow not run. |
| 2875 | Large resumable upload survives restart | FAIL — full durable transfer owner/workflow absent. |
| 2876 | Integrity failure never reports transfer success | UNVALIDATED — full transfer path required before product acceptance. |
| 2877 | Version restore retains later history and actor/source | UNVALIDATED — revision storage exists; complete user/API restore journey not qualified. |
| 2878 | Distinct Trash/restore/purge with elevated purge | FAIL — native reviewed recoverable delete/restore pass; purge remains deliberately unavailable pending its separate implementation. |
| 2879 | Sharing changes propagate; revocation prevents access | FAIL — direct/inherited ACL and connected propagation workflow incomplete. |
| 2880 | New native artifacts call owning apps | FAIL — complete Files New menu/owning-app creation routing absent. |
| 2881 | Create Stack here/from existing folder | FAIL — Files workflow absent. |
| 2882 | Owning-app collaboration commits Files revisions | UNVALIDATED — canonical artifact revision boundaries exist; live owning-app collaboration not exercised. |
| 2883 | Capability differences disable/report unsupported actions | UNVALIDATED — web unsupported Search case passes; complete provider portfolio/API/UI coverage absent. |
| 2884 | Exact bulk succeeded/failed/skipped outcomes | FAIL — full bulk operation workflow absent. |
| 2885 | Selection never silently sends private content to cloud AI | UNVALIDATED — present manual listing/details use metadata; required integrated AI/context journey not tested. |
| 2886 | Resumable feed without duplicated effects/full rescan | UNVALIDATED — local cursor/idempotency controls pass; production feed and scale not qualified. |
| 2887 | Home crash recovery preserves journal without duplication | UNVALIDATED — lost-reply/audit-fault component checks pass; actual Home process crash/sync recovery not run. |
| 2888 | Large hierarchy incremental scale | UNVALIDATED — current durable JSON provider rewrites state; no large-hierarchy performance acceptance. |

Additional mandatory clauses are not waived by this acceptance list: native folder colour presets/full picker/reset and typed parity (2586–2589), complete shell/navigation/views (2577 onward), properties/sync diagnostics (2788 onward), local/removable/network adapters and real installed platform accessibility still require closure.

## Mandatory remaining coverage

| Requirement family | Remaining implementation or evidence |
| --- | --- |
| Donor desktop shell | Full tabs/session restoration, breadcrumbs/address editing, sidebar/location portfolio, multi-pane, grid/details/grouping/columns, context menus and complete keyboard/touch/accessibility parity. |
| Provider portfolio | Real local/removable/network adapters and provider-specific constraints; the metadata registry alone is insufficient. |
| Creation/open routing | Complete owning-app New menu, source-backed Stack actions, ordinary file/default/open-with routing and preview/thumbnail renderers. |
| Structural actions | Same-store native Move passes focused Home/broker/provider/CUI verification. Copy/duplicate, system clipboard/drag/drop, transfer progress/conflict policy, multi-item results and native/web recovery parity remain incomplete. |
| Presentation | Visible folder-color presets/full picker/reset, persistent view preferences and donor layout acceptance. |
| Hosted storage/sync | Authenticated server storage, push delivery, durable reconnect reconciliation, offline mutation recovery and visible sync diagnostics/controls. |
| Content/transfers | Resumable verified transfers, quotas/reservations, hydration/eviction, raw-content conflicts and version UI/recovery. |
| Sharing/organizations | Direct/inherited grants, sharing/revocation, Shared with Me, organization storage and permission-change previews. |
| Global integration | Home index, canonical AI bar/context with privacy, typed API parity and real cross-app journeys. |
| Product qualification | Exact packaged native/deployed web workflows, responsive/theme/accessibility/lifecycle evidence, donor comparison and complete atomic requirement mapping. |

## Historical donor-only audit

The following sections are retained as historical donor analysis, not the current implementation inventory or current test results.

## Scope and Classification

Audit scope: `9to1 Workspace/Files/` and this report. The source was inspected as a donor; it is preserved unchanged.

| Location | Classification | Evidence |
| --- | --- | --- |
| `9to1 Workspace/Files/Artefacts/Android/` | REFERENCE placeholder | Directory is empty. |
| `9to1 Workspace/Files/Artefacts/Linux (.deb)/` | REFERENCE placeholder | Directory is empty. |
| `9to1 Workspace/Files/Artefacts/Windows/` | REFERENCE placeholder | Directory is empty. |
| `9to1 Workspace/Files/Source/Files/src/Files.Shared/` | DONOR, potentially reusable | Shared helpers, logging, extensions, and attributes. No direct Windows API use was found in its C# source. |
| `9to1 Workspace/Files/Source/Files/src/Files.Core.Storage/` | DONOR, potentially reusable | Storage contracts and extensions. No direct Windows API use was found in its C# source. |
| `9to1 Workspace/Files/Source/Files/src/Files.Core.SourceGenerator/` | DONOR/reference | Roslyn generator and analyzer support for the upstream application. |
| `9to1 Workspace/Files/Source/Files/src/Files.App*/` | DONOR, Windows-only application implementation | WinUI 3 application, controls, Windows storage, CsWin32, COM server, background task, and native dialog/launcher projects. |
| `9to1 Workspace/Files/Source/Files/tests/` | DONOR/reference test evidence | WinUI test host plus Windows Appium/Axe interaction tests. |
| `9to1 Workspace/Files/Source/Files/Files.slnx` and build metadata | DONOR/reference build metadata | The solution declares only Windows x86, x64, and arm64 configurations. |

There is no ACTIVE Files implementation in this scope. In particular, the Files area has no CUI project, executable, entry point, runtime artifact, or CUI verification result. The empty artifact directories are not implementations.

## Donor Architecture

The upstream donor is a .NET 10 WinUI 3 file manager. `Files.App` is the MSIX-packaged `WinExe` and depends on `Files.App.Controls`, `Files.App.Storage`, `Files.App.CsWin32`, `Files.App.BackgroundTasks`, `Files.App.Server`, `Files.Core.Storage`, and `Files.Shared`. The source defines the following useful platform-neutral seams, but no separate CUI host consumes them:

- `IStorageService` and `IFtpStorageService` for resolving files and folders.
- `IDirectCopy` and `IDirectMove` for capability-based storage operations.
- `StorageExtensions` for nullable retrieval helpers.
- `Files.Shared` logging, serialization, checksum, path, and collection helpers.

These seams are donor candidates, not an active portability layer. Their projects still declare Windows runtime identifiers, and their concrete implementations are registered from the WinUI application lifecycle.

## Windows Dependencies

The donor cannot be treated as platform-neutral without a deliberate host and adapter design:

- Build configuration targets `net10.0-windows10.0.26100.0`, requires Windows 10.0.19041+, and declares only `win-x86`, `win-x64`, and `win-arm64` runtimes.
- `Files.App` and `Files.App.Controls` use WinUI 3, Microsoft.WindowsAppSDK, C#/WinRT, XAML, MSIX packaging, Windows resource files, Windows App Lifecycle, and Windows data-transfer APIs.
- `Files.App.Storage` uses Windows Shell/COM through CsWin32 for storables, context menus, bulk file operations, drive management, watchers, taskbar, tray, jump lists, and PowerShell integration.
- `Files.App` directly links Windows native modules including ADVAPI32, CRYPT32, GDI32, KERNEL32, OLE32, OLEAUT32, USER32, and the Windows App Runtime bootstrap library.
- `Files.App.Launcher`, `Files.App.OpenDialog`, and `Files.App.SaveDialog` are C++/WinRT and Win32 projects.
- Interaction tests depend on Appium, Axe.Windows, Win32 keyboard input, package activation, and Windows UI Automation.

## Donor Feature Coverage

The statuses below describe source evidence only. They do not claim parity in a new host.

| Capability | Donor coverage | Evidence | CUI parity |
| --- | --- | --- | --- |
| Tabs and navigation history | COMPLETE in donor | `UserControls/TabBar`, `NavigationHelpers`, `NavigationInteractionTracker`, and `MultitaskingTabsHelpers` support create, close, reopen, move-to-window, back, and forward behavior. | MISSING |
| Multi-pane navigation | PARTIAL in donor | `ShellPanesPage` and navigation actions support dual panes, open/focus/close other pane, and horizontal/vertical arrangement. Shelf persistence is still marked TODO and the shelf toggle is explicitly not ready. | MISSING |
| Breadcrumb navigation | COMPLETE in donor | Custom `Files.App.Controls/BreadcrumbBar` and its WinUI test page cover root/item clicks, dropdown lifecycle, and RTL flow. | MISSING |
| Context menus | COMPLETE for the Windows donor | `ContentPageContextFlyoutFactory`, `ShellContextFlyoutFactory`, shell menu worker code, and interaction tests cover core commands, shell submenus, keyboard invocation, and accessibility. Shell-extension behavior is Windows-specific. | MISSING |
| Core file operations | PARTIAL in donor | Actions cover create file/folder, copy, cut, paste, rename, delete, permanent delete, recycle/restore, and shortcut flows. `FolderTests` exercises create, rename, copy/paste, and delete. Several operations below remain unsupported or unimplemented. | MISSING |

## Incomplete or Constrained Donor Methods

The following are material gaps or intentionally constrained paths discovered during the audit:

- `Files.App.Storage/Windows/WindowsFile.cs`: `OpenStreamAsync` throws `NotImplementedException`.
- `Files.App/Utils/Storage/StorageItems/VirtualStorageItem.cs`: both `RenameAsync` overloads and both `DeleteAsync` overloads throw `NotImplementedException`.
- `Files.App/Utils/Storage/Operations/FilesystemOperations.cs`: `CreateShortcutItemsAsync` throws `NotImplementedException` with the explicit UWP limitation.
- `Files.App/ViewModels/UserControls/ShelfViewModel.cs`: loading persisted shelf items is TODO; `ToggleShelfPaneAction` marks the shelf feature as not ready.
- `Files.App.Storage/Ftp/FtpStorageFolder.cs`: folder copy intentionally throws `NotSupportedException`.
- `Files.Core.Storage/Extensions/StorageExtensions.File.cs`: lockable-stream bridging is TODO, so a portable stream capability is not defined by the core service contract.
- ZIP, virtual, and system storage item classes intentionally reject unsupported operations. They must remain capability-limited rather than being assumed to implement normal local-file semantics.
- Nine WinUI converters throw from `ConvertBack`: the eight converters in `Files.App/Converters` and `Files.App.Controls/AdaptiveGridView/AdaptiveHeightValueConverter`. Their forward conversions are implemented; they are one-way adapters and would fail if rebound as two-way.

## CUI Status and Blockers

**Overall Files CUI parity: MISSING.** No Files CUI files or runtime verification exist, so a CUI port must not be inferred from the donor or empty artifact directories.

Blockers to a truthful CUI implementation:

- No CUI host, project, renderer, navigation model, executable, or test harness exists.
- Navigation, tabs, panes, breadcrumbs, and context menus are currently expressed through WinUI/XAML controls and UI Automation identifiers rather than host-neutral presentation contracts.
- Local file operations are coupled to Windows Shell/COM, clipboard, recycle bin, shell extensions, and Windows-specific storage types.
- The existing `IStorageService` only resolves file and folder identifiers. It does not model listing, mutation, clipboard transfer, trash, metadata, navigation state, or context-menu capabilities needed by a CUI.
- The donor interaction tests require a packaged Windows graphical runtime and cannot validate a CUI.

## Changes Made

No donor source, service contract, or test was changed. There is no safe platform-neutral implementation target to consume a new interface or test. This audit report is the only change.
