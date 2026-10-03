using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Desktop.Views.Pages.Forms;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using HavenOS.Forms;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FormsNativeWorkspaceHostTests
{
    [AvaloniaFact]
    public async Task Actual_Home_native_loader_awaits_response_close_and_failed_mount_retries_same_durable_attempt()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Window? owner = null;
        try
        {
            var services = new ServiceCollection().AddHavenInfrastructure();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            services.AddHavenFormsPublication(new FormNativePublicationValidator());
            await using var graph = services.BuildServiceProvider();
            var settings = Assert.IsType<VersionedAtomicSettingsStore>(graph.GetRequiredService<IVersionedSettingsStore>());
            var identity = await settings.GetStoreIdentityAsync(token);
            var publications = graph.GetRequiredService<FormPublicationService>();
            var project = FormProjectEditor.Create("Awaited native mount", FormModeKind.Form, DateTimeOffset.UtcNow);
            project = project with { RuntimeSettings = project.RuntimeSettings with { MaximumAttempts = 1 } };
            Assert.Equal("PermissionDenied", (await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Code);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync("forms", identity.StoreId.ToString("D"), token);
            var originalActor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var session = await publications.OpenHostSessionAsync(originalActor, token);
            var created = await session.Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token);
            Assert.True(created.Success);
            var published = await session.Publications.PublishAsync(project.FormID, created.Publication!.Revision, token);
            Assert.True(published.Success);
            async Task<Guid> OnlyDurableResponseAsync()
            {
                var snapshot = await settings.ExportAsync(token);
                Assert.Equal(identity.StoreId, snapshot.StoreIdentity!.StoreId);
                using var state = JsonDocument.Parse(snapshot.Settings["forms.response-sessions.v1." + project.FormID.ToString("N")]);
                var responses = state.RootElement.GetProperty("Responses");
                Assert.Equal(1, responses.GetArrayLength());
                return responses[0].GetProperty("Checkpoint").GetProperty("ResponseID").GetGuid();
            }
            var mountedWorkspace = await MainView.CreateFormsDocumentWorkspaceAsync(graph, originalActor, project.FormID,
                () => owner, () => true, token);
            using var host = mountedWorkspace.Host;
            var workspace = mountedWorkspace.Workspace;
            Assert.NotNull(host.Content);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await workspace.DispatchAsync("9to1.Forms.Respond", null, token));
            var originalResponseID = await OnlyDurableResponseAsync();
            Assert.NotEqual(Guid.Empty, originalResponseID);
            owner = new Window { Content = host, Width = 900, Height = 700 };
            owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(host.GetVisualDescendants().OfType<Button>());
            var pending = workspace.DispatchAsync("9to1.Forms.Respond", null, token).AsTask();
            var mountDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!owner.OwnedWindows.Any() && !pending.IsCompleted && DateTime.UtcNow < mountDeadline)
            { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, token); }
            var dialog = Assert.Single(owner.OwnedWindows);
            dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.False(pending.IsCompleted);
            Assert.Equal(originalResponseID, await OnlyDurableResponseAsync());

            Assert.NotNull(dialog.Content);
            dialog.Close();
            await pending.WaitAsync(token);
            var resumed = await session.Responses.ResumeAsync(project.FormID, originalResponseID, token);
            Assert.True(resumed.Success);
            Assert.Equal(originalResponseID, resumed.Response!.ResponseID);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await workspace.DispatchAsync("9to1.Forms.NewResponse", null, token));
            Assert.Equal(originalResponseID, await OnlyDurableResponseAsync());
            var beforeClosedAction = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token);
            host.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => host.RequireCurrentAsync(token));
            var closedResponse = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await workspace.DispatchAsync("9to1.Forms.Respond", null, token));
            Assert.Equal("PermissionDenied", closedResponse.Message);
            Assert.True(Enumerable.SequenceEqual(beforeClosedAction,
                await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"), token)));

        }
        finally
        {
            owner?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
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
