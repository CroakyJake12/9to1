# Windows native Home producer

This change connects the existing Avalonia Home executable to one canonical Home owner. It requires the Windows transport and actor-bound session foundation from PR #12 (`0c014b5a0f552911ebd834c3fac78e0f3966bdc0`). The producer commit adds the composition and its two Windows tests and changes the native Program; the pull request may also show prerequisite foundation changes until that dependency is integrated.

## Original owner and process lifetime

`HomeNativeWindowsComposition` constructs the original file state store, operating-system principal/profile, permissions service, ownership/evidence registry, authorization service, resource broker, private session issuer, Core API, productivity engine and Runtime. Its immutable service map belongs to the Home process. Optional store/resolver/owner stages capture the actual original instances in construction order. They cannot replace the canonical components or grant app authority.

The native entrypoint starts this same owner through retained original work. An ordinary window close keeps Core running. Explicit process shutdown seals admission, joins the original startup and work, drains the same Bootstrap/session issuer/Runtime and observes the resulting shutdown task before exit. The receiver's existing Home controller route repairs are preserved.

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
