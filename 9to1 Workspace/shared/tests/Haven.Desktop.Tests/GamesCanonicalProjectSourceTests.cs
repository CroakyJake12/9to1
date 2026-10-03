using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Games;
using Haven.Core.Games;
using Haven.Desktop.Services;
using Haven.Infrastructure.Games;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HavenOS.Games;

namespace Haven.Desktop.Tests;

public sealed class GamesCanonicalProjectSourceTests
{
    [Fact]
    public Task Actual_Home_Files_project_reopens_same_scene_and_revoked_store_ownership_denies() => ExerciseAsync(false);

    [GamesHomeNativeFact]
    public Task Actual_Home_Files_scene_is_observed_by_matching_native_Godot_under_current_resource_authority() => ExerciseAsync(true);

    [GamesManagedHomeNativeFact]
    public Task Actual_Home_Files_saved_project_runs_in_pinned_owning_CSharp_driver() => ExerciseAsync(true, true);

    [Fact]
    public Task Actual_Home_one_use_position_approval_reaches_same_Files_CAS_and_retains_claimed_actor() => ExerciseAsync(false, false, true);

    [Fact]
    public Task Actual_Games_commit_waiting_for_Files_lease_rejects_changed_authentication_without_revision_or_event() => ExerciseAsync(false, false, false, true);

    private static async Task ExerciseAsync(bool observeNative, bool observeManaged = false, bool homeWrite = false, bool leaseRace = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-games-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var files = new NativeFilesWorkspaceService(home, profiles);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]),
                new HomePermissionTrustService(home, (_, _) => null));
            var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
            var chosen = Path.Combine(root, "chosen-empty");
            Directory.CreateDirectory(chosen);
            var workspace = await files.ConfigureNewAsync(chosen, ownership, token);
            var projectID = Guid.NewGuid();
            var sceneID = Guid.NewGuid();
            var resourceID = Guid.NewGuid();
            var scene = new GamesSceneSnapshot(projectID, sceneID, 2,
                [new(Guid.NewGuid(), Guid.NewGuid(), null, "Canonical model", new(Guid.NewGuid(), new(1, 2, 3)), resourceID)],
                [new(resourceID, new(2, 3, 4))]);
            var project = new GamesProjectDocument(1, projectID, 3, GamesWorkspaceMode.CreationRendering, sceneID, [scene]);
            var bytes = GamesProjectCodec.Encode(project);
            var fileID = HostedItemId.New();
            var reference = new FilesArtifactReference("games", projectID.ToString("D"), fileID,
                workspace.Configuration.AppFolders["games"], nameof(FilesArtifactType.GameProject), "Canonical.9to1g");
            Assert.True((await workspace.Provider.RegisterArtifactAsync(reference, workspace.Actor.ActorId, token)).IsSuccess);
            var directory = await authority.ResolveAppDirectoryAsync("games", token);
            Assert.NotNull(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "project.9to1g"), bytes, token);
            Assert.True((await workspace.Provider.CommitDurableRevisionAsync(new(fileID, "games", "3", workspace.Actor.ActorId,
                DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "project.9to1g", null), token)).IsSuccess);

            // Move changes the visible hierarchy, not the owning Games payload anchor or canonical identities.
            var beforeMove = (await workspace.Provider.GetAsync(fileID, token)).Value!;
            var destination = workspace.Configuration.AppFolders["boards"];
            var moveTime = DateTimeOffset.UtcNow;
            var moved = await workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), workspace.Actor.ActorId,
                fileID, reference.ParentFolderId, destination, "Move", beforeMove.CurrentRevisionId, null,
                FilesOperationState.Pending, moveTime, moveTime, null, null), null, token);
            Assert.True(moved.IsSuccess);
            Assert.NotEqual(beforeMove.CurrentRevisionId, moved.Value!.ResultRevisionId);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(chosen, "Boards")));

            // Reconstruct all host services against durable Home/Files; no supplied actor or private project copy.
            var reopenedProfiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var reopenedFiles = new NativeFilesWorkspaceService(home, reopenedProfiles);
            var reopenedOwnership = new HomeLocalStoreOwnership(home, reopenedProfiles, new HomeLocalStoreEvidenceRegistry([reopenedFiles]),
                new HomePermissionTrustService(home, (_, _) => null));
            var reopenedAuthority = new NativeFilesWorkspaceAuthority(reopenedFiles, reopenedProfiles,
                new HomeResourceStoreOwnershipAuthority(reopenedOwnership, reopenedProfiles));
            GamesCanonicalProjectSource? source = null;
            var raceActor = new CommitRaceActor(reopenedProfiles);
            var metadataPath = Path.Combine(chosen, ".9to1-files", "drive.json");
            var leaseStore = new VersionedJsonStateStore<DurableDriveProvider.State>(metadataPath, 1, () => new([], [], []));
            var releaseLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? holder = null;
            var saveChecks = 0;
            var resources = new ResourceAuthorizationService(raceActor,
                [new CommitRaceResolver(new FilesArtifactResourceResolver(async (actor, ct) =>
                {
                    var current = await reopenedAuthority.GetCurrentAsync(ct);
                    return current?.Actor == actor ? current.Provider : null;
                }), async () =>
                {
                    if (!leaseRace || ++saveChecks != 3) return;
                    var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    holder = leaseStore.UpdateAsync(state => state, async ct =>
                    {
                        held.TrySetResult();
                        await releaseLease.Task.WaitAsync(ct);
                    }, token);
                    await held.Task.WaitAsync(token);
                    raceActor.TransitionAfterCapture = true;
                }), new GamesSceneResourceResolver(() => source!, raceActor)]);
            source = new GamesCanonicalProjectSource(reopenedAuthority,
                new NativeFilesArtifactContentReader(reopenedAuthority, reopenedProfiles, resources));
            var loaded = await source.GetAsync(projectID, sceneID, token);
            Assert.NotNull(loaded);
            Assert.Equal(scene.SceneID, loaded.SceneID);
            Assert.Equal(scene.Nodes[0], Assert.Single(loaded.Nodes));
            Assert.Equal(2, loaded.Revision); // Scene revision is distinct from owning project revision 3.
            var reopenedActor = await reopenedProfiles.GetCurrentAsync(token);
            Assert.NotNull(reopenedActor);
            Assert.Equal(workspace.Actor.ProfileId, reopenedActor.ProfileId);
            Assert.Equal(workspace.Actor.ActorId, reopenedActor.ActorId);
            var sceneScope = new ResourceScope("games.scene", projectID.ToString("D") + "/" + sceneID.ToString("D"), "2", ResourceAccess.Read);
            Assert.Equal(reopenedActor, await resources.AuthorizeAsync("games.scene.observe", [sceneScope], token));
            Assert.Null(await resources.AuthorizeAsync("games.scene.observe", [sceneScope with { Revision = "3" }], token));
            Assert.Null(await resources.AuthorizeAsync("games.scene.observe", [sceneScope with { Id = Guid.NewGuid().ToString("D") + "/" + sceneID.ToString("D") }], token));
            if (observeNative)
            {
                var session = new GamesSceneSessionService(resources, source,
                    new GodotSceneRuntime(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")!));
                var observed = await session.ObserveAsync(projectID, sceneID, 2, token);
                Assert.StartsWith("4.7.2", observed.ObservedEngineVersion);
                Assert.Equal(projectID, observed.ProjectID);
                Assert.Equal(sceneID, observed.SceneID);
                var node = Assert.Single(observed.Nodes);
                Assert.Equal(scene.Nodes[0].NodeID, node.NodeID);
                Assert.Equal(resourceID, node.MeshResourceID);
                Assert.Equal(new GamesVector3(1, 2, 3), node.WorldPosition);
                Assert.Equal(36, node.MeshVertexCount);
            }
            var currentWorkspace = await reopenedAuthority.GetCurrentAsync(token);
            Assert.NotNull(currentWorkspace);
            var metadata = (await currentWorkspace.Provider.GetAsync(fileID, token)).Value!;
            Assert.Equal(destination, metadata.ParentId);
            Assert.Equal(moved.Value.ResultRevisionId, metadata.CurrentRevisionId);
            var now = DateTimeOffset.UtcNow;
            Assert.True((await currentWorkspace.Provider.MutateAsync(new(new(Guid.NewGuid()), currentWorkspace.Actor.ActorId,
                fileID, null, null, "Rename", metadata.CurrentRevisionId, null, FilesOperationState.Pending,
                now, now, null, null), "Renamed.9to1g", token)).IsSuccess);
            var afterRename = await source.GetAsync(projectID, sceneID, token);
            Assert.NotNull(afterRename);
            Assert.Equal(loaded.Nodes[0], afterRename.Nodes[0]);
            Assert.Equal(loaded.Revision, afterRename.Revision);
            Assert.Equal(reopenedActor, await resources.AuthorizeAsync("games.scene.observe", [sceneScope], token));
            Assert.Null(await source.GetAsync(projectID, Guid.NewGuid(), token));
            Assert.Null(await source.GetAsync(Guid.NewGuid(), sceneID, token));
            var bridge = new GamesFilesArtifactBridge(reopenedAuthority,
                new NativeFilesArtifactContentReader(reopenedAuthority, reopenedProfiles, resources), resources, raceActor);
            var editor = new GamesProjectEditorService(bridge);
            var openedProject = await editor.OpenAsync(fileID.Value, token);
            Assert.Equal(projectID, openedProject.Project.ProjectID);
            Assert.Equal(3, openedProject.Project.Revision);
            var editedScene = GamesSceneEdits.SetPosition(openedProject.Project.Scenes[0], 2, scene.Nodes[0].NodeID, new(4, 5, 6));
            if (leaseRace)
            {
                var before = await File.ReadAllBytesAsync(metadataPath, token);
                var pending = editor.SetSceneAsync(fileID.Value, openedProject.StructuralRevisionID, 3, 2, editedScene, token);
                try
                {
                    await raceActor.Transitioned.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                    Assert.False(pending.IsCompleted);
                    Assert.NotNull(holder);
                }
                finally
                {
                    releaseLease.TrySetResult();
                    if (holder is not null) await holder;
                }
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
                Assert.Equal(before, await File.ReadAllBytesAsync(metadataPath, token));
                raceActor.Override = null;
                var retained = await bridge.OpenAsync(fileID.Value, token);
                Assert.Equal(openedProject.ContentRevisionID, retained.ContentRevisionID);
                Assert.Equal(openedProject.StructuralRevisionID, retained.StructuralRevisionID);
                return;
            }
            GamesStoredProject savedProject;
            if (homeWrite)
            {
                var permissions = new HomePermissionTrustService(home, new GamesNativeActionPolicies().TryGet);
                var broker = new HomeResourceOperationBroker(resources, permissions);
                var intent = GamesPositionWriteIntent.Capture(fileID.Value, openedProject.StructuralRevisionID,
                    projectID, 3, sceneID, 2, scene.Nodes[0].NodeID, new(4, 5, 6));
                var pending = await broker.AuthorizeAsync(GamesPositionWriteIntent.TargetAppId, GamesPositionWriteIntent.ActionId,
                    intent.Scopes, intent.Arguments, "Set this canonical node to the reviewed position", null,
                    reopenedActor.AuthenticationRevision, token);
                Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
                Assert.True((await permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                var capability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, token));
                var operation = new GamesHomePositionOperation(bridge, broker);
                var changed = GamesPositionWriteIntent.Capture(fileID.Value, openedProject.StructuralRevisionID,
                    projectID, 3, sceneID, 2, scene.Nodes[0].NodeID, new(99, 99, 99));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(changed, capability, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GamesHomePositionOperation(bridge,
                    new HomeResourceOperationBroker(resources, permissions)).ExecuteAsync(intent, capability, token));
                savedProject = await operation.ExecuteAsync(intent, capability, token);
                Assert.Equal(reopenedActor.ActorId, (await currentWorkspace.Provider.GetCurrentArtifactContentAsync(fileID, token)).Value!.Revision.ActorId);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(intent, capability, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.SaveForActorAsync(
                    reopenedActor with { AuthenticationRevision = "expired-claim" }, fileID.Value,
                    savedProject.StructuralRevisionID, savedProject.Project.Revision,
                    GamesProjectEdits.ChangeWorkspace(savedProject.Project, savedProject.Project.Revision, GamesWorkspaceMode.Development), token));
                Assert.Equal(savedProject.StructuralRevisionID, (await bridge.OpenAsync(fileID.Value, token)).StructuralRevisionID);
            }
            else savedProject = await editor.SetSceneAsync(fileID.Value, openedProject.StructuralRevisionID, 3, 2, editedScene, token);
            Assert.Equal(4, savedProject.Project.Revision);
            Assert.Equal(3, savedProject.Project.Scenes[0].Revision);
            Assert.Equal(projectID, savedProject.Project.ProjectID);
            Assert.Equal(fileID.Value, savedProject.FileID);
            Assert.Equal(destination, (await currentWorkspace.Provider.GetAsync(fileID, token)).Value!.ParentId);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(chosen, "Boards")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => editor.SetSceneAsync(fileID.Value,
                openedProject.StructuralRevisionID, 3, 2, editedScene, token));
            var freshlyOpened = await new GamesFilesArtifactBridge(reopenedAuthority,
                new NativeFilesArtifactContentReader(reopenedAuthority, reopenedProfiles, resources), resources, reopenedProfiles).OpenAsync(fileID.Value, token);
            Assert.Equal(savedProject.ContentRevisionID, freshlyOpened.ContentRevisionID);
            Assert.Equal(new GamesVector3(4, 5, 6), freshlyOpened.Project.Scenes[0].Nodes[0].Spatial.Position);
            if (observeNative)
            {
                var afterSave = await new GamesSceneSessionService(resources, source,
                    new GodotSceneRuntime(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")!)).ObserveAsync(projectID, sceneID, 3, token);
                Assert.Equal(new GamesVector3(4, 5, 6), Assert.Single(afterSave.Nodes).WorldPosition);
            }
            if (observeManaged)
            {
                var executable = Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")!;
                var module = Environment.GetEnvironmentVariable("ASTRA_GAMES_MANAGED_MODULE")!;
                var executableHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, token)));
                var moduleHash = await GodotManagedProjectRuntime.FingerprintModuleAsync(module, token);
                var managed = new GamesManagedProjectSessionService(resources, bridge,
                    new GodotManagedProjectRuntime(executable, module, executableHash, moduleHash));
                var observedManaged = await managed.ObserveAsync(fileID.Value, savedProject.StructuralRevisionID,
                    projectID, 4, sceneID, 3, token);
                Assert.Equal(projectID, observedManaged.ProjectID);
                Assert.Equal(4, observedManaged.ProjectRevision);
                Assert.Equal(3, observedManaged.SceneRevision);
                Assert.Equal(new GamesVector3(4, 5, 6), Assert.Single(observedManaged.Nodes).WorldPosition);
                Assert.Equal(36, Assert.Single(observedManaged.Nodes).MeshVertexCount);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => managed.ObserveAsync(fileID.Value,
                    openedProject.StructuralRevisionID, projectID, 3, sceneID, 2, token));
            }
            var state = (await home.ReadAsync(token)).State!;
            var record = Assert.Single(state.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "foreign-profile" }) }, record.Revision, token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.GetAsync(projectID, sceneID, token));
            Assert.Null(await resources.AuthorizeAsync("games.scene.observe", [sceneScope], token));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(directory, "project.9to1g"), token));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class CommitRaceResolver(ICanonicalResourceAccessResolver inner, Func<Task> finalValidation) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => inner.ResourceKind;
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
            ResourceScope scope, CancellationToken cancellationToken)
        {
            var result = await inner.EvaluateAsync(actor, actionId, scope, cancellationToken);
            if (result.Allowed && actionId == "games.file.save") await finalValidation();
            return result;
        }
    }
    private sealed class CommitRaceActor(IAuthenticatedResourceActorSource inner) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Override { get; set; }
        public bool TransitionAfterCapture { get; set; }
        public TaskCompletionSource Transitioned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            var captured = Override ?? await inner.GetCurrentAsync(cancellationToken);
            if (TransitionAfterCapture)
            {
                TransitionAfterCapture = false;
                Override = captured! with { AuthenticationRevision = "changed-after-final-validation" };
                Transitioned.TrySetResult();
            }
            return captured;
        }
    }

}

public sealed class GamesHomeNativeFactAttribute : FactAttribute
{
    public GamesHomeNativeFactAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")))
            Skip = "Requires the explicitly configured actual matching Godot runtime; no cross-app native claim without it.";
    }
}

public sealed class GamesManagedHomeNativeFactAttribute : FactAttribute
{
    public GamesManagedHomeNativeFactAttribute([CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME"))
            || !Directory.Exists(Environment.GetEnvironmentVariable("ASTRA_GAMES_MANAGED_MODULE")))
            Skip = "Requires the actual controlled Godot Mono runtime and owning first-party C# module.";
    }
}
