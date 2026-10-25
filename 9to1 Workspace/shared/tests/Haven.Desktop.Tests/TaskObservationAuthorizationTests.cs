using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Tasks;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_permission_review_responds_only_through_the_same_actual_Ask_owner_without_resuming_work(bool approve)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(tools: false);
        var task = rig.Service.CurrentCanonicalTask!;
        var originalRequest = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        _ = await rig.Tasks.SubmitFollowUpAsync(task.TaskId, "retain this queued work", TaskFollowUpMode.Queue, null, null, token);
        var review = Assert.IsType<TaskRunOriginalTaskPermissionReview>(await rig.Tasks.ReadOriginalTaskPermissionReviewAsync(task.TaskId, task.ExecutionId, token));
        Assert.Equal(originalRequest.Id, review.Request.Id); Assert.True(review.CanRespond);
        Assert.Contains("does not approve", review.Request.Explanation, StringComparison.Ordinal);
        Assert.Equal(task.ContextId, review.CanonicalTaskContext.ContextId);
        var reply = await rig.Tasks.RespondOriginalTaskPermissionAsync(review, approve, token);
        Assert.Equal(approve ? PermissionDecisionKind.Allowed : PermissionDecisionKind.Denied, reply.Decision.Kind);
        Assert.Equal(task.TaskId, reply.TaskId); Assert.Equal(task.ExecutionId, reply.ExecutionId);
        var current = (await rig.Tasks.GetAsync(task.TaskId, token))!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, current.State); Assert.Empty(current.Attempts);
        Assert.Equal("retain this queued work", Assert.Single(current.Queue).Summary);
        var after = Assert.IsType<TaskRunOriginalTaskPermissionReview>(await rig.Tasks.ReadOriginalTaskPermissionReviewAsync(task.TaskId, task.ExecutionId, token));
        Assert.False(after.CanRespond); Assert.Equal(reply.Decision, after.AcknowledgedDecision);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
    }

    [Fact]
    public async Task Task_permission_review_refuses_copied_foreign_and_stale_reviews_before_any_decision()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await using var foreign = new Rig();
        await rig.RunAsync(tools: false);
        var task = rig.Service.CurrentCanonicalTask!;
        var review = Assert.IsType<TaskRunOriginalTaskPermissionReview>(await rig.Tasks.ReadOriginalTaskPermissionReviewAsync(task.TaskId, task.ExecutionId, token));
        var copied = (TaskRunOriginalTaskPermissionReview)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(review, null)!;
        Assert.Throws<UnauthorizedAccessException>(() => { _ = rig.Tasks.RespondOriginalTaskPermissionAsync(copied, true, token); });
        Assert.Throws<UnauthorizedAccessException>(() => { _ = foreign.Tasks.RespondOriginalTaskPermissionAsync(review, true, token); });
        _ = await rig.Tasks.SubmitFollowUpAsync(task.TaskId, "newer actual task revision", TaskFollowUpMode.Queue, null, null, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.RespondOriginalTaskPermissionAsync(review, true, token));
        Assert.True(rig.Owner.CanRespond(review.Request.Id));
        Assert.Null(rig.Owner.GetAcknowledgedOriginalDecision(review.Request.Id));
        Assert.Equal(0, rig.Client.Dispatches);
        var fresh = Assert.IsType<TaskRunOriginalTaskPermissionReview>(await rig.Tasks.ReadOriginalTaskPermissionReviewAsync(task.TaskId, task.ExecutionId, token));
        _ = await rig.Tasks.RespondOriginalTaskPermissionAsync(fresh, false, token);
    }

    [Fact]
    public async Task Task_permission_review_joins_actual_metadata_read_on_owner_retirement_and_preserves_all_source_faults()
    {
        var token = TestContext.Current.CancellationToken;
        var rig = new Rig(temporary: false);
        var held = new TaskCompletionSource<RemediationRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new OperationCanceledException("actual permission metadata faulted OCE");
        var sibling = new IOException("actual permission metadata storage failure");
        try
        {
            await rig.RunAsync(tools: false);
            var task = rig.Service.CurrentCanonicalTask!;
            rig.RemediationRows.OriginalRead = held.Task;
            var reading = rig.Tasks.ReadOriginalTaskPermissionReviewAsync(task.TaskId, task.ExecutionId, token);
            await rig.RemediationRows.OriginalReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var close = rig.Owner.CloseAndDrainAsync();
            Assert.False(close.IsCompleted); Assert.False(reading.IsCompleted); Assert.False(held.Task.IsCompleted);
            held.SetException([first, sibling]);
            var error = await Record.ExceptionAsync(() => reading);
            Assert.NotNull(error); Assert.True(reading.IsFaulted);
            Assert.Contains(Leaves(error), leaf => ReferenceEquals(leaf, first));
            Assert.Contains(Leaves(error), leaf => ReferenceEquals(leaf, sibling));
            var closed = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closed); Assert.True(close.IsFaulted);
            Assert.Contains(Leaves(closed), leaf => ReferenceEquals(leaf, first));
            Assert.Contains(Leaves(closed), leaf => ReferenceEquals(leaf, sibling));
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            held.TrySetException([first, sibling]);
            rig.RemediationRows.OriginalRead = null;
            var originalClose = await Record.ExceptionAsync(() => rig.DisposeAsync().AsTask());
            Assert.NotNull(originalClose);
            Assert.Contains(Leaves(originalClose), leaf => ReferenceEquals(leaf, first));
            Assert.Contains(Leaves(originalClose), leaf => ReferenceEquals(leaf, sibling));
        }
    }

    [AvaloniaFact]
    public async Task Task_permission_published_original_response_from_another_view_refreshes_the_same_dashboard_and_unsubscribes_on_close()
    {
        var token = TestContext.Current.CancellationToken;
        var eventRows = new ObservationEventRows();
        await using var hub = new ExecutionEventHub(eventRows);
        await using var rig = new Rig(temporary: false, originalEvents: hub);
        using var paths = new ObservationPaths();
        ExecutionEvent? decisionPublication = null;
        EventHandler<ExecutionEvent> observed = (_, value) =>
        { if (value.SafeMetadata?.GetValueOrDefault("permissionPhase") == "decision") decisionPublication = value; };
        hub.Published += observed;
        await rig.RunAsync(tools: false);
        var task = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        var preferences = new UserPreferencesService(paths); preferences.SetShownTasks(TaskShownScope.AllTasks);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var page = new SpaceTasksDashboardPage(source, rig.Tasks, SpaceRegistry.TasksSpaceId,
            _ => Task.CompletedTask, null, null, preferences, _ => Task.CompletedTask);
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token);
            Assert.True(Assert.Single(page.CurrentObservations).PermissionReview!.CanRespond);
            ObservationAuthoredControls(page.Scene.Root!).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "tasks-tile-review-permission").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPermissionReview).WaitAsync(token);
            // This decision bypasses the dashboard presentation, using the SAME actual
            // existing owner that another view/console uses. Hub publication is a hint;
            // the dashboard must re-read the current original permission owner.
            var actual = await rig.Owner.DenyOriginalAsync(request.Id, token);
            Assert.Equal(PermissionDecisionKind.Denied, actual.Kind);
            var refreshing = Assert.IsAssignableFrom<Task>(page.ActualPermissionInvalidation);
            await refreshing.WaitAsync(token);
            var row = Assert.Single(page.CurrentObservations);
            Assert.Equal(task.TaskId, row.Snapshot!.TaskId); Assert.Equal(task.ExecutionId, row.Snapshot.ExecutionId);
            Assert.False(row.PermissionReview!.CanRespond);
            Assert.Equal(PermissionDecisionKind.Denied, row.PermissionReview.AcknowledgedDecision!.Kind);
            Assert.DoesNotContain(ObservationAuthoredControls(page.Scene.Root!).OfType<Button>(),
                button => AutomationProperties.GetAutomationId(button) == "tasks-tile-review-permission");
            Assert.Equal(TaskExecutionLifecycle.Suspended, (await rig.Tasks.GetAsync(task.TaskId, token))!.State);
            Assert.Equal(0, rig.Client.Dispatches);
            var answers = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>().ToArray();
            Assert.False(answers.Single(button => AutomationProperties.GetAutomationId(button) == "tasks-permission-allow").IsEffectivelyEnabled);
            Assert.False(answers.Single(button => AutomationProperties.GetAutomationId(button) == "tasks-permission-deny").IsEffectivelyEnabled);
            answers.Single(button => AutomationProperties.GetAutomationId(button) == "tasks-permission-close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            page.RequestRetirement(); await page.CloseAndDrainAsync();
            Assert.NotNull(decisionPublication);
            Assert.True(hub.TryPublish(decisionPublication! with { EventId = Guid.NewGuid() }));
            Assert.Same(refreshing, page.ActualPermissionInvalidation); // Retired watch cannot start another source read.
            Assert.Null(page.Scene.Root);
        }
        finally
        {
            page.RequestRetirement(); await page.CloseAndDrainAsync();
            window.Content = null; window.Close(); hub.Published -= observed;
        }
    }

    private sealed class ObservationEventRows : IExecutionEventRepository
    {
        private readonly List<ExecutionEvent> _events = [];
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> values, CancellationToken token)
        { token.ThrowIfCancellationRequested(); lock (_events) _events.AddRange(values); return Task.CompletedTask; }
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken token)
        { lock (_events) return Task.FromResult<IReadOnlyList<ExecutionEvent>>(_events.Where(value => value.ExecutionId == executionId).ToArray()); }
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken token) =>
            throw new NotSupportedException();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_permission_authored_popout_reviews_and_records_real_response_without_starting_or_losing_drafts(bool approve)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        using var paths = new ObservationPaths();
        await rig.RunAsync(tools: false);
        var task = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        _ = await rig.Tasks.SubmitFollowUpAsync(task.TaskId, "keep queued instructions", TaskFollowUpMode.Queue, null, null, token);
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        var preferences = new UserPreferencesService(paths); preferences.SetShownTasks(TaskShownScope.AllTasks);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var page = new SpaceTasksDashboardPage(source, rig.Tasks, SpaceRegistry.TasksSpaceId,
            _ => Task.CompletedTask, null, null, preferences, _ => Task.CompletedTask);
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); window.UpdateLayout();
            var row = Assert.Single(page.CurrentObservations);
            Assert.Equal(request.Id, row.PermissionReview!.Request.Id);
            page.Scene.Instruction.Text = "preserve this unsent new-task draft";
            var reviewButton = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "tasks-tile-review-permission");
            reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPermissionReview).WaitAsync(token);
            window.UpdateLayout();
            var content = string.Join("\n", ObservationAuthoredControls(page.Scene.Root!).OfType<TextBlock>().Select(text => text.Text));
            Assert.Contains(request.Title, content, StringComparison.Ordinal);
            Assert.Contains(request.Explanation, content, StringComparison.Ordinal);
            Assert.Contains(request.ProviderName!, content, StringComparison.Ordinal);
            var buttonId = approve ? "tasks-permission-allow" : "tasks-permission-deny";
            var respond = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == buttonId);
            Assert.True(respond.IsEffectivelyEnabled);
            respond.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPermissionResponse).WaitAsync(token);
            var response = Assert.IsType<TaskRunOriginalTaskPermissionResponse>(page.AcknowledgedPermissionResponse);
            Assert.Equal(request.Id, response.RequestId);
            Assert.Equal(approve ? PermissionDecisionKind.Allowed : PermissionDecisionKind.Denied, response.Decision.Kind);
            Assert.Equal(task.TaskId, response.TaskId); Assert.Equal(task.ExecutionId, response.ExecutionId);
            Assert.Equal("preserve this unsent new-task draft", page.Scene.Instruction.Text);
            var current = (await rig.Tasks.GetAsync(task.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, current.State); Assert.Empty(current.Attempts);
            Assert.Equal("keep queued instructions", Assert.Single(current.Queue).Summary);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
            var controls = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>().ToArray();
            Assert.False(controls.Single(button => AutomationProperties.GetAutomationId(button) == "tasks-permission-allow").IsEffectivelyEnabled);
            Assert.False(controls.Single(button => AutomationProperties.GetAutomationId(button) == "tasks-permission-deny").IsEffectivelyEnabled);
            controls.Single(button => AutomationProperties.GetAutomationId(button) == "tasks-permission-close")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            Assert.True(page.Scene.TryGetValue("ReviewOpen", out var open)); Assert.Equal(false, open);
        }
        finally
        {
            page.RequestRetirement(); await page.CloseAndDrainAsync();
            window.Content = null; window.Close();
        }
    }

    private static IEnumerable<Control> ObservationAuthoredControls(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container ? container.AuthoredChildren : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var control in ObservationAuthoredControls(child)) yield return control;
    }

    [AvaloniaFact]
    public async Task All_tasks_authored_scope_menus_persist_and_page_the_actual_store_then_open_the_exact_selected_task()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        using var paths = new ObservationPaths();
        var preferences = new UserPreferencesService(paths);
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        rig.Conversations.Current = rig.Conversation;
        for (var index = 0; index < 41; index++)
            _ = await rig.Tasks.BeginAuthorizedAsync(index < 2 ? rig.Conversation.Id : Guid.NewGuid(), Guid.NewGuid(),
                $"genuine owned task {index}", TaskExecutionDurability.PersistedPlan, [], token);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var opened = new TaskCompletionSource<AuthorizedTaskContextObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new SpaceTasksDashboardPage(source, rig.Tasks, SpaceRegistry.TasksSpaceId,
            _ => throw new InvalidOperationException("No legacy Space task was selected."), null, null, preferences,
            selected => { opened.TrySetResult(selected); return Task.CompletedTask; });
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); window.UpdateLayout();
            Assert.Equal(TaskShownScope.TaskSpaceOnly, page.ShownScope); Assert.Empty(page.CurrentObservations);
            var owner = page.Scene.RecentRows;
            var context = Assert.IsType<ContextMenu>(owner.ContextMenu);
            var point = owner.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right);
            window.UpdateLayout();
            Assert.True(context.IsOpen); // Actual pointer context route, not a second menu implementation.
            var shown = Assert.IsAssignableFrom<MenuItem>(Assert.Single(context.Items));
            Assert.Equal("Shown Tasks", shown.Header);
            var all = shown.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "All Tasks"));
            shown.IsSubMenuOpen = true;
            all.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualScopeChange).WaitAsync(token);
            Assert.Equal(TaskShownScope.AllTasks, new UserPreferencesService(paths).ShownTasks);
            Assert.Equal(40, page.CurrentObservations.Count);
            var firstIds = page.CurrentObservations.Select(row => row.Snapshot!.TaskId).ToArray();
            var tile = page.Scene.RecentRows.GetVisualDescendants().OfType<Button>().First();
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var actualOpened = await opened.Task.WaitAsync(token);
            Assert.Contains(actualOpened.Snapshot.TaskId, firstIds);
            Assert.True(source.IsIssuedSelection(actualOpened.Selection));
            Assert.Equal(actualOpened.Selection.ExecutionId, actualOpened.Snapshot.ExecutionId);
            var next = page.Scene.Root!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "tasks-next-page");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPageNavigation).WaitAsync(token);
            Assert.Single(page.CurrentObservations);
            Assert.DoesNotContain(page.CurrentObservations[0].Snapshot!.TaskId, firstIds);
            var previous = page.Scene.Root!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "tasks-previous-page");
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPageNavigation).WaitAsync(token);
            Assert.Equal(firstIds.Order(), page.CurrentObservations.Select(row => row.Snapshot!.TaskId).Order());
            var overflow = page.Scene.Root!.GetVisualDescendants().OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "tasks-overflow");
            Assert.Equal("Task list options", AutomationProperties.GetName(overflow));
            overflow.IsSubMenuOpen = true;
            var overflowShown = Assert.IsAssignableFrom<MenuItem>(Assert.Single(overflow.Items));
            overflowShown.IsSubMenuOpen = true;
            overflowShown.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Task Space Only"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualScopeChange).WaitAsync(token);
            Assert.Empty(page.CurrentObservations);
            Assert.Equal(TaskShownScope.TaskSpaceOnly, new UserPreferencesService(paths).ShownTasks);
            Assert.Null(rig.Conversation.SpaceId); Assert.Equal(0, rig.Conversations.Writes); Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            page.RequestRetirement(); await page.CloseAndDrainAsync();
            window.Content = null; window.Close();
        }
    }

    [AvaloniaFact]
    public async Task All_tasks_native_widget_keeps_historical_task_and_null_origin_through_readiness_and_same_context_new_task()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        rig.Conversations.Current = rig.Conversation;
        var first = await rig.Tasks.BeginAuthorizedAsync(rig.Conversation.Id, Guid.NewGuid(), "first original task", TaskExecutionDurability.PersistedPlan, [], token);
        var source = new SpaceTaskWorkspaceService(new SpaceRegistry(new ObservationReadOnlySettings()), rig.Conversations, rig.Tasks);
        var selected = Assert.Single((await source.ReadAllTasksPageAsync(10, null, token)).Tasks);
        var readiness = new ObservationReadinessForwarder();
        var home = new ObservationUnavailableHome();
        using var connection = new CancellationTokenSource(); using var lifetime = new CancellationTokenSource();
        var widget = new SpaceTaskWidgetPage(source, rig.Tasks, selected.Selection, readiness);
        var window = new Window { Width = 1000, Height = 800, Content = widget };
        try
        {
            readiness.Owner = await widget.AcquireOriginalNativeReadinessAsync(page => NativeCanonicalTaskSceneReadiness.BindOriginal(
                page, home, rig.Actors, connection.Token, lifetime.Token));
            window.Show(); await widget.ActivateAsync(token);
            Assert.Equal("CONTROLLED_OBSERVATION_UNAVAILABLE", (await readiness.ActualCheck!).Code);
            Assert.True(home.Calls > 0); // Real current Task reads around the actual Home observation; no Ready fabricated.
            Assert.Null(widget.OriginalTaskSelection!.OriginSpaceId);
            Assert.Equal(first.TaskId, widget.OriginalReadinessContext.TaskId);
            var newer = await rig.Tasks.BeginAuthorizedAsync(rig.Conversation.Id, Guid.NewGuid(), "newer same-context task", TaskExecutionDurability.PersistedPlan, [], token);
            Assert.True(widget.IsActionAvailable("task.refresh"));
            await widget.DispatchAsync("task.refresh", null, token);
            Assert.Equal(first.TaskId, widget.OriginalReadinessContext.TaskId);
            Assert.Equal(first.ExecutionId, widget.OriginalReadinessContext.RunId);
            widget.TrySetValue("Instruction", "follow up only the exact historical task");
            await widget.DispatchAsync("task.queue", null, token);
            Assert.Equal(first.TaskId, widget.AcknowledgedFollowUp!.Snapshot.TaskId);
            Assert.Single((await rig.Tasks.GetAsync(first.TaskId, token))!.Queue);
            Assert.Empty((await rig.Tasks.GetAsync(newer.TaskId, token))!.Queue);
            Assert.Equal(HavenMode.Studio, rig.Conversations.Current!.Mode);
            Assert.Null(rig.Conversations.Current.SpaceId); Assert.Equal(0, rig.Conversations.Writes);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, home.CloseCalls);
        }
        finally
        {
            widget.RequestRetirement(); await widget.CloseAndDrainAsync();
            window.Content = null; window.Close();
        }
    }

    private sealed class ObservationReadinessForwarder : ICuiSceneReadiness
    {
        internal NativeCanonicalTaskSceneReadiness? Owner;
        internal Task<CuiSceneAvailability>? ActualCheck;
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { var actual = Owner!.CheckAsync(token).AsTask(); ActualCheck = actual; return new(actual); }
    }
    private sealed class ObservationUnavailableHome : IHomeNativeStartupSession
    {
        internal int Calls, CloseCalls;
        public Task<HomeNativeStartupObservation> CheckAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new HomeNativeStartupObservation(HomeNativeStartupState.Unready,
            "CONTROLLED_OBSERVATION_UNAVAILABLE", "Controlled negative compatibility observation; no installed or native acceptance.")); }
        public Task CloseAndDrainAsync() { CloseCalls++; return Task.CompletedTask; }
    }
    private sealed class ObservationPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "actual-task-observation-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
    private sealed class ObservationSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Task_observation_requires_a_complete_current_authentication_revision_even_for_historical_readonly_tasks(string invalidRevision)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var task = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "actual owned task", TaskExecutionDurability.PersistedPlan, [], token);
        rig.Actors.Current = rig.Actors.Current! with { AuthenticationRevision = invalidRevision };
        var single = await rig.Tasks.ReadAuthorizedTaskObservationAsync(task.TaskId, token);
        Assert.Equal(TaskObservationDisposition.Denied, single.Disposition); Assert.Null(single.Snapshot);
        var page = await rig.Tasks.ReadAuthorizedTaskPageAsync(10, null, token);
        Assert.Equal(TaskObservationDisposition.Denied, page.Disposition); Assert.Empty(page.Tasks);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Task_observation_reopens_historical_own_task_after_authentication_renewal_without_renewing_command_admission()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var task = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "historical own task", TaskExecutionDurability.PersistedPlan, [], token);
        var originalActor = rig.Actors.Current!;
        rig.Actors.Current = originalActor with { AuthenticationRevision = "genuinely-current-renewed-session" };
        // A fresh actual authority over the SAME task store models process reopening;
        // no old private activation, provider or command lease is reconstructed.
        var authority = new TaskRunPermissionAuthority(rig.Actors, new ProviderRegistry(rig.Provider),
            new Configurations(), new Privacy(), new(new Permissions()), cloud: new ControlledCloudAdmission());
        var cold = new TaskExecutionCoordinator(rig.TaskRows, rig.Events, admissionAuthority: authority);
        var observed = await cold.ReadAuthorizedTaskObservationAsync(task.TaskId, token);
        Assert.Equal(TaskObservationDisposition.Authorized, observed.Disposition);
        Assert.Equal(task.TaskId, observed.Snapshot!.TaskId);
        Assert.Equal(task.OwnerBinding, observed.Snapshot.OwnerBinding);
        Assert.Equal(task.PersistenceRevision, observed.Snapshot.PersistenceRevision);
        Assert.False(observed.HasCurrentCommandIdentity);
        Assert.Single((await cold.ReadAuthorizedTaskPageAsync(10, null, token)).Tasks);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => cold.GetOriginalRunControlAvailabilityAsync(task.TaskId, task.ExecutionId, token));
        rig.Actors.Current = rig.Actors.Current with { ActorId = "different-actual-owner" };
        var denied = await cold.ReadAuthorizedTaskObservationAsync(task.TaskId, token);
        Assert.Equal(TaskObservationDisposition.Denied, denied.Disposition); Assert.Null(denied.Snapshot);
        Assert.Empty((await cold.ReadAuthorizedTaskPageAsync(10, null, token)).Tasks);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task All_tasks_source_issues_exact_historical_selection_and_preserves_null_and_external_context_origins()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        rig.Conversations.Current = rig.Conversation;
        var first = await rig.Tasks.BeginAuthorizedAsync(rig.Conversation.Id, Guid.NewGuid(), "historical task", TaskExecutionDurability.PersistedPlan, [], token);
        _ = await rig.Tasks.BeginAuthorizedAsync(rig.Conversation.Id, Guid.NewGuid(), "new task sharing the original context", TaskExecutionDurability.PersistedPlan, [], token);
        var external = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "external app task", TaskExecutionDurability.PersistedPlan, [], token);
        var settings = new ObservationReadOnlySettings();
        var registry = new SpaceRegistry(settings);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var page = await source.ReadAllTasksPageAsync(10, null, token);
        Assert.Equal(3, page.Tasks.Count);
        var historical = Assert.Single(page.Tasks, row => row.Snapshot.TaskId == first.TaskId);
        var selected = await source.ReadSelectionAsync(historical.Selection, token);
        Assert.Equal(first.TaskId, selected.Snapshot.TaskId);
        Assert.Equal(first.ExecutionId, selected.Snapshot.ExecutionId);
        Assert.Equal(rig.Conversation.Id, selected.ContextId);
        Assert.Equal(HavenMode.Studio, selected.OriginalConversation!.Mode);
        Assert.Null(selected.OriginSpaceId);
        var appTask = Assert.Single(page.Tasks, row => row.Snapshot.TaskId == external.TaskId);
        Assert.Null(appTask.OriginalConversation); Assert.Null(appTask.OriginSpaceId);
        Assert.Equal(external.ContextId, appTask.ContextId);
        Assert.Equal("external app task", appTask.Title);
        Assert.False(appTask.HasObservedLiveExecution);
        var other = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.ReadSelectionAsync(historical.Selection, token));
        var copied = (TaskContextSelection)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(historical.Selection, null)!;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadSelectionAsync(copied, token));
        rig.Conversations.Current = rig.Conversation with { SpaceId = SpaceRegistry.TasksSpaceId };
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadSelectionAsync(historical.Selection, token));
        Assert.Equal(0, rig.Conversations.Writes); Assert.Equal(0, settings.Reads);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    private sealed class ObservationReadOnlySettings : IVersionedSettingsStore
    {
        public int Reads;
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { token.ThrowIfCancellationRequested(); Reads++; return Task.FromResult<T?>(null); }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Task_observation_pages_actual_owned_tasks_by_exact_identity_without_changing_their_origin_or_latest_context()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var context = rig.Conversation.Id; // This actual conversation has no Space origin.
        var first = await rig.Tasks.BeginAuthorizedAsync(context, Guid.NewGuid(), "first original task", TaskExecutionDurability.PersistedPlan, [], token);
        var second = await rig.Tasks.BeginAuthorizedAsync(context, Guid.NewGuid(), "second task in the same context", TaskExecutionDurability.PersistedPlan, [], token);
        Assert.True(rig.Tasks.HasTaskObservationCensus);
        var page = await rig.Tasks.ReadAuthorizedTaskPageAsync(1, null, token);
        var selected = Assert.Single(page.Tasks);
        Assert.NotNull(page.Next);
        var rest = await rig.Tasks.ReadAuthorizedTaskPageAsync(1, page.Next, token);
        Assert.Single(rest.Tasks); Assert.Null(rest.Next);
        Assert.Equal(new[] { first.TaskId, second.TaskId }.OrderBy(id => id.ToString("D"), StringComparer.Ordinal),
            page.Tasks.Concat(rest.Tasks).Select(task => task.TaskId));
        var exact = await rig.Tasks.ReadAuthorizedTaskObservationAsync(first.TaskId, token);
        Assert.Equal(TaskObservationDisposition.Authorized, exact.Disposition);
        Assert.Equal(first.TaskId, exact.Snapshot!.TaskId);
        Assert.Equal(first.ExecutionId, exact.Snapshot.ExecutionId);
        Assert.Equal(context, exact.Snapshot.ContextId);
        Assert.Equal(first.OwnerBinding, exact.Snapshot.OwnerBinding);
        Assert.Null(rig.Conversation.SpaceId);
        Assert.Empty(rig.Stream); Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(first.PersistenceRevision, exact.Snapshot.PersistenceRevision);
    }

    [Fact]
    public async Task Task_observation_denies_a_foreign_actor_and_refuses_cross_actor_or_cross_issuer_cursor_reuse()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await using var other = new Rig();
        var first = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "owned task", TaskExecutionDurability.PersistedPlan, [], token);
        _ = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "another owned task", TaskExecutionDurability.PersistedPlan, [], token);
        var page = await rig.Tasks.ReadAuthorizedTaskPageAsync(1, null, token);
        Assert.NotNull(page.Next);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.Tasks.ReadAuthorizedTaskPageAsync(1, page.Next, token));
        var owner = rig.Actors.Current!;
        rig.Actors.Current = owner with { ProfileId = "another-actual-profile" };
        var denied = await rig.Tasks.ReadAuthorizedTaskObservationAsync(first.TaskId, token);
        Assert.Equal(TaskObservationDisposition.Denied, denied.Disposition); Assert.Null(denied.Snapshot);
        var foreignPage = await rig.Tasks.ReadAuthorizedTaskPageAsync(100, null, token);
        Assert.Empty(foreignPage.Tasks);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Tasks.ReadAuthorizedTaskPageAsync(1, page.Next, token));
        rig.Actors.Current = owner;
        Assert.Equal(first.TaskId, (await rig.Tasks.ReadAuthorizedTaskObservationAsync(first.TaskId, token)).Snapshot!.TaskId);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_observation_preserves_actual_actor_source_faults_instead_of_returning_an_empty_denied_page(bool single)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var task = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "actual task", TaskExecutionDurability.PersistedPlan, [], token);
        var canceledFault = new OperationCanceledException("actual actor source faulted OCE");
        var sibling = new IOException("actual actor storage read failure");
        var actual = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        actual.SetException([canceledFault, sibling]);
        rig.Actors.OriginalRead = actual.Task;
        try
        {
            Task read = single ? rig.Tasks.ReadAuthorizedTaskObservationAsync(task.TaskId, token)
                : rig.Tasks.ReadAuthorizedTaskPageAsync(10, null, token);
            var error = await Record.ExceptionAsync(() => read);
            Assert.NotNull(error); Assert.True(read.IsFaulted);
            Assert.Contains(Leaves(error), leaf => ReferenceEquals(leaf, canceledFault));
            Assert.Contains(Leaves(error), leaf => ReferenceEquals(leaf, sibling));
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally { rig.Actors.OriginalRead = null; }
    }
}
