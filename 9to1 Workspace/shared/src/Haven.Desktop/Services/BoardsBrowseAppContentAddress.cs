using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Canonical notebook identity only; URI text supplies no storage or Home authority.</summary>
internal sealed record BoardsBrowseAppContentAddress(Guid? DocumentId, Guid? StoreId = null,
    Guid? ParentFolderId = null, Guid? FileId = null, Guid? RevisionId = null, Guid? ReadToken = null,
    bool Writable = false)
{
    internal const string Authority = "boards";
    internal static BoardsBrowseAppContentAddress Parse(AppContentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != Authority)
            throw new UnauthorizedAccessException("Select the registered native Boards application.");
        var parts = address.ResourceSegments;
        if (parts.Count == 0) return new((Guid?)null);
        if (parts.Count == 2 && parts[0] == "notebooks" &&
            Guid.TryParseExact(parts[1], "D", out var id) && id != Guid.Empty && parts[1] == id.ToString("D"))
            return new(id);
        if ((parts.Count is 5 or 6) && (parts[0] is "files-open" or "files-edit") &&
            TryId(parts[1], out var savedStore) && TryId(parts[2], out var savedFolder) &&
            TryId(parts[3], out var savedFile) && TryId(parts[4], out var savedRevision) &&
            (parts.Count == 5 || TryId(parts[5], out _)))
            return new(parts.Count == 6 ? Guid.Parse(parts[5]) : null, savedStore, savedFolder,
                savedFile, savedRevision, null, parts[0] == "files-edit");
        if (parts.Count == 6 && (parts[0] is "files" or "files-write") &&
            Guid.TryParseExact(parts[1], "D", out var store) && store != Guid.Empty && parts[1] == store.ToString("D") &&
            Guid.TryParseExact(parts[2], "D", out var folder) && folder != Guid.Empty && parts[2] == folder.ToString("D") &&
            Guid.TryParseExact(parts[3], "D", out var file) && file != Guid.Empty && parts[3] == file.ToString("D") &&
            Guid.TryParseExact(parts[4], "D", out var revision) && revision != Guid.Empty && parts[4] == revision.ToString("D") &&
            Guid.TryParseExact(parts[5], "D", out var token) && token != Guid.Empty && parts[5] == token.ToString("D"))
            return new(null, store, folder, file, revision, token, parts[0] == "files-write");
        throw new FormatException("A Boards location requires its exact canonical notebook or prepared Files document identity.");
    }

    internal bool IsFilesDocument => FileId is not null;

    internal bool IdentifiesSameFileRevision(BoardsBrowseAppContentAddress other) =>
        IsFilesDocument && other.IsFilesDocument && StoreId == other.StoreId && ParentFolderId == other.ParentFolderId &&
        FileId == other.FileId && RevisionId == other.RevisionId && Writable == other.Writable &&
        (DocumentId is null || other.DocumentId == DocumentId);

    private static bool TryId(string text, out Guid id) => Guid.TryParseExact(text, "D", out id) &&
        id != Guid.Empty && text == id.ToString("D");

    /// <summary>Durable identity is a request for fresh Home admission, never a saved capability.
    /// Legacy snapshots may not yet know the document ID; the exact file revision still pins bytes.</summary>
    internal AppContentAddress ForRestoration(Guid? documentId = null, Guid? currentRevision = null)
    {
        if (!IsFilesDocument) throw new InvalidOperationException("Only Files boards have a restoration identity.");
        var document = documentId ?? DocumentId;
        var suffix = document is { } id ? "/" + id.ToString("D") : "";
        return AppContentAddress.Parse($"9to1://boards/{(Writable ? "files-edit" : "files-open")}/{StoreId:D}/{ParentFolderId:D}/{FileId:D}/{currentRevision ?? RevisionId:D}{suffix}");
    }

    internal static AppContentAddress WithoutPreparedCapability(AppContentAddress address)
    {
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != Authority) return address;
        BoardsBrowseAppContentAddress board;
        try { board = Parse(address); }
        catch (FormatException)
        {
            // Persistence also retains failed and future routes. Their owning factory
            // can explain/reject them; snapshot/close must not discard or reinterpret them.
            return address;
        }
        return board.IsFilesDocument ? board.ForRestoration() : address;
    }

    internal static AppContentAddress ForFile(Guid store, Guid folder, Guid file, Guid revision, Guid readToken)
    {
        if (store == Guid.Empty || folder == Guid.Empty || file == Guid.Empty || revision == Guid.Empty || readToken == Guid.Empty)
            throw new ArgumentException("Retain the exact Files store, folder, file, revision and prepared Home read identity.");
        return AppContentAddress.Parse($"9to1://boards/files/{store:D}/{folder:D}/{file:D}/{revision:D}/{readToken:D}");
    }

    internal static AppContentAddress ForWritableFile(Guid store, Guid folder, Guid file, Guid revision, Guid preparedWrite)
    {
        if (store == Guid.Empty || folder == Guid.Empty || file == Guid.Empty || revision == Guid.Empty || preparedWrite == Guid.Empty)
            throw new ArgumentException("Retain the exact Files store, folder, file, revision and prepared Home write identity.");
        return AppContentAddress.Parse($"9to1://boards/files-write/{store:D}/{folder:D}/{file:D}/{revision:D}/{preparedWrite:D}");
    }
}
