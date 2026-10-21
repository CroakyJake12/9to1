using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// Desktop-only packaged-SVG icon family for the authored Home/NewTab icon rail (the 56px rail in
/// <c>UnifiedHome.cui</c>). It composes with the shared canonical browser catalog, mirroring the
/// Spaces rail family: known rail icon keys are decoded from strict, filled, rounded packaged SVG
/// assets painted with the live scoped semantic <c>CuiTextBrush</c>; every other source falls
/// through to the existing catalog, so non-rail glyphs keep their honest current behavior.
///
/// The resolver is a *registration adapter*: it exists because the Home surface must render genuine
/// filled rounded SVG (the shared <c>HavenIconCatalog</c> is stroke geometry, so it cannot express
/// fills). Hosts that render the Home surface call <see cref="Register"/> on their control registry;
/// the authored <c>UnifiedHome.cui</c> document and <see cref="HomeRailCuiBindings"/> are unchanged.
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
