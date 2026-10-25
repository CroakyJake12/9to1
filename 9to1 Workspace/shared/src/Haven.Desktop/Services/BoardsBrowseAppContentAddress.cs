using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Canonical notebook identity only; URI text supplies no storage or Home authority.</summary>
internal sealed record BoardsBrowseAppContentAddress(Guid? DocumentId, Guid? StoreId = null,
    Guid? ParentFolderId = null, Guid? FileId = null, Guid? RevisionId = null, Guid? ReadToken = null)
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
        if (parts.Count == 6 && parts[0] == "files" &&
            Guid.TryParseExact(parts[1], "D", out var store) && store != Guid.Empty && parts[1] == store.ToString("D") &&
            Guid.TryParseExact(parts[2], "D", out var folder) && folder != Guid.Empty && parts[2] == folder.ToString("D") &&
            Guid.TryParseExact(parts[3], "D", out var file) && file != Guid.Empty && parts[3] == file.ToString("D") &&
            Guid.TryParseExact(parts[4], "D", out var revision) && revision != Guid.Empty && parts[4] == revision.ToString("D") &&
            Guid.TryParseExact(parts[5], "D", out var token) && token != Guid.Empty && parts[5] == token.ToString("D"))
            return new(null, store, folder, file, revision, token);
        throw new FormatException("A Boards location requires its exact canonical notebook or prepared Files document identity.");
    }

    internal bool IsFilesDocument => FileId is not null;

    internal static AppContentAddress ForFile(Guid store, Guid folder, Guid file, Guid revision, Guid readToken)
    {
        if (store == Guid.Empty || folder == Guid.Empty || file == Guid.Empty || revision == Guid.Empty || readToken == Guid.Empty)
            throw new ArgumentException("Retain the exact Files store, folder, file, revision and prepared Home read identity.");
        return AppContentAddress.Parse($"9to1://boards/files/{store:D}/{folder:D}/{file:D}/{revision:D}/{readToken:D}");
    }
}
