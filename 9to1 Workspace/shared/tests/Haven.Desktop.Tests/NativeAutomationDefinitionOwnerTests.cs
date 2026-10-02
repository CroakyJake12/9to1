using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Controls;
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

public sealed class NativeAutomationDefinitionOwnerTests
{
    [AvaloniaFact]
    public async Task Actual_native_definition_save_requires_individual_Home_review_and_archive_keeps_canonical_row_without_run_or_raw_delete()
    {
        using var owningLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = owningLifetime.Token;
        var root = Path.Combine(Path.GetTempPath(), "astra-native-automation-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Window? window = null;
        try
        {
            HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Automations, HavenUiAppearance.SuperDark));
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            await using var graph = services.BuildServiceProvider();
            var database = graph.GetRequiredService<SqliteDatabase>(); await database.InitializeAsync(token);
            var actors = graph.GetRequiredService<IAuthenticatedResourceActorSource>();
            var actor = (await actors.GetCurrentAsync(token))!;
            var store = await database.GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync(actor, "automations", store.StoreId.ToString("D"), token);
            var caller = graph.GetRequiredService<IAutomationDefinitionReviewCaller>();
            var selection = await caller.CaptureAsync(actor, token);
            var runtimeCalls = 0;
            using var page = new NativeAutomationsPage(graph.GetRequiredService<IWorkspaceStateRepository>(),
                graph.GetRequiredService<IAutomationRepository>(), null, () => Task.CompletedTask,
                _ => { runtimeCalls++; return Task.CompletedTask; }, ownerCaller: caller, originalSelection: selection);
            window = new Window { Content = page, Width = 1200, Height = 800 }; window.Show(); window.UpdateLayout();
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("0 reusable workflows", StringComparison.Ordinal), token);
            Press("Automations.New");
            Input("Automations.Editor.Name").Text = "Real individually reviewed library draft";
            Input("Automations.Editor.Goal").Text = "Preserve original scope and disabled state";
            Input("Automations.Editor.Rules").Text = "No automatic graph execution";
            var mutationGate = Assert.IsType<SemaphoreSlim>(typeof(NativeAutomationsPage)
                .GetField("_definitionChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            await mutationGate.WaitAsync(token);
            try
            {
                Press("Automations.Editor.Save"); // Actual keyboard handler captures A before waiting on its real gate.
                Input("Automations.Editor.Name").Text = "Substituted queued name B";
                Input("Automations.Editor.Goal").Text = "Substituted queued goal B";
                Input("Automations.Editor.Rules").Text = "Substituted queued rules B";
            }
            finally { mutationGate.Release(); }
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("Home request:", StringComparison.Ordinal), token, () => page.Scene.StatusText.Content);
            var tasks = graph.GetRequiredService<IReusableTaskOwnerRepository>();
            Assert.Empty((await tasks.ListOwnedTasksAsync(new(), token)).Items);
            var permissions = graph.GetRequiredService<HomePermissionTrustService>();
            var pending = Assert.Single((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.True((await permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            // Restore actual displayed A to finish ONLY its original review; B must never become the proposal.
            Input("Automations.Editor.Name").Text = "Real individually reviewed library draft";
            Input("Automations.Editor.Goal").Text = "Preserve original scope and disabled state";
            Input("Automations.Editor.Rules").Text = "No automatic graph execution";
            Press("Automations.Editor.Save");
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("1 reusable workflow", StringComparison.Ordinal), token);
            var saved = Assert.Single((await tasks.ListOwnedTasksAsync(new(), token)).Items).Value;
            Assert.Equal("Real individually reviewed library draft", saved.Name);
            Assert.Equal("Preserve original scope and disabled state", saved.Description);
            Assert.Contains("No automatic graph execution", saved.Instruction, StringComparison.Ordinal);
            Assert.DoesNotContain("Substituted queued", saved.Instruction, StringComparison.Ordinal);
            Assert.False(saved.IsEnabled); Assert.Null(saved.GraphJson);
            Assert.Equal(AutomationOperationalState.NeedsAttention, saved.OperationalState);
            Assert.Equal(1, saved.Revision);
            Assert.Empty(await graph.GetRequiredService<IAutomationRepository>().GetAllAsync(token));
            Press("Automations.Tab.Library");
            var run = Assert.Single(page.Scene.Root.DescendantsAndSelf().OfType<SceneButton>(), button => button.Name == $"Automations.Workflow.{saved.Id:N}.Run");
            Assert.True(run.State.HasFlag(HavenElementState.Disabled));
            Assert.True(run.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None)));
            Assert.True(run.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None)));
            Press($"Automations.Workflow.{saved.Id:N}.Enabled");
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("publication authority is unavailable", StringComparison.Ordinal), token);
            Assert.Equal(saved.Revision, (await tasks.GetOwnedTaskAsync(saved.Id, token))!.Value.Revision);
            Assert.Equal(0, runtimeCalls);
            Press($"Automations.Workflow.{saved.Id:N}.Delete");
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("Home request:", StringComparison.Ordinal), token, () => page.Scene.StatusText.Content);
            Assert.NotNull(await tasks.GetOwnedTaskAsync(saved.Id, token));
            var archiveRequest = Assert.Single((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            Assert.True((await permissions.DecideAsync(archiveRequest.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            Press($"Automations.Workflow.{saved.Id:N}.Delete");
            await UntilAsync(() => page.Scene.StatusText.Content.Contains("0 reusable workflows", StringComparison.Ordinal), token);
            var archived = (await tasks.GetOwnedTaskAsync(saved.Id, token))!.Value;
            Assert.Equal(AutomationOperationalState.Archived, archived.OperationalState);
            Assert.NotNull(archived.ArchivedAt); Assert.Equal(2, archived.Revision); Assert.Equal(saved.Instruction, archived.Instruction);
            Assert.Empty((await tasks.ListOwnedTasksAsync(new(), token)).Items);
            Assert.Single((await tasks.ListOwnedTasksAsync(new(IncludeArchived: true), token)).Items);
            page.Dispose();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => page.RequireCurrentAsync(actor, token));
            Assert.Equal(0, runtimeCalls);
            Assert.Equal(archived.LastOwnerCommit, (await tasks.GetOwnedTaskAsync(saved.Id, token))!.Value.LastOwnerCommit);

            void Press(string name)
            {
                var button = Assert.Single(page.Scene.Root.DescendantsAndSelf().OfType<SceneButton>(), value => value.Name == name);
                Assert.True(button.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None)));
                Assert.True(button.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None)));
            }
            Input Input(string name) => Assert.Single(page.Scene.Root.DescendantsAndSelf().OfType<Input>(), input => input.Name == name);
        }
        finally { window?.Close(); SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private static async Task UntilAsync(Func<bool> condition, CancellationToken token, Func<string>? diagnostic = null)
    {
        var end = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition() && DateTimeOffset.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, token); }
        Assert.True(condition(), diagnostic?.Invoke());
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
