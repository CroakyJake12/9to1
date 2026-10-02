using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationGraphAssociationJourneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Real_SQL_journal_then_private_Home_graph_then_SQL_association_preserves_uncertain_failure_without_graph_replay(int fault)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = deadline.Token;
        var root = Directory.CreateTempSubdirectory("astra-graph-association-").FullName;
        try
        {
            var schemas = new NodeGraphSchemaRegistry([], [new("actual-empty-draft", new HashSet<string>(), new HashSet<string>())]);
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            services.AddSingleton(schemas);
            services.AddHavenLocalAutomationGraphPublication();
            await using var provider = services.BuildServiceProvider();
            var database = provider.GetRequiredService<SqliteDatabase>(); await database.InitializeAsync(ct);
            var tasks = provider.GetRequiredService<WorkspaceStateRepository>();
            var actor = Assert.IsType<AuthenticatedResourceActor>(await provider.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(ct));
            var identity = await database.GetStoreIdentityAsync(ct);
            await provider.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync(actor, "automations", identity.StoreId.ToString("D"), ct);
            var broker = provider.GetRequiredService<HomeResourceOperationBroker>();
            var permissions = provider.GetRequiredService<HomePermissionTrustService>();
            var authority = provider.GetRequiredService<AutomationLocalStoreAuthority>();
            var prepareHome = new HomeAutomationDefinitionCommitFenceSource(database,
                Assert.IsType<FileHomeCoreStateStore>(provider.GetRequiredService<IHomeCoreStateStore>()),
                provider.GetRequiredService<HomeLocalProfileIdentity>(),
                provider.GetRequiredService<HomeResourceStoreOwnershipAuthority>(), broker);
            var operation = provider.GetRequiredService<AutomationHomeDefinitionOperation>();
            async Task<HomeResourceExecutionCapability> Approve(AutomationDefinitionChange change)
            {
                var review = await broker.AuthorizeForActorAsync(actor, AutomationDefinitionChange.TargetAppID, change.ActionID,
                    change.Scopes, change.Arguments, "Exact graph association", null, "actual-association-test", ct);
                Assert.Equal(HomePermissionRequestState.PendingApproval, review.State);
                Assert.True((await permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
                return Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(review.RequestId, change.Arguments, ct));
            }
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var reusable = new ReusableTaskDefinition(id, "Actual disabled draft", "", "No effects", null, false, now, now);
            var create = AutomationDefinitionChange.Capture(identity.StoreId, actor, reusable, 0, AutomationDefinitionChangeKind.Create);
            Assert.True((await operation.ExecuteAsync(create, await Approve(create), ct)).Committed);
            var original = (await tasks.GetOwnedTaskAsync(id, ct))!.Value;
            var graph = new GraphDocument(1, Guid.NewGuid(), 1, "actual-empty-draft", GraphRevisionState.Draft, [], []);
            // The graph's definition scope is the exact revision AFTER the separately approved durable preparation.
            var intent = GraphPublicationIntent.Capture(new("automations", identity.StoreId, "automation.reusable-task", id), actor,
                Guid.NewGuid(), GraphPublicationKind.SaveDraft, graph, 0,
                [new("automation.reusable-task", identity.StoreId.ToString("D") + "/" + id.ToString("D"), "2", ResourceAccess.Write),
                 new("nodegraph.definition", actor.ProfileId + "/" + graph.GraphId.ToString("D"), "0", ResourceAccess.Write)], schemas, new(1024 * 1024, 32));
            var journal = new AutomationGraphPublicationJournal(intent.OperationId, 1, graph.GraphId, null, null,
                AutomationGraphPublicationPhase.Prepared, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(intent.Arguments.GetRawText()))), null);
            var prepare = AutomationDefinitionChange.Capture(identity.StoreId, actor, original with { PublicationJournal = journal },
                1, AutomationDefinitionChangeKind.Update);
            var prepareCapability = await Approve(prepare);
            var prepareAdmission = Assert.IsAssignableFrom<IAutomationDefinitionCommitAdmission>(await authority.ClaimAsync(prepare, prepareCapability, ct));
            Assert.True((await tasks.PrepareGraphPublicationAsync(prepare, prepareAdmission, prepareHome, ct)).Committed);
            Assert.True((await broker.CompleteExecutionAsync(prepareCapability, new(HomePermissionRequestState.Succeeded,
                "PublicationPrepared", "Durable journal committed before graph effect", []), ct)).Succeeded);
            SqliteConnection.ClearAllPools();
            var prepared = (await tasks.GetOwnedTaskAsync(id, ct))!.Value;
            Assert.Equal(journal, prepared.PublicationJournal); Assert.Null(prepared.GraphBinding);
            var caller = provider.GetRequiredService<AutomationDefinitionReviewCaller>();
            var callerSelection = await caller.CaptureAsync(actor, ct);
            var origin = await provider.GetRequiredService<AutomationGraphOriginAuthority>().CaptureAsync(callerSelection, ct);
            var owner = provider.GetRequiredService<HomeGraphPublicationOwner>();
            var reviewGraph = owner.Prepare(intent, origin, "automations", "actual-association-test");
            var submitted = await reviewGraph.SubmitAsync(ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, submitted.Request?.State);
            Assert.True((await permissions.DecideAsync(reviewGraph.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
            var graphReceipt = Assert.IsType<GraphPublicationReceipt>(await reviewGraph.PublishAsync(ct));
            var observation = Assert.IsType<HomeGraphPublicationObservation>(await reviewGraph.CaptureObservationAsync(ct));
            Assert.Equal(graphReceipt, observation.Receipt);
            var associate = AutomationDefinitionChange.Capture(identity.StoreId, actor, prepared with
            {
                GraphBinding = new(graph.GraphId, 1, null),
                PublicationJournal = journal with { Phase = AutomationGraphPublicationPhase.DefinitionCommitted, PublishedDraftRevision = 1 }
            }, 2, AutomationDefinitionChangeKind.Update);
            var associateCapability = await Approve(associate);
            var admission = Assert.IsAssignableFrom<IAutomationDefinitionCommitAdmission>(await authority.ClaimAsync(associate, associateCapability, ct));
            if (fault == 1)
            {
                var newer = AutomationDefinitionChange.Capture(identity.StoreId, actor, prepared with { Name = "Actual independent newer row" },
                    2, AutomationDefinitionChangeKind.Update);
                Assert.True((await operation.ExecuteAsync(newer, await Approve(newer), ct)).Committed);
            }
            if (fault == 3)
            {
                // Real SQL transaction precedes the exact privately issued observation/claim lease.
                // This primitive companion proves retention across actual SQL commit, not an injected
                // pause inside the owner's association writer or a simulated association receipt.
                Assert.True((await reviewGraph.FinishAuditAsync(ct)).Succeeded);
                await using var sql = await database.OpenAsync(ct);
                await using var tx = (SqliteTransaction)await sql.BeginTransactionAsync(ct);
                var attestation = Assert.IsType<HomeClaimedResourceAttestation>(broker.CaptureClaimedAttestation(associateCapability));
                var held = Assert.IsAssignableFrom<IHomeLocalOperationLease>(await provider
                    .GetRequiredService<HomeGraphSqlAssociationLeaseSource>().AcquireAsync(observation, [attestation], ct));
                var completion = broker.CompleteExecutionAsync(associateCapability,
                    new(HomePermissionRequestState.Failed, "CompetingCompletion", "Original claim completed after retained SQL lease", []), ct);
                var retirement = reviewGraph.RetireAsync(ct);
                try
                {
                    Assert.False(completion.IsCompleted); Assert.False(retirement.IsCompleted);
                    Assert.True(await held.IsCurrentAsync(ct));
                    await using var unchanged = sql.CreateCommand(); unchanged.Transaction = tx;
                    unchanged.CommandText = "UPDATE reusable_tasks SET revision=revision WHERE id=$id AND revision=2;";
                    unchanged.Parameters.AddWithValue("$id", id.ToString("D"));
                    Assert.Equal(1, await unchanged.ExecuteNonQueryAsync(ct));
                    Assert.True(await held.IsCurrentAsync(ct));
                    await tx.CommitAsync(ct);
                    Assert.False(completion.IsCompleted); Assert.False(retirement.IsCompleted);
                }
                finally { await held.DisposeAsync(); }
                Assert.True((await completion.WaitAsync(ct)).Succeeded);
                Assert.True(await retirement.WaitAsync(ct));
            }
            var before = (await tasks.GetOwnedTaskAsync(id, ct))!;
            AutomationDefinitionCommitResult? result = null;
            if (fault == 2)
            {
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tasks.AssociatePublishedDraftAsync(associate, admission,
                    observation, provider.GetRequiredService<HomeGraphSqlAssociationLeaseSource>(), canceled.Token));
            }
            else
            {
                result = await tasks.AssociatePublishedDraftAsync(associate, admission, observation,
                    provider.GetRequiredService<HomeGraphSqlAssociationLeaseSource>(), ct);
                Assert.Equal(fault == 0, result.Committed);
            }
            SqliteConnection.ClearAllPools();
            var after = (await tasks.GetOwnedTaskAsync(id, ct))!;
            if (fault == 0)
            {
                Assert.Equal(3, after.Value.Revision); Assert.False(after.Value.IsEnabled);
                Assert.Equal(new AutomationDefinitionGraphBinding(graph.GraphId, 1, null), after.Value.GraphBinding);
                Assert.Equal(AutomationGraphPublicationPhase.DefinitionCommitted, after.Value.PublicationJournal!.Phase);
                Assert.False((await tasks.AssociatePublishedDraftAsync(associate, admission, observation,
                    provider.GetRequiredService<HomeGraphSqlAssociationLeaseSource>(), ct)).Committed);
            }
            else
            {
                Assert.Equal(JsonSerializer.Serialize(before.RetainedProtectedDescriptors), JsonSerializer.Serialize(after.RetainedProtectedDescriptors));
                Assert.Equal(journal, after.Value.PublicationJournal); Assert.Null(after.Value.GraphBinding);
            }
            // Known actual Home graph remains one exact publication after SQL refusal/cancellation/retry.
            var canonical = await provider.GetRequiredService<IVersionedNodeGraphRepository>().GetAsync(graph.GraphId, ct);
            Assert.Equal(graphReceipt, canonical!.LastPublication); Assert.Equal(1, canonical.Draft.Revision);
            if (fault != 3) Assert.Equal(graphReceipt, await reviewGraph.PublishAsync(ct));
            if (fault != 3) Assert.True((await broker.CompleteExecutionAsync(associateCapability, new(fault == 0 ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                result?.Code ?? "AssociationCanceled", "Actual SQL result recorded after lease release; graph effect never replayed", []), ct)).Succeeded);
            if (fault != 3) Assert.True((await reviewGraph.FinishAuditAsync(ct)).Succeeded);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
