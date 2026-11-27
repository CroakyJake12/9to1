using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// The packaged, filled SVG family retained for Home app fallbacks, shortcut artwork, and
/// secondary controls. Home navigation uses separate outline sources from the canonical catalogue.
/// </summary>
public static class HomeRailPackagedIcons
{
    /// <summary>Packaged asset URI scheme/path prefix for the Home rail icon family.</summary>
    public static readonly string AssetUriPrefix = "avares://" +
        (typeof(HomeRailPackagedIcons).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The Home rail asset owner has no assembly name.")) +
        "/Assets/HomeRail/";

    private const string IconPrefix = "icon:";
    private const string SvgExtension = ".svg";
    private const string HomeBrandAsset = "avares://HavenOS.Apps.Browse.NativeUI/Assets/9to1-logo.png";
    private static readonly HavenAvaloniaImageResolver BrandImages = new();

    /// <summary>
    /// Symbolic icon keys that resolve to a filled, rounded packaged SVG asset on the Home rail.
    /// Keys mirror the real first-party Home destinations (Apps / Dashboard / Models-Discover /
    /// New Tab) plus the rail's Library / Events / Mesh destinations and the anchored Settings footer.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RailAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apps"] = "home-rail-apps",
            ["dashboard"] = "home-rail-dashboard",
            ["sparkles"] = "home-rail-sparkles",
            ["plus"] = "home-rail-plus",
            ["folder"] = "home-rail-folder",
            ["calendar"] = "home-rail-calendar",
            ["mesh"] = "home-rail-mesh",
            ["settings"] = "home-rail-settings",
        };

    /// <summary>
    /// Composes the filled-rounded Home rail SVG resolver ahead of the shared canonical catalog.
    /// Call once on the Home control registry before any Image source is resolved.
    /// </summary>
    public static void Register(CuiControlRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        NineToOne.Web.BrowserCanonicalIcons.Register(registry, (source, target) => Resolve(source, target));
    }

    /// <summary>
    /// Resolves a known rail icon key to its packaged SVG drawing, or returns <c>null</c> so the
    /// shared catalog resolves custom/extension/non-rail icons honestly.
    /// </summary>
    public static IImage? Resolve(string source, Control target)
    {
        if (source == HomeBrandAsset)
            return BrandImages.TryResolve(source, out var mark) ? mark : null;
        if (!source.StartsWith(IconPrefix, StringComparison.Ordinal))
            return null;
        var key = source.AsSpan(IconPrefix.Length).Trim();
        if (key.IsEmpty || !RailAssets.TryGetValue(key.ToString(), out var asset))
            return null;
        var uri = AssetUriPrefix + asset + SvgExtension;
        return HavenPackagedSvgResolver.TryResolve(uri, target, out var image) ? image : null;
    }

    /// <summary>The packaged asset name (without extension) for a rail key, or null.</summary>
    public static string? AssetNameForKey(string key) =>
        string.IsNullOrWhiteSpace(key) ? null : RailAssets.TryGetValue(key, out var asset) ? asset : null;
}
