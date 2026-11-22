# Open native Files through the Windows desktop host

Haven supplies the original Home session, profile, provider and window lifetime. The Files route should select the existing Files app implementation and keep the same provider identity when the user navigates away and returns.

For a route change, start the actual Windows desktop candidate, open Files, navigate a directory, switch tabs and return. Check cancellation or a denied provider without losing the prior location. Native shell and account access depend on the installed Windows foundation and the current actor.

The Windows project commands below identify the owning test and publish target. Use the repository’s build allocation for that environment and keep the candidate’s platform and checks with the receipt.

```powershell
dotnet test "9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj" -c Debug -f net10.0-windows10.0.19041.0 -p:Platform=x64 --logger "trx;LogFileName=files-native-debug.trx"
dotnet test "9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj" -c Release -f net10.0-windows10.0.19041.0 -p:Platform=x64 --logger "trx;LogFileName=files-native-release.trx"
```

```powershell
dotnet publish "9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj" -c Release -p:PublishProfile=FilesWindows
```

[Haven host](../../9to1%20Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj) · [Files sources](../../9to1%20Workspace/Files) · [Build workflow](workflow.md)

## Dated delivery evidence

The 4 October 2026 record retains the Windows source ownership, publication tests, foundation dependency and unverified package gates. [Original record](evidence/files-delivery-before-atlas-v4/files-native-windows-route.md)
