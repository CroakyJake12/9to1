# Files Parity Audit

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
