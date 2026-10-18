using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

// Real notification backend, dispatcher and timer; controlled repository I/O only.
// These headless ownership checks do not certify actual full-window native close.
public sealed class NotificationServiceOriginalLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [AvaloniaFact]
    public async Task Close_waits_for_held_repository_then_rejects_late_publication_and_new_work()
    {
        var raw = new TaskCompletionSource<IReadOnlyList<HavenNotification>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { Read = _ => raw.Task };
        await using var rig = new Rig(repository);
        var owner = rig.Owner;
        try
        {
            Assert.Equal(1, repository.ReadCount);
            var close = owner.CloseAndDrainAsync();
            Assert.Same(close, owner.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.True(repository.LastReadToken.IsCancellationRequested);
            raw.SetResult([Notification()]);
            await close.WaitAsync(Bound);
            Assert.Empty(owner.Notifications);
            Assert.Throws<ObjectDisposedException>(() => owner.Show("late", "late"));
            owner.Dismiss(Guid.NewGuid());
            await rig.Backend.PublishAsync(Notification(), CancellationToken.None);
            Assert.Equal(1, repository.ReadCount);
            Assert.Equal(0, repository.DismissCount);
            Assert.Empty(owner.Notifications);
        }
        finally { raw.TrySetResult([]); }
    }

    [AvaloniaFact]
    public async Task Actual_canceled_repository_task_with_exact_owned_token_settles_normally()
    {
        var raw = new TaskCompletionSource<IReadOnlyList<HavenNotification>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { Read = _ => raw.Task };
        await using var rig = new Rig(repository);
        try
        {
            var close = rig.Owner.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            var issued = repository.LastReadToken;
            Assert.True(issued.IsCancellationRequested);
            raw.SetCanceled(issued);
            await close.WaitAsync(Bound);
            Assert.True(raw.Task.IsCanceled);
            Assert.True(close.IsCompletedSuccessfully);
        }
        finally { raw.TrySetResult([]); }
    }

    [AvaloniaFact]
    public async Task Faulted_OCE_with_owned_token_and_independent_sibling_both_block_clean_close()
    {
        var raw = new TaskCompletionSource<IReadOnlyList<HavenNotification>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { Read = _ => raw.Task };
        await using var rig = new Rig(repository);
        try
        {
            var close = rig.Owner.CloseAndDrainAsync();
            var fault = new OperationCanceledException(repository.LastReadToken);
            var sibling = new IOException("controlled independent notification read failure");
            raw.SetException([fault, sibling]);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(Bound));
            Assert.True(raw.Task.IsFaulted);
            Assert.True(close.IsFaulted);
            Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, fault));
            Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, sibling));
            Assert.Same(close, rig.Owner.CloseAndDrainAsync());
            rig.VerifiedFailedClose = close;
        }
        finally { raw.TrySetResult([]); }
    }

    [AvaloniaFact]
    public async Task Canceled_repository_task_with_foreign_token_remains_a_close_failure()
    {
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var raw = new TaskCompletionSource<IReadOnlyList<HavenNotification>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository { Read = _ => raw.Task };
        await using var rig = new Rig(repository);
        try
        {
            var close = rig.Owner.CloseAndDrainAsync();
            raw.SetCanceled(foreign.Token);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(Bound));
            Assert.Contains(Causes(failure), cause => cause is OperationCanceledException stopped && stopped.CancellationToken == foreign.Token);
            Assert.True(close.IsFaulted);
            rig.VerifiedFailedClose = close;
        }
        finally { raw.TrySetResult([]); }
    }

    [AvaloniaFact]
    public async Task Queued_UI_publication_is_joined_without_adding_notifications_after_retirement()
    {
        var owner = new NotificationService();
        var additions = 0;
        owner.Notifications.CollectionChanged += (_, change) =>
        { if (change.Action == NotifyCollectionChangedAction.Add) additions++; };
        try
        {
            owner.Show("controlled queued toast", "controlled content");
            var close = owner.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            Assert.Same(close, owner.OriginalClose);
            await close.WaitAsync(Bound);
            Assert.Equal(0, additions);
            Assert.Empty(owner.Notifications);
        }
        finally { await owner.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    [AvaloniaFact]
    public async Task Changed_read_and_dismiss_are_real_originals_and_source_callback_cannot_self_join()
    {
        var changedRead = new TaskCompletionSource<IReadOnlyList<HavenNotification>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dismiss = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Repository();
        await using var rig = new Rig(repository);
        var earlier = ExecutionContext.Capture()!;
        var guarded = false;
        repository.Read = _ => changedRead.Task;
        repository.OnDismiss = _ =>
        {
            ExecutionContext.Run(earlier, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Owner.CloseAndDrainAsync(); });
                guarded = true;
            }, null);
            return dismiss.Task;
        };
        try
        {
            await rig.Backend.PublishAsync(Notification(), CancellationToken.None);
            Assert.Equal(2, repository.ReadCount);
            rig.Owner.Dismiss(Guid.NewGuid());
            Assert.True(guarded);
            Assert.Equal(1, repository.DismissCount);
            var close = rig.Owner.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            changedRead.SetResult([]);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.False(close.IsCompleted); // The separate accepted Dismiss still owns raw I/O.
            dismiss.SetResult();
            await close.WaitAsync(Bound);
            Assert.Empty(rig.Owner.Notifications);
        }
        finally { changedRead.TrySetResult([]); dismiss.TrySetResult(); }
    }

    [AvaloniaFact]
    public async Task Actual_timer_expires_a_local_toast_then_its_owner_drains()
    {
        var owner = new NotificationService();
        var added = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Notifications.CollectionChanged += (_, change) =>
        {
            if (change.Action == NotifyCollectionChangedAction.Add) added.TrySetResult();
            if (change.Action == NotifyCollectionChangedAction.Remove) removed.TrySetResult();
        };
        try
        {
            owner.Show("controlled expiry", "controlled content", duration: TimeSpan.FromMilliseconds(1));
            await added.Task.WaitAsync(Bound);
            await removed.Task.WaitAsync(Bound);
            Assert.Empty(owner.Notifications);
            await owner.CloseAndDrainAsync().WaitAsync(Bound);
        }
        finally { await owner.CloseAndDrainAsync().WaitAsync(Bound); }
    }

    private static IEnumerable<Exception> Causes(Exception cause) =>
        cause is AggregateException aggregate ? aggregate.InnerExceptions.SelectMany(Causes) : [cause];

    private static HavenNotification Notification() => new(Guid.NewGuid(), HavenNotificationKind.LiveActivity,
        HavenNotificationPriority.Information, "controlled", "controlled", "controlled", "controlled",
        true, false, false, false, null, new(), [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class Rig : IAsyncDisposable
    {
        private readonly ExecutionEventHub _events = new(new EventRepository());
        internal HavenNotificationService Backend { get; }
        internal NotificationService Owner { get; }
        internal Task? VerifiedFailedClose { get; set; }
        internal Rig(Repository repository)
        { Backend = new(repository, _events); Owner = new(Backend); }
        public async ValueTask DisposeAsync()
        {
            var close = Owner.CloseAndDrainAsync();
            try { await close.WaitAsync(Bound); }
            catch when (ReferenceEquals(close, VerifiedFailedClose) && close.IsFaulted) { _ = close.Exception; }
            finally { Backend.Dispose(); await _events.DisposeAsync(); }
        }
    }

    private sealed class Repository : IHavenNotificationRepository
    {
        internal Func<CancellationToken, Task<IReadOnlyList<HavenNotification>>> Read { get; set; } =
            _ => Task.FromResult<IReadOnlyList<HavenNotification>>([]);
        internal Func<CancellationToken, Task> OnDismiss { get; set; } = _ => Task.CompletedTask;
        internal int ReadCount { get; private set; }
        internal int DismissCount { get; private set; }
        internal CancellationToken LastReadToken { get; private set; }
        public Task<IReadOnlyList<HavenNotification>> GetRecentAsync(int limit, bool includeDismissed, CancellationToken token)
        { ReadCount++; LastReadToken = token; return Read(token); }
        public Task DismissAsync(Guid id, CancellationToken token) { DismissCount++; return OnDismiss(token); }
        public Task UpsertAsync(HavenNotification notification, CancellationToken token) => Task.CompletedTask;
        public Task SetReadAsync(Guid id, bool isRead, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class EventRepository : IExecutionEventRepository
    {
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ExecutionEvent>>([]);
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
    }
}
