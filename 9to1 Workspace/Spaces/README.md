# Spaces task and development workflow

## Assistants placement and compatibility

Assistants is a distinct built-in Space with stable ID `b1000000-0000-0000-0000-000000000009`
and appended `SpaceKind.Assistants = 9`. The existing Agent ID, enum value, settings and
conversations remain independent. Registry reconciliation adds the new built-in without
renaming a pre-existing user Space named Assistants; stable IDs and origin distinguish
those records. New create/rename operations still reject conflicting names, and saved
same-name user records remain editable.

The native Spaces manager and sidebar picker route this destination to the existing
App-owned Assistants workspace through `OpenOriginalRegisteredAssistantsAsync`.
They do not create a replacement Chat, Assistant, Task, permission issuer or Den store.
A host without the configured owner reports setup required. The sidebar routes selected
Spaces through the shell's typed callback, while the shell's scope-selection callback
remains separate to avoid recursive navigation. Canonical Chat keeps its Chat Groups
and existing unscoped Chat history visible; other Space conversation lists retain their
scope filter. Restored Assistants scope routes its primary action to the same owner,
and the generic Chat move menu does not claim it can create Assistant conversations.
Duplicate built-in and user Space names receive distinct picker labels without changing
their saved names. Successful native Assistants navigation refreshes the persisted
scope in the live sidebar.

This is placement integration, not full acceptance of the revised Assistants Space.
Permanent Main, Teams, handoff, revised rail, full sidebar customisation, browser owner
composition and real native/browser acceptance remain separate required work. Native
route/scene tests and registry reconciliation fixtures do not certify live Home/Den,
models or platform capabilities.

## Browser configuration checkpoint

The browser now composes the canonical manager and Builder documents through its
normal `app.spaces` route. Both use the same private `SpaceRegistry`; custom Space
creation/editing, fork, archive/restore and Basic/Advanced configuration changes
persist on this browser under the verified signed-in profile. Archived Spaces can
be shown and restored. A conflict retains the draft. An unconfirmed save cannot
be retried merely by reopening the view. Home navigation retains drafts, while
private revocation clears them and joins original operations before replacement.

This checkpoint does not supply browser Chat, model/Task execution, account sync,
the revised workspace rail/Summary, or Expert graph activation. Their controls
remain unavailable with explicit explanations. A signed-out manager does not
invent private profile records. See `apps/Web/README.md` and the private storage
contract for integration, lifetime and previous-deployment compatibility.

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
