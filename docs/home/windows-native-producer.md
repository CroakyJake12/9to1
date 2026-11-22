# The Windows native Home producer

The Windows Home executable and shared Desktop entry use the canonical Home runtime and actor-bound session. Startup, permission decisions and shutdown remain with that original owner. A shell window cannot grant itself access by recreating the provider after a rejected connection.

For a candidate, identify the consuming host and active profile, observe the required service state and test both initial startup and normal shutdown. A configured producer, a process lease and a user-visible successful workflow are distinct observations.

The retained candidate-admission details describe the relevant Windows binding. Use its source and actual process evidence when making a new claim.

## Candidate configuration and admission

The parameterless native entrypoint uses the operating-system local application-data directory under `9to1` and the routing name `9to1.home.candidate.v1`. These are candidate engineering defaults, not installed package identities or an installation receipt. A trusted platform composition may supply the actual state, endpoint, policies and installed-peer verifier.

The default `UnavailableHomeNativeInstalledPeerVerifier` refuses installed-app admission. Core, profile and Home bootstrap can start while that verifier is unavailable. Installed admission still requires actual configured package/AppID/ABI/publisher policy, authenticated protected descriptor and receipt, current PID/SID/image/process-lifetime and controlled-launch evidence. Compatibility and `GetServices` do not issue Files or Spaces mutation authority or transfer raw Home owner objects into another process.

## Validation

Two authored Windows Facts contain 36 assertions and cover real Core/profile/lease/pipe composition, denied copied caller identity, retained startup cancellation/finally and coalesced close. They have not been compiled or run. Independent source review and exact whole-source preservation checks support this proposal; Windows package, physical assembly/source mapping, installed trust and real native acceptance remain to be executed by the owning integration environment.

With the maintained .NET 10 SDK and repository dependency restoration, the owning test command is:

```powershell
dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" -c Release --filter FullyQualifiedName~HomeNativeWindowsCompositionTests
```

The actual native product project is `apps/Home/src/AvaloniaHome/AvaloniaHome.csproj`. Team C owns its coherent Windows packaging, invocation and global integration.

[Home](../../9to1%20Workspace/Home/ReadMe.md) · [Native Home host](../../apps/Home/src/AvaloniaHome) · [Development workflow](../development/live-preview.md)
