# Data standalone local component candidate

This executable hosts the existing `Haven.Desktop.Views.Pages.Data.DataPage`, its Haven.UI scene, and the existing physical `DataWorkbookRepository`. It opens the OS user's local workbook directory, forwards the landing's New workbook intent to the same maintained editor action, edits cells, and saves/reopens the same workbook identity. It creates no sample workbook and requires no cloud sign-in, model registration, or account migration. `HAVEN_DATA_DIR` retains the maintained `AppPaths` override for an isolated local data directory.

**Source component candidate; not PACKAGEABLE Data acceptance.** Data's owner README requires the authoritative Calc/UNO spreadsheet engine, DuckDB typed snapshot/query bridge, and Data.Cui workflow. This local managed editor does not satisfy those requirements. The existing Data/App, Data/Cui and worker sources remain untouched. There is no second Calc engine, worker-ready claim, substituted DuckDB query engine, or complete Data package claim here.

The remaining package prerequisites are a genuine Data.Cui native host over the maintained Calc/DuckDB sessions; the approved Windows LibreOffice/UNO, Python and DuckDB worker/runtime/notice closure; actual donor open/edit/recalculate/save-as ODS or XLSX/reopen and typed range publication/read-only query/new-sheet materialisation; and owning Windows build, launch, input and package smoke. The older Ubuntu CI results in `../README.md` do not establish those Windows observations.

The close request joins this host's actual initialization and save-preparation task. If the original page reports a busy editor or failed save, the window stays open with its draft. This does not claim complete drain of the maintained page's legacy async-void input, autosave and detach callbacks. Initialization and cleanup faults remain observable in their original task and trace; an uninitialized empty component may retire with its original failure retained. No Task permission, Home approval or worker readiness is inferred from UI availability.

`Tests/DataAppWorkflowTests.cs` contains two headless controls with real temporary physical repositories: original New intent → edit → save → reopen, and a test-owned refusal before any write → retained dirty editor and unchanged prior file → explicit successful retry. They have no Windows early-return guard and no fake acknowledged save. Source authors have not executed them. The Windows publish profile produces a useful local-component build candidate only; it cannot change `mandatoryDonorWorkflowSatisfied=false` or `packageable=false`.

Root owns SDK scheduling. Candidate commands after exact source integration:

```sh
dotnet build "9to1 Workspace/Data/Native/HavenOS.Data.Native.csproj" -c Debug -p:EnableWindowsTargeting=true
dotnet test "9to1 Workspace/Data/Native/Tests/HavenOS.Data.Native.Tests.csproj" -c Debug -p:EnableWindowsTargeting=true
dotnet publish "9to1 Workspace/Data/Native/HavenOS.Data.Native.csproj" -c Debug -p:PublishProfile=DataWindows -p:EnableWindowsTargeting=true
```

An actual publish still needs the current whole dependency/source closure, Windows runtime packs and licence/source companions. Build, tests, GUI startup and package smoke are unexecuted in this source packet.
