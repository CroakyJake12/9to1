using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Presentation location only. A native URI never supplies Home, Files or execution authority.</summary>
internal sealed record CanvasBrowseAppContentAddress
{
    internal static CanvasBrowseAppContentAddress Parse(AppContentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != "canvas")
            throw new UnauthorizedAccessException("Select the registered native Canvas application.");
        if (address.ResourceSegments.Count != 0)
            throw new NotSupportedException("This Canvas entry opens its real launch surface. Open an authorised artifact there.");
        return new();
    }
}
