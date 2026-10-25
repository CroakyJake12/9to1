using System.Security.Cryptography;
using CakeOS.Apps.Boards.App;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>Home-selected same-file write lease. It retains the exact original READ buffer for
/// the editor and advances its expected revision only after Files and Home return a committed receipt.</summary>
internal sealed class NativeHomeFilesBoardWriteLease : IAsyncDisposable
{
    private readonly NativeHomeFilesBoardReadLease _read;
    private readonly Func<string, string, string, Guid, ReadOnlyMemory<byte>, CancellationToken,
        Task<FilesOriginalBoardDocumentWriteOutcome>> _save;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _closeGate = new();
    private string _revision;
    private string _sha256;
    private bool _disposed;
    private Task? _close;

    internal NativeHomeFilesBoardWriteLease(NativeHomeFilesBoardReadLease read, string sourceId, Guid documentId,
        Func<string, string, string, Guid, ReadOnlyMemory<byte>, CancellationToken,
            Task<FilesOriginalBoardDocumentWriteOutcome>> save)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        if (string.IsNullOrWhiteSpace(sourceId) || documentId == Guid.Empty) throw new ArgumentException("Retain the exact selected Files board identity.");
        SourceId = sourceId;
        DocumentId = documentId;
        _revision = read.RevisionId.ToString("D");
        _sha256 = read.Sha256;
        _save = save ?? throw new ArgumentNullException(nameof(save));
    }

    internal string SourceId { get; }
    internal Guid DocumentId { get; }
    internal string ProfileId => _read.ProfileId;
    internal Guid StoreId => _read.StoreId;
    internal Guid ParentFolderId => _read.ParentFolderId;
    internal Guid FileId => _read.FileId;
    internal Guid RevisionId => Guid.Parse(_revision);
    internal bool IsOpen => !_disposed && _read.IsOpen;
    internal ReadOnlyMemory<byte> Bytes => _read.Bytes;

    internal async Task DemandCurrentAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _read.DemandCurrentAsync(token).ConfigureAwait(false);
    }

    internal async ValueTask<BoardsExternalSaveReceipt> SaveAsync(string sourceId, string expectedRevision,
        ReadOnlyMemory<byte> document, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!StringComparer.Ordinal.Equals(sourceId, SourceId) ||
                !StringComparer.Ordinal.Equals(expectedRevision, _revision))
                return new(false, null, "FilesRevisionConflict", "The selected Files board revision advanced; reopen its current revision.", true);
            if (document.Length is <= 0 or > FilesNativeBrowserService.MaximumBoardDocumentBytes)
                return new(false, null, "FilesBoardDocumentSize", "The rich board exceeds the supported Files document size.");
            await _read.DemandCurrentAsync(token).ConfigureAwait(false);
            var result = await _save(SourceId, _revision, _sha256, DocumentId, document, token).ConfigureAwait(false);
            if (result.Committed && result.NewRevisionId is { } revision && revision != Guid.Empty &&
                !StringComparer.Ordinal.Equals(revision.ToString("D"), _revision))
            {
                _revision = revision.ToString("D");
                _sha256 = Convert.ToHexString(SHA256.HashData(document.Span));
            }
            return new(result.Committed, result.NewRevisionId?.ToString("D"), result.Code, result.Message, result.Conflict);
        }
        finally { _gate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_closeGate)
        {
            if (_close is not null) return new(_close);
            _disposed = true;
            return new(_close = CloseCoreAsync());
        }
    }

    private async Task CloseCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _read.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
