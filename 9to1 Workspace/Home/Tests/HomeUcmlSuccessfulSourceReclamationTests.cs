using Haven.Application;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeUcmlSuccessfulSourceReclamationTests
{
    [Fact]
    public async Task Reclamation_preserves_pending_faulted_and_canceled_originals_and_their_exact_causes()
    {
        var ledger = new CloudflareOriginalTaskLedger();
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("The original source failed.");
        var failed = Task.FromException<int>(cause);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = Task.FromCanceled<int>(cancellation.Token);
        var success = Task.FromResult(5);
        _ = ledger.Track(pending.Task); _ = ledger.Track(failed); _ = ledger.Track(canceled); _ = ledger.Track(success);

        ledger.ReleaseSuccessfullyObservedOriginals();
        Assert.DoesNotContain(success, ledger.OriginalTasks);
        Assert.Contains(pending.Task, ledger.OriginalTasks);
        Assert.Contains(failed, ledger.OriginalTasks);
        Assert.Contains(canceled, ledger.OriginalTasks);
        var joined = ledger.ObserveAllOriginalTasksAsync(); Assert.False(joined.IsCompleted);
        pending.SetResult(7); await joined;
        ledger.ReleaseSuccessfullyObservedOriginals();

        Assert.DoesNotContain(pending.Task, ledger.OriginalTasks);
        Assert.Contains(failed, ledger.OriginalTasks);
        Assert.Contains(canceled, ledger.OriginalTasks);
        Assert.Contains(cause, ledger.OriginalErrors);
        Assert.Contains(ledger.OriginalErrors, error => error is TaskCanceledException observed &&
            ReferenceEquals(observed.Task, canceled) && observed.CancellationToken == cancellation.Token);
    }
}
