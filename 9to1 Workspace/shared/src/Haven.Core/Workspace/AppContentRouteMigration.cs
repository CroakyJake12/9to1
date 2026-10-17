namespace Haven.Core;

/// <summary>Forward-only product route aliases. Migration preserves resource segments
/// and grants no authority; the resulting owner must perform fresh admission.</summary>
public static class AppContentRouteMigration
{
    public static AppContentAddress Canonicalize(AppContentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != "assistants")
            return address;
        var suffix = address.ResourceSegments.Count == 0 ? string.Empty : "/" + string.Join("/", address.ResourceSegments);
        return AppContentAddress.Parse("9to1://spaces/assistants" + suffix);
    }
}
