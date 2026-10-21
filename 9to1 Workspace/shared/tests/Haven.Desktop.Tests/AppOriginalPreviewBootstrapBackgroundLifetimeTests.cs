using System.IO;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Verifies the Dulche20 owned-tracked background model-bootstrap pattern using the SAME
/// source-backed helper used in App production (App.RunOwnedSourceAsync) and the ACTUAL
/// DesktopOriginalWorkLifetime wrapper (Original.AwaitAsync) + owner Close/CollectRetained. Only a
/// qualified same-owner cooperative close cancellation (owner lifetime token) is normalized to benign;
/// unrelated OCE, genuine IOException and sibling stop/cleanup failures stay retained as exact causes.
/// These claim no native app/engine/model acceptance.</summary>
public sealed class AppOriginalPreviewBootstrapBackgroundLifetimeTests
{
    // SAME production wiring as BeginPreviewModelBootstrapBackground: background original + AwaitAsync + retain.
    private static Task RunBootstrapOriginalAsync(DesktopOriginalWorkLifetime owner, Func<CancellationToken, Task> start)
    {
        return owner.RunAsync(async original =>
        {
            try { await original.AwaitAsync(App.RunOwnedSourceAsync(original, start)); }
            catch (Exception failure) { original.Retain(failure); }
        });
    }

    [Fact]
    public async Task Background_model_readiness_does_not_block_home_and_same_owner_close_cancels_joins_raw_source_cleanly()
    {
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var readinessStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? rawSource = null;

        // A genuine pending readiness operation that ends canceled under the owner lifetime token.
        Func<CancellationToken, Task> start = async token =>
        {
            var pending = Task.Delay(Timeout.Infinite, token);
            rawSource = pending;
            readinessStarted.SetResult();
            await pending;
        };

        var bootstrap = RunBootstrapOriginalAsync(owner, start);
        await readinessStarted.Task;
        Assert.False(bootstrap.IsCompleted); // readiness still held
        Assert.NotNull(rawSource);
        Assert.False(rawSource.IsCompleted);

        // Home startup completes independently while model readiness is held.
        var homeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var home = owner.RunAsync(async original =>
        {
            homeStarted.SetResult();
            await Task.Delay(20);
        });
        await homeStarted.Task;
        await home;

        var close = owner.CloseAndDrainAsync();
        await close;
        Assert.True(close.IsCompletedSuccessfully); // qualified same-owner cancel is NOT a fatal aggregate
        Assert.True(rawSource.IsCanceled);          // raw source actually ended canceled
        Assert.True(bootstrap.IsCompletedSuccessfully);
        Assert.True(home.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Background_bootstrap_genuine_failure_is_retained_and_rethrows_sole_exact_cause_at_close()
    {
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var cause = new IOException("bundled model bootstrap failed");

        var bootstrap = RunBootstrapOriginalAsync(owner, _ => Task.FromException(cause));
        await bootstrap; // outer retain absorbs; the background original completes
        Assert.True(bootstrap.IsCompletedSuccessfully);

        // A sole non-cancellation cause is rethrown DIRECTLY (not wrapped in an AggregateException).
        var ex = await Assert.ThrowsAsync<IOException>(() => owner.CloseAndDrainAsync());
        Assert.Same(cause, ex);
    }

    [Fact]
    public async Task Background_bootstrap_unrelated_oc_e_stays_retained_as_exact_cause()
    {
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        using var foreign = new CancellationTokenSource();
        var foreignOce = new OperationCanceledException(foreign.Token); // NOT the owner token

        var bootstrap = RunBootstrapOriginalAsync(owner, _ => Task.FromException(foreignOce));
        await bootstrap;
        Assert.True(bootstrap.IsCompletedSuccessfully);

        // An unrelated OCE is NOT qualified as cooperative close, so it stays retained (single OCE wraps).
        var close = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync());
        Assert.Contains(foreignOce, close.InnerExceptions);
    }

    [Fact]
    public async Task Stop_sibling_failure_during_close_cancel_remains_genuine()
    {
        var stopFailure = new InvalidOperationException("independent stop failed");
        var owner = new DesktopOriginalWorkLifetime(() => Task.FromException(stopFailure), () => Task.CompletedTask);

        // The bootstrap source is pending until close requests cancellation under the owner token (qualified),
        // while the independent stop sibling failure is genuine and must NOT be normalized away.
        var bootstrap = RunBootstrapOriginalAsync(owner, token => Task.Delay(Timeout.Infinite, token));

        // Initiate Close FIRST to request cancellation; the pending bootstrap then joins cleanly.
        var close = owner.CloseAndDrainAsync();
        await bootstrap;
        Assert.True(bootstrap.IsCompletedSuccessfully);

        // Sole non-cancellation sibling failure is rethrown directly; genuine failure is never normalized away.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => close);
        Assert.Same(stopFailure, ex);
    }

    [Fact]
    public async Task Faulted_raw_source_with_owner_oc_e_and_io_preserves_entire_sibling_set_at_close()
    {
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var ioCause = new IOException("child cleanup failed after cancel");
        OperationCanceledException? ownerOce = null;

        // A FAULTED raw source carrying an owner-token OCE AND a sibling IOException must NOT be normalized.
        var bootstrap = RunBootstrapOriginalAsync(owner, token =>
        {
            ownerOce = new OperationCanceledException(token);
            return Task.FromException(new AggregateException(ownerOce, ioCause));
        });
        await bootstrap;
        Assert.True(bootstrap.IsCompletedSuccessfully);

        // The ENTIRE raw fault sibling set is preserved (never only the first `await` exception).
        var close = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync());
        Assert.Equal(2, close.InnerExceptions.Count);
        Assert.Contains(ownerOce!, close.InnerExceptions);
        Assert.Contains(ioCause, close.InnerExceptions);
    }

    [Fact]
    public async Task Faulted_owner_token_oc_e_only_during_close_stays_faulted_genuine()
    {
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);

        // A FAULTED source (even carrying the owner-token OCE) is never accepted as raw-canceled.
        var bootstrap = RunBootstrapOriginalAsync(owner, token => Task.FromException(new OperationCanceledException(token)));
        await bootstrap;
        Assert.True(bootstrap.IsCompletedSuccessfully);

        // Single faulted OCE stays genuine (wrapped per lifetime semantics), not normalized to benign.
        var close = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync());
        Assert.Single(close.InnerExceptions);
        Assert.IsType<OperationCanceledException>(close.InnerExceptions[0]);
    }
}
