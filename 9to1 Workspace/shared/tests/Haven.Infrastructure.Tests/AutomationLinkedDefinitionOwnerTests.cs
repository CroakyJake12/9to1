using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PermissionState = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationLinkedDefinitionOwnerTests
{
    [Fact]
    public async Task Actual_two_individual_Home_reviews_commit_both_archived_revisions_once_without_run_or_raw_delete()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var beforeTask = (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value;
        var beforeSchedule = (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value;
        var review = await fixture.PairCaller.ReviewAsync(fixture.Selection, beforeTask, beforeSchedule,
            beforeTask.Revision, beforeSchedule.Revision, AutomationDefinitionChangeKind.Archive, token);
        Assert.True(review.ReviewDeliveryConfirmed); Assert.Equal(2, review.RequestIDs.Count);
        Assert.Null((await review.FinishAsync(token)).Committed);
        await fixture.AssertRevisionsAsync(beforeTask.Revision, beforeSchedule.Revision, token);
        await fixture.ApproveAsync(review.RequestIDs[0], token);
        Assert.Null((await review.FinishAsync(token)).Committed);
        await fixture.AssertRevisionsAsync(beforeTask.Revision, beforeSchedule.Revision, token);
        await fixture.ApproveAsync(review.RequestIDs[1], token);
        var committed = await review.FinishAsync(token); Assert.True(committed.Committed);
        Assert.Equal("LinkedDefinitionsCommitted", committed.Code);
        var task = (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value;
        var schedule = (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value;
        Assert.Equal(beforeTask.Revision + 1, task.Revision); Assert.Equal(beforeSchedule.Revision + 1, schedule.Revision);
        Assert.Equal(AutomationOperationalState.Archived, task.OperationalState);
        Assert.Equal(AutomationOperationalState.Archived, schedule.OperationalState);
        Assert.Equal(beforeTask.GraphJson, task.GraphJson); Assert.Equal(beforeSchedule.Instruction, schedule.Instruction);
        Assert.False(task.IsEnabled); Assert.False(schedule.IsEnabled); Assert.Null(schedule.NextRunAt);
        Assert.NotNull(task.LastOwnerCommit); Assert.NotNull(schedule.LastOwnerCommit);
        Assert.NotEqual(task.LastOwnerCommit!.OperationId, schedule.LastOwnerCommit!.OperationId);
        Assert.True((await review.FinishAsync(token)).Committed);
        Assert.Equal(task.LastOwnerCommit, (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value.LastOwnerCommit);
        Assert.Equal(schedule.LastOwnerCommit, (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value.LastOwnerCommit);
        Assert.Empty(await fixture.Definitions.GetRunsAsync(fixture.ID, 10, token));
    }
    [Fact]
    public async Task Actual_second_SQL_update_fault_rolls_back_first_candidate_and_same_recovery_never_replays_after_fault_removed()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var task = (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value;
        var schedule = (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value;
        await fixture.ExecuteSqlAsync("CREATE TRIGGER actual_pair_second_fault BEFORE UPDATE ON automations BEGIN SELECT RAISE(ABORT,'actual second row fault'); END;", token);
        var review = await fixture.PairCaller.ReviewAsync(fixture.Selection, task, schedule, task.Revision,
            schedule.Revision, AutomationDefinitionChangeKind.Archive, token);
        foreach (var request in review.RequestIDs) await fixture.ApproveAsync(request, token);
        var unknown = await review.FinishAsync(token); Assert.Null(unknown.Committed);
        Assert.Equal("LinkedOutcomeUnconfirmed", unknown.Code);
        await fixture.AssertRevisionsAsync(task.Revision, schedule.Revision, token);
        Assert.Equal(task.LastOwnerCommit, (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value.LastOwnerCommit);
        Assert.Equal(schedule.LastOwnerCommit, (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value.LastOwnerCommit);
        await fixture.ExecuteSqlAsync("DROP TRIGGER actual_pair_second_fault;", token);
        Assert.Null((await review.FinishAsync(token)).Committed); // Receipt observation only, no Claim/CAS replay.
        await fixture.AssertRevisionsAsync(task.Revision, schedule.Revision, token);
        Assert.Empty(await fixture.Definitions.GetRunsAsync(fixture.ID, 10, token));
    }
    [Fact]
    public async Task Actual_private_pair_admission_cannot_be_used_for_a_partial_single_row_write()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var task = (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value;
        var schedule = (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value;
        var pair = AutomationDefinitionChange.CaptureLinkedPair(fixture.Selection.StoreId, fixture.Selection.Actor,
            task, task.Revision, schedule, schedule.Revision, AutomationDefinitionChangeKind.Archive);
        var change = pair.ReusableChange;
        var broker = fixture.Graph.GetRequiredService<HomeResourceOperationBroker>();
        var request = await broker.AuthorizeForActorAsync(change.OriginalActor, AutomationDefinitionChange.TargetAppID,
            change.ActionID, change.Scopes, change.Arguments, "Actual exact pair denial test", null, "pair-owning-test", token);
        await fixture.ApproveAsync(request.RequestId, token);
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(request.RequestId, change.Arguments, token));
        var admission = await fixture.Graph.GetRequiredService<AutomationLocalStoreAuthority>().ClaimAsync(change, capability, token);
        Assert.NotNull(admission);
        var refused = await fixture.Tasks.CompareExchangeOwnedTaskAsync(change, admission!, token);
        Assert.False(refused.Committed); Assert.Equal("OwnerAdmissionUnavailable", refused.Code);
        await fixture.AssertRevisionsAsync(task.Revision, schedule.Revision, token);
        Assert.True((await broker.CompleteExecutionAsync(capability, new HomeExecutionOutcome(PermissionState.Failed,
            "PartialLinkedWriteDenied", "The actual single-row writer refused this pair intent without writing.", []), token)).Succeeded);
    }
    [Fact]
    public async Task Actual_copied_SQL_store_identity_cannot_substitute_the_original_caller_repository_instances()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var foreignRoot = Path.Combine(Path.GetTempPath(), "astra-linked-copied-origin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(foreignRoot);
        try
        {
            var originalDatabase = fixture.Graph.GetRequiredService<SqliteDatabase>();
            var foreignDatabase = new SqliteDatabase(new Paths(foreignRoot));
            await foreignDatabase.InitializeAsync(token);
            await using (var original = await originalDatabase.OpenAsync(token))
            await using (var foreign = await foreignDatabase.OpenAsync(token))
                original.BackupDatabase(foreign); // Real independent database, colliding UUID and all row identities.
            Assert.NotSame(originalDatabase, foreignDatabase);
            Assert.Equal(fixture.Selection.StoreId, (await foreignDatabase.GetStoreIdentityAsync(token)).StoreId);
            var foreignDefinitions = new AutomationRepository(foreignDatabase);
            var foreignTasks = new WorkspaceStateRepository(foreignDatabase);
            var task = (await foreignTasks.GetOwnedTaskAsync(fixture.ID, token))!.Value;
            var schedule = (await foreignDefinitions.GetOwnedAsync(fixture.ID, token))!.Value;
            var beforeHome = await File.ReadAllBytesAsync(fixture.HomePath, token);
            var foreignCaller = new AutomationLinkedDefinitionReviewCaller(
                fixture.Graph.GetRequiredService<AutomationDefinitionReviewCaller>(), foreignDefinitions, foreignTasks,
                fixture.Graph.GetRequiredService<AutomationLocalStoreAuthority>(),
                fixture.Graph.GetRequiredService<HomeResourceOperationBroker>(),
                fixture.Graph.GetRequiredService<HomePermissionTrustService>());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreignCaller.ReviewAsync(fixture.Selection,
                task, schedule, task.Revision, schedule.Revision, AutomationDefinitionChangeKind.Archive, token));
            Assert.Equal(beforeHome, await File.ReadAllBytesAsync(fixture.HomePath, token));
            Assert.Equal(task, (await foreignTasks.GetOwnedTaskAsync(fixture.ID, token))!.Value);
            Assert.Equal(schedule, (await foreignDefinitions.GetOwnedAsync(fixture.ID, token))!.Value);
            await fixture.AssertRevisionsAsync(task.Revision, schedule.Revision, token);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(foreignRoot, true); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        public string HomePath => Path.Combine(_root, "home.json");
        public ServiceProvider Graph { get; }
        public AutomationRepository Definitions => Graph.GetRequiredService<AutomationRepository>();
        public WorkspaceStateRepository Tasks => Graph.GetRequiredService<WorkspaceStateRepository>();
        public AutomationLinkedDefinitionReviewCaller PairCaller => Graph.GetRequiredService<AutomationLinkedDefinitionReviewCaller>();
        public IAutomationDefinitionCallerSelection Selection { get; }
        public Guid ID { get; }
        private Fixture(string root, ServiceProvider graph, IAutomationDefinitionCallerSelection selection, Guid id)
        { _root = root; Graph = graph; Selection = selection; ID = id; }
        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-linked-pair-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            services.AddSingleton<AutomationLinkedDefinitionReviewCaller>(); // Same real graph, no alternate store/authority.
            var graph = services.BuildServiceProvider();
            try
            {
                var db = graph.GetRequiredService<SqliteDatabase>(); await db.InitializeAsync(token);
                var actor = Assert.IsType<AuthenticatedResourceActor>(await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token));
                var identity = await db.GetStoreIdentityAsync(token);
                await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync(actor, "automations", identity.StoreId.ToString("D"), token);
                var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
                // Real legacy rows remain disabled/NeedsAttention; recover them through actual high individual Home reviews.
                // The retained graph is inspection data, not a published canonical graph or runnable trigger.
                var graphJson = AutomationGraphCodec.Serialize(AutomationGraphDefinition.Empty);
                await graph.GetRequiredService<WorkspaceStateRepository>().UpsertReusableTaskAsync(new(id, "Original linked workflow", "Keep both rows",
                    "Original instruction", null, false, now, now, graphJson), token);
                await graph.GetRequiredService<AutomationRepository>().UpsertAsync(new(id, "Original linked schedule", default,
                    ScheduledGraphAutomationPayloadCodec.Serialize(id, Guid.NewGuid(), "Original linked workflow", graphJson, null),
                    AutomationScheduleKind.Daily, "{\"time\":\"08:00\"}", null, null, false, now, now), token);
                var tasks = graph.GetRequiredService<WorkspaceStateRepository>(); var definitions = graph.GetRequiredService<AutomationRepository>();
                await RecoverAsync(AutomationDefinitionChange.CaptureLegacyRecovery(identity.StoreId, actor,
                    (await tasks.GetOwnedTaskAsync(id, token))!), graph, token);
                await RecoverAsync(AutomationDefinitionChange.CaptureLegacyRecovery(identity.StoreId, actor,
                    (await definitions.GetOwnedAsync(id, token))!), graph, token);
                var selection = await graph.GetRequiredService<IAutomationDefinitionReviewCaller>().CaptureAsync(actor, token);
                return new(root, graph, selection, id);
            }
            catch { await graph.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(root, true); throw; }
        }
        private static async Task RecoverAsync(AutomationDefinitionChange change, ServiceProvider graph, CancellationToken token)
        {
            var broker = graph.GetRequiredService<HomeResourceOperationBroker>(); var permissions = graph.GetRequiredService<HomePermissionTrustService>();
            var request = await broker.AuthorizeForActorAsync(change.OriginalActor, AutomationDefinitionChange.TargetAppID,
                change.ActionID, change.Scopes, change.Arguments, "Recover actual disabled legacy row", null, "pair-legacy-recovery", token);
            Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            var capability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(request.RequestId, change.Arguments, token));
            Assert.True((await graph.GetRequiredService<AutomationHomeDefinitionOperation>().ExecuteAsync(change, capability, token)).Committed);
        }
        public async Task ApproveAsync(string request, CancellationToken token) =>
            Assert.True((await Graph.GetRequiredService<HomePermissionTrustService>().DecideAsync(request, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        public async Task AssertRevisionsAsync(long task, long schedule, CancellationToken token)
        {
            Assert.Equal(task, (await Tasks.GetOwnedTaskAsync(ID, token))!.Value.Revision);
            Assert.Equal(schedule, (await Definitions.GetOwnedAsync(ID, token))!.Value.Revision);
        }
        public async Task ExecuteSqlAsync(string sql, CancellationToken token)
        { await using var connection = await Graph.GetRequiredService<SqliteDatabase>().OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(token); }
        public async ValueTask DisposeAsync()
        { await Graph.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
