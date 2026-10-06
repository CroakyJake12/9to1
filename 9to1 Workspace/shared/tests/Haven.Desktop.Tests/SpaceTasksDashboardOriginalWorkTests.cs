using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Tasks;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

/// <summary>Actual dashboard/scene controls with controlled repository/open originals.
/// These do not issue an actor, permission, live attempt or business-completion receipt.</summary>
public sealed partial class SpaceTasksDashboardOriginalWorkTests
{
    [AvaloniaFact]
    public async Task Existing_dashboard_opens_same_task_run_and_keeps_saved_running_as_observation()
    {
        await using var rig = await Rig.CreateAsync();
        SpaceTaskObservation? opened = null;
        var openEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Mount(row => { opened = row; openEntered.TrySetResult(); return Task.CompletedTask; });
        await rig.Page!.ActivateAsync(CancellationToken.None);
        var actual = Assert.Single(rig.Page.OriginalObservations);
        Assert.Equal(rig.Snapshot.TaskId, actual.Snapshot!.TaskId);
        Assert.Equal(rig.Snapshot.ExecutionId, actual.Snapshot.ExecutionId);
        Assert.Equal(rig.Conversation.Id, actual.Snapshot.ContextId);
        var text = string.Join("\n", rig.Page.OriginalScene.RecentRows.DescendantsAndSelf().OfType<HavenText>().Select(x => x.Content));
        Assert.Contains(rig.Snapshot.TaskId.ToString("D"), text);
        Assert.Contains(rig.Snapshot.ExecutionId.ToString("D"), text);
        Assert.Contains("Saved task", text);
        Assert.Contains("No provider attempt is recorded", text);
        Assert.DoesNotContain("is executing", text);
        Click(rig, rig.Page.OriginalScene.RecentRows.DescendantsAndSelf().OfType<HavenButton>().Single());
        await openEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); // Actual callback entry, not elapsed/idle success.
        await rig.Page.RefreshOriginalAsync();
        Assert.NotNull(opened);
        Assert.Equal(actual.Snapshot.TaskId, opened!.Snapshot!.TaskId);
        Assert.Equal(actual.Snapshot.ExecutionId, opened.Snapshot.ExecutionId);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Held_actual_repository_read_is_joined_before_renderer_detach_after_seal()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Tasks.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Mount(_ => Task.CompletedTask);
        var activate = rig.Page!.ActivateAsync(CancellationToken.None);
        await rig.Tasks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var root = rig.Page.Scene.Root;
        rig.Page.RequestRetirement();
        var close = rig.Page.CloseAndDrainAsync();
        Assert.Same(close, rig.Page.CloseAndDrainAsync());
        Assert.False(close.IsCompleted);
        Assert.False(rig.Tasks.Held.Task.IsCompleted);
        Assert.Same(root, rig.Page.Scene.Root);
        rig.Tasks.Held.SetResult(rig.Snapshot);
        _ = await ObserveAsync(activate);
        rig.RecordClose(close);
        Assert.NotNull(await ObserveAsync(close));
        Assert.True(activate.IsCompleted);
        Assert.True(rig.Tasks.Held.Task.IsCompletedSuccessfully);
        Assert.Null(rig.Page.Scene.Root);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Whole_explicit_new_work_callback_and_direct_fault_siblings_survive_view_close()
    {
        await using var rig = await Rig.CreateAsync();
        var body = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Mount(_ => Task.CompletedTask, _ => { entered.TrySetResult(); return body.Task; }, () => Task.CompletedTask);
        await rig.Page!.ActivateAsync(CancellationToken.None);
        rig.Page.OriginalScene.Instruction.Text = "Explicit new work in the actual owner";
        Click(rig, rig.Page.OriginalScene.DelegateTask);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var root = rig.Page.Scene.Root;
        rig.Page.RequestRetirement();
        var close = rig.Page.CloseAndDrainAsync();
        Assert.False(body.Task.IsCompleted);
        Assert.False(close.IsCompleted);
        Assert.Same(root, rig.Page.Scene.Root);
        var first = new IOException("controlled original body fault");
        var second = new OperationCanceledException("controlled faulted OCE, not synthetic cancellation");
        body.SetException([first, second]);
        rig.RecordClose(close);
        Assert.NotNull(await ObserveAsync(close));
        Assert.True(body.Task.IsFaulted);
        Assert.Contains(body.Task.Exception!.InnerExceptions, x => ReferenceEquals(first, x));
        Assert.Contains(body.Task.Exception.InnerExceptions, x => ReferenceEquals(second, x));
        Assert.True(ContainsSameCause(close.Exception!, first));
        Assert.True(ContainsSameCause(close.Exception!, second));
        Assert.Null(rig.Page.Scene.Root);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Restored_pre_page_context_source_callback_cannot_join_its_actual_parent_close()
    {
        var beforePage = ExecutionContext.Capture()!;
        await using var rig = await Rig.CreateAsync();
        Exception? refusal = null;
        var sourceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Mount(_ => Task.CompletedTask, _ =>
        {
            ExecutionContext.Run(beforePage, _ =>
            {
                rig.Page!.RequestRetirement();
                refusal = Record.Exception(() => rig.Page.CloseAndDrainAsync().GetAwaiter().GetResult());
            }, null);
            sourceEntered.TrySetResult();
            return Task.CompletedTask;
        }, () => Task.CompletedTask);
        await rig.Page!.ActivateAsync(CancellationToken.None);
        rig.Page.OriginalScene.Instruction.Text = "Actual source factory callback";
        Click(rig, rig.Page.OriginalScene.DelegateTask);
        await sourceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.Contains("source callback", refusal!.Message);
        var close = rig.Page.CloseAndDrainAsync(); rig.RecordClose(close);
        Assert.NotNull(await ObserveAsync(close));
        Assert.True(close.IsFaulted);
        Assert.Null(rig.Page.Scene.Root);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [AvaloniaFact]
    public void Guarded_recent_replace_rechecks_after_actual_removal_callback_and_preserves_legacy_null_hook()
    {
        using var scene = new TasksSpaceHavenScene();
        var oldId = Guid.NewGuid(); var newId = Guid.NewGuid();
        scene.SetRecent([new(oldId, "Original", "Original subtitle")]);
        var live = true;
        EventHandler retire = (_, _) => live = false;
        scene.RecentRows.Invalidated += retire;
        try
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => scene.SetRecent([new(newId, "Replacement", "Replacement subtitle")],
                () => { if (!live) throw new InvalidOperationException("actual source withdrawal"); }));
            Assert.Equal("actual source withdrawal", refusal.Message);
            Assert.Empty(scene.RecentRows.Children);
            Assert.DoesNotContain(scene.Root.DescendantsAndSelf().OfType<HavenButton>(), x => x.Content == "Replacement");
        }
        finally { scene.RecentRows.Invalidated -= retire; }
        scene.SetRecent([new(newId, "Legacy null guard", "Same original domain")]);
        Assert.Single(scene.RecentRows.Children);
        Assert.Contains(scene.Root.DescendantsAndSelf().OfType<HavenText>(), x => x.Content == "Same original domain");
        Assert.Contains(scene.Root.DescendantsAndSelf().OfType<HavenText>(), x => x.Content.Contains("Automations", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Existing_widget_refuses_replaced_task_run_before_actual_readiness_or_native_publication()
    {
        await using var rig = await Rig.CreateAsync();
        var readiness = new UnavailableReadiness();
        var widget = new SpaceTaskWidgetPage(rig.Service, rig.Canonical, rig.Space.Id, rig.Conversation.Id,
            readiness, expectedOriginalTaskId: Guid.NewGuid(), expectedOriginalExecutionId: rig.Snapshot.ExecutionId);
        Task? close = null;
        try
        {
            var activate = widget.ActivateAsync(CancellationToken.None);
            Assert.NotNull(await ObserveAsync(activate));
            Assert.True(activate.IsFaulted);
            var actualRefusal = FindCause<InvalidOperationException>(activate.Exception!,
                cause => cause.Message.Contains("different Task/Run", StringComparison.Ordinal));
            Assert.NotNull(actualRefusal);
            Assert.Equal(0, readiness.Calls);
            Assert.Null(widget.OriginalHost.Content);
            Assert.Equal(0, rig.Tasks.Writes);
            close = widget.CloseAndDrainAsync();
            Assert.NotNull(await ObserveAsync(close));
            Assert.True(ContainsSameCause(close.Exception!, actualRefusal!));
        }
        finally
        {
            widget.RequestRetirement();
            var originalClose = widget.CloseAndDrainAsync();
            var fault = await ObserveAsync(originalClose);
            if (!ReferenceEquals(originalClose, close) && fault is not null) throw fault;
        }
    }
    [AvaloniaFact]
    public async Task Post_await_actual_repository_factory_restored_context_cannot_join_same_page_close()
    {
        var beforePage = ExecutionContext.Capture()!;
        await using var rig = await Rig.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        object? capturedState = null;
        var reads = 0;
        rig.Settings.ReadOverride = (_, state) =>
        {
            if (Interlocked.Increment(ref reads) != 1) return Task.FromResult(state);
            capturedState = state; entered.TrySetResult(); return held.Task;
        };
        Exception? refusal = null;
        rig.ConversationRows.BeforeRead = () => ExecutionContext.Run(beforePage, _ =>
        {
            rig.Page!.RequestRetirement();
            refusal = Record.Exception(() => rig.Page.CloseAndDrainAsync().GetAwaiter().GetResult());
        }, null);
        rig.Mount(_ => Task.CompletedTask);
        var activate = rig.Page!.ActivateAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(activate.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            held.TrySetResult(capturedState);
            Assert.NotNull(await ObserveAsync(activate));
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains("source callback", refusal!.Message);
            var close = rig.Page.CloseAndDrainAsync(); rig.RecordClose(close);
            Assert.NotNull(await ObserveAsync(close));
            Assert.True(activate.IsCompleted);
            Assert.True(held.Task.IsCompletedSuccessfully);
            Assert.Null(rig.Page.Scene.Root);
            Assert.Equal(0, rig.Tasks.Writes);
        }
        finally
        {
            held.TrySetResult(capturedState);
            _ = await ObserveAsync(activate);
        }
    }

    [Fact]
    public async Task Synchronous_original_repository_OCE_is_faulted_and_preserves_the_exact_cause()
    {
        await using var rig = await Rig.CreateAsync();
        var cause = new OperationCanceledException("Actual synchronous repository factory, not a canceled Task");
        rig.ConversationRows.BeforeRead = () => throw cause;
        var actual = rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(await ObserveAsync(actual));
        Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled);
        Assert.True(ContainsSameCause(actual.Exception!, cause));
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [Fact]
    public async Task Source_scope_failure_after_real_raw_acquisition_independently_joins_all_direct_siblings()
    {
        await using var rig = await Rig.CreateAsync();
        var raw = new TaskCompletionSource<Conversation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.ConversationRows.NextRead = raw.Task;
        var acquisition = new OperationCanceledException("Exact caller scope failure after raw acquisition");
        var first = new IOException("Exact original raw repository fault");
        var second = new UnauthorizedAccessException("Exact original raw repository sibling");
        var factories = 0;
        var actual = rig.Service.ReadAsync(rig.Space.Id, rig.Conversation.Id, TestContext.Current.CancellationToken, callback =>
        {
            callback();
            if (Interlocked.Increment(ref factories) == 2) throw acquisition;
        });
        try
        {
            Assert.Equal(2, factories);
            Assert.False(actual.IsCompleted);
            Assert.False(raw.Task.IsCompleted);
            raw.TrySetException([first, second]);
            Assert.NotNull(await ObserveAsync(actual));
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            Assert.True(ContainsSameCause(actual.Exception!, acquisition));
            Assert.True(ContainsSameCause(actual.Exception!, first));
            Assert.True(ContainsSameCause(actual.Exception!, second));
            Assert.Equal(0, rig.Tasks.Writes);
        }
        finally { raw.TrySetException([first, second]); _ = await ObserveAsync(actual); }
    }

    [AvaloniaFact]
    public async Task Acknowledged_replacement_pair_withdraws_held_original_readiness_before_any_publication()
    {
        await using var rig = await Rig.CreateAsync();
        var readiness = new HeldUnavailableReadiness();
        Window? window = null; SpaceTaskWidgetPage? widget = null; Task? activate = null; Task? inspectedClose = null;
        try
        {
            window = new Window { Width = 960, Height = 720 };
            widget = new(rig.Service, rig.Canonical, rig.Space.Id, rig.Conversation.Id, readiness,
                expectedOriginalTaskId: rig.Snapshot.TaskId, expectedOriginalExecutionId: rig.Snapshot.ExecutionId);
            window.Content = widget; window.Show();
            activate = widget.ActivateAsync(CancellationToken.None);
            await readiness.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(activate.IsCompleted);
            Assert.Null(widget.OriginalHost.Content);
            var replacement = await rig.Canonical.BeginAsync(rig.Conversation.Id, Guid.NewGuid(),
                "Actual acknowledged replacement metadata", TaskExecutionDurability.PersistedPlan, [], TestContext.Current.CancellationToken);
            Assert.NotEqual(rig.Snapshot.TaskId, replacement.TaskId);
            Assert.NotEqual(rig.Snapshot.ExecutionId, replacement.ExecutionId);
            Assert.Equal(rig.Conversation.Id, replacement.ContextId);
            Assert.False(widget.IsActionAvailable("task.refresh"));
            var close = widget.CloseAndDrainAsync(); inspectedClose = close;
            Assert.False(close.IsCompleted);
            Assert.Null(widget.OriginalHost.Content);
            readiness.Release();
            Assert.NotNull(await ObserveAsync(activate));
            Assert.NotNull(await ObserveAsync(close));
            Assert.True(activate.IsCompleted);
            Assert.False(activate.IsCompletedSuccessfully);
            Assert.True(close.IsCompleted);
            Assert.False(close.IsCompletedSuccessfully);
            Assert.Null(widget.OriginalHost.Content);
            // Withdrawal prevents publication. A failed host close does not acknowledge
            // resource detachment, so the SAME host remains in the failed owner cohort.
            Assert.Same(widget.OriginalHost, widget.Content);
            Assert.Equal(replacement.TaskId, rig.Tasks.Current!.TaskId);
            Assert.Equal(replacement.ExecutionId, rig.Tasks.Current.ExecutionId);
            Assert.Equal(1, rig.Tasks.Writes);
        }
        finally
        {
            readiness.Release();
            var failures = new List<Exception>();
            if (activate is not null) _ = await ObserveAsync(activate);
            if (widget is not null)
            {
                try { widget.RequestRetirement(); var close = widget.CloseAndDrainAsync();
                    var fault = await ObserveAsync(close); if (fault is not null && !ReferenceEquals(close, inspectedClose)) failures.Add(fault); }
                catch (Exception error) { failures.Add(error); }
            }
            if (window is not null)
            {
                try { window.Content = null; } catch (Exception error) { failures.Add(error); }
                try { window.Close(); } catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException(failures);
        }
    }

    [Fact]
    public async Task Actual_dispatcher_close_join_retains_faulted_OCE_and_all_direct_siblings()
    {
        var first = new OperationCanceledException("Original faulted dispatcher cause");
        var second = new IOException("Original direct dispatcher sibling");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = SpaceTaskWidgetPage.JoinOriginalDispatcherCloseAsync(raw.Task);
        Assert.False(joined.IsCompleted);
        raw.SetException([first, second]);
        Assert.NotNull(await ObserveAsync(joined));
        Assert.True(raw.Task.IsFaulted);
        Assert.True(joined.IsFaulted);
        Assert.False(joined.IsCanceled);
        Assert.True(ContainsSameCause(joined.Exception!, first));
        Assert.True(ContainsSameCause(joined.Exception!, second));
    }

    [AvaloniaFact]
    public async Task Actual_native_content_detach_restored_callback_cannot_join_same_widget_close()
    {
        var beforePage = ExecutionContext.Capture()!;
        await using var rig = await Rig.CreateAsync();
        var widget = new SpaceTaskWidgetPage(rig.Service, rig.Canonical, rig.Space.Id, rig.Conversation.Id,
            new UnavailableReadiness());
        Exception? refusal = null;
        EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs> callback = (_, args) =>
        {
            if (args.Property != ContentControl.ContentProperty || args.NewValue is not null) return;
            ExecutionContext.Run(beforePage, _ =>
                refusal = Record.Exception(() => widget.CloseAndDrainAsync().GetAwaiter().GetResult()), null);
        };
        ((Avalonia.AvaloniaObject)widget).PropertyChanged += callback;
        try
        {
            var close = widget.CloseAndDrainAsync();
            await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains("source callback", refusal!.Message);
            Assert.Null(widget.Content);
            Assert.Same(close, widget.CloseAndDrainAsync());
        }
        finally
        {
            ((Avalonia.AvaloniaObject)widget).PropertyChanged -= callback;
            widget.RequestRetirement(); await widget.CloseAndDrainAsync();
        }
    }

    private sealed class HeldUnavailableReadiness : ICuiSceneReadiness
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CuiSceneAvailability> _raw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { Entered.TrySetResult(); return new(_raw.Task); }
        internal void Release() => _raw.TrySetResult(new(CuiSceneAvailabilityState.Unavailable,
            "NO_ACTUAL_NATIVE_FRAME", "A held negative read observation; this control cannot issue readiness."));
    }
    private sealed class UnavailableReadiness : ICuiSceneReadiness
    {
        internal int Calls;
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { Calls++; return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
            "NO_ACTUAL_NATIVE_FRAME", "This negative control has no native readiness producer.")); }
    }
    private static T? FindCause<T>(Exception actual, Func<T, bool> predicate) where T : Exception
    {
        if (actual is T typed && predicate(typed)) return typed;
        if (actual is AggregateException group)
            foreach (var child in group.InnerExceptions)
                if (FindCause(child, predicate) is { } found) return found;
        return null;
    }

    private static void Click(Rig rig, HavenElement element)
    {
        rig.Window!.UpdateLayout();
        var router = new HavenInputRouter(rig.Page!.OriginalScene.Root);
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
    private static async Task<Exception?> ObserveAsync(Task actual)
    { try { await actual.WaitAsync(TimeSpan.FromSeconds(5)); return null; } catch (Exception error) { return actual.Exception ?? error; } }
    private static bool ContainsSameCause(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException aggregate && aggregate.InnerExceptions.Any(cause => ContainsSameCause(cause, expected));

    private sealed class Rig : IAsyncDisposable
    {
        internal SpaceDefinition Space = null!;
        internal Conversation Conversation = null!;
        internal TaskExecutionSnapshot Snapshot = null!;
        internal ControlledTasks Tasks = new();
        internal TaskExecutionCoordinator Canonical = null!;
        internal SpaceTaskWorkspaceService Service = null!;
        internal Settings Settings = new();
        internal Conversations ConversationRows = null!;
        internal SpaceTasksDashboardPage? Page;
        internal Window? Window;
        private Task? _observedClose;
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig(); var spaces = new SpaceRegistry(rig.Settings);
            rig.Space = await spaces.CreateAsync("Actual dashboard control");
            var now = DateTimeOffset.UtcNow;
            rig.Conversation = new(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task, "Original context", null, null,
                false, false, now, now, SpaceId: rig.Space.Id);
            rig.Snapshot = new(Guid.NewGuid(), rig.Conversation.Id, Guid.NewGuid(), "Original objective",
                TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 0, [], [], [], [], null, now, now)
                { PersistenceRevision = 2 };
            rig.Tasks.Current = rig.Snapshot;
            rig.Canonical = new(rig.Tasks, new Events());
            rig.ConversationRows = new(rig.Conversation);
            rig.Service = new(spaces, rig.ConversationRows, rig.Canonical);
            return rig;
        }
        internal void Mount(Func<SpaceTaskObservation, Task> open, Func<string, Task>? start = null, Func<Task>? blank = null)
        {
            Window = new Window { Width = 960, Height = 720 }; // Retained before later page/configuration acquisitions.
            Page = new(Service, Canonical, Space.Id, open, start, blank);
            Window.Content = Page;
            Window.Show(); Window.UpdateLayout();
        }
        internal void RecordClose(Task actual) => _observedClose = actual;
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            Tasks.Held?.TrySetResult(Snapshot);
            if (Page is { } page)
            {
                try { page.RequestRetirement(); } catch (Exception error) { failures.Add(error); }
                try
                {
                    var actual = page.CloseAndDrainAsync();
                    var fault = await ObserveAsync(actual);
                    if (fault is not null && !ReferenceEquals(actual, _observedClose)) failures.Add(fault);
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (Window is { } window)
            {
                try { window.Content = null; } catch (Exception error) { failures.Add(error); }
                try { window.Close(); } catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException(failures);
        }
    }
    private sealed class ControlledTasks : ITaskExecutionRepository
    {
        internal TaskExecutionSnapshot? Current;
        internal TaskCompletionSource<TaskExecutionSnapshot?>? Held;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Writes;
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token)
        { Entered.TrySetResult(); return Held?.Task ?? Task.FromResult(Current?.ContextId == id ? Current : null); }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Current?.TaskId == id ? Current : null);
        public Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token) { Writes++; Current = value; return Task.CompletedTask; }
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Conversations(Conversation row) : IConversationRepository
    {
        internal Action? BeforeRead;
        internal Task<Conversation?>? NextRead;
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token)
        { BeforeRead?.Invoke(); return NextRead ?? Task.FromResult<Conversation?>(row.Id == id ? row : null); }
        public Task<IReadOnlyList<Conversation>> GetBySpaceAsync(Guid id, int limit, CancellationToken token)
        { BeforeRead?.Invoke(); return Task.FromResult<IReadOnlyList<Conversation>>([row]); }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) => throw new NotSupportedException();
        public Task AddMessageAsync(ChatMessage value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Events : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed class Settings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        internal Func<string, object?, Task<object?>>? ReadOverride;
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { var value = _values.GetValueOrDefault(key); return ReadOverride is null ? value as T : await ReadOverride(key, value) as T; }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }
}
