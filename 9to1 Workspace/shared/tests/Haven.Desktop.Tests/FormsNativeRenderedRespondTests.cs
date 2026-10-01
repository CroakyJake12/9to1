using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using HavenOS.Forms;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FormsNativeRenderedRespondTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rendered_Respond_click_uses_actual_factory_and_suspended_closed_host_cannot_start_response(bool disposeWhileSuspended)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var root = Directory.CreateTempSubdirectory("astra-forms-rendered-").FullName;
        Window? owner = null;
        try
        {
            var homeFile = Path.Combine(root, "home.json");
            var home = new SuspendedActualHomeRead(new FileHomeCoreStateStore(homeFile));
            var services = new ServiceCollection().AddHavenInfrastructure();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(home);
            services.AddHavenFormsPublication(new FormNativePublicationValidator());
            await using var graph = services.BuildServiceProvider();
            var settings = Assert.IsType<VersionedAtomicSettingsStore>(graph.GetRequiredService<IVersionedSettingsStore>());
            var identity = await settings.GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))
                ?? throw new InvalidOperationException("Actual native Home actor is required.");
            var publications = graph.GetRequiredService<FormPublicationService>();
            var project = FormProjectEditor.Create("Rendered response", FormModeKind.Form, DateTimeOffset.UtcNow);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Name", null,
                JsonSerializer.SerializeToElement(new { }), false, new());
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, DateTimeOffset.UtcNow);
            var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token); Assert.True(created.Success);
            var published = await publications.PublishAsync(project.FormID, created.Publication!.Revision, token); Assert.True(published.Success);
            var version = Assert.Single(published.Publication!.Versions);
            owner = new Window { Width = 1000, Height = 800 };
            var mounted = await MainView.CreateFormsDocumentWorkspaceAsync(graph, actor, project.FormID,
                () => owner, () => true, token);
            using var host = mounted.Host; owner.Content = host; owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var respond = Assert.Single(host.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Open my response"));
            Assert.True(respond.IsEnabled);
            var settingsFile = Path.Combine(root, "settings.json");
            var beforeSettings = await File.ReadAllBytesAsync(settingsFile, token);
            var beforeHome = await File.ReadAllBytesAsync(homeFile, token);
            if (disposeWhileSuspended) home.Armed = true;
            // Invoke the actual rendered control's routed click; the loader's registered handler
            // passes through the real host OriginDispatcher rather than calling workspace.DispatchAsync.
            respond.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var renderedActionCompletion = host.WhenActionsIdleAsync();
            if (disposeWhileSuspended)
            {
                await home.Entered.Task.WaitAsync(token);
                host.Dispose(); home.Release.TrySetResult();
                await renderedActionCompletion.WaitAsync(token); Dispatcher.UIThread.RunJobs();
                Assert.Empty(owner.OwnedWindows);
                Assert.Equal(beforeSettings, await File.ReadAllBytesAsync(settingsFile, token));
                Assert.Equal(beforeHome, await File.ReadAllBytesAsync(homeFile, token));
                await AssertNoPersistedPendingAsync(graph.GetRequiredService<IHomeCoreStateStore>(), token);
                // A retained rendered control has its loader handler detached after the tab closes.
                respond.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await host.WhenActionsIdleAsync().WaitAsync(token); Dispatcher.UIThread.RunJobs();
                Assert.Equal(beforeSettings, await File.ReadAllBytesAsync(settingsFile, token));
                Assert.Equal(beforeHome, await File.ReadAllBytesAsync(homeFile, token));
                await AssertNoPersistedPendingAsync(graph.GetRequiredService<IHomeCoreStateStore>(), token);
                return;
            }
            await UntilAsync(() => Task.FromResult(owner.OwnedWindows.Count() == 1), token);
            var dialog = Assert.Single(owner.OwnedWindows); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var responseState = JsonDocument.Parse((await settings.ExportAsync(token)).Settings["forms.response-sessions.v1." + project.FormID.ToString("N")]);
            var retained = Assert.Single(responseState.RootElement.GetProperty("Responses").EnumerateArray());
            var responseID = retained.GetProperty("Checkpoint").GetProperty("ResponseID").GetGuid();
            var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), control => AutomationProperties.GetName(control) == "Name");
            input.Text = "Ada";
            var save = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Save answers"));
            Assert.True(save.IsEnabled); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var sessions = graph.GetRequiredService<FormResponseSessionService>();
            await UntilAsync(async () =>
            {
                var state = await sessions.ResumeAsync(project.FormID, responseID, token);
                return state.Success && state.Response!.Answers.Any(answer => answer.FieldID == field.FieldID && answer.Value.GetString() == "Ada");
            }, token);
            Assert.False(respond.IsEnabled); // Awaited native response lifetime still owns the workspace operation.
            dialog.Close(); await renderedActionCompletion.WaitAsync(token);
            await UntilAsync(() => Task.FromResult(respond.IsEnabled), token);
            var resumed = await sessions.ResumeAsync(project.FormID, responseID, token); Assert.True(resumed.Success);
            Assert.Equal(version.FormVersionID, resumed.Response!.FormVersionID);
            Assert.Equal(field.FieldID, Assert.Single(resumed.Response.Answers).FieldID);
            Assert.Equal(identity.StoreId, (await settings.GetStoreIdentityAsync(token)).StoreId);
            // A second real rendered click resumes the same durable attempt after the first dialog closed.
            respond.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var secondActionCompletion = host.WhenActionsIdleAsync();
            await UntilAsync(() => Task.FromResult(owner.OwnedWindows.Count() == 1), token);
            var secondDialog = Assert.Single(owner.OwnedWindows); secondDialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var secondState = JsonDocument.Parse((await settings.ExportAsync(token)).Settings["forms.response-sessions.v1." + project.FormID.ToString("N")]);
            Assert.Equal(responseID, Assert.Single(secondState.RootElement.GetProperty("Responses").EnumerateArray()).GetProperty("Checkpoint").GetProperty("ResponseID").GetGuid());
            secondDialog.Close(); await secondActionCompletion.WaitAsync(token);
            await UntilAsync(() => Task.FromResult(respond.IsEnabled), token);
            Assert.Equal(version.Project.GetRawText(), Assert.Single((await publications.ReadAsync(project.FormID, token)).Publication!.Versions).Project.GetRawText());
        }
        finally { owner?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static async Task AssertNoPersistedPendingAsync(IHomeCoreStateStore home, CancellationToken token)
    {
        var read = await home.ReadAsync(token); Assert.True(read.IsSuccess);
        var record = Assert.Single(read.State!.Records, item => item.RecordId == "home.permissions-trust");
        Assert.Equal("home.permissions-trust", record.RecordType); Assert.Equal(1, record.SchemaVersion);
        var requests = record.Payload.GetProperty("Requests").Deserialize<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest[]>()
            ?? throw new InvalidDataException("Actual persisted permission requests required.");
        Assert.DoesNotContain(requests, request => request.State == HomePermissionRequestState.PendingApproval);
    }
    private static async Task UntilAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        while (!await condition()) { token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, token); }
    }
    // This seam suspends an actual filesystem Home read; it never fabricates actors, records or approvals.
    private sealed class SuspendedActualHomeRead(IHomeCoreStateStore actual) : IHomeCoreStateStore
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<HomeStateReadResult> ReadAsync(CancellationToken token = default)
        {
            var state = await actual.ReadAsync(token);
            if (!Armed) return state;
            Armed = false; Entered.TrySetResult();
            try { await Release.Task.WaitAsync(token); return state; }
            finally { Completed.TrySetResult(); }
        }
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long revision, CancellationToken token = default)
            => actual.WriteAsync(record, revision, token);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long revision, AuthenticatedResourceActor expectedActor,
            IHomeStateCommitActorGuard guard, CancellationToken token = default)
            => actual.WriteGuardedAsync(record, revision, expectedActor, guard, token);
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
