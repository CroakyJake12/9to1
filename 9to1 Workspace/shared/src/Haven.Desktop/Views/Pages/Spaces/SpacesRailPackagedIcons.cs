using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>
/// Desktop-only packaged-SVG icon family for the authored Spaces rail (the 70px icon rail in
/// <c>SpacesHome.cui</c>). It composes with the shared canonical browser catalog: known rail
/// icon keys are decoded from strict, filled, rounded packaged SVG assets painted with the live
/// scoped semantic <c>CuiTextBrush</c>; every other source falls through to the existing catalog,
/// so custom/extension Space icons and non-rail glyphs keep their honest current behavior.
///
/// The resolver is a *registration adapter*: it exists because the Spaces project must not
/// reference Haven.Desktop, and the packaged assets live in the desktop assembly. Hosts that
/// render the Spaces Home surface call <see cref="Register"/> on their control registry; the
/// authored <c>SpacesHome.cui</c> document and <see cref="SpacesHomeCuiBindings"/> mapping are
/// unchanged and shared with standalone.
/// </summary>
public static class SpacesRailPackagedIcons
{
    /// <summary>Packaged asset URI scheme/path prefix for the rail icon family.</summary>
    public static readonly string AssetUriPrefix = "avares://" +
        (typeof(SpacesRailPackagedIcons).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The Spaces rail asset owner has no assembly name.")) +
        "/Assets/SpacesRail/";

    private const string IconPrefix = "icon:";
    private const string SvgExtension = ".svg";

    /// <summary>
    /// Symbolic icon keys that resolve to a filled, rounded packaged SVG asset on the Spaces
    /// rail. Keys mirror the real first-party Spaces destinations (<see cref="SpacesAppSurface"/>)
    /// plus the rail's static tool icons (create/manage/expand) and the custom-space fallback.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RailAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plus"] = "spaces-rail-plus",
            ["settings"] = "spaces-rail-settings",
            ["chevron-right"] = "spaces-rail-chevron-right",
            ["home"] = "spaces-rail-home",
            ["chat"] = "spaces-rail-chat",
            ["book"] = "spaces-rail-book",
            ["tasks"] = "spaces-rail-tasks",
            ["agents"] = "spaces-rail-agents",
            ["cart"] = "spaces-rail-cart",
            ["search"] = "spaces-rail-search",
            ["translate"] = "spaces-rail-translate",
            ["experiences"] = "spaces-rail-experiences",
            ["unknown"] = "spaces-rail-unknown",
        };

    /// <summary>
    /// Composes the filled-rounded Spaces rail SVG resolver ahead of the shared canonical catalog.
    /// Call once on the Spaces Home control registry before any Image source is resolved.
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
