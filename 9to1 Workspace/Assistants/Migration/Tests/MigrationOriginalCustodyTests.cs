using Xunit;

namespace HavenOS.Apps.Assistants.Migration.Tests;

public sealed class MigrationOriginalCustodyTests
{
    [Fact]
    public async Task Only_exact_issued_pre_effect_refusal_with_successful_cleanup_is_acknowledged()
    {
        var originals = new MigrationOriginals();
        var actual = originals.Admit<int>(() => originals.Source<int>(() => throw originals.Refuse("Changed", "Refresh this preview.")));
        await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => actual);
        Assert.True(originals.IsAcknowledged(actual));
        await originals.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Foreign_raw_source_reusing_prior_refusal_does_not_gain_acknowledgement()
    {
        var originals = new MigrationOriginals();
        var issued = originals.Admit<int>(() => throw originals.Refuse("Changed", "Refresh this preview."));
        var prior = await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => issued);
        Assert.True(originals.IsAcknowledged(issued));
        var foreign = originals.Admit(() => originals.Source(() => Task.FromException<int>(prior)));
        await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => foreign);
        Assert.False(originals.IsAcknowledged(foreign));
        var close = await Assert.ThrowsAsync<AggregateException>(() => originals.CloseAndDrainAsync());
        Assert.Contains(close.InnerExceptions, value => ReferenceEquals(value, prior));
    }

    [Fact]
    public async Task Cleanup_failure_with_local_refusal_remains_failed()
    {
        var originals = new MigrationOriginals();
        var cleanup = new IOException("Actual source cleanup failed.");
        var actual = originals.Admit(() => originals.Source(() =>
            Task.FromException<int>(new AggregateException(originals.Refuse("Changed", "Refresh preview."), cleanup)))));
        await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.False(originals.IsAcknowledged(actual));
        var close = await Assert.ThrowsAsync<AggregateException>(() => originals.CloseAndDrainAsync());
        Assert.Contains(close.Flatten().InnerExceptions, value => ReferenceEquals(value, cleanup));
    }

    [Fact]
    public async Task Partial_effect_prevents_refusal_waiver_and_close_retains_original()
    {
        var originals = new MigrationOriginals();
        var actual = originals.Admit<int>(() =>
        { originals.EffectStarting(); throw originals.Refuse("Pending", "Inspect staged migration."); });
        var failure = await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => actual);
        Assert.False(originals.IsAcknowledged(actual));
        var close = await Assert.ThrowsAsync<AggregateException>(() => originals.CloseAndDrainAsync());
        Assert.Contains(close.InnerExceptions, value => ReferenceEquals(value, failure));
    }

    [Fact]
    public async Task Close_seals_new_admission_and_joins_same_inflight_source_before_returning()
    {
        var originals = new MigrationOriginals();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = originals.Admit(() => originals.Source(() => { entered.SetResult(); return release.Task; }));
        await entered.Task;
        var close = originals.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => { _ = originals.Admit(() => Task.FromResult(1)); });
        release.SetResult(42); Assert.Equal(42, await actual); await close;
        Assert.Same(close, originals.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Held_raw_reader_result_is_captured_after_source_postguard_failure()
    {
        var result = new object(); object? captured = null;
        var failure = new IOException("Actual source postguard failed after reader acquisition began.");
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? retained = null;
        var callbacks = new LegacySavedAgentSqliteSource.Callbacks(body => { body(); throw failure; }, task => retained = task);
        var read = callbacks.Read(() => release.Task, value => captured = value);
        Assert.Same(release.Task, retained); Assert.False(read.IsCompleted); Assert.Null(captured);
        release.SetResult(result);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => read));
        Assert.Same(result, captured);
    }

    [Fact]
    public async Task Same_source_callback_cannot_acquire_two_originals()
    {
        var calls = 0; var retained = new List<Task>();
        var callbacks = new LegacySavedAgentSqliteSource.Callbacks(body => { body(); body(); }, retained.Add);
        await Assert.ThrowsAsync<InvalidOperationException>(() => callbacks.Read(() => { calls++; return Task.FromResult(7); }));
        Assert.Equal(1, calls); Assert.Single(retained); Assert.True(retained[0].IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Successful_command_cannot_prune_a_failed_retained_raw_source()
    {
        var originals = new MigrationOriginals(); var sourceFailure = new IOException("Raw source settled independently.");
        var source = Task.FromException(sourceFailure);
        var command = originals.Admit(() => { originals.Retain(source); return Task.FromResult(4); });
        Assert.Equal(4, await command);
        Assert.Equal(5, await originals.Admit(() => Task.FromResult(5)));
        var close = await Assert.ThrowsAsync<AggregateException>(() => originals.CloseAndDrainAsync());
        Assert.Same(sourceFailure, Assert.Single(close.InnerExceptions));
    }
}
