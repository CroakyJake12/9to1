using System.Security.Cryptography;
using System.Collections.Concurrent;
using Haven.Application;
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

    internal async Task<NativeHomeFilesBoardReadLease> ReadSelectedAsync(FilesNativeBrowserPage page,
        HostedItemMetadata row, Func<bool> lifetime, Func<CancellationToken, Task<bool>> homeAuthorityCurrent,
        Action<Action> withinOriginalSource, Action<Task> retainOriginalTask,
        CloudflareOriginalTaskLedger originals,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(originals);
        if (page.IsTrash || page.Items.All(item => !ReferenceEquals(item, row)) ||
            !FilesNativeBrowserService.IsBoardDocumentCandidate(row) || row.ParentId?.Value != page.ParentID)
            throw new UnauthorizedAccessException("Select an actual current board document from Home Library.");

        var readTask = originals.Invoke(() => browser.ReadOriginalBoardDocumentWithinSourceAsync(page, row,
            actor, lifetime, withinOriginalSource, retainOriginalTask, token));
        var read = await originals.AwaitAsync(readTask).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!lifetime() || read.StoreId != page.StoreID || read.FileId != row.Id.Value ||
            read.ParentFolderId != row.ParentId!.Value.Value || read.RevisionId != row.CurrentRevisionId!.Value.Value ||
            read.Bytes.Length != row.SizeBytes)
        {
            CryptographicOperations.ZeroMemory(read.Bytes);
            throw new UnauthorizedAccessException("The original Home Library board selection changed during its read.");
        }
        if (!await homeAuthorityCurrent(token).ConfigureAwait(false))
        {
            CryptographicOperations.ZeroMemory(read.Bytes);
            throw new UnauthorizedAccessException("The original Home profile changed while reading the selected board.");
        }
        var lease = new NativeHomeFilesBoardReadLease(read, actor, lifetime, homeAuthorityCurrent);
        CryptographicOperations.ZeroMemory(read.Bytes);
        return lease;
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
        var leases = _prepared.ToArray();
        foreach (var pair in leases)
            if (_prepared.TryRemove(pair.Key, out var lease)) await lease.DisposeAsync().ConfigureAwait(false);
    }
}
