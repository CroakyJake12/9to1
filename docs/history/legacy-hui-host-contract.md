# Legacy HUI Host Contract Evidence

The Linux and Windows HUI hosts load application-owned surfaces through the
shared `CakeOS.Platform.IHuiRootProvider` contract. A provider returns an
`IHuiRootElement`; each host validates that `NativeRoot` is a
`Haven.UI.Components.Page` and mounts it in the existing `HuiAppSurface` with
its platform-specific `HavenPlatform` value.

The Windows host has no project reference to a Windows-specific application UI
project. An application is supplied at launch with
`--hui-root-provider-assembly` and `--hui-root-provider-type`, so application
code depends on the shared contract rather than on the Windows host or its
Avalonia desktop lifetime.

Validation is limited to managed compilation of both host projects. No native
Linux runtime smoke is asserted from this Windows workspace. This document does
not claim a graphical runtime smoke for either platform.

## Observed Validation

On 2026-09-20, both commands completed with `Build succeeded`, `0 Warning(s)`,
and `0 Error(s)`:

```powershell
dotnet build HUI/WindowsHost/CakeOS.HuiWindowsHost.csproj -c Release
dotnet build HUI/LinuxHost/CakeOS.HuiLinuxHost.csproj -c Release
```

`dotnet list HUI/WindowsHost/CakeOS.HuiWindowsHost.csproj reference` lists only
Platform, Haven.UI, HuiRenderer, and CanvasApp; it does not list either Boards
application project.
