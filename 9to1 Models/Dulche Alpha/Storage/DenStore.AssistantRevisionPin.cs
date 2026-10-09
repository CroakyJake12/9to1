using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace NineToOne.Dulche.Den;

public sealed partial class DenStore
{
    /// <summary>Read-only exclusion over this store's existing writer lease. This is
    /// physical revision custody, not an Assistant, Home or destination WRITE grant.
    /// Obtain actual user approval before entry. Never recursively open/write this Den
    /// or enter Home ownership observation while the returned pin is held.</summary>
    public async Task<AssistantRevisionPin> PinOriginalAssistantRevisionsAsync(
        AgentDefinitionRecord expectedDefinition, SessionRecord expectedSession,
        IDenAccessPolicy originalAccess, string originalPrincipal,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        Action<AssistantRevisionPin>? captureOriginalPin, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expectedDefinition); ArgumentNullException.ThrowIfNull(expectedSession);
        ArgumentNullException.ThrowIfNull(originalAccess); ArgumentException.ThrowIfNullOrWhiteSpace(originalPrincipal);
        var source = new AssistantPinSource(originalSynchronousScope, retainOriginalTask);
        var definition = source.Invoke(() => JsonSerializer.SerializeToUtf8Bytes<DenRecord>(expectedDefinition, DenJson.Options));
        var session = source.Invoke(() => JsonSerializer.SerializeToUtf8Bytes<DenRecord>(expectedSession, DenJson.Options));
        if (expectedDefinition.NamespaceId != expectedSession.NamespaceId)
            throw new DenException(DenErrorCode.Forbidden, "The exact original definition and membership must share a namespace.");
        source.Invoke(() => { ThrowIfUnavailableForWrite(); ValidateRecord(expectedDefinition); ValidateRecord(expectedSession); return 0; });
        var entered = false; AssistantRevisionPin? pin = null; var errors = new List<Exception>();
        try
        {
            // Original personal policy can read Home ownership. Complete those
            // admissions BEFORE the Den writer lease; the product's later held Home
            // entry must independently check its SAME captured Den ownership receipt.
            await DemandReadAsync(expectedDefinition).ConfigureAwait(false);
            await DemandReadAsync(expectedSession).ConfigureAwait(false);
            await source.Take(() => EnterLockAsync(token), () => entered = true).ConfigureAwait(false);
            await DemandExactAsync(expectedDefinition, definition).ConfigureAwait(false);
            await DemandExactAsync(expectedSession, session).ConfigureAwait(false);
            source.Invoke(() =>
            {
                ThrowIfUnavailableForWrite();
                pin = new AssistantRevisionPin(this, _heldFileLock!, expectedDefinition.Id,
                    expectedDefinition.Revision, expectedSession.Id, expectedSession.Revision);
                // Ownership is captured before external publication can reject it.
                captureOriginalPin?.Invoke(pin);
                return 0;
            });
        }
        catch (Exception failure) { errors.Add(failure); }
        try { await source.JoinAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
        if (errors.Count != 0)
        {
            if (pin is not null)
                try { await pin.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            else if (entered)
                try { ExitLock(); } catch (Exception failure) { errors.Add(failure); }
            AssistantPinSource.Throw(errors);
        }
        return pin ?? throw new InvalidOperationException("No original revision pin was captured.");

        async Task DemandExactAsync(DenRecord expected, byte[] expectedBytes)
        {
            ValidateSegment(expected.NamespaceId, "namespace"); ValidateSegment(expected.Id, "record ID");
            var bytes = await source.Take(() => File.ReadAllBytesAsync(RecordPath(expected.NamespaceId, expected.Id), token)).ConfigureAwait(false);
            source.Invoke(() =>
            {
                var actual = JsonSerializer.Deserialize<DenRecord>(bytes, DenJson.Options)
                    ?? throw new DenException(DenErrorCode.InvalidRecord, "The pinned original record is empty.");
                ValidateRecord(actual);
                if (!JsonSerializer.SerializeToUtf8Bytes<DenRecord>(actual, DenJson.Options).AsSpan().SequenceEqual(expectedBytes))
                    throw new DenException(DenErrorCode.Conflict, "The original Assistant definition or membership changed before the writer pin.", recoverable: true, retryable: true);
                return 0;
            });
        }
        async Task DemandReadAsync(DenRecord expected)
        {
            if (!await source.Take(() => originalAccess.IsAllowedAsync(originalPrincipal, expected.NamespaceId,
                    expected.Id, DenPermission.Read, token).AsTask()).ConfigureAwait(false))
                throw new DenException(DenErrorCode.Forbidden, "The original Den policy does not permit this revision pin.");
        }
    }

    public sealed class AssistantRevisionPin : IAsyncDisposable
    {
        private readonly DenStore _store;
        private readonly FileStream _originalLock;
        private readonly object _gate = new();
        private Task? _close;
        internal AssistantRevisionPin(DenStore store, FileStream originalLock, string definitionId,
            long definitionRevision, string sessionId, long sessionRevision)
        { _store = store; _originalLock = originalLock; DefinitionId = definitionId; DefinitionRevision = definitionRevision; SessionId = sessionId; SessionRevision = sessionRevision; }
        public string DefinitionId { get; }
        public long DefinitionRevision { get; }
        public string SessionId { get; }
        public long SessionRevision { get; }
        public bool IsOriginalStore(DenStore store) => ReferenceEquals(_store, store);
        public void DemandOriginalPinnedRevisions()
        {
            lock (_gate)
            {
                if (_close is not null || Volatile.Read(ref _store._disposed) || !ReferenceEquals(_store._heldFileLock, _originalLock) ||
                    _originalLock.SafeFileHandle.IsClosed || _originalLock.SafeFileHandle.IsInvalid)
                    throw new ObjectDisposedException(nameof(AssistantRevisionPin), "The actual original revision lease is no longer held.");
            }
        }
        public Task CloseAndDrainAsync()
        {
            lock (_gate)
            {
                if (_close is not null) return _close;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = completion.Task; // cache before actual release; never replay a failed release
                try
                {
                    if (!ReferenceEquals(_store._heldFileLock, _originalLock))
                        throw new InvalidOperationException("The original Den writer lease was replaced while pinned.");
                    _store.ExitLock(); completion.SetResult();
                }
                catch (Exception failure) { completion.SetException(failure); }
                return _close;
            }
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    }

    private sealed class AssistantPinSource(Action<Action> scope, Action<Task> retain)
    {
        private readonly List<Task> _tasks = [];
        internal T Invoke<T>(Func<T> body)
        {
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            T result = default!; Exception? inside = null; Exception? protocol = null; Exception? outer = null;
            try
            {
                scope(() =>
                {
                    if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                    {
                        var failure = new InvalidOperationException("The original Den pin callback expired, repeated or moved threads.");
                        Interlocked.CompareExchange(ref protocol, failure, null); throw failure;
                    }
                    try { result = body(); } catch (Exception failure) { inside = failure; throw; }
                });
            }
            catch (Exception failure) { outer = failure; }
            finally { Volatile.Write(ref active, 0); }
            var failures = new List<Exception>();
            foreach (var failure in new[] { inside, protocol, outer })
                if (failure is not null && !failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
            if (used != 1 && protocol is null) failures.Add(new InvalidOperationException("The original Den pin callback was not entered exactly once."));
            Throw(failures); return result;
        }
        internal async Task<T> Take<T>(Func<Task<T>> create, Action<T>? capture = null)
        {
            Task<T>? actual = null; Exception? publication = null;
            try { Invoke(() => { actual = create(); _tasks.Add(actual); retain(actual); return 0; }); }
            catch (Exception failure) { publication = failure; }
            T result = default!; var errors = new List<Exception>();
            if (actual is not null)
                try { result = await actual.ConfigureAwait(false); capture?.Invoke(result); }
                catch (Exception failure) { Capture(errors, actual, failure); }
            if (publication is not null) errors.Add(publication);
            Throw(errors); return result;
        }
        internal async Task Take(Func<Task> create, Action capture)
        {
            Task? actual = null; Exception? publication = null;
            try { Invoke(() => { actual = create(); _tasks.Add(actual); retain(actual); return 0; }); }
            catch (Exception failure) { publication = failure; }
            var errors = new List<Exception>();
            if (actual is not null)
                try { await actual.ConfigureAwait(false); capture(); }
                catch (Exception failure) { Capture(errors, actual, failure); }
            if (publication is not null) errors.Add(publication);
            Throw(errors);
        }
        internal async Task JoinAsync()
        {
            var errors = new List<Exception>();
            foreach (var task in _tasks)
                try { await task.ConfigureAwait(false); } catch (Exception failure) { Capture(errors, task, failure); }
            Throw(errors);
        }
        private static void Capture(List<Exception> errors, Task original, Exception failure)
        { if (original.Exception is { } aggregate) errors.AddRange(aggregate.InnerExceptions); else errors.Add(failure); }
        internal static void Throw(List<Exception> errors)
        { if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw(); if (errors.Count > 1) throw new AggregateException("Original Den revision pin acquisition or release failed.", errors); }
    }
}
