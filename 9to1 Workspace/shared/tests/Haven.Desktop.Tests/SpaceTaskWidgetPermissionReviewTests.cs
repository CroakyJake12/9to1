using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Spaces.Tasks;
using System.Reflection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Conversation_permission_review_requires_a_fresh_matching_current_principal_observation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(tools: false);
        var originalTask = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        var spaces = new SpaceRegistry(new ObservationSettings());
        var space = await spaces.CreateAsync("Task permission authorization", cancellationToken: token);
        var conversation = rig.Conversation with
        {
            Mode = HavenMode.Tasks,
            Kind = ConversationKind.Task,
            SpaceId = space.Id,
            IsTemporary = false
        };
        rig.Conversations.Current = conversation;
        var source = new SpaceTaskWorkspaceService(spaces, rig.Conversations, rig.Tasks);

        // The task still carries its historical owner binding, but this is now a
        // different authenticated actor. That binding must not surface the request.
        rig.Actors.Current = rig.Actors.Current! with { ActorId = "different-current-actor" };
        var observation = await source.ReadAsync(space.Id, conversation.Id, token);

        Assert.Equal(originalTask.TaskId, observation.Snapshot!.TaskId);
        Assert.Equal(originalTask.ExecutionId, observation.Snapshot.ExecutionId);
        Assert.NotNull(observation.Snapshot.OwnerBinding);
        Assert.False(observation.HasCurrentCommandIdentity);
        Assert.Null(observation.PermissionReview);
        Assert.True(rig.Owner.CanRespond(request.Id)); // Still pending at its actual owner; this read disclosed no action.
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [AvaloniaFact]
    public async Task Space_task_widget_reviews_and_denies_same_actual_pending_permission_without_resuming()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(tools: false);
        var originalTask = rig.Service.CurrentCanonicalTask!;
        var originalActor = rig.Actors.Current!;
        var originalRequest = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        var spaces = new SpaceRegistry(new ObservationSettings());
        var space = await spaces.CreateAsync("Task permission review", cancellationToken: token);
        var conversation = rig.Conversation with
        {
            Mode = HavenMode.Tasks,
            Kind = ConversationKind.Task,
            SpaceId = space.Id,
            IsTemporary = false
        };
        rig.Conversations.Current = conversation;
        var source = new SpaceTaskWorkspaceService(spaces, rig.Conversations, rig.Tasks);
        var widget = new SpaceTaskWidgetPage(source, rig.Tasks, space.Id, conversation.Id,
            new PermissionReviewUnavailableReadiness(), expectedOriginalTaskId: originalTask.TaskId,
            expectedOriginalExecutionId: originalTask.ExecutionId);
        var window = new Window { Width = 960, Height = 720, Content = widget };
        Exception? responseFailure = null;
        try
        {
            window.Show();
            await widget.ActivateAsync(token);
            window.UpdateLayout();

            Assert.True(widget.TryGetValue("HasPermissionReview", out var hasReview));
            Assert.Equal(true, hasReview);
            Assert.True(widget.IsActionAvailable("task.review-permission"));
            await widget.DispatchAsync("task.review-permission", null, token);
            Assert.True(widget.TryGetValue("PermissionOpen", out var open)); Assert.Equal(true, open);
            Assert.True(widget.TryGetValue("PermissionTitle", out var title)); Assert.Equal(originalRequest.Title, title);
            Assert.True(widget.TryGetValue("PermissionQuestion", out var explanation));
            Assert.Equal(originalRequest.Explanation, explanation);
            Assert.True(widget.TryGetValue("PermissionProvider", out var provider)); Assert.Equal(originalRequest.ProviderName, provider);
            Assert.True(widget.TryGetValue("PermissionSensitivity", out var sensitivity));
            Assert.Contains(originalRequest.Sensitivity.ToString(), Assert.IsType<string>(sensitivity), StringComparison.Ordinal);
            Assert.True(widget.TryGetValue("PermissionAllowedActions", out var options));
            Assert.Contains(string.Join(", ", originalRequest.AllowedActions), Assert.IsType<string>(options), StringComparison.Ordinal);
            Assert.True(widget.TryGetValue("PermissionAction", out var action));
            Assert.Contains(originalRequest.Id.ToString("D"), Assert.IsType<string>(action), StringComparison.Ordinal);
            Assert.True(widget.IsActionAvailable("task.deny-permission"));

            // A live refresh after authentication changes must retire the already-open
            // review, not just prevent opening a new one.
            rig.Actors.Current = originalActor with { ActorId = "different-current-actor" };
            await widget.DispatchAsync("task.refresh", null, token);
            Assert.True(widget.TryGetValue("PermissionOpen", out open)); Assert.Equal(false, open);
            Assert.True(widget.TryGetValue("PermissionQuestion", out var retiredQuestion));
            Assert.NotEqual(originalRequest.Explanation, retiredQuestion);
            Assert.False(widget.IsActionAvailable("task.review-permission"));
            Assert.False(widget.IsActionAvailable("task.allow-permission"));
            Assert.False(widget.IsActionAvailable("task.deny-permission"));
            Assert.True(rig.Owner.CanRespond(originalRequest.Id));
            rig.Actors.Current = originalActor;
            await widget.DispatchAsync("task.refresh", null, token);
            await widget.DispatchAsync("task.review-permission", null, token);
            Assert.True(widget.TryGetValue("PermissionOpen", out open)); Assert.Equal(true, open);

            var currentObservation = await source.ReadAsync(space.Id, conversation.Id, token);
            Assert.True(currentObservation.HasCurrentCommandIdentity);
            Assert.Equal(originalTask.TaskId, currentObservation.Snapshot!.TaskId);
            Assert.Equal(originalTask.ExecutionId, currentObservation.Snapshot.ExecutionId);
            Assert.Equal(originalRequest.Id, currentObservation.PermissionReview!.Request.Id);
            var mismatched = currentObservation with
            { Snapshot = currentObservation.Snapshot! with { TaskId = Guid.NewGuid() } };
            typeof(SpaceTaskWidgetPage).GetMethod("ReconcilePermissionReview", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(widget, [mismatched]);
            Assert.True(widget.TryGetValue("PermissionOpen", out open)); Assert.Equal(false, open);
            Assert.False(widget.IsActionAvailable("task.allow-permission"));
            Assert.True(rig.Owner.CanRespond(originalRequest.Id));

            await widget.DispatchAsync("task.review-permission", null, token);
            Assert.True(widget.TryGetValue("PermissionOpen", out open)); Assert.Equal(true, open);

            // Force the post-decision view refresh to fail after the original owner
            // has published its real decision event. The actual ACK must survive.
            rig.Events.OnEvent = entry =>
            {
                if (entry.SafeMetadata?.GetValueOrDefault("permissionPhase") == "decision")
                    rig.Conversations.Current = null;
            };
            responseFailure = await Record.ExceptionAsync(() => widget.DispatchAsync("task.deny-permission", null, token).AsTask());
            Assert.NotNull(responseFailure);
            var response = Assert.IsType<TaskRunOriginalTaskPermissionResponse>(widget.AcknowledgedPermissionResponse);
            Assert.Equal(originalTask.TaskId, response.TaskId);
            Assert.Equal(originalTask.ExecutionId, response.ExecutionId);
            Assert.Equal(originalTask.ContextId, response.ContextId);
            Assert.Equal(originalRequest.Id, response.RequestId);
            Assert.Equal(PermissionDecisionKind.Denied, response.Decision.Kind);
            Assert.True(widget.TryGetValue("PermissionStatus", out var status));
            Assert.Contains("decision was recorded; the task view could not refresh", Assert.IsType<string>(status), StringComparison.OrdinalIgnoreCase);
            Assert.False(widget.IsActionAvailable("task.allow-permission"));
            Assert.False(widget.IsActionAvailable("task.deny-permission"));

            var current = await rig.Tasks.GetAsync(originalTask.TaskId, token);
            Assert.NotNull(current);
            Assert.Equal(originalTask.TaskId, current.TaskId);
            Assert.Equal(originalTask.ExecutionId, current.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
            Assert.Empty(current.Attempts);
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            widget.RequestRetirement();
            var closeFailure = await Record.ExceptionAsync(() => widget.CloseAndDrainAsync());
            if (responseFailure is null) Assert.Null(closeFailure);
            else Assert.NotNull(closeFailure);
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Space_task_permission_review_retires_when_exact_revision_changes_during_owner_read()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(tools: false);
        var originalTask = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        var spaces = new SpaceRegistry(new ObservationSettings());
        var space = await spaces.CreateAsync("Task permission revision race", cancellationToken: token);
        var conversation = rig.Conversation with
        {
            Mode = HavenMode.Tasks,
            Kind = ConversationKind.Task,
            SpaceId = space.Id,
            IsTemporary = false
        };
        rig.Conversations.Current = conversation;
        var source = new SpaceTaskWorkspaceService(spaces, rig.Conversations, rig.Tasks);
        var widget = new SpaceTaskWidgetPage(source, rig.Tasks, space.Id, conversation.Id,
            new PermissionReviewUnavailableReadiness(), expectedOriginalTaskId: originalTask.TaskId,
            expectedOriginalExecutionId: originalTask.ExecutionId);
        var window = new Window { Width = 960, Height = 720, Content = widget };
        var heldPermissionRead = new TaskCompletionSource<RemediationRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? reviewFailure = null;
        try
        {
            window.Show();
            await widget.ActivateAsync(token);
            rig.RemediationRows.OriginalRead = heldPermissionRead.Task;
            var review = widget.DispatchAsync("task.review-permission", null, token).AsTask();
            await rig.RemediationRows.OriginalReadEntered.Task.WaitAsync(token);
            Assert.True(widget.TryGetValue("PermissionOpen", out var opened)); Assert.Equal(true, opened);

            // Revise the actual canonical task while the fresh principal observation
            // for the review is blocked inside the real owner metadata read; this is a
            // real owner write, not a forged view or synthetic permission response.
            rig.RemediationRows.OriginalRead = null;
            var queued = await rig.Tasks.SubmitFollowUpAsync(originalTask.TaskId,
                "retain this queued work during review", TaskFollowUpMode.Queue, null, null, token);
            Assert.Equal(originalTask.TaskId, queued.Snapshot.TaskId);
            Assert.True(queued.Snapshot.PersistenceRevision > originalTask.PersistenceRevision);
            var actualRequest = Assert.IsType<RemediationRequest>(rig.RemediationRows.Rows.GetValueOrDefault(request.Id));
            heldPermissionRead.SetResult(actualRequest);
            reviewFailure = await Record.ExceptionAsync(() => review);

            Assert.NotNull(reviewFailure);
            Assert.True(widget.TryGetValue("PermissionOpen", out var open)); Assert.Equal(false, open);
            Assert.False(widget.IsActionAvailable("task.allow-permission"));
            Assert.False(widget.IsActionAvailable("task.deny-permission"));
            Assert.True(widget.TryGetValue("PermissionStatus", out var status));
            Assert.Contains("changed or could not be authorized", Assert.IsType<string>(status), StringComparison.OrdinalIgnoreCase);
            Assert.Null(widget.AcknowledgedPermissionResponse);
            Assert.True(rig.Owner.CanRespond(request.Id));
            var current = (await rig.Tasks.GetAsync(originalTask.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
            Assert.Contains(current.Queue, item => item.Summary == "retain this queued work during review");
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            heldPermissionRead.TrySetResult(rig.RemediationRows.Rows.GetValueOrDefault(request.Id));
            rig.RemediationRows.OriginalRead = null;
            widget.RequestRetirement();
            var closeFailure = await Record.ExceptionAsync(() => widget.CloseAndDrainAsync());
            if (reviewFailure is null) Assert.Null(closeFailure);
            else Assert.NotNull(closeFailure);
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, PermissionDecisionKind.Allowed)]
    [InlineData(false, PermissionDecisionKind.Denied)]
    public async Task Tasks_dashboard_retains_exact_permission_ack_when_real_page_refresh_fails(bool approve,
        PermissionDecisionKind expectedDecision)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        using var paths = new ObservationPaths();
        await rig.RunAsync(tools: false);
        var task = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        _ = await rig.Tasks.SubmitFollowUpAsync(task.TaskId, "preserve queued work", TaskFollowUpMode.Queue, null, null, token);
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        var preferences = new UserPreferencesService(paths);
        preferences.SetShownTasks(TaskShownScope.AllTasks);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var page = new SpaceTasksDashboardPage(source, rig.Tasks, SpaceRegistry.TasksSpaceId,
            _ => Task.CompletedTask, null, null, preferences, _ => Task.CompletedTask);
        var window = new Window { Width = 1000, Height = 800, Content = page };
        Exception? responseFailure = null;
        try
        {
            window.Show(); await page.ActivateAsync(token); window.UpdateLayout();
            var reviewButton = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "tasks-tile-review-permission");
            reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPermissionReview).WaitAsync(token);
            window.UpdateLayout();
            var responseButton = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == (approve ? "tasks-permission-allow" : "tasks-permission-deny"));

            // Let the same real owner publish and return its decision, then fail the
            // dashboard's subsequent canonical observation page.
            rig.Events.OnEvent = entry =>
            {
                if (entry.SafeMetadata?.GetValueOrDefault("permissionPhase") == "decision")
                    rig.Actors.OriginalRead = Task.FromException<AuthenticatedResourceActor?>(
                        new IOException("controlled current-principal refresh failure after real decision"));
            };
            responseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            responseFailure = await Record.ExceptionAsync(() => Assert.IsAssignableFrom<Task>(page.ActualPermissionResponse));

            Assert.NotNull(responseFailure);
            var ack = Assert.IsType<TaskRunOriginalTaskPermissionResponse>(page.AcknowledgedPermissionResponse);
            Assert.Equal(task.TaskId, ack.TaskId); Assert.Equal(task.ExecutionId, ack.ExecutionId);
            Assert.Equal(task.ContextId, ack.ContextId); Assert.Equal(request.Id, ack.RequestId);
            Assert.Equal(expectedDecision, ack.Decision.Kind);
            Assert.True(page.Scene.TryGetValue("Status", out var status));
            var message = Assert.IsType<string>(status);
            Assert.Contains("decision acknowledged", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("couldn’t refresh", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no task was resumed", message, StringComparison.OrdinalIgnoreCase);
            Assert.True(page.Scene.TryGetValue("ReviewOpen", out var open)); Assert.Equal(false, open);
            Assert.Empty(page.CurrentObservations);
            Assert.False(page.Scene.IsActionAvailable("tasks.allow-permission"));
            Assert.False(page.Scene.IsActionAvailable("tasks.deny-permission"));
            Assert.False(rig.Owner.CanRespond(request.Id));
            var current = (await rig.Tasks.GetAsync(task.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, current.State); Assert.Empty(current.Attempts);
            Assert.Equal("preserve queued work", Assert.Single(current.Queue).Summary);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            rig.Actors.OriginalRead = null;
            page.RequestRetirement();
            var closeFailure = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            if (responseFailure is null) Assert.Null(closeFailure);
            else Assert.NotNull(closeFailure);
            window.Content = null; window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Tasks_dashboard_without_a_matching_permission_ack_reports_unknown_and_retires_stale_selection()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        using var paths = new ObservationPaths();
        await rig.RunAsync(tools: false);
        var task = rig.Service.CurrentCanonicalTask!;
        var originalActor = rig.Actors.Current!;
        var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        var preferences = new UserPreferencesService(paths);
        preferences.SetShownTasks(TaskShownScope.AllTasks);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var page = new SpaceTasksDashboardPage(source, rig.Tasks, SpaceRegistry.TasksSpaceId,
            _ => Task.CompletedTask, null, null, preferences, _ => Task.CompletedTask);
        var window = new Window { Width = 1000, Height = 800, Content = page };
        Exception? responseFailure = null;
        try
        {
            window.Show(); await page.ActivateAsync(token); window.UpdateLayout();
            var reviewButton = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "tasks-tile-review-permission");
            reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPermissionReview).WaitAsync(token);
            window.UpdateLayout();

            // This is a fresh authorization refusal before the owner returns a decision.
            rig.Actors.Current = rig.Actors.Current! with { ActorId = "different-current-actor" };
            var deny = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "tasks-permission-deny");
            deny.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            responseFailure = await Record.ExceptionAsync(() => Assert.IsAssignableFrom<Task>(page.ActualPermissionResponse));

            Assert.NotNull(responseFailure);
            Assert.Null(page.AcknowledgedPermissionResponse);
            Assert.True(page.Scene.TryGetValue("Status", out var status));
            var message = Assert.IsType<string>(status);
            Assert.Contains("outcome is unknown", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no matching decision acknowledgment", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("acknowledged", message, StringComparison.OrdinalIgnoreCase);
            Assert.True(page.Scene.TryGetValue("ReviewOpen", out var open)); Assert.Equal(false, open);
            Assert.Empty(page.CurrentObservations);
            Assert.False(page.Scene.IsActionAvailable("tasks.allow-permission"));
            Assert.False(page.Scene.IsActionAvailable("tasks.deny-permission"));
            Assert.True(rig.Owner.CanRespond(request.Id));
            var current = (await rig.Tasks.GetAsync(task.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, current.State); Assert.Empty(current.Attempts);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            rig.Actors.Current = originalActor;
            page.RequestRetirement();
            var closeFailure = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            if (responseFailure is null) Assert.Null(closeFailure);
            else Assert.NotNull(closeFailure);
            window.Content = null; window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Tasks_dashboard_successful_refresh_retires_open_review_when_current_command_identity_changes()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        using var paths = new ObservationPaths();
        await rig.RunAsync(tools: false);
        var task = rig.Service.CurrentCanonicalTask!;
        var originalActor = rig.Actors.Current!;
        var request = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        var registry = new SpaceRegistry(new ObservationSettings());
        _ = await registry.GetAllAsync(cancellationToken: token);
        var preferences = new UserPreferencesService(paths);
        preferences.SetShownTasks(TaskShownScope.AllTasks);
        var source = new SpaceTaskWorkspaceService(registry, rig.Conversations, rig.Tasks);
        var page = new SpaceTasksDashboardPage(source, rig.Tasks, SpaceRegistry.TasksSpaceId,
            _ => Task.CompletedTask, null, null, preferences, _ => Task.CompletedTask);
        var window = new Window { Width = 1000, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(token); window.UpdateLayout();
            var reviewButton = ObservationAuthoredControls(page.Scene.Root!).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "tasks-tile-review-permission");
            reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.Scene.WhenActionsIdleAsync();
            await Assert.IsAssignableFrom<Task>(page.ActualPermissionReview).WaitAsync(token);
            Assert.True(page.Scene.TryGetValue("ReviewOpen", out var initiallyOpen)); Assert.Equal(true, initiallyOpen);
            Assert.True(page.Scene.TryGetValue("ReviewExplanation", out var initialExplanation));
            Assert.Equal(request.Explanation, initialExplanation);
            Assert.True(page.Scene.IsActionAvailable("tasks.deny-permission"));

            // Renew authentication for the same principal. The canonical page read
            // succeeds for historical observation but no longer proves current command
            // identity; matching TaskID/RunID and saved owner binding are insufficient.
            rig.Actors.Current = originalActor with { AuthenticationRevision = "genuine-renewed-session" };
            await page.RefreshOriginalAsync(token);

            var refreshed = Assert.Single(page.CurrentObservations);
            Assert.Equal(task.TaskId, refreshed.Snapshot!.TaskId);
            Assert.Equal(task.ExecutionId, refreshed.Snapshot.ExecutionId);
            Assert.False(refreshed.HasCurrentCommandIdentity);
            Assert.Null(refreshed.PermissionReview);
            Assert.True(page.Scene.TryGetValue("ReviewOpen", out var open)); Assert.Equal(false, open);
            Assert.True(page.Scene.TryGetValue("ReviewExplanation", out var retiredExplanation));
            Assert.NotEqual(request.Explanation, retiredExplanation);
            Assert.False(page.Scene.IsActionAvailable("tasks.allow-permission"));
            Assert.False(page.Scene.IsActionAvailable("tasks.deny-permission"));
            Assert.True(page.Scene.TryGetValue("CanRespondReview", out var canRespond)); Assert.Equal(false, canRespond);
            Assert.True(rig.Owner.CanRespond(request.Id));
            var current = (await rig.Tasks.GetAsync(task.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
            Assert.Empty(current.Attempts);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            rig.Actors.Current = originalActor;
            page.RequestRetirement();
            await page.CloseAndDrainAsync();
            window.Content = null; window.Close();
        }
    }

    private sealed class PermissionReviewUnavailableReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                "NO_ACTUAL_NATIVE_FRAME", "This review test exercises owner authorization, not native readiness."));
    }
}
