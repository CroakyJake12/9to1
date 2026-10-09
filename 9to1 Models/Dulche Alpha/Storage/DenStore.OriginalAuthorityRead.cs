using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace NineToOne.Dulche.Den;

public sealed partial class DenStore
{
    /// <summary>SAME non-writer authority reader and manifest predicates, with each raw
    /// parse and independent stream close retained inside the finite original callback.
    /// Neither the namespace snapshot nor callback interface grants Den access.</summary>
    private readonly object _authorityReadGate = new();
    private readonly List<AuthorityReadInvocation> _authorityReads = [];
    private readonly AsyncLocal<AuthorityReadInvocation?> _authorityExecuting = new();
    [ThreadStatic] private static HashSet<DenStore>? _authorityPhysical;
    private Task? _originalAuthorityDispose;
    private sealed class AuthorityReadInvocation(AuthorityReadSources sources)
    {
        internal readonly AuthorityReadSources Sources = sources;
        internal FileStream? Stream;
        internal Task? OriginalStreamClose;
        internal Task<DenAuthoritySnapshot> Driver = null!;
        internal bool IsHealthy => Driver.IsCompletedSuccessfully &&
            (Stream is null || OriginalStreamClose?.IsCompletedSuccessfully == true) && Sources.IsHealthy;
    }
    public Task<DenAuthoritySnapshot> ReadAuthoritySnapshotWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, Action<Action> cleanupScope, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain); ArgumentNullException.ThrowIfNull(cleanupScope);
        AuthorityReadInvocation invocation; TaskCompletionSource start;
        lock (_authorityReadGate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _originalAuthorityDispose is not null, this);
            _authorityReads.RemoveAll(value => value.IsHealthy);
            if (_authorityReads.Count >= 128) throw new InvalidOperationException("Settle actual failed Den authority reads before another acquisition.");
            var sources = new AuthorityReadSources(body => InvokeAuthorityPhysical(() => scope(body)), retain);
            invocation = new(sources); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            invocation.Driver = ReadAuthorityOriginalAfterStart(invocation, start.Task, cleanupScope, token);
            _authorityReads.Add(invocation);
        }
        // The full driver is rooted before the borrowed callback can start a close or fail.
        // Do not include this encompassing task in its own finite child ledger.
        try { InvokeAuthorityPhysical(() => retain(invocation.Driver)); }
        catch (Exception cause) { invocation.Sources.Add(cause); }
        finally { start.SetResult(); }
        return invocation.Driver;
    }
    private async Task<DenAuthoritySnapshot> ReadAuthorityOriginalAfterStart(AuthorityReadInvocation invocation,
        Task start, Action<Action> cleanupScope, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var prior = _authorityExecuting.Value; _authorityExecuting.Value = invocation;
        try { return await ReadAuthorityOriginalBody(invocation, cleanupScope, token).ConfigureAwait(false); }
        finally { _authorityExecuting.Value = prior; }
    }
    private async Task<DenAuthoritySnapshot> ReadAuthorityOriginalBody(AuthorityReadInvocation invocation,
        Action<Action> cleanupScope, CancellationToken token)
    {
        var originals = invocation.Sources;
        DenAuthoritySnapshot? snapshot = null; Exception? primary = null; Task? close = null;
        try
        {
            originals.Invoke(() =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                invocation.Stream = new FileStream(_manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (invocation.Stream.Length > 2 * 1024 * 1024)
                    throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest exceeds the supported size.");
                return true;
            });
            DenManifest current;
            try
            {
                current = await originals.Read(() => JsonSerializer.DeserializeAsync<DenManifest>(invocation.Stream!, DenJson.Options, token).AsTask()).ConfigureAwait(false)
                    ?? throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest is empty.");
            }
            catch (JsonException cause)
            {
                originals.Add(cause);
                throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest is invalid: " + cause.Message);
            }
            snapshot = originals.Invoke(() =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // Exact existing canonical reader validation. No writer lease acquisition.
                ValidateManifest(current, _root);
                if (current.DenId != Manifest.DenId || current.SchemaVersion != Manifest.SchemaVersion ||
                    CompareVersion(current.MinimumReaderVersion, CurrentVersion) > 0 ||
                    CompareVersion(current.MinimumWriterVersion, CurrentVersion) > 0)
                    throw new DenException(DenErrorCode.Conflict, "The canonical Den identity or schema changed; reopen for recovery.", recoverable: true);
                return new DenAuthoritySnapshot(current.DenId, current.Revision, Array.AsReadOnly(current.Namespaces.ToArray()));
            });
        }
        catch (Exception cause) { primary = cause; originals.Add(cause); }
        finally
        {
            if (invocation.Stream is { } actual)
            {
                // Attempt once. A task returned before a post-scope refusal is captured
                // here, joined below and never reacquired by retrying DisposeAsync.
                try { originals.InvokeCleanup(body => InvokeAuthorityPhysical(() => cleanupScope(body)), () => { close = invocation.OriginalStreamClose = actual.DisposeAsync().AsTask(); originals.Retain(close); }); }
                catch (Exception cause) { originals.Add(cause); }
                if (close is not null) try { await originals.Join(close).ConfigureAwait(false); } catch (Exception cause) { originals.Add(cause); }
                else originals.Add(new InvalidOperationException("The actual Den manifest stream close was not acknowledged; retain its source evidence."));
            }
        }
        await originals.Settle(primary).ConfigureAwait(false);
        return snapshot ?? throw new InvalidOperationException("No actual canonical authority snapshot returned.");
    }

    private void InvokeAuthorityPhysical(Action body)
    {
        var physical = _authorityPhysical ??= [];
        var entered = physical.Add(this);
        try { body(); } finally { if (entered) physical.Remove(this); }
    }
    private Task CloseOriginalAuthorityReadersAsync()
    {
        if (_authorityExecuting.Value is { } live && !live.Driver.IsCompleted ||
            _ownershipExecuting.Value is { } ownership && !ownership.Driver.IsCompleted ||
            _authorityPhysical?.Contains(this) == true)
            throw new InvalidOperationException("An actual Den manifest source cannot join its encompassing disposal.");
        TaskCompletionSource? start = null; Task actual;
        lock (_authorityReadGate)
        {
            if (_originalAuthorityDispose is null)
            {
                _disposed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _originalAuthorityDispose = CloseAuthorityReadersPublished(start.Task, _authorityReads.ToArray(), _ownershipObservations.ToArray());
            }
            actual = _originalAuthorityDispose;
        }
        start?.SetResult(); return actual;
    }
    private async Task CloseAuthorityReadersPublished(Task start, AuthorityReadInvocation[] actualReads,
        OwnershipObservationInvocation[] actualOwnership)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        foreach (var invocation in actualReads)
        {
            try { await invocation.Driver.ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(invocation.Driver.Exception ?? cause); }
            // Independently inspect and join actual cleanup receipt; never reacquire a
            // missing stream Dispose attempt. Failed sources remain rooted in DenStore.
            if (invocation.OriginalStreamClose is { } close)
                try { await close.ConfigureAwait(false); } catch (Exception cause) { failures.Add(close.Exception ?? cause); }
            else if (invocation.Stream is not null)
                failures.Add(new InvalidOperationException("The actual retained Den reader has no acknowledged stream close."));
            if (!invocation.IsHealthy && invocation.Driver.IsCompletedSuccessfully)
                failures.Add(new InvalidOperationException("The actual Den read has no full healthy source/cleanup proof."));
        }
        await JoinOriginalOwnershipObservations(actualOwnership, failures).ConfigureAwait(false);
        if (failures.Count != 0) throw new AggregateException("Actual Den authority readers failed; retained original resources require recovery.", failures);
    }

    private sealed class AuthorityReadSources(Action<Action> caller, Action<Task> retain)
    {
        private readonly object _gate = new(); private readonly List<Exception> _errors = []; private readonly List<Task> _tasks = [];
        internal bool IsHealthy { get { lock (_gate) return _errors.Count == 0 && _tasks.All(value => value.IsCompletedSuccessfully); } }
        internal void Add(Exception cause) { lock (_gate) if (!_errors.Any(value => ReferenceEquals(value, cause))) _errors.Add(cause); }
        internal void Retain(Task raw)
        {
            lock (_gate) if (!_tasks.Any(value => ReferenceEquals(value, raw))) _tasks.Add(raw);
            retain(raw); // Local custody always precedes borrowed publication.
        }
        private T InvokeOnce<T>(Action<Action> scope, Func<T> factory)
        {
            var thread = Environment.CurrentManagedThreadId; var active = 1; var used = 0; T result = default!;
            try
            {
                scope(() =>
                {
                    try
                    {
                        if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                            throw new InvalidOperationException("The actual Den manifest callback is foreign-thread, inactive or consumed.");
                        result = factory();
                    }
                    catch (Exception cause) { Add(cause); throw; }
                });
                if (used != 1) throw new InvalidOperationException("The actual Den manifest callback was not invoked."); return result;
            }
            finally { Interlocked.Exchange(ref active, 0); }
        }
        internal T Invoke<T>(Func<T> factory)
        {
            lock (_gate) if (_errors.Count != 0) throw new AggregateException("Prior actual manifest source failure refuses another productive factory.", _errors);
            try
            {
                var value = InvokeOnce(caller, factory);
                lock (_gate) if (_errors.Count != 0) throw new AggregateException("Actual manifest callback swallowed or replaced a source failure.", _errors);
                return value;
            }
            catch (Exception cause) { Add(cause); throw; }
        }
        internal void InvokeCleanup(Action<Action> cleanup, Action factory)
        { try { InvokeOnce(cleanup, () => { factory(); return true; }); } catch (Exception cause) { Add(cause); throw; } }
        internal async Task<T> Read<T>(Func<Task<T>> factory)
        {
            Task<T>? raw = null; T result = default!; Exception? invocation = null, observed = null;
            try { Invoke(() => { raw = factory(); Retain(raw); return true; }); } catch (Exception cause) { invocation = cause; Add(cause); }
            if (raw is not null) try { result = await raw.ConfigureAwait(false); } catch (Exception cause) { observed = cause; Capture(raw, cause); }
            if (invocation is null && observed is not null) ExceptionDispatchInfo.Capture(observed).Throw();
            if (invocation is not null || raw is null || !raw.IsCompletedSuccessfully)
            { lock (_gate) throw new AggregateException("Actual Den manifest invocation/raw parse failed.", _errors); }
            return result;
        }
        private void Capture(Task raw, Exception cause)
        { if (raw.Exception is { } group) foreach (var direct in group.InnerExceptions) Add(direct); else Add(cause); }
        internal async Task Join(Task raw)
        { try { await raw.ConfigureAwait(false); } catch (Exception cause) { Capture(raw, cause); throw; } }
        internal async Task Settle(Exception? primary)
        {
            Task[] tasks; lock (_gate) tasks = _tasks.ToArray();
            foreach (var raw in tasks) try { await Join(raw).ConfigureAwait(false); } catch (Exception cause) { Add(cause); }
            Exception[] errors; lock (_gate) errors = _errors.ToArray();
            // Cancellation is retained as an actual occurrence. A token or exception
            // class cannot erase an independent borrowed-scope OCE or cleanup sibling.
            if (errors.Length != 0) throw new AggregateException("Actual Den manifest parse and independent original close failed.", errors);
        }
    }
}
