using System.Text.Json;
using Haven.Application;
using Haven.Application.NodeGraph;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class SpaceGraphPublicationTests
{
    [Fact]
    public async Task Actual_store_requires_ownership_publication_and_distinct_association_before_pinning()
    {
        using var paths = new Paths();
        var rig = new Rig(paths);
        var space = await rig.Spaces.CreateAsync("Actual graph Space");
        var graph = rig.Graph();
        Assert.Equal(3, JsonSerializer.Deserialize<JsonElement>((await rig.Settings.ExportAsync(default)).Settings["spaces.registry"]).GetProperty("Version").GetInt32());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.PrepareAsync(space.Id, space.Revision,
            SpaceGraphPage.UserMessage, GraphPublicationKind.SaveDraft, graph, "session"));
        Assert.Null((await rig.Spaces.ReadExistingAsync(space.Id))!.GraphConfiguration);
        // Existing populated settings require actual Home import review, never implicit adoption.
        await rig.BindExistingAsync();
        var operation = await rig.Owner.PrepareAsync(space.Id, space.Revision,
            SpaceGraphPage.UserMessage, GraphPublicationKind.SaveDraft, graph, "session");
        var pending = (await rig.Spaces.ReadExistingAsync(space.Id))!;
        Assert.Equal(space.Revision + 1, pending.Revision);
        Assert.NotNull(pending.GraphConfiguration!.Pending);
        Assert.Equal(4, JsonSerializer.Deserialize<JsonElement>((await rig.Settings.ExportAsync(default)).Settings["spaces.registry"]).GetProperty("Version").GetInt32());
        var backup = JsonSerializer.Deserialize<SettingsExportManifest>(await File.ReadAllTextAsync(Path.Combine(paths.DataDirectory, "settings.json.bak")))!;
        Assert.Equal(3, JsonSerializer.Deserialize<JsonElement>(backup.Settings["spaces.registry"]).GetProperty("Version").GetInt32());
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Spaces.RenameAsync(space.Id, "Must not replace pending"));
        var review = await operation.SubmitPublicationAsync();
        Assert.Equal(HomePermissionRequestState.PendingApproval, review.Request!.State);
        Assert.Null(await operation.PublishAsync());
        await rig.AcceptAsync(operation.PublicationRequestId!);
        var receipt = await operation.PublishAsync();
        Assert.NotNull(receipt);
        var saved = await rig.Graphs.GetAsync(graph.GraphId);
        Assert.Equal(receipt, saved!.LastPublication);
        Assert.Null(saved.Active);
        Assert.Null((await rig.Spaces.ReadExistingAsync(space.Id))!.GraphConfiguration!.UserMessage);
        var association = await operation.SubmitAssociationAsync();
        Assert.NotEqual(operation.PublicationRequestId, operation.AssociationRequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, association.Request!.State);
        Assert.False(await operation.AssociateAsync());
        await rig.AcceptAsync(operation.AssociationRequestId!);
        Assert.True(await operation.AssociateAsync());
        var after = (await rig.Spaces.ReadExistingAsync(space.Id))!;
        Assert.Null(after.GraphConfiguration!.Pending);
        Assert.Equal(graph.GraphId, after.GraphConfiguration.UserMessage!.GraphId);
        Assert.Equal(1, after.GraphConfiguration.UserMessage.DraftRevision);
        Assert.Null(after.GraphConfiguration.UserMessage.ActiveRevision);
        var version = (await rig.Settings.ExportAsync(default)).Version;
        Assert.True(await operation.AssociateAsync());
        Assert.Equal(version, (await rig.Settings.ExportAsync(default)).Version);
        Assert.True((await operation.FinishAssociationAuditAsync()).Succeeded);
        Assert.True((await operation.FinishPublicationAuditAsync()).Succeeded);
        Assert.True(await operation.RetireAsync());
    }

    [Fact]
    public async Task Active_graph_changes_only_after_exact_approved_association_and_basic_edit_preserves_it()
    {
        using var paths = new Paths();
        var rig = new Rig(paths);
        await rig.BindNewEmptyAsync();
        var space = await rig.Spaces.CreateAsync("Pinned activation");
        var graph = rig.Graph();
        await rig.PublishAndAssociateAsync(space, graph, GraphPublicationKind.SaveDraft);
        space = (await rig.Spaces.ReadExistingAsync(space.Id))!;
        var activation = await rig.Owner.PrepareAsync(space.Id, space.Revision, SpaceGraphPage.UserMessage,
            GraphPublicationKind.Activate, graph, "activate-session");
        await activation.SubmitPublicationAsync(); await rig.AcceptAsync(activation.PublicationRequestId!);
        Assert.NotNull(await activation.PublishAsync());
        Assert.Equal(1, (await rig.Graphs.GetAsync(graph.GraphId))!.Active!.Revision);
        Assert.Null((await rig.Spaces.ReadExistingAsync(space.Id))!.GraphConfiguration!.UserMessage!.ActiveRevision);
        await activation.SubmitAssociationAsync(); await rig.AcceptAsync(activation.AssociationRequestId!);
        Assert.True(await activation.AssociateAsync());
        space = (await rig.Spaces.ReadExistingAsync(space.Id))!;
        Assert.Equal(1, space.GraphConfiguration!.UserMessage!.ActiveRevision);
        var edited = await rig.Spaces.ApplyBuilderBasicAsync(space.Id, space.Revision,
            new("Preserved graph identity", "Description", "space", "Keep the acknowledged graph"));
        Assert.Equal(space.GraphConfiguration, edited.GraphConfiguration);
        var newer = graph with { Revision = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Owner.PrepareAsync(edited.Id, edited.Revision,
            SpaceGraphPage.UserMessage, GraphPublicationKind.SaveDraft, newer, "must-retire-original"));
        Assert.True((await activation.FinishAssociationAuditAsync()).Succeeded);
        Assert.True((await activation.FinishPublicationAuditAsync()).Succeeded);
        Assert.True(await activation.RetireAsync());
        await rig.PublishAndAssociateAsync(edited, newer, GraphPublicationKind.SaveDraft);
        var revised = (await rig.Spaces.ReadExistingAsync(space.Id))!;
        Assert.Equal(2, revised.GraphConfiguration!.UserMessage!.DraftRevision);
        Assert.Equal(1, revised.GraphConfiguration.UserMessage.ActiveRevision);
        Assert.Single((await rig.Graphs.GetAsync(graph.GraphId))!.ActivatedRevisions);
    }

    [Fact]
    public async Task Restart_uses_exact_published_receipt_and_fresh_association_approval_without_republishing()
    {
        using var paths = new Paths();
        var original = new Rig(paths);
        await original.BindNewEmptyAsync();
        var space = await original.Spaces.CreateAsync("Recover pending graph");
        var graph = original.Graph();
        var operation = await original.Owner.PrepareAsync(space.Id, space.Revision, SpaceGraphPage.UserMessage,
            GraphPublicationKind.SaveDraft, graph, "old-session");
        await operation.SubmitPublicationAsync(); await original.AcceptAsync(operation.PublicationRequestId!);
        Assert.NotNull(await operation.PublishAsync());
        var graphRecord = (await original.Home.ReadAsync()).State!.Records.Single(record => record.RecordType == "home.node-graph");
        var restarted = new Rig(paths);
        var recovered = await restarted.Owner.RecoverPublishedAsync(space.Id, operation.OperationId, "fresh-session");
        Assert.Null(recovered.PublicationRequestId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovered.PublishAsync());
        var review = await recovered.SubmitAssociationAsync();
        Assert.Equal(HomePermissionRequestState.PendingApproval, review.Request!.State);
        Assert.False(await recovered.AssociateAsync());
        await restarted.AcceptAsync(recovered.AssociationRequestId!);
        Assert.True(await recovered.AssociateAsync());
        var currentRecord = (await restarted.Home.ReadAsync()).State!.Records.Single(record => record.RecordType == "home.node-graph");
        Assert.Equal(JsonSerializer.Serialize(graphRecord), JsonSerializer.Serialize(currentRecord));
        Assert.Null((await restarted.Spaces.ReadExistingAsync(space.Id))!.GraphConfiguration!.Pending);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.SubmitAssociationAsync());
    }

    [Fact]
    public async Task Reconstructed_selection_and_equal_intent_metadata_cannot_replace_original_handle()
    {
        using var paths = new Paths();
        var rig = new Rig(paths); await rig.BindNewEmptyAsync();
        var space = await rig.Spaces.CreateAsync("Private original");
        var graph = rig.Graph();
        var operation = await rig.Owner.PrepareAsync(space.Id, space.Revision, SpaceGraphPage.UserMessage,
            GraphPublicationKind.SaveDraft, graph, "selection-session");
        var pending = (await rig.Spaces.ReadExistingAsync(space.Id))!.GraphConfiguration!.Pending!;
        var metadata = pending.PublicationArguments;
        var recreated = GraphPublicationIntent.Capture(pending.Owner, pending.OriginalActor, pending.OperationId,
            pending.Kind, graph, pending.ExpectedGraphRevision, metadata.GetProperty("Scopes").Deserialize<ResourceScope[]>()!,
            rig.Schemas, new(4 * 1024 * 1024, 32));
        Assert.Equal(metadata.GetRawText(), recreated.Arguments.GetRawText());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.RequireCurrentAsync(new CopiedSelection(), recreated, default).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.RequireCurrentAsync(operation, recreated, default).AsTask());
        Assert.Null(await rig.Graphs.GetAsync(graph.GraphId));
        Assert.True(await operation.CancelUnpublishedAsync());
        Assert.True(await operation.CancelUnpublishedAsync());
        Assert.Null((await rig.Spaces.ReadExistingAsync(space.Id))!.GraphConfiguration);
        Assert.True(await operation.RetireAsync());
    }

    [Fact]
    public async Task Equal_graph_content_with_another_canonical_receipt_never_recovers_the_pending_operation()
    {
        using var paths = new Paths();
        var rig = new Rig(paths); await rig.BindNewEmptyAsync();
        var space = await rig.Spaces.CreateAsync("Receipt identity");
        var graph = rig.Graph();
        var operation = await rig.Owner.PrepareAsync(space.Id, space.Revision, SpaceGraphPage.UserMessage,
            GraphPublicationKind.SaveDraft, graph, "old-session");
        await operation.SubmitPublicationAsync(); await rig.AcceptAsync(operation.PublicationRequestId!);
        Assert.NotNull(await operation.PublishAsync());
        var record = (await rig.Home.ReadAsync()).State!.Records.Single(row => row.RecordType == "home.node-graph");
        var value = record.Payload.Deserialize<OwnedGraphDefinition>()!;
        // Privileged test storage mutation represents replaced/superseded Home
        // receipt evidence; graph content and every graph identity stay equal.
        var altered = value with { LastPublication = value.LastPublication! with { OperationId = Guid.NewGuid() } };
        Assert.True((await rig.Home.WriteAsync(record with { Payload = JsonSerializer.SerializeToElement(altered) }, record.Revision)).IsSuccess);
        var restarted = new Rig(paths);
        var before = (await restarted.Settings.ExportAsync(default)).Version;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.Owner.RecoverPublishedAsync(space.Id, operation.OperationId, "fresh-session"));
        Assert.Contains("NEEDS_ATTENTION", error.Message);
        Assert.Equal(before, (await restarted.Settings.ExportAsync(default)).Version);
        Assert.Equal(operation.OperationId, (await restarted.Spaces.ReadExistingAsync(space.Id))!.GraphConfiguration!.Pending!.OperationId);
    }

    private sealed class CopiedSelection : IOriginalGraphOwnerSelection { }

    private sealed class Rig
    {
        public VersionedAtomicSettingsStore Settings { get; }
        public SpaceRegistry Spaces { get; }
        public FileHomeCoreStateStore Home { get; }
        public HomeLocalProfileIdentity Actors { get; }
        public HomePermissionTrustService Permissions { get; }
        public HomeLocalStoreOwnership LocalOwnership { get; }
        public HomeVersionedNodeGraphRepository Graphs { get; }
        public NodeGraphSchemaRegistry Schemas { get; }
        public HomeSpaceGraphOwner Owner { get; }
        public Rig(Paths paths)
        {
            Settings = new(paths); Spaces = new(Settings);
            Home = new(Path.Combine(paths.DataDirectory, "Home", "state.json"));
            Actors = new(Home, new OperatingSystemPrincipalSource());
            var policies = new HomeSpaceGraphActionPolicies();
            Permissions = new(Home, (app, action) => app == "9to1.home.local-profile" && action == "home.profile.importStore"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, RequiresPerActionApproval: true) : policies.TryGet(app, action));
            LocalOwnership = new(Home, Actors, new HomeLocalStoreEvidenceRegistry([new SpaceLocalStoreEvidenceProvider(Settings)]), Permissions);
            var ownership = new HomeResourceStoreOwnershipAuthority(LocalOwnership, Actors);
            var spaceResources = new HomeSpaceGraphResourceRegistry();
            var graphResources = new HomeGraphPublicationResourceRegistry();
            var resources = new ResourceAuthorizationService(Actors, [spaceResources, graphResources]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            var schemas = Schemas = new NodeGraphSchemaRegistry(
                [new("test.literal", 1, "spaces", "test.literal", JsonSerializer.SerializeToElement(new { type = "object" }), [])],
                [new("spaces.user-message.v1", new HashSet<string> { "test.literal" }, new HashSet<string> { "test.literal" })]);
            Graphs = new(Home, schemas);
            Owner = new(Home, Actors, ownership, broker, resources, Permissions, Settings, Spaces,
                new SpaceLocalStoreAuthority(Settings, Actors, ownership, () => true), Graphs, schemas, spaceResources, graphResources, () => true);
        }
        public GraphDocument Graph() => new(1, Guid.NewGuid(), 1, "spaces.user-message.v1", GraphRevisionState.Draft,
            [new(Guid.NewGuid(), "test.literal", 1, "spaces", "test.literal", null, JsonSerializer.SerializeToElement(new { }), [])], []);
        public async Task BindNewEmptyAsync()
        {
            var identity = await Settings.GetStoreIdentityAsync(default);
            await LocalOwnership.BindNewEmptyAsync((await Actors.GetCurrentAsync(default))!, "spaces", identity.StoreId.ToString("D"));
        }
        public async Task BindExistingAsync()
        {
            var identity = await Settings.GetStoreIdentityAsync(default);
            var request = await LocalOwnership.RequestImportAsync((await Actors.GetCurrentAsync(default))!,
                "spaces", identity.StoreId.ToString("D"), "explicit-owning-store-import");
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            await AcceptAsync(request.RequestId);
            var binding = await LocalOwnership.CompleteImportAsync(request.RequestId);
            Assert.Equal(identity.StoreId.ToString("D"), binding.StoreId);
        }
        public async Task AcceptAsync(string id) => Assert.True((await Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
        public async Task PublishAndAssociateAsync(SpaceDefinition space, GraphDocument graph, GraphPublicationKind kind)
        {
            var operation = await Owner.PrepareAsync(space.Id, space.Revision, SpaceGraphPage.UserMessage, kind, graph, "publication-session");
            await operation.SubmitPublicationAsync(); await AcceptAsync(operation.PublicationRequestId!);
            Assert.NotNull(await operation.PublishAsync());
            await operation.SubmitAssociationAsync(); await AcceptAsync(operation.AssociationRequestId!);
            Assert.True(await operation.AssociateAsync());
            Assert.True((await operation.FinishAssociationAuditAsync()).Succeeded);
            Assert.True((await operation.FinishPublicationAuditAsync()).Succeeded);
            Assert.True(await operation.RetireAsync());
        }
    }
    private sealed class Paths : IDisposable, IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "spaces-graph-" + Guid.NewGuid().ToString("N"));
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
