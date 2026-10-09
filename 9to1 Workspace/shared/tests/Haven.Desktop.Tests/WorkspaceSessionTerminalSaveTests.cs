using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

// Uses the actual coordinator, UI dispatcher and workspace DTOs. Only repository
// scheduling is controlled; these cases supply no Home actor/provider/authority.
// Source proposal only: not compiled or executed in the authoring workspace.
public sealed class WorkspaceSessionTerminalSaveTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [AvaloniaFact]
    public Task Final_capture_precedes_reentrant_unregister_and_detaches_mutable_popup_values() =>
        RunCaseAsync(async scope =>
        {
            var model = scope.RegisterPopUp();
            var callbackRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? callbackClose = null;
            scope.Repository.OnSave = write =>
            {
                if (write.Index != 0) { write.Complete(); return; }
                scope.Own(write.Token.Register(() =>
                {
                    callbackClose = scope.Coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
                    scope.Coordinator.UnregisterPopUp(model.WindowId);
                    model.OrderedTabIds.Clear();
                    model.Panes.Clear();
                    scope.Coordinator.QueueSave();
                    callbackRan.SetResult();
                }));
            };
            var older = await scope.Repository.NextAsync();
            var final = scope.Seal();
            await callbackRan.Task.WaitAsync(Bound);
            Assert.Same(final, callbackClose);
            Assert.False(final.IsCompleted);
            Assert.Single(scope.Repository.Writes);
            Assert.Same(final, scope.Coordinator.SaveNowAsync(CancellationToken.None));
            Assert.Same(final, scope.Coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None));
            older.Complete();
            await final.WaitAsync(Bound);
            var saved = Assert.Single(scope.Repository.Writes, write => write.Index == 1).Snapshot;
            Assert.Equal(model.Tab.Id, Assert.Single(saved.Tabs).Id);
            var window = Assert.Single(saved.Windows);
            Assert.Equal(model.WindowId, window.Id);
            Assert.Equal(model.Tab.Id, Assert.Single(window.OrderedTabIds));
            Assert.Equal(model.Tab.Id, Assert.Single(window.Layout.Panes).TabId);
            Assert.Same(final, scope.Coordinator.SaveNowAsync(CancellationToken.None));
            scope.Coordinator.UnregisterPopUp(model.WindowId);
            scope.Coordinator.QueueSave();
            await Task.Delay(450);
            Assert.Equal(2, scope.Repository.Writes.Count);
        });

    [AvaloniaFact]
    public Task A_held_direct_explicit_write_cannot_overtake_the_captured_final_snapshot() =>
        RunCaseAsync(async scope =>
        {
            var model = scope.RegisterPopUp();
            scope.Repository.OnSave = write => { if (write.Index != 0) write.Complete(); };
            var explicitSave = scope.Track(scope.Coordinator.SaveNowAsync(CancellationToken.None));
            var held = await scope.Repository.NextAsync();
            var final = scope.Seal();
            scope.Coordinator.UnregisterPopUp(model.WindowId);
            var refused = scope.Coordinator.SaveNowAsync(CancellationToken.None);
            Assert.Same(final, refused);
            Assert.False(explicitSave.IsCompleted);
            Assert.False(final.IsCompleted);
            Assert.Single(scope.Repository.Writes);
            held.Complete();
            await explicitSave.WaitAsync(Bound);
            await final.WaitAsync(Bound);
            Assert.Equal(2, scope.Repository.Writes.Count);
            Assert.Equal(model.WindowId, Assert.Single(scope.Repository.Writes[1].Snapshot.Windows).Id);
            Assert.Same(final, scope.Coordinator.SaveNowAsync(CancellationToken.None));
            await Task.Delay(450);
            Assert.Equal(2, scope.Repository.Writes.Count);
        });

    [AvaloniaFact]
    public Task Cancel_pending_explicit_save_is_retained_through_real_window_Closed_unregister() =>
        RunCaseAsync(async scope =>
        {
            var window = scope.AcquireWindow();
            var model = scope.RegisterPopUp(window);
            window.Closed += (_, _) => scope.Coordinator.UnregisterPopUp(model.WindowId);
            window.Content = new ContentControl();
            window.Show();
            scope.Repository.OnSave = write => { if (write.Index != 0) write.Complete(); };
            var explicitSave = scope.Track(scope.Coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None));
            var held = await scope.Repository.NextAsync();
            var final = scope.Seal();
            window.Close();
            Assert.False(explicitSave.IsCompleted);
            Assert.False(final.IsCompleted);
            Assert.Single(scope.Repository.Writes);
            held.Complete();
            await explicitSave.WaitAsync(Bound);
            await final.WaitAsync(Bound);
            Assert.Equal(model.WindowId, Assert.Single(scope.Repository.Writes[1].Snapshot.Windows).Id);
            Assert.Equal(model.Tab.Id, Assert.Single(scope.Repository.Writes[1].Snapshot.Tabs).Id);
            Assert.Equal(2, scope.Repository.Writes.Count);
        });

    [AvaloniaFact]
    public Task Snapshot_delegate_reentry_observes_the_published_final_task_and_admits_no_save() =>
        RunCaseAsync(async scope =>
        {
            Task? reenteredSave = null;
            Task? reenteredFinal = null;
            scope.RegisterPopUp(onTabSnapshot: () =>
            {
                reenteredSave = scope.Coordinator.SaveNowAsync(CancellationToken.None);
                reenteredFinal = scope.Coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
            });
            scope.Repository.OnSave = write => write.Complete();
            var final = scope.Seal();
            Assert.Same(final, reenteredSave);
            Assert.Same(final, reenteredFinal);
            await final.WaitAsync(Bound);
            Assert.Single(scope.Repository.Writes);
        });

    [AvaloniaFact]
    public Task Compound_repository_and_callback_causes_survive_a_faulting_final_write_exactly() =>
        RunCaseAsync(async scope =>
        {
            var first = new IOException("Original first cause.");
            var second = new IOException("Original second cause.");
            var empty = new AggregateException();
            var callbackCause = new InvalidOperationException("Original cancellation callback cause.");
            var finalCause = new IOException("Original final write cause.");
            scope.RegisterPopUp();
            scope.Repository.OnSave = write =>
            {
                if (write.Index == 0)
                    scope.Own(write.Token.Register(() => throw callbackCause));
                else write.Fail(finalCause);
            };
            var held = await scope.Repository.NextAsync();
            var final = scope.Seal();
            held.Fail(first, second, empty);
            var fault = await Assert.ThrowsAsync<AggregateException>(() => final);
            Assert.Same(first, Assert.Single(fault.InnerExceptions, cause => ReferenceEquals(cause, first)));
            Assert.Same(second, Assert.Single(fault.InnerExceptions, cause => ReferenceEquals(cause, second)));
            Assert.Same(empty, Assert.Single(fault.InnerExceptions, cause => ReferenceEquals(cause, empty)));
            Assert.Same(finalCause, Assert.Single(fault.InnerExceptions, cause => ReferenceEquals(cause, finalCause)));
            var callbackGroup = Assert.IsType<AggregateException>(Assert.Single(fault.InnerExceptions,
                cause => cause is AggregateException group &&
                    group.InnerExceptions.Any(item => ReferenceEquals(item, callbackCause))));
            Assert.Same(callbackCause, Assert.Single(callbackGroup.InnerExceptions));
            Assert.Equal(5, fault.InnerExceptions.Count);
            Assert.Same(final, scope.Coordinator.SaveNowAsync(CancellationToken.None));
            scope.Coordinator.QueueSave();
            Assert.Equal(2, scope.Repository.Writes.Count);
            scope.ExpectObservedFault(final);
            foreach (var write in scope.Repository.Writes) scope.ExpectObservedFault(write.OriginalTask);
        });

    [AvaloniaFact]
    public Task Concurrent_held_cancellation_callback_is_joined_before_final_write_or_resource_disposal() =>
        RunCaseAsync(async scope =>
        {
            scope.RegisterPopUp();
            var releaseCallback = new ManualResetEventSlim();
            scope.OwnResource(releaseCallback);
            scope.ReleaseOnCleanup(releaseCallback.Set);
            var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Repository.OnSave = write =>
            {
                if (write.Index != 0) { write.Complete(); return; }
                scope.Own(write.Token.Register(() =>
                {
                    callbackEntered.SetResult();
                    // Controlled real CTS callback; finite bound also protects failed fixture setup.
                    if (!releaseCallback.Wait(Bound)) throw new TimeoutException("Held callback was not released.");
                }));
            };
            var held = await scope.Repository.NextAsync();
            var ordinaryQueue = scope.Track(Task.Run(scope.Coordinator.QueueSave));
            await callbackEntered.Task.WaitAsync(Bound);
            var final = scope.Seal();
            held.Complete();
            await held.OriginalTask.WaitAsync(Bound);
            await Task.Delay(30);
            Assert.False(ordinaryQueue.IsCompleted);
            Assert.False(final.IsCompleted);
            Assert.Single(scope.Repository.Writes);
            releaseCallback.Set();
            await ordinaryQueue.WaitAsync(Bound);
            await final.WaitAsync(Bound);
            Assert.Equal(2, scope.Repository.Writes.Count);
        });

    [AvaloniaFact]
    public Task Snapshot_failure_keeps_the_seal_and_independently_drains_an_older_actual_write() =>
        RunCaseAsync(async scope =>
        {
            var failCapture = false;
            var captureCause = new IOException("Original snapshot delegate failure.");
            scope.RegisterPopUp(onTabSnapshot: () => { if (failCapture) throw captureCause; });
            var held = await scope.Repository.NextAsync();
            failCapture = true;
            var final = scope.Seal();
            Assert.False(final.IsCompleted);
            Assert.Same(final, scope.Coordinator.SaveNowAsync(CancellationToken.None));
            held.Complete();
            var actual = await Assert.ThrowsAsync<IOException>(() => final);
            Assert.Same(captureCause, actual);
            Assert.Single(scope.Repository.Writes);
            scope.ExpectObservedFault(final);
        });

    [AvaloniaFact]
    public Task Entered_explicit_cancellation_is_retained_and_never_waived_as_a_debounce() =>
        RunCaseAsync(async scope =>
        {
            scope.RegisterPopUp();
            var caller = new CancellationTokenSource();
            scope.OwnResource(caller);
            scope.Repository.OnSave = write => { if (write.Index != 0) write.Complete(); };
            var explicitSave = scope.Track(scope.Coordinator.SaveNowAndCancelPendingAsync(caller.Token));
            var entered = await scope.Repository.NextAsync();
            caller.Cancel();
            entered.Cancel(caller.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => explicitSave);
            var final = scope.Seal();
            var errors = await Record.ExceptionAsync(() => final);
            Assert.NotNull(errors);
            Assert.Equal(2, scope.Repository.Writes.Count);
            Assert.Same(final, scope.Coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None));
            scope.ExpectObservedFault(explicitSave);
            scope.ExpectObservedFault(entered.OriginalTask);
            scope.ExpectObservedFault(final);
        });

    [AvaloniaFact]
    public Task Ordinary_unsealed_unregister_still_persists_the_remaining_workspace() =>
        RunCaseAsync(async scope =>
        {
            var model = scope.RegisterPopUp();
            var first = await scope.Repository.NextAsync();
            Assert.Equal(model.WindowId, Assert.Single(first.Snapshot.Windows).Id);
            first.Complete();
            await first.OriginalTask.WaitAsync(Bound);
            scope.Coordinator.UnregisterPopUp(model.WindowId);
            var next = await scope.Repository.NextAsync();
            Assert.Empty(next.Snapshot.Windows);
            Assert.Empty(next.Snapshot.Tabs);
            next.Complete();
            await next.OriginalTask.WaitAsync(Bound);
            Assert.Equal(2, scope.Repository.Writes.Count);
        });

    [AvaloniaFact]
    public Task Direct_repository_OCE_before_returned_Task_is_retained_as_fault() =>
        RunCaseAsync(async scope =>
        {
            var cause = new OperationCanceledException("No canceled repository Task was returned.");
            scope.Repository.OnSave = write => { if (write.Index == 0) throw cause; write.Complete(); };
            var actual = scope.Track(scope.Coordinator.SaveNowAsync(CancellationToken.None));
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, cause));
            Assert.True(actual.IsFaulted);
            scope.ExpectObservedFault(actual);
            var final = scope.Seal();
            var finalError = await Assert.ThrowsAsync<AggregateException>(() => final);
            Assert.Contains(finalError.Flatten().InnerExceptions, item => ReferenceEquals(item, cause));
            Assert.True(final.IsFaulted);
            scope.ExpectObservedFault(final);
        });

    [AvaloniaFact]
    public Task Faulted_repository_OCE_keeps_reference_and_faulted_public_save() =>
        RunCaseAsync(async scope =>
        {
            var cause = new OperationCanceledException("Actual faulted save task.");
            scope.Repository.OnSave = write => { if (write.Index == 0) write.Fail(cause); else write.Complete(); };
            var actual = scope.Track(scope.Coordinator.SaveNowAsync(CancellationToken.None));
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, cause));
            Assert.True(actual.IsFaulted);
            var raw = Assert.Single(scope.Repository.Writes).OriginalTask;
            Assert.True(raw.IsFaulted);
            scope.ExpectObservedFault(actual);
            scope.ExpectObservedFault(raw);
            var final = scope.Seal();
            var finalError = await Assert.ThrowsAsync<AggregateException>(() => final);
            Assert.Contains(finalError.Flatten().InnerExceptions, item => ReferenceEquals(item, cause));
            scope.ExpectObservedFault(final);
        });

    [AvaloniaFact]
    public Task Dispatcher_snapshot_compound_failure_retains_all_original_members() =>
        RunCaseAsync(async scope =>
        {
            var first = new IOException("Actual snapshot primary.");
            var second = new InvalidOperationException("Actual snapshot sibling.");
            var cause = new AggregateException(first, second);
            scope.RegisterPopUp(onTabSnapshot: () => throw cause);
            var actual = scope.Track(scope.Coordinator.SaveNowAsync(CancellationToken.None));
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, first));
            Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, second));
            Assert.Empty(scope.Repository.Writes);
            scope.ExpectObservedFault(actual);
            var final = scope.Seal();
            var finalError = await Assert.ThrowsAsync<AggregateException>(() => final);
            Assert.Contains(finalError.Flatten().InnerExceptions, item => ReferenceEquals(item, first));
            Assert.Contains(finalError.Flatten().InnerExceptions, item => ReferenceEquals(item, second));
            scope.ExpectObservedFault(final);
        });

    [AvaloniaFact]
    public Task Cancel_pending_save_joins_held_predecessor_despite_callback_failure() =>
        RunCaseAsync(async scope =>
        {
            var callbackCause = new IOException("Actual cancellation callback.");
            var saveCause = new InvalidOperationException("Actual held predecessor save.");
            scope.RegisterPopUp();
            scope.Repository.OnSave = write =>
            {
                if (write.Index != 0) { write.Complete(); return; }
                scope.Own(write.Token.Register(() => throw callbackCause));
            };
            var held = await scope.Repository.NextAsync();
            var actual = scope.Track(scope.Coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None));
            await Task.Delay(30);
            Assert.False(actual.IsCompleted);
            Assert.Single(scope.Repository.Writes);
            held.Fail(saveCause);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, callbackCause));
            Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, saveCause));
            Assert.Single(scope.Repository.Writes);
            scope.ExpectObservedFault(actual);
            scope.ExpectObservedFault(held.OriginalTask);
            var final = scope.Seal();
            var finalError = await Assert.ThrowsAsync<AggregateException>(() => final);
            Assert.Contains(finalError.Flatten().InnerExceptions, item => ReferenceEquals(item, callbackCause));
            Assert.Contains(finalError.Flatten().InnerExceptions, item => ReferenceEquals(item, saveCause));
            scope.ExpectObservedFault(final);
        });

    [AvaloniaFact]
    public Task Live_snapshot_requests_same_seal_but_external_join_refuses_self_join() =>
        RunCaseAsync(async scope =>
        {
            Task? requested = null;
            Exception? refusal = null;
            scope.RegisterPopUp(onTabSnapshot: () =>
            {
                requested = scope.Coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
                refusal = Assert.Throws<InvalidOperationException>(() => { _ = scope.Coordinator.JoinOriginalFinalSaveAsync(); });
            });
            scope.Repository.OnSave = write => write.Complete();
            var final = scope.Seal();
            Assert.Same(final, requested);
            Assert.NotNull(refusal);
            Assert.Same(final, scope.Coordinator.JoinOriginalFinalSaveAsync());
            await final;
        });

    [AvaloniaFact]
    public Task Suppressed_context_cancellation_callback_cannot_join_its_same_close() =>
        RunCaseAsync(async scope =>
        {
            Task? requested = null;
            Exception? refusal = null;
            scope.RegisterPopUp();
            scope.Repository.OnSave = write =>
            {
                if (write.Index != 0) { write.Complete(); return; }
                using (ExecutionContext.SuppressFlow())
                    scope.Own(write.Token.Register(() =>
                    {
                        requested = scope.Coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
                        refusal = Assert.Throws<InvalidOperationException>(() => { _ = scope.Coordinator.JoinOriginalFinalSaveAsync(); });
                    }));
            };
            var held = await scope.Repository.NextAsync();
            var final = scope.Seal();
            Assert.Same(final, requested);
            Assert.NotNull(refusal);
            Assert.False(final.IsCompleted);
            held.Complete();
            await final;
            Assert.Same(final, scope.Coordinator.JoinOriginalFinalSaveAsync());
        });

    [AvaloniaFact]
    public Task Retired_inherited_snapshot_context_does_not_refuse_external_join() =>
        RunCaseAsync(async scope =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.ReleaseOnCleanup(() => release.TrySetResult());
            Task? child = null;
            Task? final = null;
            scope.RegisterPopUp(onTabSnapshot: () =>
            {
                child = scope.Track(Task.Run(async () =>
                {
                    await release.Task;
                    var joined = scope.Coordinator.JoinOriginalFinalSaveAsync();
                    Assert.Same(final, joined);
                    await joined;
                }));
            });
            scope.Repository.OnSave = write => write.Complete();
            final = scope.Seal();
            await final;
            release.SetResult();
            Assert.NotNull(child);
            await child!;
        });

    [AvaloniaFact]
    public Task Failed_original_capacity_refuses_before_new_repository_admission_and_stays_inspectable() =>
        RunCaseAsync(async scope =>
        {
            scope.Repository.OnSave = write => { if (write.Index < 128) write.Fail(new IOException("Retained original " + write.Index)); else write.Complete(); };
            for (var index = 0; index < 128; index++)
            {
                var actual = scope.Track(scope.Coordinator.SaveNowAsync(CancellationToken.None));
                await Assert.ThrowsAsync<IOException>(() => actual);
                scope.ExpectObservedFault(actual);
                scope.ExpectObservedFault(scope.Repository.Writes[index].OriginalTask);
            }
            var refusal = Assert.Throws<InvalidOperationException>(() => { _ = scope.Coordinator.SaveNowAsync(CancellationToken.None); });
            Assert.Equal(128, scope.Repository.Writes.Count);
            var final = scope.Seal();
            var error = await Assert.ThrowsAsync<AggregateException>(() => final);
            Assert.Contains(error.Flatten().InnerExceptions, item => ReferenceEquals(item, refusal));
            Assert.Same(final, scope.Coordinator.JoinOriginalFinalSaveAsync());
            Assert.Equal(129, scope.Repository.Writes.Count);
            scope.ExpectObservedFault(final);
        });

    [AvaloniaFact]
    public Task Refused_recursive_cancel_pending_save_preserves_original_for_next_queue_cancellation() =>
        RunCaseAsync(async scope =>
        {
            var callbackReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            InvalidOperationException? refused = null;
            Write? original = null;
            var wasCanceledBySuccessor = false;
            scope.Repository.OnSave = write =>
            {
                if (write.Index == 0)
                {
                    original = write;
                    refused = Assert.Throws<InvalidOperationException>(
                        () => { _ = scope.Coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None); });
                    scope.Coordinator.QueueSave();
                    wasCanceledBySuccessor = write.Token.IsCancellationRequested;
                    callbackReturned.SetResult();
                }
                write.Complete();
            };
            scope.RegisterPopUp();
            await callbackReturned.Task.WaitAsync(Bound);
            Assert.NotNull(refused);
            Assert.NotNull(original);
            Assert.True(wasCanceledBySuccessor);
            Assert.True(original.Token.IsCancellationRequested);
            var final = scope.Seal();
            await final.WaitAsync(Bound);
            Assert.Same(final, scope.Coordinator.JoinOriginalFinalSaveAsync());
        });

    private static async Task RunCaseAsync(Func<Scope, Task> body)
    {
        var scope = new Scope();
        var failures = new List<Exception>();
        try { await body(scope); }
        catch (Exception error) { Add(failures, error); }

        // No teardown path skips a release, actual close, repository task, Window.Close
        // or callback-resource cleanup merely because an earlier one failed.
        foreach (var release in scope.Releases)
        {
            try { release(); }
            catch (Exception error) { Add(failures, error); }
        }
        scope.Repository.Draining = true;
        scope.Repository.ReleaseAll();
        try { _ = scope.Seal(); }
        catch (Exception error) { Add(failures, error); }
        foreach (var task in scope.Tasks.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray())
        {
            try { await task.WaitAsync(Bound); }
            catch (Exception error)
            {
                if (scope.ObservedFaults.Contains(task)) continue;
                if (task.Exception is { } group)
                    foreach (var cause in group.InnerExceptions) Add(failures, cause);
                else Add(failures, error);
            }
        }
        // Acquire this census AFTER joining final close: its actual final repository
        // Task may have been published during that join, and must be observed itself.
        foreach (var task in scope.Repository.Writes.Select(write => write.OriginalTask)
                     .Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray())
        {
            try { await task.WaitAsync(Bound); }
            catch (Exception error)
            {
                if (scope.ObservedFaults.Contains(task)) continue;
                if (task.Exception is { } group)
                    foreach (var cause in group.InnerExceptions) Add(failures, cause);
                else Add(failures, error);
            }
        }
        if (scope.Window is { } window)
        {
            try { window.Close(); }
            catch (Exception error) { Add(failures, error); }
        }
        foreach (var registration in scope.Registrations)
        {
            try { registration.Dispose(); }
            catch (Exception error) { Add(failures, error); }
        }
        foreach (var resource in scope.Resources)
        {
            try { resource.Dispose(); }
            catch (Exception error) { Add(failures, error); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Controlled case and independent teardown failed.", failures);
    }

    private static void Add(List<Exception> failures, Exception error)
    {
        if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error);
    }

    private sealed class Scope
    {
        internal readonly ControlledRepository Repository = new();
        internal readonly WorkspaceSessionCoordinator Coordinator;
        internal readonly List<Task> Tasks = [];
        internal readonly List<Action> Releases = [];
        internal readonly List<CancellationTokenRegistration> Registrations = [];
        internal readonly List<IDisposable> Resources = [];
        internal readonly HashSet<Task> ObservedFaults = new(ReferenceEqualityComparer.Instance);
        internal Window? Window;
        private Task? _final;

        internal Scope() => Coordinator = new WorkspaceSessionCoordinator(Repository);
        internal Task Track(Task task) { Tasks.Add(task); return task; }
        internal void Own(CancellationTokenRegistration registration) => Registrations.Add(registration);
        internal void OwnResource(IDisposable resource) => Resources.Add(resource);
        internal void ReleaseOnCleanup(Action release) => Releases.Add(release);
        // Only used AFTER exact production fault assertions. Never waives a coordinator cause.
        internal void ExpectObservedFault(Task task) => ObservedFaults.Add(task);
        internal Task Seal() => _final ??= Track(Coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None));
        internal Window AcquireWindow()
        {
            var window = new Window();
            Window = window;
            window.Width = 960;
            window.Height = 720;
            return window;
        }

        internal PopupModel RegisterPopUp(Window? window = null, Action? onTabSnapshot = null)
        {
            var model = new PopupModel();
            Coordinator.RegisterPopUp(model.WindowId,
                () => { onTabSnapshot?.Invoke(); return model.Tab; },
                () => new WorkspaceWindowSnapshot(model.WindowId, WorkspaceWindowKind.PopUp,
                    new WorkspaceLayoutSnapshot(model.LayoutId, WorkspaceLayoutKind.Single,
                        SplitOrientation.Vertical, 1, model.Panes), model.OrderedTabIds,
                    model.Tab.Id, window is null ? "{\"Width\":960,\"Height\":720}" :
                        System.Text.Json.JsonSerializer.Serialize(new { window.Width, window.Height }),
                    DateTimeOffset.UnixEpoch));
            return model;
        }
    }

    private sealed class PopupModel
    {
        internal readonly Guid WindowId = Guid.NewGuid();
        internal readonly Guid LayoutId = Guid.NewGuid();
        internal readonly TabSessionSnapshot Tab = new(Guid.NewGuid(), "terminal", "Controlled terminal",
            "{\"TerminalSessionId\":\"controlled-session\",\"TerminalGeneration\":7}", null, null,
            null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        internal readonly List<Guid> OrderedTabIds;
        internal readonly List<WorkspacePaneSnapshot> Panes;
        internal PopupModel()
        {
            OrderedTabIds = [Tab.Id];
            Panes = [new WorkspacePaneSnapshot(Guid.NewGuid(), Tab.Id, 0)];
        }
    }

    private sealed class ControlledRepository : IWorkspaceSessionRepository
    {
        private readonly object _gate = new();
        private readonly List<Write> _writes = [];
        private readonly Channel<Write> _entered = Channel.CreateUnbounded<Write>();
        internal Action<Write>? OnSave;
        internal volatile bool Draining;
        internal IReadOnlyList<Write> Writes { get { lock (_gate) return _writes.ToArray(); } }

        public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken token) =>
            Task.FromResult<WorkspaceSessionSnapshot?>(null);

        public Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken token)
        {
            Write write;
            lock (_gate) { write = new Write(_writes.Count, snapshot, token); _writes.Add(write); }
            if (Draining) write.Complete();
            else OnSave?.Invoke(write);
            if (!_entered.Writer.TryWrite(write)) throw new InvalidOperationException("Fixture entry was not retained.");
            return write.OriginalTask;
        }

        internal Task<Write> NextAsync() => _entered.Reader.ReadAsync().AsTask().WaitAsync(Bound);
        internal void ReleaseAll() { foreach (var write in Writes) write.Complete(); }
    }

    private sealed class Write(int index, WorkspaceSessionSnapshot snapshot, CancellationToken token)
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Index { get; } = index;
        internal WorkspaceSessionSnapshot Snapshot { get; } = snapshot;
        internal CancellationToken Token { get; } = token;
        internal Task OriginalTask => _completion.Task;
        internal void Complete() => _completion.TrySetResult();
        internal void Fail(params Exception[] causes) => _completion.TrySetException(causes);
        internal void Cancel(CancellationToken cancelledToken) => _completion.TrySetCanceled(cancelledToken);
    }
}
