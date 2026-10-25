using System.Security.Cryptography;
using System.Collections.Concurrent;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>Read-only byte lease for one Home-selected original Files board revision.</summary>
internal sealed class NativeHomeFilesBoardReadLease : IAsyncDisposable
{
    private byte[]? _bytes;

    private readonly Func<bool> _sourceAlive;
    private readonly Func<CancellationToken, Task<bool>> _homeAuthorityCurrent;
    internal NativeHomeFilesBoardReadLease(FilesOriginalBoardDocumentRead read, AuthenticatedResourceActor actor,
        Func<bool> sourceAlive, Func<CancellationToken, Task<bool>> homeAuthorityCurrent)
    {
        StoreId = read.StoreId;
        FileId = read.FileId;
        ParentFolderId = read.ParentFolderId;
        RevisionId = read.RevisionId;
        Sha256 = read.Sha256;
        _bytes = read.Bytes.ToArray();
        ProfileId = actor.ProfileId;
        _sourceAlive = sourceAlive;
        _homeAuthorityCurrent = homeAuthorityCurrent;
    }

    internal Guid StoreId { get; }
    internal Guid FileId { get; }
    internal Guid ParentFolderId { get; }
    internal Guid RevisionId { get; }
    internal string Sha256 { get; }
    internal string ProfileId { get; }
    internal bool IsOpen => Volatile.Read(ref _bytes) is not null;
    internal ReadOnlyMemory<byte> Bytes => Volatile.Read(ref _bytes)
        ?? throw new ObjectDisposedException(nameof(NativeHomeFilesBoardReadLease));
    internal async Task DemandCurrentAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsOpen || !_sourceAlive() || !await _homeAuthorityCurrent(token).ConfigureAwait(false) || !IsOpen || !_sourceAlive())
            throw new UnauthorizedAccessException("The original Home Files board-read authority retired.");
    }

    public ValueTask DisposeAsync()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Consumes only a row already issued by the current Home Files Library page.
/// It never accepts a path or resolves a replacement row from caller supplied identifiers.</summary>
internal sealed class NativeHomeFilesBoardReadSource(FilesNativeBrowserService browser,
    AuthenticatedResourceActor actor)
{
    private readonly ConcurrentDictionary<Guid, NativeHomeFilesBoardReadLease> _prepared = new();
    private readonly object _originalReadGate = new();
    private readonly HashSet<Task> _originalReads = new(ReferenceEqualityComparer.Instance);
    internal sealed record ReadOutcome(NativeHomeFilesBoardReadLease? Lease, Exception? Refusal);

    internal async Task<NativeHomeFilesBoardReadLease> ReadSelectedAsync(FilesNativeBrowserPage page,
        HostedItemMetadata row, Func<bool> lifetime, Func<CancellationToken, Task<bool>> homeAuthorityCurrent,
        Action<Action> withinOriginalSource, Action<Task> retainOriginalTask,
        CloudflareOriginalTaskLedger originals,
        CancellationToken token)
    {
        var outcome = await ReadSelectedOutcomeAsync(page, row, lifetime, homeAuthorityCurrent,
            withinOriginalSource, retainOriginalTask, originals, token).ConfigureAwait(false);
        if (outcome.Refusal is { } refusal) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(refusal).Throw();
        return outcome.Lease ?? throw new InvalidDataException("The original Files board read returned no lease.");
    }

    internal async Task<ReadOutcome> ReadSelectedOutcomeAsync(FilesNativeBrowserPage page,
        HostedItemMetadata row, Func<bool> lifetime, Func<CancellationToken, Task<bool>> homeAuthorityCurrent,
        Action<Action> withinOriginalSource, Action<Task> retainOriginalTask,
        CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(originals);
        if (page.IsTrash || page.Items.All(item => !ReferenceEquals(item, row)) ||
            !FilesNativeBrowserService.IsBoardDocumentCandidate(row) || row.ParentId?.Value != page.ParentID)
            throw new UnauthorizedAccessException("Select an actual current board document from Home Library.");
        lock (_originalReadGate)
        {
            PruneOriginalReads();
            if (_originalReads.Count >= 256) throw new InvalidOperationException("Original Files board read failures remain retained.");
        }

        Task<FilesOriginalBoardDocumentRead>? acquired = null; Exception? admissionFailure = null;
        try
        {
            // Return metadata from the finite scope: this source owns the raw read,
            // and its caller retains the exact observation task below.
            originals.Invoke(() => {
                acquired = browser.ReadOriginalBoardDocumentWithinSourceAsync(page, row,
                    actor, lifetime, withinOriginalSource, task => {
                        RetainOriginalRead(task); retainOriginalTask(ObserveOriginalRead(task));
                    }, token);
                RetainOriginalRead(acquired); return true;
            });
        }
        catch (Exception cause) { admissionFailure = cause; }
        if (acquired is not { } readTask)
            throw admissionFailure ?? new InvalidOperationException("The original Files board read was not acquired.");
        ReadOutcome? outcome = null; Exception? readFailure = null;
        try { outcome = await originals.AwaitAsync(ObserveRead()).ConfigureAwait(false); }
        catch (Exception cause) { readFailure = cause; }
        if (admissionFailure is not null)
        {
            var failures = new List<Exception> { admissionFailure };
            if (readFailure is not null) failures.Add(readFailure);
            if (outcome?.Lease is { } held)
                try { await held.DisposeAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            throw new AggregateException("The original Files read acquisition/observation retained failures.", failures);
        }
        if (readFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(readFailure).Throw();
        return outcome ?? throw new InvalidDataException("The original Files read returned no observation.");

        async Task<ReadOutcome> ObserveRead()
        {
            FilesOriginalBoardDocumentRead read;
            try { read = await readTask.ConfigureAwait(false); }
            catch (Exception error) when (browser.IsOriginalBoardDocumentReadRefusal(readTask, error))
            {
                var refusal = error;
                while (refusal is AggregateException group) refusal = group.InnerExceptions[0];
                return new(null, refusal);
            }
            return new(await CreateLeaseAfterReadAsync(read, page, row, actor, lifetime, homeAuthorityCurrent, token).ConfigureAwait(false), null);
        }
    }

    private void RetainOriginalRead(Task actual)
    {
        lock (_originalReadGate)
        {
            PruneOriginalReads();
            _originalReads.Add(actual);
        }
    }
    private void PruneOriginalReads()
    {
        foreach (var task in _originalReads.Where(task => task.IsCompletedSuccessfully)) task.GetAwaiter().GetResult();
        _originalReads.RemoveWhere(task => task.IsCompletedSuccessfully || task.IsFaulted &&
            browser.IsOriginalBoardDocumentReadRefusal(task, task.Exception!));
    }
    private async Task ObserveOriginalRead(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) when (browser.IsOriginalBoardDocumentReadRefusal(actual, error)) { }
    }

    internal static async Task<NativeHomeFilesBoardReadLease> CreateLeaseAfterReadAsync(
        FilesOriginalBoardDocumentRead read, FilesNativeBrowserPage page, HostedItemMetadata row,
        AuthenticatedResourceActor actor, Func<bool> lifetime,
        Func<CancellationToken, Task<bool>> homeAuthorityCurrent, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!lifetime() || read.StoreId != page.StoreID || read.FileId != row.Id.Value ||
                read.ParentFolderId != row.ParentId!.Value.Value || read.RevisionId != row.CurrentRevisionId!.Value.Value ||
                read.Bytes.Length != row.SizeBytes)
                throw new UnauthorizedAccessException("The original Home Library board selection changed during its read.");
            if (!await homeAuthorityCurrent(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The original Home profile changed while reading the selected board.");
            return new NativeHomeFilesBoardReadLease(read, actor, lifetime, homeAuthorityCurrent);
        }
        finally
        {
            // The Files original-read result is a transport buffer. Only the detached lease copy
            // survives; cancellation, authority failure, or construction failure must clear it too.
            CryptographicOperations.ZeroMemory(read.Bytes);
        }
    }

    internal Guid RegisterPrepared(NativeHomeFilesBoardReadLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (_prepared.Count >= 64) throw new InvalidOperationException("Settle prepared Files board opens before opening more documents.");
        var id = Guid.NewGuid();
        if (!_prepared.TryAdd(id, lease)) throw new InvalidOperationException("The prepared board-read identity collided.");
        return id;
    }

    internal NativeHomeFilesBoardReadLease TakePrepared(Guid id, Guid storeId, Guid parentFolderId, Guid fileId, Guid revisionId)
    {
        if (id == Guid.Empty || storeId == Guid.Empty || parentFolderId == Guid.Empty || fileId == Guid.Empty || revisionId == Guid.Empty ||
            !_prepared.TryRemove(id, out var lease))
            throw new UnauthorizedAccessException("Open the exact current board row from Home Library first.");
        if (!lease.IsOpen || lease.StoreId != storeId || lease.ParentFolderId != parentFolderId ||
            lease.FileId != fileId || lease.RevisionId != revisionId)
        {
            lease.DisposeAsync().GetAwaiter().GetResult();
            throw new UnauthorizedAccessException("The prepared Home board selection does not match this exact Files revision.");
        }
        return lease;
    }

    internal Task DiscardPreparedAsync(Guid id) => _prepared.TryRemove(id, out var lease)
        ? lease.DisposeAsync().AsTask() : Task.CompletedTask;

    internal async Task DrainPreparedAsync()
    {
        var failures = new List<Exception>();
        var leases = _prepared.ToArray();
        foreach (var pair in leases)
            if (_prepared.TryRemove(pair.Key, out var lease))
                try { await lease.DisposeAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        Task[] original; lock (_originalReadGate) original = _originalReads.ToArray();
        foreach (var task in original)
            try { await task.ConfigureAwait(false); }
            catch (Exception cause) { if (!browser.IsOriginalBoardDocumentReadRefusal(task, cause)) failures.Add(task.Exception ?? cause); }
        if (failures.Count != 0) throw new AggregateException("The original Files board reads/prepared leases retained failures.", failures);
    }
}
