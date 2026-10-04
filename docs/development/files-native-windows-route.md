# Files native Windows route

This source adds a Files surface to the shared Avalonia Desktop host. The route retains the original Home startup session, provider, current actor and Files owner binding; it refuses publication after any of those retained owners retire.

## Source and owner boundaries

`Files/NativeUI/HavenOS.Files.NativeUI.csproj` is a library. It embeds `UI/FilesBrowser.cui` and consumes the existing native Files host and CUI runtime. The shared Desktop project references that library and retains its existing Sites and Files NativeHost references.

`NativeFilesDesktopRoute`, `FilesNativeWindowSession` and `MainView.FilesNative.cs` provide the explicit host-only route. Binding requires the same canonical profile, resource authorization service, manual permission broker, store ownership and Files services. The added broker and Files composition predicates compare those original instances; they issue no access or mutation grant.

The route borrows its provider and startup connection. The owning caller must retain its original initializer, show and close tasks. Native publication rechecks the Files binding after the final startup/current-actor await and fences synchronous UI notifications. Recoverable command failures preserve later Refresh; close retains original failures, withdraws content immediately on the UI thread and independently drains retained tasks before disposing scene resources.

## Windows foundation dependency

This proposal is based on the Windows Home foundation in PR #12 at `0c014b5a0f552911ebd834c3fac78e0f3966bdc0`. Adopt that dependency first or reconcile its whole source alongside this proposal.

The installed app needs a genuine Home-issued connection, app-owned provider and dispatcher-bound initializer, with authenticated Files domain and ownership ports. Compatibility readiness and GetServices alone do not provide Files authority. This proposal supplies the host-only native route and fixtures. The current Desktop Program/App entrypoint does not select and initialize an installed Files app, and the installed Files domain transport remains unavailable.

`FilesWindows.pubxml` declares `NineToOne.Files`, Windows app ID `9to1.Files`, initial app `files` and `win-x64`. Those declarations do not wire the missing entrypoint or establish an installed package.

## Owning validation

Use a Windows x64 environment with the repository-pinned .NET SDK and package versions. Run the actual unfiltered owning project in both configurations:

```powershell
dotnet test "9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj" -c Debug -f net10.0-windows10.0.19041.0 -p:Platform=x64 --logger "trx;LogFileName=files-native-debug.trx"
dotnet test "9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj" -c Release -f net10.0-windows10.0.19041.0 -p:Platform=x64 --logger "trx;LogFileName=files-native-release.trx"
```

The two original `FilesNativeBrowserSurfaceTests` Facts are preserved in full. Five additive `FilesNativeOriginalPublicationTests` Facts use real Windows named pipes, kernel peer identity, canonical File Home state/current actors, manual permission decisions and actual Avalonia controls. Their installed-peer/host verifier is explicitly synthetic. All seven Files cases are uncompiled and unrun; their count is a selected minimum, not the whole Desktop suite denominator.

Retain actual evaluated Compile/reference/restore inputs, the physical owning PE/PDB and source-document checks, full discovery/TRX results and all original task/cleanup failures. A passing headless fixture does not prove protected installation or a usable package.

After genuine entrypoint and domain-port integration is reviewed and verified, the declared package command is:

```powershell
dotnet publish "9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj" -c Release -p:PublishProfile=FilesWindows
```

Do not substitute another app executable or create a default permission/provider graph to make the Files package start.
