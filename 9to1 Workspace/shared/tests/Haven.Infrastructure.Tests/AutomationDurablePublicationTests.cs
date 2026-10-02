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

public sealed class AutomationDurablePublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_SQL_preparation_survives_restart_and_stale_original_approval_never_rewrites_it(bool supersede)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = lifetime.Token;
        var root = Path.Combine(Path.GetTempPath(), "astra-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            await using var provider = services.BuildServiceProvider();
            var database = provider.GetRequiredService<SqliteDatabase>(); await database.InitializeAsync(ct);
            var tasks = provider.GetRequiredService<WorkspaceStateRepository>();
            var actor = Assert.IsType<AuthenticatedResourceActor>(await provider.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(ct));
            var store = await database.GetStoreIdentityAsync(ct);
            await provider.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync(actor, "automations", store.StoreId.ToString("D"), ct);
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
                    change.Scopes, change.Arguments, "Exact durable graph journal preparation", null, "publication-test", ct);
                Assert.Equal(HomePermissionRequestState.PendingApproval, review.State);
                Assert.True((await permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
                return Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(review.RequestId, change.Arguments, ct));
            }
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var draft = new ReusableTaskDefinition(id, "Retain exact original", "Description", "Instruction", null, false, now, now);
            var create = AutomationDefinitionChange.Capture(store.StoreId, actor, draft, 0, AutomationDefinitionChangeKind.Create);
            Assert.True((await operation.ExecuteAsync(create, await Approve(create), ct)).Committed);
            var before = (await tasks.GetOwnedTaskAsync(id, ct))!;
            var journal = new AutomationGraphPublicationJournal(Guid.NewGuid(), 1, Guid.NewGuid(), null, null,
                AutomationGraphPublicationPhase.Prepared, new string('A', 64), null);
            var prepare = AutomationDefinitionChange.Capture(store.StoreId, actor, before.Value with { PublicationJournal = journal },
                1, AutomationDefinitionChangeKind.Update);
            var capability = await Approve(prepare);
            var admission = Assert.IsAssignableFrom<IAutomationDefinitionCommitAdmission>(await authority.ClaimAsync(prepare, capability, ct));
            var homeBeforeMissingFence = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), ct);
            Assert.False((await tasks.PrepareGraphPublicationAsync(prepare, admission, ct)).Committed);
            Assert.Equal(homeBeforeMissingFence, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), ct));
            // Ordinary writer stays closed to journal substitution even with the real individual approval.
            Assert.False((await tasks.CompareExchangeOwnedTaskAsync(prepare, admission, ct)).Committed);
            Assert.False((await tasks.PrepareGraphPublicationAsync(prepare, new ForgedAdmission(), prepareHome, ct)).Committed);
            if (supersede)
            {
                var update = AutomationDefinitionChange.Capture(store.StoreId, actor, before.Value with { Name = "New independently approved row" },
                    1, AutomationDefinitionChangeKind.Update);
                Assert.True((await operation.ExecuteAsync(update, await Approve(update), ct)).Committed);
                before = (await tasks.GetOwnedTaskAsync(id, ct))!;
            }
            var result = await tasks.PrepareGraphPublicationAsync(prepare, admission, prepareHome, ct);
            Assert.Equal(!supersede, result.Committed);
            var after = (await tasks.GetOwnedTaskAsync(id, ct))!;
            if (supersede)
                Assert.Equal(JsonSerializer.Serialize(before.RetainedProtectedDescriptors), JsonSerializer.Serialize(after.RetainedProtectedDescriptors));
            else
            {
                Assert.Equal(journal, after.Value.PublicationJournal);
                Assert.Null(after.Value.GraphBinding); Assert.False(after.Value.IsEnabled);
                Assert.Equal(AutomationOperationalState.NeedsAttention, after.Value.OperationalState);
                Assert.Equal(2, after.Value.Revision);
                Assert.False((await tasks.PrepareGraphPublicationAsync(prepare, admission, prepareHome, ct)).Committed);
                // Simulated process interruption BEFORE any graph effect: actual SQL reread after pool restart retains Prepared.
                SqliteConnection.ClearAllPools();
                var restart = (await tasks.GetOwnedTaskAsync(id, ct))!;
                Assert.Equal(journal, restart.Value.PublicationJournal);
                Assert.Equal(after.Value.LastOwnerCommit, restart.Value.LastOwnerCommit);
                var receipt = await tasks.ObserveTaskCommitAsync(id, prepare.OperationID, prepare.PayloadSHA256, 1, ct);
                Assert.Equal(AutomationCommitReceiptObservation.CurrentCommitted, receipt.Observation);
                // A public journal cannot be repurposed as a completed graph observation through the preparation entry.
                var forged = AutomationDefinitionChange.Capture(store.StoreId, actor, restart.Value with
                { PublicationJournal = journal with { Phase = AutomationGraphPublicationPhase.DefinitionCommitted, PublishedDraftRevision = 1 },
                  GraphBinding = new(journal.GraphId, 1, null) }, 2, AutomationDefinitionChangeKind.Update);
                var forgedCapability = await Approve(forged);
                var realAdmission = Assert.IsAssignableFrom<IAutomationDefinitionCommitAdmission>(await authority.ClaimAsync(forged, forgedCapability, ct));
                Assert.False((await tasks.PrepareGraphPublicationAsync(forged, realAdmission, prepareHome, ct)).Committed);
                Assert.Equal(journal, (await tasks.GetOwnedTaskAsync(id, ct))!.Value.PublicationJournal);
                await broker.CompleteExecutionAsync(forgedCapability, new(HomePermissionRequestState.Failed, "PublicationTransitionUnavailable", "No graph observation", []), ct);
            }
            Assert.True((await broker.CompleteExecutionAsync(capability, new(result.Committed ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                result.Code, "Fixed observed owning result", []), ct)).Succeeded);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private sealed class ForgedAdmission : IAutomationDefinitionCommitAdmission
    { public ValueTask<bool> CheckAsync(AutomationDefinitionCommitContext context, CancellationToken ct) => ValueTask.FromResult(true); }
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
