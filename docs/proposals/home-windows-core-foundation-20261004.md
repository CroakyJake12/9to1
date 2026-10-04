# Windows Home Core foundation

This proposal adds an owning Windows Home named-pipe host and a connection-bound native startup client. Unix transport behavior is retained. The current Home runtime's explicit process shutdown drains original work and Core; closing its visible window keeps the shared service alive.

The host observes PID and SID from the actual accepted server pipe. Its private session issuer requires a configured installed-peer verifier binding the original current actor, controlled process lifetime and installation evidence. Pipe names, compatibility results and installed discovery metadata grant no authority.

A trusted producer supplies one HomeNativeServiceSession with its canonical IServiceProvider, HomeCoreRuntime and authenticated actor source, its actual IAppPaths, endpoint and owning process token. The same provider must register HomeNativeCoreApiSessions as IHomeCoreAuthorization, the same runtime and actor source. HomeNativeWindowsBootstrap.Start owns the original lease and accepting host; retain OriginalStartTask. Its CloseAndDrainAsync must be joined during explicit process shutdown before the dispatcher exits. The native entrypoint's complete producer registration and bootstrap hook remain separate integration work.

A trusted app producer supplies its configured IHomeNativeSessionHostVerifier, exact host requirement and HomeCompatibilityRequest. HomeNativeWindowsAppConnection.ConnectAsync captures those requirements before connecting. Retain the returned connection and its original startup checks. InitializeOriginalAsync retains a provider-bound initializer between current startup checks. CloseAndDrainAsync must complete while the initializer's dispatcher remains alive, before physical resources and the native loop are retired.

Manual permissions retain HomePermissionTrustService policy. The current actor, accepted original channel and retained private operation are rechecked at dispatch and reply. Core.Read and Ready grant no Files, Spaces or package mutation rights. Missing protected installed-peer or host verification remains unavailable.

## Validation

Both complete source reviews are clear. The seven authored Windows test methods represent twelve cases and have not been compiled or executed for this proposal. Tests require actual connected Windows named pipes and their real PID/SID observations. Their controlled installed verifier is fixture evidence.

```sh
dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" -c Release --filter FullyQualifiedName~HomeWindowsCoreTransportTests
```

All 26 supplied bodies were compared with immutable parent 2e48d31da2e9c13a54d4a6dc3d12561d41debf87; 22 targets were absent from its complete Git tree, three existing predecessors matched in full, and the existing HomeCoreRuntime already matched the supplied body and is unchanged. All sixteen reviewed API dependencies matched the current parent in full.

Protected installation, native producer registration, physical PE/PDB checks, native application integration, signing and clean Windows installation remain separate acceptance gates. Endpoint/version metadata is not a release or signing receipt.
