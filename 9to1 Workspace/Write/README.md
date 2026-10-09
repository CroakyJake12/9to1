# Write standalone host

`HavenOS.Write` starts the existing retained Write page. It uses the original
Notes repository, document editor, structured import/export services, native
`.9to1w` codec, attachment store, provider routing and local read-aloud services.
Documents remain in the maintained `IAppPaths` data directory. The host owns and
awaits the disposal of the services it acquires.

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
dotnet publish "9to1 Workspace/Write/HavenOS.Write.csproj" -c Release -r win-x64 --self-contained true --output <new-empty-write-publish-directory> -p:UseSharedCompilation=false -p:PublishReadyToRun=false
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
