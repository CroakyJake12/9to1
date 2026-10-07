using System.Text.Json;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

/// <summary>Actual Windows OS profile, private Home broker, File state and owned Files store.
/// No GUI/native-engine/installed-domain acceptance is inferred from these guard controls.</summary>
public sealed class CanvasOriginalHomeCompositionTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    { public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires the actual Windows Home producer."; } }

    [WindowsFact]
    public async Task Factory_refuses_foreign_Home_Files_tuple_and_canceled_original_process()
    {
        await using var original = await Rig.CreateAsync(); await using var foreign = await Rig.CreateAsync();
        _ = new CanvasHomeWindowFactory(original.Home, original.Files, original.Authority, original.Process.Token);
        Assert.Throws<UnauthorizedAccessException>(() => new CanvasHomeWindowFactory(original.Home, foreign.Files, foreign.Authority, original.Process.Token));
        Assert.Throws<UnauthorizedAccessException>(() => new CanvasHomeWindowFactory(foreign.Home, original.Files, original.Authority, foreign.Process.Token));
        Assert.Throws<UnauthorizedAccessException>(() => new CanvasHomeWindowFactory(original.Home, original.Files, original.Authority, CancellationToken.None));
        original.Process.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new CanvasHomeWindowFactory(original.Home, original.Files, original.Authority, original.Process.Token));
    }

    [WindowsFact]
    public async Task Same_original_Home_claim_publishes_actual_ACK_once_and_reopens_durable_Files_bytes()
    {
        await using var rig = await Rig.CreateAsync();
        var intent = await rig.CaptureAsync(); var capability = await rig.ApproveAsync(intent);
        var bridge = rig.Bridge();
        var actual = new CanvasHomeCreateOperation(bridge, rig.Home.Broker, rig.Home.Profiles).ExecuteAsync(intent, capability);
        var committed = await actual;
        Assert.True(bridge.TryGetOriginalCommit(capability, out var acknowledged));
        Assert.Equal(committed.FileId, acknowledged!.FileId); Assert.Equal(committed.FilesRevision.Id, acknowledged.Revision.Id);
        var opened = await bridge.OpenAsync(committed.FileId, rig.Workspace.Configuration.StoreId);
        Assert.Equal(CanvasArtifactCodec.Serialize(committed.Artifact), CanvasArtifactCodec.Serialize(opened.Artifact));
        var scopes = new[] { new ResourceScope("files.item", committed.FileId.ToString(), opened.CasRevisionId.ToString(), ResourceAccess.Read) };
        var readiness = new CanvasOriginalResourceReadiness(rig.Home.Runtime, rig.Home.Profiles, rig.Home.Resources,
            "canvas.file.open", _ => ValueTask.FromResult<IReadOnlyList<ResourceScope>>(scopes));
        Assert.Equal(CuiSceneAvailabilityState.Ready, (await readiness.CheckAsync(default)).State);
        var wrongAccess = new CanvasOriginalResourceReadiness(rig.Home.Runtime, rig.Home.Profiles, rig.Home.Resources,
            "canvas.file.open", _ => ValueTask.FromResult<IReadOnlyList<ResourceScope>>([scopes[0] with { Access = ResourceAccess.Write }]));
        Assert.Equal(CuiSceneAvailabilityState.Unavailable, (await wrongAccess.CheckAsync(default)).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.CreateOriginalAsync(committed.Artifact, intent.Target, rig.Workspace.Actor, capability));
        Assert.Single((await rig.Workspace.Provider.ListAsync(intent.Target.FolderId, new("", Limit: 100), null, default)).Items);
    }

    [WindowsFact]
    public async Task Actual_foreign_privately_claimed_capability_cannot_capture_original_Canvas_commit_fence()
    {
        await using var original = await Rig.CreateAsync(); await using var foreign = await Rig.CreateAsync();
        var intent = await foreign.CaptureAsync(); var capability = await foreign.ApproveAsync(intent);
        Assert.NotNull(await foreign.Home.Broker.ClaimExecutionAsync(capability, CanvasCreateIntent.TargetAppId, CanvasCreateIntent.ActionId, intent.Scopes, intent.Arguments));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original.Authority.CaptureOriginalCanvasCommitFenceAsync(
            original.Workspace, original.Home.Broker, capability, () => true, original.Process.Token).AsTask());
        Assert.Empty((await original.Workspace.Provider.ListAsync(original.Workspace.Configuration.AppFolders["canvas"], new("", Limit: 100), null, default)).Items);
    }

    [WindowsFact]
    public async Task Changed_actual_Home_workspace_configuration_refuses_the_original_claim_before_Files_publication()
    {
        await using var rig = await Rig.CreateAsync(); var intent = await rig.CaptureAsync(); var capability = await rig.ApproveAsync(intent);
        Assert.NotNull(await rig.Home.Broker.ClaimExecutionAsync(capability, CanvasCreateIntent.TargetAppId, CanvasCreateIntent.ActionId, intent.Scopes, intent.Arguments));
        var read = await rig.Home.StateStore.ReadAsync();
        var record = Assert.Single(read.State!.Records, row => row.RecordId == "files.native-workspace:" + rig.Workspace.Actor.ProfileId);
        var folders = new Dictionary<string, HostedItemId>(rig.Workspace.Configuration.AppFolders) { ["canvas"] = rig.Workspace.Configuration.AppFolders["write"] };
        var changed = record with { Revision = record.Revision + 1, Payload = JsonSerializer.SerializeToElement(rig.Workspace.Configuration with { AppFolders = folders }) };
        Assert.True((await rig.Home.StateStore.WriteGuardedAsync(changed, record.Revision, rig.Workspace.Actor, rig.Home.Profiles)).IsSuccess);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Authority.CaptureOriginalCanvasCommitFenceAsync(
            rig.Workspace, rig.Home.Broker, capability, () => true, rig.Process.Token).AsTask());
        Assert.Empty((await rig.Workspace.Provider.ListAsync(intent.Target.FolderId, new("", Limit: 100), null, default)).Items);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-original-home-" + Guid.NewGuid().ToString("N"));
        internal readonly CancellationTokenSource Process = new();
        internal HomeNativeWindowsComposition Home = null!;
        internal NativeFilesWorkspaceService Files = null!;
        internal NativeFilesWorkspaceAuthority Authority = null!;
        internal NativeFilesWorkspace Workspace = null!;
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                rig.Home = new(new FileHomeCoreStateStore(Path.Combine(rig._root, "Home", "home.json")), new OperatingSystemPrincipalSource(),
                    new Paths(rig._root), new("canvas.original.control." + Guid.NewGuid().ToString("N")), originalActionPolicies: [new CanvasNativeActionPolicies()],
                    configureOriginalStores: identity =>
                    {
                        rig.Files = new(identity.StateStore, identity.Profiles);
                        return new([rig.Files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = rig.Files });
                    },
                    configureOriginalResolvers: ownership =>
                    {
                        rig.Authority = new(rig.Files, ownership.Identity.Profiles, ownership.Ownership);
                        var resolver = new FilesArtifactResourceResolver(async (actor, token) =>
                        { var current = await rig.Authority.GetCurrentAsync(token); return current?.Actor == actor ? current.Provider : null; },
                        async (actor, app, token) =>
                        { var current = await rig.Authority.GetCurrentAsync(token); return current?.Actor == actor && current.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null; });
                        return new([resolver], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceAuthority)] = rig.Authority });
                    });
                await rig.Home.StartOriginalAsync();
                var workspace = Path.Combine(rig._root, "explicitly-chosen-empty-Files"); Directory.CreateDirectory(workspace);
                await rig.Files.ConfigureNewAsync(workspace, rig.Home.LocalStoreOwnership, rig.Process.Token);
                rig.Workspace = await rig.Authority.GetCurrentAsync(rig.Process.Token) ?? throw new InvalidOperationException("The genuine Files owner was not bound.");
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal async Task<CanvasCreateIntent> CaptureAsync()
        {
            var folder = Workspace.Configuration.AppFolders["canvas"]; var metadata = await Workspace.Provider.GetAsync(folder, default);
            return CanvasCreateIntent.Capture(CanvasArtifact.Create("Original durable guard control"),
                new(folder, metadata.Value!.CurrentRevisionId) { ExpectedStoreId = Workspace.Configuration.StoreId });
        }
        internal async Task<HomeResourceExecutionCapability> ApproveAsync(CanvasCreateIntent intent)
        {
            var request = await Home.Broker.AuthorizeAsync(CanvasCreateIntent.TargetAppId, CanvasCreateIntent.ActionId, intent.Scopes, intent.Arguments,
                "Explicit guard control using this genuine Home profile", null, "canvas-original-control");
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            Assert.True((await Home.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.Broker.BeginExecutionCapabilityAsync(request.RequestId, intent.Arguments));
        }
        internal CanvasFilesArtifactBridge Bridge() => new(Home.Profiles, actor => actor == Workspace.Actor ? Workspace.Provider : null,
            Workspace.Directories, Home.Resources, () => !Process.IsCancellationRequested, captureOriginalHomeFence: (actor, provider, cap, token) =>
            {
                if (actor != Workspace.Actor || !ReferenceEquals(provider, Workspace.Provider)) throw new UnauthorizedAccessException();
                return Authority.CaptureOriginalCanvasCommitFenceAsync(Workspace, Home.Broker, cap, () => !Process.IsCancellationRequested, token);
            }, originalFilesRoot: Workspace.Configuration.RootDirectory);
        public async ValueTask DisposeAsync()
        {
            Process.Cancel(); var errors = new List<Exception>(); Task? close = null;
            try { if (Home is not null) { close = Home.CloseAndDrainAsync(); await close; } }
            catch (Exception error) { errors.Add((Exception?)close?.Exception ?? error); }
            try { Process.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Genuine Canvas/Home guard fixture did not drain.", errors);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
