namespace CakeOS.Cui.Runtime;

/// <summary>
/// Typed description of the host that is composing a CUI surface. Hosting mode is presentation
/// data only: it does not grant permission, imply service availability, or assert that shared
/// model, notification or profile controls exist.
/// </summary>
public sealed record CuiHostContext(bool IsEmbedded, string HostAppId)
{
    public string HostAppId { get; init; } = RequireAppId(HostAppId);

    public static CuiHostContext Standalone(string appId) =>
        new(false, RequireAppId(appId));

    public static CuiHostContext EmbeddedInHaven(string appId) =>
        new(true, RequireAppId(appId));

    private static string RequireAppId(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("A CUI host context requires a non-empty application id.", nameof(appId));
        return appId.Trim();
    }
}

/// <summary>Supplies the current host context to a CUI surface without granting it authority.</summary>
public interface ICuiHostContextSource
{
    CuiHostContext Current { get; }
    event EventHandler? Changed;
}