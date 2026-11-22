# Present standalone and embedded entry points

Present belongs to Work. Its standalone entry starts the original desktop bootstrap and selects the existing Present workspace. Home and the native provider must admit the current actor before publishing the tab. Use the same document repository, editor, import/export services and playback engine for the embedded entry.

Open an existing presentation, edit a slide, save, close and reopen the same document. Use a distinct check for playback, external format interchange or donor-engine behavior. A packaged executable can exist before the installed-peer and platform integration are verified.

```powershell
dotnet build "9to1 Workspace/Present/HavenOS.Present.csproj" -c Release -m:1 -p:UseSharedCompilation=false
dotnet test "9to1 Workspace/Present/Tests/HavenOS.Present.Tests.csproj" -c Release -m:1 -p:UseSharedCompilation=false
dotnet publish "9to1 Workspace/Present/HavenOS.Present.csproj" -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:EnableWindowsTargeting=true --output <new-empty-present-publish-directory> -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:PublishReadyToRun=false
```

[Present entry](HavenOS.Present.csproj) · [Shared editor](../shared/src/Haven.Application/Present/PresentEditor.cs) · [Impress bridge](present-engine/README.md) · [Native layout](../shared/docs/ui/present-editor-redesign.md)
