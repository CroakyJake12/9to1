using Haven.Application;
using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Home.Core;

public sealed partial class HomeClaimedResourceCommitFence
{
    private readonly object _originalFenceGate = new();
    private readonly List<OriginalFenceOperation> _originalFenceOperations = [];
    private Task? _originalFenceClose;
    private OriginalFenceSources? _originalFenceCloseSources;
    private Task? _originalHomeRelease;
    private Task? _originalCompletionRelease;
    private bool _originalFenceRetiring;
    private bool _originalFenceScoped;
    private static readonly object OriginalCaptureGate = new();
    private static readonly List<OriginalFenceCapture> OriginalCaptures = [];

    private sealed class OriginalFenceCapture
    {
        internal OriginalFenceSources Sources = null!;
        internal Task<HomeClaimedResourceCommitFence?> Driver = null!;
        internal HomeClaimedResourceCommitFence? Fence;
    }
    private sealed class OriginalFenceOperation(OriginalFenceSources sources)
    {
        internal readonly OriginalFenceSources Sources = sources;
        internal Task<bool> Driver = null!;
    }

    /// <summary>The same claimed operation, ownership receipt and original configuration.
    /// Scope and raw-task custody are forwarded to each actual nested Home source.</summary>
    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureWithinOriginalSourceAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, string resourceKind, string originalStoreId,
        HomeResourceExecutionCapability originalCapability, AuthenticatedResourceActor originalActor,
        IReadOnlyList<HomeCoreStateRecord> originalConfigurationRecords, Func<bool> isOriginalLifetimeCurrent,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(originalConfigurationRecords); ArgumentNullException.ThrowIfNull(isOriginalLifetimeCurrent);
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var original = new OriginalFenceCapture(); original.Sources = new(original, scope, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (OriginalCaptureGate)
        {
            OriginalCaptures.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Sources.IsHealthy &&
                (value.Fence is null || value.Fence.OriginalClose?.IsCompletedSuccessfully == true));
            if (OriginalCaptures.Count >= 128) throw new InvalidOperationException("Actual unresolved Home fence captures remain retained.");
            original.Driver = Drive(); OriginalCaptures.Add(original);
        }
        original.Sources.Publish(original.Driver); begin.SetResult(); return new(original.Driver);

        async Task<HomeClaimedResourceCommitFence?> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var active = CloudflareOriginalExecutionGuard.EnterOriginal(original);
            HomeClaimedResourceCommitFence? result = null;
            try
            {
                original.Sources.DemandHealthy();
                var records = original.Sources.Invoke(() =>
                {
                    var values = new List<(string Id, byte[] Fingerprint)>();
                    foreach (var record in originalConfigurationRecords)
                    {
                        if (values.Count == 8 || record is null || string.IsNullOrWhiteSpace(record.RecordId) || values.Any(item => item.Id == record.RecordId)) return null;
                        values.Add((record.RecordId, SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record))));
                    }
                    return values.Count == 0 ? null : values.ToArray();
                });
                if (records is not null)
                    result = await original.Sources.Capture(() => CaptureCoreAsync(broker, store, profiles, ownership,
                        resourceKind, originalStoreId, originalCapability, originalActor, records,
                        isOriginalLifetimeCurrent, token, original.Sources).AsTask(), actual => original.Fence = actual).ConfigureAwait(false);
            }
            catch (Exception cause) { original.Sources.Add(cause); }
            await original.Sources.Settle().ConfigureAwait(false); return result;
        }
    }

    public ValueTask<bool> ValidateWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new OriginalFenceSources(this, scope, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OriginalFenceOperation operation;
        lock (_originalFenceGate)
        {
            ObjectDisposedException.ThrowIf(_originalFenceRetiring || _disposed, this);
            _originalFenceScoped = true;
            _originalFenceOperations.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Sources.IsHealthy);
            if (_originalFenceOperations.Count >= 128) throw new InvalidOperationException("Actual unresolved held-fence validations remain retained.");
            operation = new(sources); operation.Driver = Drive(); _originalFenceOperations.Add(operation);
        }
        sources.Publish(operation.Driver); begin.SetResult(); return new(operation.Driver);

        async Task<bool> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var active = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            bool result = false;
            try { sources.DemandHealthy(); result = await ValidateCoreAsync(token, sources).ConfigureAwait(false); }
            catch (Exception cause) { sources.Add(cause); }
            await sources.Settle().ConfigureAwait(false); return result;
        }
    }

    public Task? OriginalClose { get { lock (_originalFenceGate) return _originalFenceClose; } }
    public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

    public ValueTask DisposeWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        DemandExternalOriginalRetirementJoin(); Task actual; TaskCompletionSource? begin = null;
        lock (_originalFenceGate)
        {
            if (_originalFenceClose is null)
            {
                _originalFenceRetiring = true; _originalFenceCloseSources = new(this, scope, retain);
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _originalFenceClose = Close(begin.Task, _originalFenceOperations.ToArray(), _originalFenceCloseSources);
            }
            actual = _originalFenceClose;
        }
        if (begin is not null) { _originalFenceCloseSources!.Publish(actual); begin.SetResult(); }
        return new(actual);
    }

    private async Task Close(Task start, OriginalFenceOperation[] operations, OriginalFenceSources cleanup)
    {
        await start.ConfigureAwait(false);
        foreach (var original in operations)
        {
            await cleanup.Join(original.Driver).ConfigureAwait(false);
            try { await original.Sources.Settle().ConfigureAwait(false); } catch (Exception cause) { cleanup.Add(cause); }
        }
        bool held = false;
        try
        {
            await cleanup.Capture(() => WaitGate(_gate, CancellationToken.None), _ => held = true).ConfigureAwait(false);
            _disposed = true;
        }
        catch (Exception cause) { cleanup.Add(cause); }
        // Accepted releases are acquired independently even when the preceding original
        // or release failed. Actual lease objects remain on this owner after a fault.
        if (held)
        {
            try
            {
                if (_lease is not null && _originalHomeRelease is null)
                    await cleanup.Capture(() => (_lease as IHomeOriginalScopedLocalOperationLeaseCleanup
                        ?? throw new InvalidOperationException("The SAME Home lease lacks scoped original cleanup."))
                        .DisposeWithinOriginalSourceAsync(cleanup.Scope, cleanup.Retain).AsTask(), () => { },
                        raw => _originalHomeRelease = raw).ConfigureAwait(false);
            }
            catch (Exception cause) { cleanup.Add(cause); }
            try
            {
                if (_completionLease is not null && _originalCompletionRelease is null)
                    await cleanup.Capture(() => _completionLease.DisposeAsync().AsTask(), () => { },
                        raw => _originalCompletionRelease = raw).ConfigureAwait(false);
            }
            catch (Exception cause) { cleanup.Add(cause); }
            finally { _gate.Release(); }
        }
        await cleanup.Settle().ConfigureAwait(false);
    }

    // The full operation roots this source and all raw tasks before caller callbacks.
    // Failures are occurrence based; foreign groups remain intact.
    private sealed class OriginalFenceSources(object owner, Action<Action> caller, Action<Task> retain)
    {
        private readonly object _sync = new();
        private readonly List<Task> _raw = [];
        private readonly List<Exception> _errors = [];
        private readonly Dictionary<Task, Exception> _nonFaultedFailures = new(ReferenceEqualityComparer.Instance);
        internal bool IsHealthy { get { lock (_sync) return _errors.Count == 0 && _raw.All(value => value.IsCompletedSuccessfully); } }
        internal Action<Action> Scope => body => Invoke(() => { body(); return true; });
        internal void Add(Exception cause) { lock (_sync) if (!_errors.Any(value => ReferenceEquals(value, cause))) _errors.Add(cause); }
        internal void Retain(Task raw)
        {
            ArgumentNullException.ThrowIfNull(raw);
            lock (_sync) if (!_raw.Any(value => ReferenceEquals(value, raw))) _raw.Add(raw);
            CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { retain(raw); return true; });
        }
        internal void Publish(Task driver)
        {
            try { Invoke(() => { retain(driver); return true; }); }
            catch (Exception cause) { Add(cause); }
        }
        internal T Invoke<T>(Func<T> body)
        {
            int active = 1, used = 0, thread = Environment.CurrentManagedThreadId; T value = default!;
            List<Exception> local = [];
            void Record(Exception cause) { Add(cause); lock (local) if (!local.Any(value => ReferenceEquals(value, cause))) local.Add(cause); }
            try
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                {
                    caller(() =>
                    {
                        if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                        {
                            var refusal = new InvalidOperationException("The actual fence callback is inactive, foreign-thread or consumed.");
                            Record(refusal); throw refusal;
                        }
                        try { value = body(); } catch (Exception cause) { Record(cause); throw; }
                    });
                    if (Volatile.Read(ref used) == 0)
                    {
                        var refusal = new InvalidOperationException("The actual fence callback was not invoked."); Record(refusal); throw refusal;
                    }
                    Exception[] failures; lock (local) failures = local.ToArray();
                    if (failures.Length != 0) throw new AggregateException("The actual fence callback retained a swallowed protocol/body failure.", failures);
                    return true;
                });
                return value;
            }
            catch (Exception cause) { Add(cause); throw; }
            finally { Volatile.Write(ref active, 0); }
        }
        internal async Task<T> Read<T>(Func<Task<T>> factory) => await Capture(factory, _ => { }).ConfigureAwait(false);
        internal async Task<T> Capture<T>(Func<Task<T>> factory, Action<T> capture, Action<Task>? captureTask = null)
        {
            Task<T>? raw = null; T value = default!;
            try { Invoke(() => { raw = factory() ?? throw new InvalidOperationException("No actual fence source task returned."); captureTask?.Invoke(raw); Retain(raw); return true; }); }
            catch (Exception cause) { Add(cause); }
            if (raw is not null)
                try { value = await raw.ConfigureAwait(false); capture(value); }
                catch (Exception cause) { CaptureFailure(raw, cause); }
            DemandHealthyErrors(); return value;
        }
        internal async Task Capture(Func<Task> factory, Action capture, Action<Task>? captureTask = null)
        {
            Task? raw = null;
            try { Invoke(() => { raw = factory() ?? throw new InvalidOperationException("No actual fence cleanup task returned."); captureTask?.Invoke(raw); Retain(raw); return true; }); }
            catch (Exception cause) { Add(cause); }
            if (raw is not null)
                try { await raw.ConfigureAwait(false); capture(); }
                catch (Exception cause) { CaptureFailure(raw, cause); }
            DemandHealthyErrors();
        }
        internal async Task Join(Task raw)
        {
            try { await raw.ConfigureAwait(false); } catch (Exception cause) { CaptureFailure(raw, cause); }
        }
        private void CaptureFailure(Task raw, Exception cause)
        {
            if (raw.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); }
            else
            {
                Exception original;
                lock (_sync)
                {
                    if (!_nonFaultedFailures.TryGetValue(raw, out original!)) { original = cause; _nonFaultedFailures.Add(raw, cause); }
                }
                Add(original);
            }
        }
        internal void DemandHealthy() => DemandHealthyErrors();
        private void DemandHealthyErrors()
        {
            Exception[] errors; lock (_sync) errors = _errors.ToArray();
            if (errors.Length != 0) throw new AggregateException("Actual scoped Home fence sources failed.", errors);
        }
        internal async Task Settle()
        {
            Task[] raw; lock (_sync) raw = _raw.ToArray();
            foreach (var task in raw) await Join(task).ConfigureAwait(false);
            DemandHealthyErrors();
        }
    }
}

/// <summary>Optional cleanup forwarding on the same privately issued held Home lease.</summary>
public interface IHomeOriginalScopedLocalOperationLeaseCleanup : IHomeOriginalScopedLocalOperationLease
{
    ValueTask DisposeWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain);
}
