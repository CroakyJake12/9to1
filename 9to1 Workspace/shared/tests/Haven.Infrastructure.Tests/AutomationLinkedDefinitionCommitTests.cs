using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class AutomationLinkedDefinitionCommitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_two_Home_admissions_commit_both_rows_or_rollback_first_candidate_on_second_revision_conflict(bool supersedeSchedule)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = lifetime.Token;
        var root = Path.Combine(Path.GetTempPath(), "astra-linked-definitions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            await using var provider = services.BuildServiceProvider();
            var database = provider.GetRequiredService<SqliteDatabase>(); await database.InitializeAsync(token);
            var definitions = provider.GetRequiredService<AutomationRepository>();
            var tasks = provider.GetRequiredService<WorkspaceStateRepository>();
            Assert.Same(database, provider.GetRequiredService<ISqliteConnectionFactory>());
            var actor = Assert.IsType<AuthenticatedResourceActor>(await provider.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token));
            var store = await database.GetStoreIdentityAsync(token);
            await provider.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync(actor, "automations", store.StoreId.ToString("D"), token);
            var broker = provider.GetRequiredService<HomeResourceOperationBroker>();
            var permissions = provider.GetRequiredService<HomePermissionTrustService>();
            var authority = provider.GetRequiredService<AutomationLocalStoreAuthority>();
            var operation = provider.GetRequiredService<AutomationHomeDefinitionOperation>();
            async Task<HomeResourceExecutionCapability> Approve(AutomationDefinitionChange change)
            {
                var request = await broker.AuthorizeForActorAsync(actor, AutomationDefinitionChange.TargetAppID,
                    change.ActionID, change.Scopes, change.Arguments, "Review the full exact linked effect", null, "owning-linked-test", token);
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                return Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(request.RequestId, change.Arguments, token));
            }
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var reusable = new ReusableTaskDefinition(id, "Actual reusable", "", "Retain task", null, false, now, now);
            var schedule = new AutomationDefinition(id, "Actual linked schedule", HavenMode.Tasks, "Retain schedule",
                AutomationScheduleKind.Daily, "{}", null, null, false, now, now);
            var createTask = AutomationDefinitionChange.Capture(store.StoreId, actor, reusable, 0, AutomationDefinitionChangeKind.Create);
            Assert.True((await operation.ExecuteAsync(createTask, await Approve(createTask), token)).Committed);
            var createSchedule = AutomationDefinitionChange.Capture(store.StoreId, actor, schedule, 0, AutomationDefinitionChangeKind.Create);
            Assert.True((await operation.ExecuteAsync(createSchedule, await Approve(createSchedule), token)).Committed);
            var taskBefore = (await tasks.GetOwnedTaskAsync(id, token))!;
            var scheduleBefore = (await definitions.GetOwnedAsync(id, token))!;
            var pair = AutomationDefinitionChange.CaptureLinkedPair(store.StoreId, actor, taskBefore.Value, 1,
                scheduleBefore.Value, 1, AutomationDefinitionChangeKind.Archive);
            Assert.Equal(2, pair.ReusableChange.Scopes.Count);
            Assert.Equal(pair.ReusableChange.Scopes, pair.ScheduleChange.Scopes);
            var taskCapability = await Approve(pair.ReusableChange);
            var scheduleCapability = await Approve(pair.ScheduleChange);
            var taskAdmission = Assert.IsAssignableFrom<IAutomationDefinitionCommitAdmission>(await authority.ClaimAsync(pair.ReusableChange, taskCapability, token));
            var scheduleAdmission = Assert.IsAssignableFrom<IAutomationDefinitionCommitAdmission>(await authority.ClaimAsync(pair.ScheduleChange, scheduleCapability, token));
            // The public single-row writers cannot partially execute an approved pair.
            Assert.False((await tasks.CompareExchangeOwnedTaskAsync(pair.ReusableChange, taskAdmission, token)).Committed);
            Assert.False((await definitions.CompareExchangeOwnedAsync(pair.ScheduleChange, scheduleAdmission, token)).Committed);
            if (supersedeSchedule)
            {
                var update = AutomationDefinitionChange.Capture(store.StoreId, actor, scheduleBefore.Value with { Name = "Independently approved newer revision" },
                    1, AutomationDefinitionChangeKind.Update);
                Assert.True((await operation.ExecuteAsync(update, await Approve(update), token)).Committed);
                scheduleBefore = (await definitions.GetOwnedAsync(id, token))!;
            }
            var result = await definitions.CompareExchangeLinkedPairAsync(pair, tasks, taskAdmission, scheduleAdmission, token);
            Assert.Equal(!supersedeSchedule, result.Committed);
            var taskAfter = (await tasks.GetOwnedTaskAsync(id, token))!;
            var scheduleAfter = (await definitions.GetOwnedAsync(id, token))!;
            if (supersedeSchedule)
            {
                Assert.Equal("RevisionConflict", result.Code);
                // Real first-row candidate write rolled back when the second exact row was stale.
                Assert.Equal(JsonSerializer.Serialize(taskBefore.RetainedProtectedDescriptors), JsonSerializer.Serialize(taskAfter.RetainedProtectedDescriptors));
                Assert.Equal(JsonSerializer.Serialize(scheduleBefore.RetainedProtectedDescriptors), JsonSerializer.Serialize(scheduleAfter.RetainedProtectedDescriptors));
            }
            else
            {
                Assert.Equal(AutomationOperationalState.Archived, taskAfter.Value.OperationalState);
                Assert.Equal(AutomationOperationalState.Archived, scheduleAfter.Value.OperationalState);
                Assert.Equal(2, taskAfter.Value.Revision); Assert.Equal(2, scheduleAfter.Value.Revision);
                Assert.Equal(pair.ReusableChange.OperationID, taskAfter.Value.LastOwnerCommit!.OperationId);
                Assert.Equal(pair.ScheduleChange.OperationID, scheduleAfter.Value.LastOwnerCommit!.OperationId);
                Assert.Equal(pair.ReusableChange.PayloadSHA256, taskAfter.Value.LastOwnerCommit.PayloadSha256);
                Assert.Equal(pair.ScheduleChange.PayloadSHA256, scheduleAfter.Value.LastOwnerCommit.PayloadSha256);
                Assert.False((await definitions.CompareExchangeLinkedPairAsync(pair, tasks, taskAdmission, scheduleAdmission, token)).Committed);
                Assert.Equal(taskAfter.Value.LastOwnerCommit, (await tasks.GetOwnedTaskAsync(id, token))!.Value.LastOwnerCommit);
                Assert.Equal(scheduleAfter.Value.LastOwnerCommit, (await definitions.GetOwnedAsync(id, token))!.Value.LastOwnerCommit);
            }
            // TWO consumed capabilities each receive their own exact fixed outcome. This performs no SQL replay.
            var outcome = new HomeExecutionOutcome(result.Committed ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                result.Code, result.Committed ? "Both canonical rows committed." : "Neither linked candidate committed.", []);
            Assert.True((await broker.CompleteExecutionAsync(taskCapability, outcome, token)).Succeeded);
            Assert.True((await broker.CompleteExecutionAsync(scheduleCapability, outcome, token)).Succeeded);
            Assert.Empty(await definitions.GetRunsAsync(id, 10, token));
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
