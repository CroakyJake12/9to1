using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Keeps the original startup-write witness alive after borrowed diagnostics retire.
/// This is a recovery-store writer, never permission, actor or presentation authority.
/// </summary>
public sealed partial class StartupRecoveryCoordinator : IStartupRecoveryFinalCleanWriterSource
{
    private Task? _originalStartupWrite;
    private StartupState? _originalStartupState;
    private Task? _latestStateWrite;
    private Task? _acknowledgedStateWrite;
    private StartupState? _acknowledgedState;
    private bool _lastStoreMutationAcknowledged;
    private bool _startupBegan;
    private bool _startupCompleted;
    private bool _finalWriterPreparationAttempted;

    /// <summary>
    /// Audits while diagnostics are alive and issues a writer for this exact acknowledged run.
    /// The host must prepare only after its actual startup originals finish, before teardown.
    /// </summary>
    public async Task<IStartupRecoveryFinalCleanWriter> PrepareFinalCleanWriterAsync(CancellationToken cancellationToken)
    {
        StartupState expected;
        Task startupWrite;
        Task stateWrite;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_finalWriterPreparationAttempted)
                throw new InvalidOperationException("A final-clean writer was already attempted for this startup original.");
            if (!_lastStoreMutationAcknowledged || !_startupBegan || !_startupCompleted || _originalStartupWrite is not { IsCompletedSuccessfully: true }
                || _acknowledgedStateWrite is not { IsCompletedSuccessfully: true }
                || !ReferenceEquals(_latestStateWrite, _acknowledgedStateWrite)
                || _acknowledgedState?.CurrentRun is not { StartupCompleted: true, CleanShutdown: false }
                || !BelongsToOriginalStartup(_acknowledgedState))
                throw new InvalidOperationException("Final clean requires the original acknowledged startup and completion writes.");
            _finalWriterPreparationAttempted = true;
            expected = _acknowledgedState!;
            startupWrite = _originalStartupWrite!;
            stateWrite = _acknowledgedStateWrite!;
        }
        finally { _gate.Release(); }

        // No provider callback is invoked while the store/state gates are held.
        var audit = AcquireRecoveryAuditOriginal(() => diagnostics.WriteAsync(ReliabilitySeverity.Information, "startup", "shutdown-prepared",
            "Haven prepared to join its original shutdown work before recording a clean exit.",
            new Dictionary<string, string> { ["runId"] = expected.CurrentRun!.Id },
            expected.CurrentRun.Id, cancellationToken));
        await AwaitRecoveryOriginalAsync(audit).ConfigureAwait(false);
        return new FinalCleanWriter(this, expected, startupWrite, stateWrite, audit);
    }

    /// <summary>
    /// Serializes the existing legacy reset under the same participating store fence.
    /// The desktop's prepared final path does not call this early reset.
    /// </summary>
    public async Task ClearLegacyCleanShutdownAsync(string expectedStatePath, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetFullPath(expectedStatePath), Path.GetFullPath(_statePath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The legacy reset does not belong to this recovery store.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastStoreMutationAcknowledged = false;
            await WithRecoveryStoreLeaseAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(_statePath)) File.Delete(_statePath);
                if (File.Exists(_statePath + ".bak")) File.Delete(_statePath + ".bak");
                return Task.FromResult(true);
            }, cancellationToken).ConfigureAwait(false);
            RuntimeSafetyState.DisableSafeMode();
        }
        finally { _gate.Release(); }
    }

    private async Task WriteAcknowledgedStateAsync(StartupState state, bool startupOriginal, CancellationToken cancellationToken)
    {
        var originalWrite = WriteAsync(state, cancellationToken);
        _latestStateWrite = originalWrite;
        if (startupOriginal) _originalStartupWrite = originalWrite;
        await AwaitRecoveryOriginalAsync(originalWrite).ConfigureAwait(false);
        _acknowledgedState = state with { RecentUncleanStarts = state.RecentUncleanStarts.ToArray() };
        _acknowledgedStateWrite = originalWrite;
        if (startupOriginal) _originalStartupState = _acknowledgedState;
    }

    private async Task FinalizeOriginalCleanAsync(StartupState expected, Task startupWrite, Task stateWrite,
        Task audit, Task actualRequiredOriginalDrain, CancellationToken cancellationToken)
    {
        // Caller cancellation cannot abandon the admitted host/provider close original.
        await AwaitRecoveryOriginalAsync(actualRequiredOriginalDrain).ConfigureAwait(false);
        await AwaitRecoveryOriginalAsync(audit).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_lastStoreMutationAcknowledged || !ReferenceEquals(_originalStartupWrite, startupWrite)
                || !ReferenceEquals(_latestStateWrite, stateWrite)
                || !ReferenceEquals(_acknowledgedStateWrite, stateWrite)
                || !StateEquals(_acknowledgedState, expected) || !BelongsToOriginalStartup(expected))
                throw new InvalidOperationException("The original startup writer is no longer current.");

            _lastStoreMutationAcknowledged = false;
            await WithRecoveryStoreLeaseAsync(async () =>
            {
                var current = await ReadStrictRecoveryStateAsync(cancellationToken).ConfigureAwait(false);
                if (!StateEquals(current, expected))
                    throw new InvalidOperationException("The recovery store no longer contains the original acknowledged run.");
                cancellationToken.ThrowIfCancellationRequested();
                var clean = expected with
                {
                    CurrentRun = expected.CurrentRun! with { StartupCompleted = true, CleanShutdown = true },
                    RecentUncleanStarts = Array.Empty<DateTimeOffset>()
                };
                await WriteRecoveryStateOwnedAsync(clean, requireExisting: true, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken, allowDirectoryCreation: false).ConfigureAwait(false);
            // A successful acknowledgement includes the actual participating lease release.
            // No borrowed diagnostic or provider is used after the final atomic commit.
            RuntimeSafetyState.DisableSafeMode();
        }
        finally { _gate.Release(); }
    }

    private bool BelongsToOriginalStartup(StartupState? state) =>
        state?.CurrentRun is { } run && _originalStartupState?.CurrentRun is { } original
        && state.Version == _originalStartupState.Version
        && run.Id == original.Id && run.StartedAt == original.StartedAt
        && state.RecentUncleanStarts is not null
        && state.RecentUncleanStarts.SequenceEqual(_originalStartupState.RecentUncleanStarts);

    private static bool StateEquals(StartupState? left, StartupState right) =>
        left is not null && left.Version == right.Version && left.CurrentRun == right.CurrentRun
        && left.RecentUncleanStarts is not null
        && left.RecentUncleanStarts.SequenceEqual(right.RecentUncleanStarts);

    private async Task<StartupState> ReadStrictRecoveryStateAsync(CancellationToken cancellationToken)
    {
        FileStream? stream = null;
        Task<StartupState?>? read = null;
        StartupState? state = null;
        var errors = new List<Exception>();
        var canceled = false;
        try
        {
            stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            read = JsonSerializer.DeserializeAsync<StartupState>(stream, JsonOptions, cancellationToken).AsTask();
            state = await read.ConfigureAwait(false);
            if (state is null || state.Version != 1 || state.CurrentRun is null || state.RecentUncleanStarts is null)
                throw new InvalidDataException("The original recovery state is missing or malformed.");
        }
        catch (Exception error)
        {
            CaptureRecoveryErrors(errors, read, error);
            canceled = read?.IsCanceled == true;
        }
        if (stream is not null)
        {
            Task? close = null;
            try { close = stream.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
            catch (Exception error) { CaptureRecoveryErrors(errors, close, error); canceled = false; }
        }
        ThrowRecoveryErrors(errors, canceled);
        return state!;
    }

    private async Task WriteRecoveryStateOwnedAsync(StartupState state, bool requireExisting, CancellationToken cancellationToken)
    {
        // The directory already belongs to the actual successful startup write. No late setup.
        var temporary = _statePath + ".final-" + Guid.NewGuid().ToString("N");
        FileStream? stream = null;
        var ownsTemporary = false;
        var errors = new List<Exception>();
        var canceled = false;
        Task? original = null;
        try
        {
            stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            ownsTemporary = true;
            original = JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            await original.ConfigureAwait(false);
            original = stream.FlushAsync(cancellationToken);
            await original.ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception error)
        {
            CaptureRecoveryErrors(errors, original, error);
            canceled = original?.IsCanceled == true;
        }
        if (stream is not null)
        {
            Task? close = null;
            try { close = stream.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
            catch (Exception error) { CaptureRecoveryErrors(errors, close, error); canceled = false; }
        }
        if (errors.Count == 0)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Participating writers hold the same lease through read/check/stage/swap.
                // Nonparticipating external writers are outside that exclusion contract.
                if (requireExisting || File.Exists(_statePath))
                    File.Replace(temporary, _statePath, _statePath + ".bak", ignoreMetadataErrors: true);
                else File.Move(temporary, _statePath);
                ownsTemporary = false;
            }
            catch (Exception error) { CaptureRecoveryErrors(errors, null, error); canceled = false; }
        }
        if (ownsTemporary)
        {
            try { File.Delete(temporary); }
            catch (Exception error) { CaptureRecoveryErrors(errors, null, error); canceled = false; }
        }
        // If native replacement acted then threw, the physical outcome is uncertain.
        // Neither an empty staging path nor an exception establishes no effect or clean acknowledgement.
        ThrowRecoveryErrors(errors, canceled);
    }

    private async Task<T> WithRecoveryStoreLeaseAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken, bool allowDirectoryCreation = true)
    {
        var lease = await AcquireRecoveryStoreLeaseAsync(cancellationToken, allowDirectoryCreation).ConfigureAwait(false);
        Task<T>? original = null;
        T? result = default;
        var errors = new List<Exception>();
        var canceled = false;
        try { original = operation(); result = await original.ConfigureAwait(false); }
        catch (Exception error)
        {
            CaptureRecoveryErrors(errors, original, error);
            canceled = original?.IsCanceled == true;
        }
        Task? close = null;
        try { close = lease.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
        catch (Exception error) { CaptureRecoveryErrors(errors, close, error); canceled = false; }
        ThrowRecoveryErrors(errors, canceled);
        return result!;
    }

    private async Task<FileStream> AcquireRecoveryStoreLeaseAsync(CancellationToken cancellationToken, bool allowDirectoryCreation)
    {
        if (allowDirectoryCreation) Directory.CreateDirectory(paths.DataDirectory);
        var lockPath = _statePath + ".writer-lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? lease = null;
            try
            {
                lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.Asynchronous);
                try
                {
                    using var unenforced = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite,
                        FileShare.None, 1, FileOptions.Asynchronous);
                    throw new IOException("Startup storage cannot enforce its exclusive writer lease.");
                }
                catch (IOException error) when (IsRecoveryLeaseContention(error)) { }
                cancellationToken.ThrowIfCancellationRequested();
                return lease;
            }
            catch (IOException error) when (IsRecoveryLeaseContention(error) && lease is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                var errors = new List<Exception>();
                CaptureRecoveryErrors(errors, null, error);
                if (lease is not null)
                {
                    Task? close = null;
                    try { close = lease.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
                    catch (Exception closeError) { CaptureRecoveryErrors(errors, close, closeError); }
                }
                ThrowRecoveryErrors(errors, allowCancellation: error is OperationCanceledException && errors.Count == 1);
                throw;
            }
        }
    }

    private static bool IsRecoveryLeaseContention(IOException error)
    {
        var code = error.HResult & 0xffff;
        return OperatingSystem.IsWindows() ? code is 32 or 33
            : OperatingSystem.IsMacOS() ? code == 35
            : (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) && code == 11;
    }

    private static Task AcquireRecoveryAuditOriginal(Func<ValueTask> acquisition)
    {
        try { return acquisition().AsTask(); }
        catch (Exception error)
        {
            // No original Task exists: an opaque OCE is a fault, not a proven canceled original.
            var errors = new List<Exception>();
            CaptureRecoveryErrors(errors, null, error);
            ThrowRecoveryErrors(errors, allowCancellation: false);
            throw;
        }
    }

    private static async Task AwaitRecoveryOriginalAsync(Task original)
    {
        try { await original.ConfigureAwait(false); }
        catch (Exception error) when (original.IsFaulted)
        {
            var errors = new List<Exception>();
            CaptureRecoveryErrors(errors, original, error);
            ThrowRecoveryErrors(errors, allowCancellation: false);
            throw;
        }
    }

    private static void CaptureRecoveryErrors(List<Exception> errors, Task? original, Exception observed)
    {
        if (original is { IsFaulted: true, Exception: { } fault })
        {
            foreach (var member in fault.InnerExceptions) AddRecoveryCause(errors, member);
        }
        else AddRecoveryCause(errors, observed);
    }

    private static void AddRecoveryCause(List<Exception> errors, Exception original)
    {
        if (original is AggregateException { InnerExceptions.Count: > 0 } aggregate)
        {
            foreach (var member in aggregate.InnerExceptions) AddRecoveryCause(errors, member);
        }
        else if (!errors.Any(existing => ReferenceEquals(existing, original))) errors.Add(original);
    }

    private static void ThrowRecoveryErrors(List<Exception> errors, bool allowCancellation)
    {
        if (errors.Count == 0) return;
        if (errors.Count == 1 && (allowCancellation || errors[0] is not OperationCanceledException))
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("Original recovery operation or cleanup failed; clean shutdown is unacknowledged.", errors);
    }

    private sealed class FinalCleanWriter(StartupRecoveryCoordinator owner, StartupState expected,
        Task startupWrite, Task stateWrite, Task audit) : IStartupRecoveryFinalCleanWriter
    {
        private readonly object _completionGate = new();
        private Task? _actualRequiredDrain;
        private Task? _completion;

        public Task CompleteAfterOriginalDrainAsync(Task actualRequiredOriginalDrain, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(actualRequiredOriginalDrain);
            lock (_completionGate)
            {
                if (_completion is not null)
                {
                    if (!ReferenceEquals(_actualRequiredDrain, actualRequiredOriginalDrain))
                        throw new InvalidOperationException("This writer is bound to another original shutdown drain.");
                    return _completion;
                }
                _actualRequiredDrain = actualRequiredOriginalDrain;
                return _completion = owner.FinalizeOriginalCleanAsync(expected, startupWrite, stateWrite,
                    audit, actualRequiredOriginalDrain, cancellationToken);
            }
        }
    }
}
