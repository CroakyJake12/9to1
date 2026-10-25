using System.Security.Cryptography;
using System.Text;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    /// <summary>
    /// Keeps the existing host profile directory as the current profile's legacy
    /// storage so existing cookies and site data remain available. Other profile
    /// identities receive deterministic, isolated roots. No legacy bytes are moved
    /// or deleted: older shared-profile data has no reliable per-profile attribution.
    /// </summary>
    private string StandardProfileDirectoryFor(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        if (profileId.Length == 0 || StringComparer.Ordinal.Equals(profileId, _homeProfileId))
            return StandardProfileDirectory;
        if (string.IsNullOrWhiteSpace(profileId))
            throw new InvalidDataException("A browser profile identity cannot be whitespace.");

        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId))).ToLowerInvariant();
        return Path.Combine(StandardProfileDirectory, "profiles", identity);
    }
}
