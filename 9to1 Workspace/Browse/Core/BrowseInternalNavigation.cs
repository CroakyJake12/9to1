namespace HavenOS.Apps.Browse;

/// <summary>Reserved native namespaces cannot be reinterpreted as an external
/// search query while this host lacks their registered native surface router.</summary>
internal static class BrowseInternalNavigation
{
    public const string UnavailableMessage = "This native route is not registered in this Browse host. Built-in pages are browse://history, browse://bookmarks, browse://downloads and browse://settings. Open other apps from Home.";

    public static bool IsReserved(string value)
    {
        var candidate = value.TrimStart();
        return candidate.StartsWith("cui:", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("browse:", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("9to1:", StringComparison.OrdinalIgnoreCase);
    }
}
