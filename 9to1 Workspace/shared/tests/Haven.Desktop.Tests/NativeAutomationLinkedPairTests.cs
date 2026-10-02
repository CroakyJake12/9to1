using Haven.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SceneButton = Haven.UI.Components.Button;
namespace Haven.Desktop.Tests;

public sealed class NativeAutomationLinkedPairTests
{
    [AvaloniaFact]
    public async Task Actual_native_archive_retains_both_individual_Home_reviews_and_commits_pair_without_runtime()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = lifetime.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Automations, HavenUiAppearance.SuperDark));
        var beforeTask = (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value;
        var beforeSchedule = (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value;
        var runtimeCalls = 0;
        using var page = new NativeAutomationsPage(fixture.Tasks, fixture.Definitions, null,
            () => Task.CompletedTask, _ => { runtimeCalls++; return Task.CompletedTask; },
            ownerCaller: fixture.Graph.GetRequiredService<IAutomationDefinitionReviewCaller>(),
            originalSelection: fixture.Selection, linkedOwnerCaller: fixture.PairCaller);
        var window = new Window { Content = page, Width = 1200, Height = 800 };
        try
        {
            window.Show(); window.UpdateLayout();
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("1 reusable workflow", StringComparison.Ordinal), token);
            Press("Automations.Tab.Library");
            Press($"Automations.Workflow.{fixture.ID:N}.Delete");
            await page.WhenLibraryActionIdleAsync().WaitAsync(token);
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("Home requests:", StringComparison.Ordinal), token);
            var permissions = fixture.Graph.GetRequiredService<HomePermissionTrustService>();
            var requests = (await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests.ToArray();
            Assert.Equal(2, requests.Length);
            await fixture.AssertRevisionsAsync(beforeTask.Revision, beforeSchedule.Revision, token);
            await fixture.ApproveAsync(requests[0].RequestId, token);
            Press($"Automations.Workflow.{fixture.ID:N}.Delete");
            await page.WhenLibraryActionIdleAsync().WaitAsync(token);
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("LinkedReviewPendingOrDeclined", StringComparison.Ordinal), token);
            await fixture.AssertRevisionsAsync(beforeTask.Revision, beforeSchedule.Revision, token);
            await fixture.ApproveAsync(requests[1].RequestId, token);
            Press($"Automations.Workflow.{fixture.ID:N}.Delete");
            await page.WhenLibraryActionIdleAsync().WaitAsync(token);
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("0 reusable workflows", StringComparison.Ordinal), token);
            var task = (await fixture.Tasks.GetOwnedTaskAsync(fixture.ID, token))!.Value;
            var schedule = (await fixture.Definitions.GetOwnedAsync(fixture.ID, token))!.Value;
            Assert.Equal(beforeTask.Revision + 1, task.Revision);
            Assert.Equal(beforeSchedule.Revision + 1, schedule.Revision);
            Assert.Equal(AutomationOperationalState.Archived, task.OperationalState);
            Assert.Equal(AutomationOperationalState.Archived, schedule.OperationalState);
            Assert.False(task.IsEnabled); Assert.False(schedule.IsEnabled); Assert.Null(schedule.NextRunAt);
            Assert.Equal(beforeTask.GraphJson, task.GraphJson); Assert.Equal(beforeSchedule.Instruction, schedule.Instruction);
            Assert.Equal(0, runtimeCalls); Assert.Empty(await fixture.Definitions.GetRunsAsync(fixture.ID, 10, token));
            Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);

            void Press(string name)
            {
                var button = Assert.Single(page.Scene.Root.DescendantsAndSelf().OfType<SceneButton>(), item => item.Name == name);
                Assert.False(button.State.HasFlag(HavenElementState.Disabled));
                button.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None));
                button.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None));
            }
        }
        finally { window.Close(); }
    }
    private static async Task UntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
        {
            token.ThrowIfCancellationRequested();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, token);
        }
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
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership()
                .AddHavenAutomationLinkedDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            var graph = services.BuildServiceProvider();
            Assert.Same(graph.GetRequiredService<AutomationLinkedDefinitionReviewCaller>(),
                graph.GetRequiredService<AutomationLinkedDefinitionReviewCaller>());
            Assert.Same(graph.GetRequiredService<AutomationDefinitionReviewCaller>(), graph.GetRequiredService<IAutomationDefinitionReviewCaller>());
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
