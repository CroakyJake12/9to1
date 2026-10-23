using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Descriptive provenance only. These values grant no Files read, Home consent,
/// Notes ownership, overwrite permission or native application readiness.</summary>
public sealed record BoardsLegacySourceIdentity(Guid StoreId, Guid FileId, string Revision);

public enum BoardsLegacyFormat { Unknown, RawSnapshot, Envelope }

public sealed record BoardsLegacyDocumentIssue(string Code, string Pointer, string Message,
    bool PreventsProjection = false);

/// <summary>An immutable observation of already supplied data. A codec never follows a
/// path, attachment reference, backup or URI. Missing source identity is not invented.</summary>
public interface IBoardsLegacyDocumentInspection
{
    BoardsLegacyFormat Format { get; }
    int? SchemaVersion { get; }
    Guid? LegacyDocumentId { get; }
    BoardsLegacySourceIdentity? Source { get; }
    long OriginalByteLength { get; }
    string OriginalSha256 { get; }
    bool OriginalBytesRetained { get; }
    IReadOnlyList<BoardsLegacyDocumentIssue> Issues { get; }
}

/// <summary>A detached canonical snapshot proposal, never a commit or authorisation.
/// Every returned document is a fresh copy of the exact proposed bytes. The owning
/// CUI must review it and the SAME Notes/Home owner must check collisions and commit.</summary>
public sealed class BoardsLegacyImportCandidate
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly byte[]? _document;

    public BoardsLegacyImportCandidate(Guid? targetDocumentId, NotesDocument? document,
        IEnumerable<BoardsLegacyDocumentIssue> issues)
    {
        TargetDocumentId = targetDocumentId;
        _document = document is null ? null : JsonSerializer.SerializeToUtf8Bytes(document, Json);
        CandidateSha256 = _document is null ? null : Convert.ToHexString(SHA256.HashData(_document)).ToLowerInvariant();
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public Guid? TargetDocumentId { get; }
    public bool IsCanonicalProjectionAvailable => _document is not null;
    public string? CandidateSha256 { get; }
    public IReadOnlyList<BoardsLegacyDocumentIssue> Issues { get; }
    public NotesDocument? CopyDocument() => _document is null ? null : JsonSerializer.Deserialize<NotesDocument>(_document, Json);
}

/// <summary>Pure data compatibility, not a second Boards store or application engine.
/// The host supplies bytes only after genuine Files admission. An explicit import is
/// an independent canonical snapshot; original export excludes later canonical edits.
/// Edited-format export/linked writeback are deliberately not provided by this contract.</summary>
public interface IBoardsLegacyDocumentCodec
{
    IBoardsLegacyDocumentInspection Inspect(ReadOnlyMemory<byte> alreadyAuthorisedBytes,
        BoardsLegacySourceIdentity? source = null);
    BoardsLegacyImportCandidate PrepareCanonicalImport(IBoardsLegacyDocumentInspection inspection,
        DateTimeOffset importObservedAt);
    byte[] ExportOriginal(IBoardsLegacyDocumentInspection inspection);
}
