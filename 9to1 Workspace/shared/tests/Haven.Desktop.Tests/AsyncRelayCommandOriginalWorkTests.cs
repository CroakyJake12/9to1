using System.Windows.Input;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual commands and exact delegate tasks; these controls do not infer
/// that async-void subscriber continuations or native UI presentation were drained.</summary>
public sealed class AsyncRelayCommandOriginalWorkTests
{
    [Fact]
    public async Task Healthy_command_keeps_original_predicate_and_disabled_enabled_notification_order()
    {
        var order = new List<string>();
        var predicateCalls = 0;
        var command = new AsyncRelayCommand(() => { order.Add("body"); return Task.CompletedTask; },
            () => { ++predicateCalls; return true; });
        command.CanExecuteChanged += (_, _) => order.Add(command.CanExecute(null) ? "enabled" : "disabled");
        var actual = command.ExecuteAsync();
        try
        {
            await actual;
            Assert.Same(actual, command.OriginalExecution);
            Assert.Equal(new[] { "disabled", "body", "enabled" }, order);
            Assert.Equal(2, predicateCalls);
            Assert.True(command.CanExecute(null));
        }
        finally { await command.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Icommand_publishes_same_complete_task_before_callback_and_close_waits_held_delegate()
    {
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var command = new AsyncRelayCommand(() => { ++calls; acquired.SetResult(); return raw.Task; });
        Task? observed = null;
        var notifications = 0;
        command.CanExecuteChanged += (_, _) =>
        { ++notifications; observed ??= command.OriginalExecution; };
        ((ICommand)command).Execute(null);
        try
        {
            await acquired.Task;
            var actual = Assert.IsAssignableFrom<Task>(command.OriginalExecution);
            Assert.Same(actual, observed);
            Assert.False(actual.IsCompleted);
            var close = command.CloseAndDrainAsync();
            Assert.Same(close, command.OriginalClose);
            Assert.Same(close, command.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(command.CanExecute(null));
            var denied = command.ExecuteAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => denied);
            Assert.Equal(1, calls);
            raw.SetResult();
            await actual;
            await close;
            Assert.True(raw.Task.IsCompletedSuccessfully);
            Assert.Equal(3, notifications); // initial, source-owned stop, final
        }
        finally
        {
            raw.TrySetResult();
            if (command.OriginalExecution is { } actual) await actual;
            await command.CloseAndDrainAsync();
        }
    }

    [Fact]
    public async Task Generic_actual_typed_parameter_and_concurrent_decline_preserve_original_delegate_once()
    {
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new List<string?>();
        var command = new AsyncRelayCommand<string>(value => { values.Add(value); return raw.Task; },
            value => value == "owned");
        var actual = command.ExecuteAsync("owned");
        try
        {
            var declined = command.ExecuteAsync("owned");
            await declined;
            Assert.False(actual.IsCompleted);
            Assert.Equal(new[] { "owned" }, values);
            Assert.False(command.CanExecute("owned"));
            raw.SetResult();
            await actual;
            Assert.True(command.CanExecute("owned"));
            Assert.False(command.CanExecute("other"));
            await command.ExecuteAsync("other");
            Assert.Single(values);
        }
        finally { raw.TrySetResult(); await actual; await command.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Initial_notification_failure_never_acquires_delegate_and_final_notification_clears_running()
    {
        var initial = new IOException("initial callback");
        var calls = 0;
        var notifications = 0;
        var command = new AsyncRelayCommand(() => { ++calls; return Task.CompletedTask; });
        command.CanExecuteChanged += (_, _) => { if (++notifications == 1) throw initial; };
        var actual = command.ExecuteAsync();
        Assert.Same(initial, await Assert.ThrowsAsync<IOException>(() => actual));
        Assert.Same(actual, command.OriginalExecution);
        Assert.Equal(0, calls);
        Assert.Equal(2, notifications);
        Assert.True(command.CanExecute(null));
        var close = command.CloseAndDrainAsync();
        Assert.Same(initial, await Assert.ThrowsAsync<IOException>(() => close));
        Assert.Same(close, command.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Direct_delegate_siblings_and_final_callback_failure_remain_exact_through_close()
    {
        var first = new IOException("delegate first");
        var second = new UnauthorizedAccessException("delegate sibling");
        var final = new InvalidOperationException("final callback");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = 0;
        var command = new AsyncRelayCommand(() => raw.Task);
        command.CanExecuteChanged += (_, _) => { if (++notifications == 2) throw final; };
        var actual = command.ExecuteAsync();
        try
        {
            raw.SetException(new Exception[] { first, second });
            var failed = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Collection(failed.InnerExceptions, cause => Assert.Same(first, cause),
                cause => Assert.Same(second, cause), cause => Assert.Same(final, cause));
            Assert.Collection(raw.Task.Exception!.InnerExceptions, cause => Assert.Same(first, cause),
                cause => Assert.Same(second, cause));
            Assert.True(command.CanExecute(null));
            var close = command.CloseAndDrainAsync();
            var closed = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.Collection(closed.InnerExceptions, cause => Assert.Same(first, cause),
                cause => Assert.Same(second, cause), cause => Assert.Same(final, cause));
            Assert.Same(close, command.OriginalClose);
        }
        finally
        {
            raw.TrySetException(new Exception[] { first, second });
            _ = await Record.ExceptionAsync(() => actual);
            _ = await Record.ExceptionAsync(() => command.CloseAndDrainAsync());
        }
    }

    [Fact]
    public async Task Faulted_OCE_and_foreign_empty_aggregate_are_not_cancellation_or_success_waivers()
    {
        var oce = new OperationCanceledException("business source fault");
        var empty = new AggregateException("opaque empty original");
        var raw = Task.FromException(oce);
        var command = new AsyncRelayCommand(() => raw);
        command.CanExecuteChanged += (_, _) => { if (command.CanExecute(null)) throw empty; };
        var actual = command.ExecuteAsync();
        var failed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(raw.IsFaulted);
        Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled);
        Assert.Collection(failed.InnerExceptions, cause => Assert.Same(oce, cause),
            cause => Assert.Same(empty, cause));
        var close = command.CloseAndDrainAsync();
        var closed = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Collection(closed.InnerExceptions, cause => Assert.Same(oce, cause),
            cause => Assert.Same(empty, cause));
        Assert.True(close.IsFaulted);
    }

    [Fact]
    public async Task Actual_delegate_cancellation_remains_canceled_raw_and_faulted_owner_with_exact_observation()
    {
        using var cts = new CancellationTokenSource();
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(() => raw.Task);
        var actual = command.ExecuteAsync();
        cts.Cancel();
        raw.SetCanceled(cts.Token);
        var failed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        var observation = Assert.IsType<TaskCanceledException>(Assert.Single(failed.InnerExceptions));
        Assert.Same(raw.Task, observation.Task);
        Assert.True(raw.Task.IsCanceled);
        Assert.True(actual.IsFaulted);
        var close = command.CloseAndDrainAsync();
        var closed = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Same(observation, Assert.Single(closed.InnerExceptions));
        Assert.True(close.IsFaulted);
    }

    [Fact]
    public async Task Predicate_retirement_refuses_before_initial_notification_or_raw_acquisition()
    {
        AsyncRelayCommand? command = null;
        var acquired = 0;
        var notifications = 0;
        command = new AsyncRelayCommand(() => { ++acquired; return Task.CompletedTask; }, () =>
        { command!.RequestRetirement(); return true; });
        command.CanExecuteChanged += (_, _) => ++notifications;
        var actual = command.ExecuteAsync();
        var failed = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(failed.InnerExceptions));
        Assert.Equal(0, acquired);
        Assert.Equal(1, notifications); // source stop only, no initial/final execution notification
        var close = command.CloseAndDrainAsync();
        var closed = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Same(Assert.Single(failed.InnerExceptions), Assert.Single(closed.InnerExceptions));
    }

    [Fact]
    public async Task Live_initial_final_and_physical_stop_callbacks_cannot_join_existing_own_close()
    {
        var guards = 0;
        AsyncRelayCommand? command = null;
        command = new AsyncRelayCommand(() => Task.CompletedTask);
        command.CanExecuteChanged += (_, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => command.DemandExternalOriginalRetirementJoin());
            Assert.Throws<InvalidOperationException>(() => { _ = command.CloseAndDrainAsync(); });
            ++guards;
        };
        await command.ExecuteAsync();
        var close = command.CloseAndDrainAsync();
        await close;
        Assert.Equal(3, guards);
        Assert.Same(close, command.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Public_notification_and_query_faults_have_actual_owned_close_custody()
    {
        var query = new IOException("query predicate");
        var notification = new InvalidOperationException("public notification");
        var command = new AsyncRelayCommand(() => Task.CompletedTask, () => throw query);
        Assert.Same(query, Assert.Throws<IOException>(() => command.CanExecute(null)));
        command.CanExecuteChanged += (_, _) => throw notification;
        Assert.Same(notification, Assert.Throws<InvalidOperationException>(() => command.RaiseCanExecuteChanged()));
        var close = command.CloseAndDrainAsync();
        var failed = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Collection(failed.InnerExceptions, cause => Assert.Same(query, cause),
            cause => Assert.Same(notification, cause));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_held_initial_or_final_notification_keeps_complete_original_and_close_pending(bool holdFinal)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = 0;
        var bodyCalls = 0;
        var command = new AsyncRelayCommand(() =>
        { Interlocked.Increment(ref bodyCalls); return Task.CompletedTask; });
        command.CanExecuteChanged += (_, _) =>
        {
            var index = Interlocked.Increment(ref notifications);
            if (index == (holdFinal ? 2 : 1))
            { entered.SetResult(); release.Wait(); }
        };
        // Actual synchronous EventHandler held on a separate caller, not a fake
        // async callback completion or replacement command implementation.
        var caller = Task.Run(() => command.ExecuteAsync(), TestContext.Current.CancellationToken);
        Task? actual = null;
        try
        {
            await entered.Task;
            actual = Assert.IsAssignableFrom<Task>(command.OriginalExecution);
            Assert.False(actual.IsCompleted);
            Assert.False(caller.IsCompleted);
            var close = command.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            Assert.Same(close, command.CloseAndDrainAsync());
            Assert.False(command.CanExecute(null));
            Assert.Equal(holdFinal ? 1 : 0, Volatile.Read(ref bodyCalls));
            release.Set();
            if (holdFinal)
            {
                await actual;
                await caller;
                await close;
            }
            else
            {
                var operation = await Assert.ThrowsAsync<AggregateException>(() => actual);
                Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(operation.InnerExceptions));
                var calling = await Assert.ThrowsAsync<AggregateException>(() => caller);
                Assert.Same(Assert.Single(operation.InnerExceptions), Assert.Single(calling.InnerExceptions));
                var closed = await Assert.ThrowsAsync<AggregateException>(() => close);
                Assert.Same(Assert.Single(operation.InnerExceptions), Assert.Single(closed.InnerExceptions));
                Assert.Equal(0, bodyCalls);
            }
        }
        finally
        {
            release.Set();
            if (actual is not null) _ = await Record.ExceptionAsync(() => actual);
            _ = await Record.ExceptionAsync(() => caller);
            _ = await Record.ExceptionAsync(() => command.CloseAndDrainAsync());
        }
    }

    [Fact]
    public async Task Healthy_complete_invocations_prune_without_sealing_or_losing_original_body_behavior()
    {
        var acquired = 0;
        var command = new AsyncRelayCommand(() => { ++acquired; return Task.CompletedTask; });
        try
        {
            for (var index = 0; index < 256; ++index)
                await command.ExecuteAsync();
            Assert.Equal(256, acquired);
            Assert.True(command.CanExecute(null));
            Assert.Null(command.OriginalClose);
        }
        finally { await command.CloseAndDrainAsync(); }
    }
}
