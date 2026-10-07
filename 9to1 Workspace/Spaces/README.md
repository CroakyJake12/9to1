# Spaces task and development workflow

Spaces reads its durable records through the maintained `SpaceRegistry`. Task dashboards and widgets use `SpaceTaskWorkspaceService` over the same profile conversation repository and canonical coordinator. A saved conversation ID is context for the existing Task/Run; selecting a Space does not create work or grant execution permission.

`SpaceDevelopmentWorkspace.AttachAsync` saves a bounded Dev project reference in the existing Space context references with the Space revision CAS. The reference preserves the conversation, Task, Run, workspace, project, root and optional repository binding identities. Its permission is `Unknown`. Before writing it, the adapter reobserves current membership and the same Task/Run after awaited project resolution. A save acknowledgement remains available through `SpaceDevelopmentAttachmentAcknowledgedException` when the following presentation read fails; inspect the saved reference rather than repeat an acknowledged attachment blindly.

`OpenAsync` supports fresh-service reopening from those stores. It validates the persisted reference, resolves the exact project revision and then rereads the current canonical task after project and Space reads. A replacement Task/Run, withdrawn membership or changed Space reference refuses. Accepted work, task persistence revision and checkpoint observations can advance under the same IDs and are returned from that fresh read. These are finite observations, not an atomic grant or a crash-recovery runtime witness.

When a project descriptor is explicitly saved at a new revision, reopening its old reference refuses with the Dev owner's revision conflict. `AttachAsync` with the current exact reference updates the existing context-reference row for that conversation, retaining its row ID and creation timestamp. This operation does not clone a project or start a replacement task.

`ExecuteAsync` refreshes the embedded selection and forwards typed source read, reviewed preview/apply, saved stage, test, Git and terminal operations to the same `DeveloperTaskWorkspaceService`. The service still requires genuine original attempt issuance, accepted workspace and current owner permission/native fences. The Desktop workbench and portable attachment session compose these same business services and retain their returned tasks. The default canonical directory listing/text search path currently refuses because safe physical per-child traversal is not implemented.

The owning managed controls use actual `VersionedAtomicSettingsStore` Space files and `FileDeveloperWorkspaceStore` Dev metadata files, including reopening with fresh store/service instances. Conversation/task repositories and withheld reads are explicit test controls; these tests do not certify native actor authority, installed Home, SQLite recovery, a rendered UI or model execution.

From the repository root, after Root integrates the reviewed source and assigns the next build ticket:

```powershell
dotnet test "9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj" -c Release --filter "FullyQualifiedName~SpaceDevelopmentWorkspaceTests"
```

The source preparation does not execute this command. The existing full Spaces suite, relevant Desktop attachment/workbench controls, required rendering and actual same-ID cloud-to-local takeover remain runtime validation work for the integrated candidate.
