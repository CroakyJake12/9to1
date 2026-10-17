using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Resource identity only. The original Files/Home owner authorizes every read.</summary>
internal sealed record FilesBrowseAppContentAddress(Guid? StoreId, Guid? FolderId)
{
    internal static FilesBrowseAppContentAddress Parse(AppContentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != "files")
            throw new UnauthorizedAccessException("Select the registered native Files app.");
        var parts = address.ResourceSegments;
        if (parts.Count == 0) return new(null, null);
        if (parts.Count is not (2 or 4) || parts[0] != "store"
            || !Guid.TryParseExact(parts[1], "D", out var store) || store == Guid.Empty)
            throw new FormatException("A Files location requires its canonical store identity.");
        if (parts.Count == 2) return new(store, null);
        if (parts[2] != "folder" || !Guid.TryParseExact(parts[3], "D", out var folder) || folder == Guid.Empty)
            throw new FormatException("Select a canonical folder within its Files store.");
        return new(store, folder);
    }

    internal static AppContentAddress For(Guid store, Guid? folder) => AppContentAddress.Parse(
        $"9to1://files/store/{store:D}" + (folder is { } id ? $"/folder/{id:D}" : string.Empty));
}
