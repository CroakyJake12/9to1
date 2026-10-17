namespace Haven.Browser;

/// <summary>Built-in Browse pages are native application surfaces, never web origins.
/// Exact route matching prevents user-info, ports, paths or encoded aliases from
/// turning an unregistered address into a privileged page.</summary>
public enum BrowserNativePage { None, History, Bookmarks, Downloads, Settings }

public static class BrowserNativePages
{
    public static bool TryResolve(string address, out BrowserNativePage page)
    {
        page = address?.ToLowerInvariant() switch
        {
            "browse://history" or "browse://history/" => BrowserNativePage.History,
            "browse://bookmarks" or "browse://bookmarks/" => BrowserNativePage.Bookmarks,
            "browse://downloads" or "browse://downloads/" => BrowserNativePage.Downloads,
            "browse://settings" or "browse://settings/" => BrowserNativePage.Settings,
            _ => BrowserNativePage.None
        };
        return page != BrowserNativePage.None;
    }

    public static Uri Address(BrowserNativePage page) => page switch
    {
        BrowserNativePage.History => new("browse://history/"),
        BrowserNativePage.Bookmarks => new("browse://bookmarks/"),
        BrowserNativePage.Downloads => new("browse://downloads/"),
        BrowserNativePage.Settings => new("browse://settings/"),
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    public static string Title(BrowserNativePage page) => page switch
    {
        BrowserNativePage.History => "History",
        BrowserNativePage.Bookmarks => "Bookmarks",
        BrowserNativePage.Downloads => "Downloads",
        BrowserNativePage.Settings => "Browse settings",
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    internal static bool IsStoredTabAddress(string address) => TryResolve(address, out _) ||
        string.Equals(address, "about:blank", StringComparison.OrdinalIgnoreCase) ||
        (Haven.Core.AppContentAddress.TryParse(address, out var parsed) &&
            (parsed!.Namespace is Haven.Core.AppContentNamespace.Web or Haven.Core.AppContentNamespace.NativeApp ||
             parsed.Namespace == Haven.Core.AppContentNamespace.Browse && parsed.ResourceSegments.Count == 0 && parsed.Authority is "home" or "newtab"));
}
