# Lane 12 Build host integration — central App.UnifiedWorkspaceHost binding patch

This patch wires the typed Build (Dev) native content factory into the central unified Workspace
host's `NativeAppContentRegistry`. It is the **only** central-file change for Build; everything else
lives in the owned Dev/`Haven.Desktop` source and tests.

## Preimage

- File: `9to1 Workspace/shared/src/Haven.Desktop/App.UnifiedWorkspaceHost.cs`
- Checkpoint (HEAD): `6ca569201fcf69500b881406be89ec4493cab859`
- Full-file SHA-256 (preimage): `a0b9f836433b8a94993b87567df739d6465b30f79206ffe676388cf20116ff29`
- The file is NOT present in the sparse working tree; apply against the HEAD blob. It is unpatch in
  the worktree, so this is the exact preimage.

## Hunk

Replace this exact text in `UnifiedWorkspaceHost.InitializeCoreAsync`:

```csharp
            var files = FilesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime);
            var spaces = SpacesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime,
                (intent, settlement) => OriginalClose is null && _workspace is { } workspace && workspace.QueueNavigation(intent, settlement));
            var modes = _services.GetRequiredService<IModeRegistry>();
            var apps = new NativeAppContentRegistry(modes,
                [new NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000030"),
                    "files", HavenAppRouteKind.Files, files),
                 new NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000022"),
                    "spaces", HavenAppRouteKind.Spaces, spaces)], () => IsCurrent);
```

with:

```csharp
            var files = FilesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime);
            var spaces = SpacesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime,
                (intent, settlement) => OriginalClose is null && _workspace is { } workspace && workspace.QueueNavigation(intent, settlement));
            var build = BuildBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime);
            var modes = _services.GetRequiredService<IModeRegistry>();
            var apps = new NativeAppContentRegistry(modes,
                [new NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000030"),
                    "files", HavenAppRouteKind.Files, files),
                 new NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000022"),
                    "spaces", HavenAppRouteKind.Spaces, spaces),
                 new NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000028"),
                    "dev", HavenAppRouteKind.Dev, build)], () => IsCurrent);
```

## Why this tuple

- `Guid.Parse("a0000000-0000-0000-0000-000000000028")` is the canonical built-in **dev** mode id
  (`BuiltInModeSeed`), verified against the current HEAD seed.
- `"dev"` is `BuildBrowseAppContentAddress.Authority` and `DeveloperAppDescriptor.AppId`.
- `HavenAppRouteKind.Dev` (19) is exactly what `HavenAppRoutePolicy.Resolve(dev)` produces (verified
  by `HavenAppRoutePolicyTests`).
- `BuildBrowseAppContentFactory.BindOriginalSameProcess` mirrors the Files/Spaces same-process binding;
  it validates the provider + `HomeNativeWindowsComposition` and retains the host/window lifetimes.

`BuildBrowseAppContentIntegrationTests` (Haven.Desktop.Tests) proves this exact tuple is accepted by
`NativeAppContentRegistry` and that a `9to1://dev/...` project-open address and the `9to1://dev`
landing both route to a native session and publish.

## Apply order

1. Apply this hunk to `App.UnifiedWorkspaceHost.cs` (preimage above).
2. Build/run `Haven.Desktop.Tests` (xUnit v3, `net10.0`): filter
   `FullyQualifiedName~BuildBrowseAppContentIntegrationTests`.

## Notes

- No other central App/DI/Spaces/Voice file is touched.
- Build landing (`9to1://dev`) presents the saved-project discovery entry; project-open requires the
  exact canonical Task/Run/context. The real session owns project/task effects and is exercised by
  the Dev workbench tests once the SDK slot is available.
