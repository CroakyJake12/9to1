namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    /// <summary>
    /// The legacy directory remains with its durably recorded original profile.
    /// A different current Home profile always receives its own deterministic root.
    /// </summary>
    private string StandardProfileDirectoryFor(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return Haven.Browser.BrowserProfileStorage.ResolveProfileDirectory(_profilePaths, profileId);
    }
}
