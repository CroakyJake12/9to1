# Present Windows candidate

`HavenOS.Present` starts the original Desktop App, OS-local Windows Home,
provider and native shell. Its compiled initial route uses the existing document
workspace factory to create the actual Present page with the maintained repository,
import/export services, editor and playback engine. Home readiness is checked
before page construction and again before native tab publication.

This is a bounded same-process candidate. The OS-profile Home runtime lease
permits one owning process; separately launched product executables cannot share
that lease or run together. Protected installed-peer admission and the unified
trusted installer remain required for the distribution model. A successful publish
alone supplies no installed Home, signing, GUI, account or smoke certification.

The page retains its actual asynchronous operations. Retirement stops new UI
admission, joins accepted writes, and releases the scene after the document is
acknowledged by storage. Failed saves retain their original causes and dirty draft.
Save/close preflight refuses an active operation or edits newer than a saved snapshot.

From the repository root:

```powershell
dotnet build "9to1 Workspace/Present/HavenOS.Present.csproj" -c Release -m:1 -p:UseSharedCompilation=false
dotnet test "9to1 Workspace/Present/Tests/HavenOS.Present.Tests.csproj" -c Release -m:1 -p:UseSharedCompilation=false
dotnet publish "9to1 Workspace/Present/HavenOS.Present.csproj" -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:EnableWindowsTargeting=true --output <new-empty-present-publish-directory> -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:PublishReadyToRun=false
```

Validation must launch that exact published executable on Windows, edit and save
an actual deck, reopen it with matching identity/revision, exercise import/export
and playback, then close with pending and failed storage controls. Headless page
tests and publish checks do not establish those Windows runtime outcomes.
