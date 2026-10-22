using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Canonical notebook identity only; URI text supplies no storage or Home authority.</summary>
internal sealed record BoardsBrowseAppContentAddress(Guid? DocumentId)
{
    internal const string Authority = "boards";
    internal static BoardsBrowseAppContentAddress Parse(AppContentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != Authority)
            throw new UnauthorizedAccessException("Select the registered native Boards application.");
        var parts = address.ResourceSegments;
        if (parts.Count == 0) return new(null);
        if (parts.Count != 2 || parts[0] != "notebooks" ||
            !Guid.TryParseExact(parts[1], "D", out var id) || id == Guid.Empty || parts[1] != id.ToString("D"))
            throw new FormatException("A Boards location requires its exact canonical notebook identity.");
        return new(id);
    }
}
