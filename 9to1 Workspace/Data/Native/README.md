# Native Data workbook entry

The standalone component opens the existing native Data page and physical workbook repository. Create a workbook in the active OS profile, edit a cell, save, close and reopen by its original identity. Select an isolated `HAVEN_DATA_DIR` for a disposable test.

This component is one entry to Data. The Calc/UNO and DuckDB workers are separate dependencies for the supported spreadsheet and query workflows. A successful local managed workbook is evidence for that route only. Verify each claimed engine operation through the configured worker and record its exact version.

```sh
dotnet build "9to1 Workspace/Data/Native/HavenOS.Data.Native.csproj" -c Debug -p:EnableWindowsTargeting=true
dotnet test "9to1 Workspace/Data/Native/Tests/HavenOS.Data.Native.Tests.csproj" -c Debug -p:EnableWindowsTargeting=true
dotnet publish "9to1 Workspace/Data/Native/HavenOS.Data.Native.csproj" -c Debug -p:PublishProfile=DataWindows -p:EnableWindowsTargeting=true
```

## Source and engine evidence

When testing the managed Data page, record which physical workbook file the repository opened and its identity after save. For Calc and DuckDB validation additionally record the selected executable or worker, protocol and version. A successful managed cell edit should not mark an unavailable UNO bridge or absent worker as verified.

[Native component](.) · [Application](../App) · [Engine workers](../workers) · [Data boundaries](../README.md)
