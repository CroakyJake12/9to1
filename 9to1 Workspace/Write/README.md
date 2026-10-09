# Write standalone host

`HavenOS.Write` starts the original Desktop App, OS-local Windows Home,
provider and native shell. Its compiled initial route uses the existing document
workspace factory to create the actual Write page with the maintained Notes
repository, editor, import/export services, native `.9to1w` codec, attachment
store, provider routing and local read-aloud services. Home readiness is checked
before page construction and again before native tab publication.

This is a bounded same-process candidate. The OS-profile Home runtime lease
permits one owning process; separately launched product executables cannot share
that lease or run together. Protected installed-peer admission and the unified
trusted installer remain required for the distribution model. Publish checks
supply no installed Home, signing, GUI, account or smoke certification.

Start in the local library, create or open a document, edit it, then save or
close. Close remains cancelled while initial loading, a document operation or a
save is in progress. A refused save retains the draft. A save submits a snapshot;
edits made while storage is pending remain dirty and require another save before
switching documents or closing. Reopen uses the same canonical document identity
and durable revision.

From the repository root:

```powershell
dotnet build "9to1 Workspace/Write/HavenOS.Write.csproj" -c Release
dotnet test "9to1 Workspace/Write/Tests/HavenOS.Write.Tests.csproj" -c Release
dotnet publish "9to1 Workspace/Write/HavenOS.Write.csproj" -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:EnableWindowsTargeting=true --output <new-empty-write-publish-directory> -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:PublishReadyToRun=false
```

The focused tests use actual local repository files and the mounted retained
title input. They cover save/reopen, refused storage with retry, edits during a
pending save, and closing during the initial library read. Production Windows
launch, chooser/native package workflows, read aloud, provider connections and
browser journeys require their own actual receipts.

AI proposals consume the existing provider router and recorded user consent in
Write. Configure a genuine provider through the existing Haven Settings
Integrations connection owner. That owner writes the protected provider secret,
configuration, health and catalogue. A Codex connector or a browser account login
does not configure Write's model provider. Local model weights belong to the
receiving tester. This host does not include or acquire them. A Cloudflare MCP
connection requires its separate maintained OAuth flow and enabled OAuthReady
state; an AI provider key does not supply that connection.

The browser Write route uses this-origin IndexedDB through its original owner
adapter. It preserves the original editor and package codec but has a separate
storage location. Browser origin storage and native filesystem documents are
not synchronized by this host. Actual browser account/ACL and private service
activation belong to the browser composition owner.
