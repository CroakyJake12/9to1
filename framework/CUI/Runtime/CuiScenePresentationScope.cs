using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// The app whose chrome is being presented by a retained scene. This is a visual
/// scope only: it does not identify, create, navigate or authorise app content.
/// </summary>
public sealed record CuiScenePresentationScope
{
    public string Surface { get; }
    public string? AppId { get; }

    public CuiScenePresentationScope(string surface, string? appId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        Surface = surface;
        AppId = appId is null ? CuiAppAccentCatalog.IdentityFor(surface)
            : CuiAppAccentCatalog.IdentityFor(appId) ?? appId;
        if (AppId is not null) _ = CuiAccentPreference.Default.WithAppColour(AppId, null);
    }
}
