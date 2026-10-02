using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Plan;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using Microsoft.Data.Sqlite;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class PlannerNativeEditorSaveTests
{
    [AvaloniaFact]
    public async Task Captured_task_save_cannot_adopt_new_event_editor_after_native_cancel_while_defaults_are_pending()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Click("NewTask"); await fixture.Authoring.PendingAction.WaitAsync(fixture.Token);
        fixture.Field("PlanEditorTitle").Text = "Captured task";
        fixture.Field("PlanEditorNotes").Text = "Original task notes";
        fixture.Held.HoldNextDefaults = true;
        fixture.Click("PlanEditorSave"); var save = fixture.Authoring.PendingAction;
        try
        {
            await fixture.Held.Entered.Task.WaitAsync(fixture.Token);
            fixture.Click("PlanEditorCancel");
            fixture.Click("NewEvent"); await fixture.Authoring.PendingAction.WaitAsync(fixture.Token);
            fixture.Field("PlanEditorTitle").Text = "Later event draft";
            fixture.Field("PlanEditorNotes").Text = "Later event notes";
            fixture.Held.Release.TrySetResult(); await save.WaitAsync(fixture.Token);
            var reopened = new PlannerRepository(new SqliteDatabase(fixture.Paths));
            var task = Assert.Single(await reopened.GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: true), fixture.Token));
            Assert.Equal("Captured task", task.Title); Assert.Equal("Original task notes", task.Notes);
            Assert.Empty(await reopened.GetEventsAsync(DateTimeOffset.Now.AddDays(-2), DateTimeOffset.Now.AddDays(2), null, fixture.Token));
            Assert.Equal("Later event draft", fixture.Field("PlanEditorTitle").Text);
            Assert.Equal("Later event notes", fixture.Field("PlanEditorNotes").Text);
            Assert.Equal(HavenVisibility.Visible, fixture.Root.DescendantsAndSelf().Single(element => element.Name == "PlanEditorRoot").GetValue(HavenProperties.Visibility));
        }
        finally { fixture.Held.Release.TrySetResult(); await save.WaitAsync(fixture.Token); }
    }

    [AvaloniaFact]
    public async Task Actual_acknowledged_new_task_keeps_canonical_id_when_calendar_refresh_fails_and_retry_updates_same_row()
    {
        await using var fixture = await Fixture.CreateAsync(failRefresh: true);
        fixture.Click("NewTask"); await fixture.Authoring.PendingAction.WaitAsync(fixture.Token);
        fixture.Field("PlanEditorTitle").Text = "First acknowledged task";
        fixture.Click("PlanEditorSave"); await fixture.Authoring.PendingAction.WaitAsync(fixture.Token);
        var reopened = new PlannerRepository(new SqliteDatabase(fixture.Paths));
        var original = Assert.Single(await reopened.GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: true), fixture.Token));
        Assert.Contains("Saved locally", fixture.Status.Content, StringComparison.Ordinal);
        Assert.Equal(HavenVisibility.Visible, fixture.Root.DescendantsAndSelf().Single(element => element.Name == "PlanEditorRoot").GetValue(HavenProperties.Visibility));
        fixture.Field("PlanEditorTitle").Text = "Correction to same task";
        fixture.Click("PlanEditorSave"); await fixture.Authoring.PendingAction.WaitAsync(fixture.Token);
        var after = Assert.Single(await new PlannerRepository(new SqliteDatabase(fixture.Paths))
            .GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: true), fixture.Token));
        Assert.Equal(original.Id, after.Id); Assert.Equal("Correction to same task", after.Title);
        Assert.Contains("Saved locally", fixture.Status.Content, StringComparison.Ordinal);
    }

    public class HeldPlanner : DispatchProxy
    {
        public IPlannerRepository Actual { get; set; } = null!;
        public CancellationToken Lifetime { get; set; }
        public bool HoldNextDefaults { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IPlannerRepository.EnsureDefaultsAsync) && HoldNextDefaults)
            {
                HoldNextDefaults = false;
                return HoldDefaultsAsync();
            }
            return targetMethod.Invoke(Actual, args);
        }
        private async Task HoldDefaultsAsync()
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(Lifetime);
            await Actual.EnsureDefaultsAsync(Lifetime);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(60));
        private readonly PlanHavenScene _scene;
        private readonly Window _window;
        internal Paths Paths { get; }
        internal PlanAuthoringCoordinator Authoring { get; }
        internal HeldPlanner Held { get; }
        internal HavenElement Root => _scene.Root;
        internal CancellationToken Token => _lifetime.Token;
        internal HavenText Status => Root.DescendantsAndSelf().OfType<HavenText>().Single(element => element.Name == "PlanEditorStatus");
        private Fixture(Paths paths, PlannerRepository actual, bool failRefresh)
        {
            Paths = paths;
            var proxy = DispatchProxy.Create<IPlannerRepository, HeldPlanner>();
            Held = (HeldPlanner)(object)proxy; Held.Actual = actual; Held.Lifetime = Token;
            _scene = new PlanHavenScene();
            Authoring = new PlanAuthoringCoordinator(_scene, proxy, _ => failRefresh
                ? Task.FromException(new IOException("Real post-save calendar refresh fixture failure")) : Task.CompletedTask);
            _window = new Window { Width = 1280, Height = 860, Content = new HavenSceneControl { Root = Root } };
            _window.Show(); _window.UpdateLayout();
        }
        internal static async Task<Fixture> CreateAsync(bool failRefresh = false)
        {
            var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
            var database = new SqliteDatabase(paths); await database.InitializeAsync(CancellationToken.None);
            var actual = new PlannerRepository(database); await actual.EnsureDefaultsAsync(CancellationToken.None);
            return new Fixture(paths, actual, failRefresh);
        }
        internal Input Field(string name) => Root.DescendantsAndSelf().OfType<Input>().Single(element => element.Name == name);
        internal void Click(string name)
        {
            _window.UpdateLayout();
            var button = Root.DescendantsAndSelf().OfType<HavenButton>().Single(element => element.Name == name);
            var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
            var router = new HavenInputRouter(Root); router.PointerPressed(point); Assert.True(router.PointerReleased(point));
        }
        public ValueTask DisposeAsync()
        {
            Held.Release.TrySetResult(); Authoring.Dispose(); _window.Content = null; _window.Close(); _scene.Dispose();
            _lifetime.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(Paths.DataDirectory, true);
            return ValueTask.CompletedTask;
        }
    }
    internal sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-planner-editor-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
