# Write document hosting

Write belongs to Work. The standalone route starts the original desktop application and its Home/provider lifetime, then selects the shared Write workspace. The editor, Notes repository, native .9to1w codec, attachment store and import/export services must retain the same active document identity.

Verify that an existing document opens, an edit commits and a normal close/reopen restores the change. Test attachments, import/export and local read-aloud only through the actual configured services. A failed Home admission must not open a separate permissive provider to bypass it.

An apphost publish verifies a bounded build. Test the real Windows session or supported native composition before asserting installed-peer admission, packaging or complete editor parity.

```powershell
dotnet build "9to1 Workspace/Write/HavenOS.Write.csproj" -c Release
dotnet test "9to1 Workspace/Write/Tests/HavenOS.Write.Tests.csproj" -c Release
dotnet publish "9to1 Workspace/Write/HavenOS.Write.csproj" -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:EnableWindowsTargeting=true --output <new-empty-write-publish-directory> -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:PublishReadyToRun=false
```

[Host component](HavenOS.Write.csproj) · [Engine bridge](Native/README.md) · [Tests](Tests) · [Write requirements](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
