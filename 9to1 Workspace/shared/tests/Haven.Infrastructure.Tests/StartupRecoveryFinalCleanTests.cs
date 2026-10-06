using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>Exercises actual recovery files and original host-task custody, not a permission fixture.</summary>
public sealed class StartupRecoveryFinalCleanTests : IDisposable
{
    private readonly Paths _paths = new();
    private string StatePath => Path.Combine(_paths.DataDirectory, "startup-recovery.json");

    [Fact]
    public async Task Persisted_run_ids_and_completed_flags_do_not_issue_an_original_writer()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var original = await StartedAsync(diagnostics);
        var copied = new StartupRecoveryCoordinator(_paths, diagnostics);
        await Assert.ThrowsAsync<InvalidOperationException>(() => copied.PrepareFinalCleanWriterAsync(CancellationToken.None));
        Assert.False(ReadClean());
        Assert.NotNull(original.Current.RunId);
    }

    [Fact]
    public async Task Startup_begin_without_original_completion_cannot_prepare_clean()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = new StartupRecoveryCoordinator(_paths, diagnostics);
        await owner.BeginStartupAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.PrepareFinalCleanWriterAsync(CancellationToken.None));
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Prepared_writer_survives_actual_diagnostic_disposal_and_resets_history_last()
    {
        var diagnostics = new ProductionDiagnostics(_paths);
        try
        {
            for (var index = 0; index < 3; index++)
                await new StartupRecoveryCoordinator(_paths, diagnostics).BeginStartupAsync(CancellationToken.None);
            var owner = await StartedAsync(diagnostics);
            var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
            var actualProviderClose = diagnostics.DisposeAsync().AsTask();
            await actualProviderClose;
            await writer.CompleteAfterOriginalDrainAsync(actualProviderClose, CancellationToken.None);
            var state = ReadState();
            Assert.True(ReadClean());
            Assert.Equal(owner.Current.RunId, state["currentRun"]!["id"]!.GetValue<string>());
            Assert.Empty(state["recentUncleanStarts"]!.AsArray());
            await using var nextDiagnostics = new ProductionDiagnostics(_paths);
            var next = await new StartupRecoveryCoordinator(_paths, nextDiagnostics).BeginStartupAsync(CancellationToken.None);
            Assert.Equal(0, next.RecentUncleanStarts);
            Assert.False(next.IsSafeMode);
        }
        finally { await diagnostics.DisposeAsync(); }
    }

    [Fact]
    public async Task Repeated_completion_joins_same_held_original_and_foreign_drain_refuses()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        var held = NewCompletion();
        Task? close = null;
        try
        {
            close = writer.CompleteAfterOriginalDrainAsync(held.Task, CancellationToken.None);
            Assert.Same(close, writer.CompleteAfterOriginalDrainAsync(held.Task, CancellationToken.None));
            Action rejectForeignDrain = () => { _ = writer.CompleteAfterOriginalDrainAsync(Task.CompletedTask, CancellationToken.None); };
            Assert.Throws<InvalidOperationException>(rejectForeignDrain);
            Assert.False(close.IsCompleted);
            Assert.False(ReadClean());
            held.SetResult();
            await close;
            Assert.True(ReadClean());
        }
        finally { held.TrySetResult(); await ObserveAsync(close); }
    }

    [Fact]
    public async Task Every_direct_drain_fault_is_retained_and_unclean_record_survives()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        var first = new IOException("first actual provider close");
        var second = new InvalidOperationException("second actual original finally");
        var original = NewCompletion();
        original.SetException([first, second]);
        var completion = writer.CompleteAfterOriginalDrainAsync(original.Task, CancellationToken.None);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => completion);
        Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, first));
        Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, second));
        Assert.True(completion.IsFaulted);
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Faulted_original_cancellation_exception_keeps_fault_identity()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        var cause = new OperationCanceledException("faulted original, not canceled task");
        var original = Task.FromException(cause);
        var completion = writer.CompleteAfterOriginalDrainAsync(original, CancellationToken.None);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => completion);
        Assert.Contains(failure.InnerExceptions, item => ReferenceEquals(item, cause));
        Assert.True(original.IsFaulted);
        Assert.True(completion.IsFaulted);
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Genuinely_canceled_original_never_records_clean()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var original = Task.FromCanceled(stop.Token);
        var completion = writer.CompleteAfterOriginalDrainAsync(original, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        Assert.True(completion.IsCanceled);
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Caller_stop_does_not_abandon_held_provider_close_or_mark_clean()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var held = NewCompletion();
        Task? completion = null;
        try
        {
            completion = writer.CompleteAfterOriginalDrainAsync(held.Task, stop.Token);
            stop.Cancel();
            Assert.False(completion.IsCompleted);
            Assert.False(ReadClean());
            held.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
            Assert.False(ReadClean());
        }
        finally { held.TrySetResult(); await ObserveAsync(completion); }
    }

    [Fact]
    public async Task Preparation_retains_all_actual_audit_faults_and_does_not_auto_retry()
    {
        var first = new IOException("audit write");
        var second = new InvalidOperationException("audit close");
        var audit = NewCompletion();
        audit.SetException([first, second]);
        await using var diagnostics = new ControlledDiagnostics("shutdown-prepared", audit.Task);
        var owner = await StartedAsync(diagnostics);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => owner.PrepareFinalCleanWriterAsync(CancellationToken.None));
        Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, first));
        Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, second));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.PrepareFinalCleanWriterAsync(CancellationToken.None));
        Assert.Equal(1, diagnostics.ControlledWrites);
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Legacy_clean_diagnostic_failure_precedes_any_clean_store_effect()
    {
        var cause = new IOException("legacy diagnostic write failed");
        await using var diagnostics = new ControlledDiagnostics("clean-shutdown", Task.FromException(cause));
        var owner = await StartedAsync(diagnostics);
        var before = await File.ReadAllTextAsync(StatePath);
        var failure = await Assert.ThrowsAsync<IOException>(() => owner.MarkCleanShutdownAsync(CancellationToken.None));
        Assert.Same(cause, failure);
        Assert.Equal(before, await File.ReadAllTextAsync(StatePath));
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Later_actual_startup_replaces_run_and_old_writer_cannot_overwrite_it()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        var next = await StartedAsync(diagnostics);
        var before = await File.ReadAllTextAsync(StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAfterOriginalDrainAsync(Task.CompletedTask, CancellationToken.None));
        Assert.Equal(before, await File.ReadAllTextAsync(StatePath));
        Assert.Equal(next.Current.RunId, ReadState()["currentRun"]!["id"]!.GetValue<string>());
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Same_run_id_with_rewritten_original_metadata_cannot_mark_clean()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        var changed = ReadState();
        changed["currentRun"]!["startedAt"] = DateTimeOffset.UtcNow.AddDays(-3);
        await File.WriteAllTextAsync(StatePath, changed.ToJsonString());
        var before = await File.ReadAllTextAsync(StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAfterOriginalDrainAsync(Task.CompletedTask, CancellationToken.None));
        Assert.Equal(before, await File.ReadAllTextAsync(StatePath));
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Strict_final_read_keeps_malformed_state_and_does_not_use_disposed_diagnostics()
    {
        var diagnostics = new ProductionDiagnostics(_paths);
        try
        {
            var owner = await StartedAsync(diagnostics);
            var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
            await diagnostics.DisposeAsync();
            await File.WriteAllTextAsync(StatePath, "{broken-original");
            await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => writer.CompleteAfterOriginalDrainAsync(Task.CompletedTask, CancellationToken.None));
            Assert.Equal("{broken-original", await File.ReadAllTextAsync(StatePath));
            Assert.Empty(Directory.GetFiles(_paths.DataDirectory, "startup-recovery.json.corrupt-*"));
            Assert.Empty(Directory.GetFiles(_paths.DataDirectory, "startup-recovery.json.final-*"));
        }
        finally { await diagnostics.DisposeAsync(); }
    }

    [Fact]
    public async Task Original_store_lease_prevents_legacy_reset_before_owned_release()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var reset = new CleanResetStartupRecoveryCoordinator(owner, _paths);
        using var stop = new CancellationTokenSource();
        var lockPath = StatePath + ".writer-lock";
        FileStream? held = null;
        Task? actualReset = null;
        try
        {
            held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            actualReset = reset.MarkCleanShutdownAsync(stop.Token);
            Assert.False(actualReset.IsCompleted);
            Assert.True(File.Exists(StatePath));
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actualReset);
            Assert.True(File.Exists(StatePath));
        }
        finally
        {
            stop.Cancel();
            try { if (held is not null) await held.DisposeAsync(); }
            finally { await ObserveAsync(actualReset); }
        }
        await reset.MarkCleanShutdownAsync(CancellationToken.None);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task Held_original_audit_finishes_before_writer_is_exposed()
    {
        var audit = NewCompletion();
        await using var diagnostics = new ControlledDiagnostics("shutdown-prepared", audit.Task);
        var owner = await StartedAsync(diagnostics);
        Task<IStartupRecoveryFinalCleanWriter>? preparation = null;
        try
        {
            preparation = owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
            Assert.Equal(1, diagnostics.ControlledWrites);
            Assert.False(preparation.IsCompleted);
            Assert.False(ReadClean());
            audit.SetResult();
            var writer = await preparation;
            await writer.CompleteAfterOriginalDrainAsync(Task.CompletedTask, CancellationToken.None);
            Assert.True(ReadClean());
        }
        finally { audit.TrySetResult(); await ObserveAsync(preparation); }
    }

    [Fact]
    public async Task Missing_current_store_refuses_without_recreating_state_or_quarantining()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = await StartedAsync(diagnostics);
        var writer = await owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        File.Delete(StatePath);
        await Assert.ThrowsAsync<FileNotFoundException>(() => writer.CompleteAfterOriginalDrainAsync(Task.CompletedTask, CancellationToken.None));
        Assert.False(File.Exists(StatePath));
        Assert.Empty(Directory.GetFiles(_paths.DataDirectory, "startup-recovery.json.final-*"));
        Assert.Empty(Directory.GetFiles(_paths.DataDirectory, "startup-recovery.json.corrupt-*"));
    }

    [Fact]
    public async Task Direct_audit_cancellation_before_any_Task_is_a_retained_fault()
    {
        var cause = new OperationCanceledException("direct diagnostic acquisition has no original canceled Task");
        await using var diagnostics = new ControlledDiagnostics("shutdown-prepared", Task.CompletedTask, cause);
        var owner = await StartedAsync(diagnostics);
        var actual = owner.PrepareFinalCleanWriterAsync(CancellationToken.None);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, cause));
        Assert.True(actual.IsFaulted);
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Direct_audit_compound_failure_before_Task_keeps_every_original_member()
    {
        var first = new IOException("direct write acquisition");
        var second = new InvalidOperationException("direct acquisition cleanup");
        var cause = new AggregateException(first, second);
        await using var diagnostics = new ControlledDiagnostics("shutdown-prepared", Task.CompletedTask, cause);
        var owner = await StartedAsync(diagnostics);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => owner.PrepareFinalCleanWriterAsync(CancellationToken.None));
        Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, first));
        Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, second));
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Earlier_owner_cannot_adopt_later_run_during_startup_completion()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var earlier = new StartupRecoveryCoordinator(_paths, diagnostics);
        await earlier.BeginStartupAsync(CancellationToken.None);
        var later = await StartedAsync(diagnostics);
        var before = await File.ReadAllTextAsync(StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => earlier.MarkStartupCompletedAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => earlier.PrepareFinalCleanWriterAsync(CancellationToken.None));
        Assert.Equal(before, await File.ReadAllTextAsync(StatePath));
        Assert.Equal(later.Current.RunId, ReadState()["currentRun"]!["id"]!.GetValue<string>());
        Assert.False(ReadClean());
    }

    [Fact]
    public async Task Startup_completion_refuses_same_run_with_rewritten_original_history()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var owner = new StartupRecoveryCoordinator(_paths, diagnostics);
        await owner.BeginStartupAsync(CancellationToken.None);
        var changed = ReadState();
        changed["recentUncleanStarts"]!.AsArray().Add(DateTimeOffset.UtcNow.AddMinutes(-1));
        await File.WriteAllTextAsync(StatePath, changed.ToJsonString());
        var before = await File.ReadAllTextAsync(StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.MarkStartupCompletedAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.PrepareFinalCleanWriterAsync(CancellationToken.None));
        Assert.Equal(before, await File.ReadAllTextAsync(StatePath));
        Assert.False(ReadClean());
    }

    private async Task<StartupRecoveryCoordinator> StartedAsync(IProductionDiagnostics diagnostics)
    {
        var owner = new StartupRecoveryCoordinator(_paths, diagnostics);
        await owner.BeginStartupAsync(CancellationToken.None);
        await owner.MarkStartupCompletedAsync(CancellationToken.None);
        return owner;
    }

    private JsonObject ReadState() => JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
    private bool ReadClean() => ReadState()["currentRun"]!["cleanShutdown"]!.GetValue<bool>();
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task ObserveAsync(Task? task)
    {
        // Test finally observes the SAME task; the owning assertion separately verifies its exact faults.
        if (task is null) return;
        try { await task; } catch { }
    }

    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        _paths.Dispose();
    }

    private sealed class ControlledDiagnostics(string eventName, Task original, Exception? directFailure = null) : IProductionDiagnostics
    {
        public int ControlledWrites { get; private set; }
        public ValueTask WriteAsync(ReliabilitySeverity severity, string component, string actualEventName,
            string message, IReadOnlyDictionary<string, string>? data = null, string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            if (actualEventName != eventName) return ValueTask.CompletedTask;
            ControlledWrites++;
            if (directFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(directFailure).Throw();
            return new ValueTask(original);
        }
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Array.Empty<ReliabilityEvent>());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-original-final-clean-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
    }
}
