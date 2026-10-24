using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Canonical resource identity only. A saved address grants no Home/Files read access.</summary>
internal sealed record FilesBrowseAppContentAddress(Guid? StoreId, Guid? FolderId,
    Guid? PreviewFileId = null, Guid? PreviewRevisionId = null)
{
    internal static FilesBrowseAppContentAddress Parse(AppContentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != "files")
            throw new UnauthorizedAccessException("Select the registered native Files app.");
        var parts = address.ResourceSegments;
        if (parts.Count == 0) return new(null, null);
        if (parts.Count is not (2 or 4 or 8) || parts[0] != "store"
            || !Guid.TryParseExact(parts[1], "D", out var store) || store == Guid.Empty)
            throw new FormatException("A Files location requires its canonical store identity.");
        if (parts.Count == 2) return new(store, null);
        if (parts[2] != "folder" || !Guid.TryParseExact(parts[3], "D", out var folder) || folder == Guid.Empty)
            throw new FormatException("Select a canonical folder within its Files store.");
        if (parts.Count == 4) return new(store, folder);
        if (parts[4] != "file" || !Guid.TryParseExact(parts[5], "D", out var file) || file == Guid.Empty ||
            parts[6] != "revision" || !Guid.TryParseExact(parts[7], "D", out var revision) || revision == Guid.Empty)
            throw new FormatException("A text preview requires its original file and immutable revision identities.");
        return new(store, folder, file, revision);
    }

    internal static AppContentAddress For(Guid store, Guid? folder) => AppContentAddress.Parse(
        $"9to1://files/store/{store:D}" + (folder is { } id ? $"/folder/{id:D}" : string.Empty));

    internal static AppContentAddress ForTextPreview(Guid store, Guid folder, Guid file, Guid revision)
    {
        if (store == Guid.Empty || folder == Guid.Empty || file == Guid.Empty || revision == Guid.Empty)
            throw new ArgumentException("Retain the canonical store, folder, file and immutable revision.");
        return AppContentAddress.Parse($"9to1://files/store/{store:D}/folder/{folder:D}/file/{file:D}/revision/{revision:D}");
    }
}
