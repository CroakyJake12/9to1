using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Home-owned observer/invalidation coordinator for actual rendered brand-image targets. The
/// registered CUI resolver reconciles every resolved source (brand or not) for an actual target control;
/// the coordinator captures the target's live scoped appearance and requests the existing typed CUI binding
/// invalidation when it changes. Implemented by the Home apps bindings and captured per registry/page by
/// <c>HomeAppsCuiDocument.CreateControlRegistry(coordinator)</c>, so multiple Home/New Tab surfaces never
/// share a global owner. No global strong references are held.</summary>
internal interface IHomeBrandObservationCoordinator
{
    void ReconcileTarget(Control target, string symbolicSource);
}

/// <summary>
/// Owner-app PNG identity artwork adapter for the authored Home launcher.
///
/// Mapping is by REAL canonical built-in identity only: the exact stable AppId (GUID) plus
/// <see cref="ModeSource.BuiltIn"/>. It never maps by name, key or shared IconKey, so custom
/// same-name lookalikes, extension/created apps and apps that merely share a glyph key keep their
/// existing catalogue icon. Each mapped app owns light/dark PNG identity assets proposed at
/// <c>avares://Haven/Assets/OwnerAppIcons/{light,dark}/&lt;stem&gt;.png</c>; the binary blobs are
/// integrated later by Home's private assembler, never here.
///
/// The adapter composes ahead of the existing filled-rounded SVG Home rail and the shared canonical
/// catalog inside the single CUI image-source registration. A known brand makes one bounded packaged
/// PNG decode attempt; if the asset is not yet integrated or cannot be decoded, it honestly falls
/// back to the app's existing catalog icon (rail SVG first, then the shared canonical vector), so the
/// launcher keeps its current look until the binaries are present.
/// </summary>
public static class HomeAppBrandingIcons
{
    /// <summary>Bounded source prefix emitted for a mapped built-in brand app.</summary>
    public const string SourcePrefix = "ownerbrand:";

    /// <summary>Themed packaged-asset root path on the Haven assembly.</summary>
    public const string AssetRoot = "/Assets/OwnerAppIcons/";

    /// <summary>Exact 19 canonical built-in brand mappings: AppId GUID → PNG stem.</summary>
    private static readonly IReadOnlyDictionary<Guid, string> BrandByGuid =
        new Dictionary<Guid, string>
        {
            [Guid.Parse("a0000000-0000-0000-0000-000000000003")] = "automations",
            [Guid.Parse("a0000000-0000-0000-0000-000000000023")] = "boards",
            [Guid.Parse("a0000000-0000-0000-0000-000000000018")] = "canvas-whiteboard",
            [Guid.Parse("a0000000-0000-0000-0000-000000000010")] = "data",
            [Guid.Parse("a0000000-0000-0000-0000-000000000030")] = "files",
            [Guid.Parse("a0000000-0000-0000-0000-000000000026")] = "forms",
            [Guid.Parse("a0000000-0000-0000-0000-000000000008")] = "picture",
            [Guid.Parse("a0000000-0000-0000-0000-000000000027")] = "shelf",
            [Guid.Parse("a0000000-0000-0000-0000-000000000024")] = "maps",
            [Guid.Parse("a0000000-0000-0000-0000-000000000025")] = "motion",
            [Guid.Parse("a0000000-0000-0000-0000-000000000012")] = "play",
            [Guid.Parse("a0000000-0000-0000-0000-000000000009")] = "present",
            [Guid.Parse("a0000000-0000-0000-0000-000000000022")] = "spaces",
            [Guid.Parse("a0000000-0000-0000-0000-000000000004")] = "ai-studio",
            [Guid.Parse("a0000000-0000-0000-0000-000000000028")] = "build",
            [Guid.Parse("a0000000-0000-0000-0000-000000000006")] = "plan",
            [Guid.Parse("a0000000-0000-0000-0000-000000000020")] = "terminal",
            [Guid.Parse("a0000000-0000-0000-0000-000000000011")] = "voice-vision",
            [Guid.Parse("a0000000-0000-0000-0000-000000000017")] = "write",
        };

    /// <summary>The only brand stems the bounded resolver recognises.</summary>
    private static readonly IReadOnlySet<string> KnownStems = new HashSet<string>(StringComparer.Ordinal)
    {
        "automations", "boards", "canvas", "data", "files", "forms", "picture", "shelf",
        "maps", "motion", "play", "present", "spaces", "ai-studio",
        "canvas-whiteboard", "build", "plan", "terminal", "voice-vision", "write", "wave"
    };

    /// <summary>Bounded packaged-PNG provider. Defaults to the backend asset decoder. This is a
    /// narrow internal seam so tests can deterministically exercise missing/corrupt packaged-resource
    /// fallback by injecting a recorder that reports the URI it was asked to open and reports absence.
    /// Production always uses the real decoder; the private default is never swapped by app code.</summary>
    internal static IHavenAvaloniaImageResolver PackagedBitmap { get; set; } = new HavenAvaloniaImageResolver();

    private static readonly string AssemblyName = typeof(HomeAppBrandingIcons).Assembly.GetName().Name
        ?? throw new InvalidOperationException("The Home app-brand asset owner has no assembly name.");

    /// <summary>True only for an exact built-in canonical GUID on the verified brand map.</summary>
    public static bool TryGetBrandStem(Guid? canonicalId, ModeSource? source, out string? stem)
    {
        stem = null;
        if (source != ModeSource.BuiltIn || canonicalId is not { } id) return false;
        return BrandByGuid.TryGetValue(id, out stem!);
    }

    /// <summary>Bounded launcher icon-source projection. A mapped built-in app emits the owner-brand
    /// token carrying its stem and its existing icon key (used for honest fallback); everything else
    /// keeps its existing canonical icon source unchanged. The token never bakes an appearance folder
    /// in: light/dark is selected at resolution time from the ACTUAL nearest scope of each rendered
    /// image target, so the bound value is stable and the folder always follows the live target scope.</summary>
    public static string SourceFor(string iconKey, Guid? canonicalId, ModeSource? source)
    {
        var key = SafeIconKey(iconKey);
        if (!TryGetBrandStem(canonicalId, source, out var stem))
            return "icon:" + key;
        return SourcePrefix + stem + "|" + key;
    }

    /// <summary>The canonical Create navigation group reuses the owner's original Canvas brush
    /// artwork byte-for-byte. It remains a group, without an invented app GUID or installation.</summary>
    public static string SourceForGroup(string? groupKey, string? iconKey) =>
        string.Equals(groupKey, HomeCreateGroup.Key, StringComparison.Ordinal)
            ? SourcePrefix + "canvas|" + SafeIconKey(iconKey)
            : "icon:" + SafeIconKey(iconKey);

    /// <summary>Real Create member artwork. The canonical built-in Wave reference has no native
    /// GUID yet; its known group identity may carry artwork without changing its disabled/unavailable
    /// state. Other unavailable references retain their catalogue fallback; custom Wave lookalikes
    /// never acquire the built-in image.</summary>
    public static string SourceForGroupMember(HomeGroupMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var key = SafeIconKey(member.IconKey);
        if (member.Source == ModeSource.BuiltIn
            && string.Equals(member.Key, "wave", StringComparison.OrdinalIgnoreCase))
            return SourcePrefix + "wave|" + key;
        return member.IsInstalled
            ? SourceFor(key, member.CanonicalId, member.Source)
            : "icon:" + key;
    }

    /// <summary>Resolves a brand source to a packaged themed PNG, or falls back to the app's existing
    /// catalog icon. Non-brand sources return null so the composed chain continues. The token must be
    /// exactly <c>ownerbrand:&lt;stem&gt;|&lt;key&gt;</c>; the light/dark folder is always derived from
    /// the ACTUAL nearest scope of the given <paramref name="target"/> control (<see cref="DetectAppearance"/>).
    /// Malformed tokens (missing/extra segments, unknown stems) are rejected rather than approximated.</summary>
    public static IImage? Resolve(string source, Control target)
    {
        if (!TryParseBrandSource(source, out var stem, out var fallbackKey)) return null;
        var folder = AppearanceFolder(DetectAppearance(target));
        if (PackagedBitmap.TryResolve(ResourceUri(stem, folder), out var image)) return image;
        return FallbackFor(fallbackKey, target);
    }

    /// <summary>Strictly parses a valid bounded brand token <c>ownerbrand:&lt;stem&gt;|&lt;key&gt;</c>.
    /// Only an exact known stem, a single separator, and a bounded symbolic key qualify; malformed,
    /// unknown-stem, over-segmented, non-brand or unbounded-key sources are rejected.</summary>
    internal static bool TryParseBrandSource(string source, out string stem, out string fallbackKey)
    {
        stem = null!;
        fallbackKey = null!;
        if (source is null || !source.StartsWith(SourcePrefix, StringComparison.Ordinal)) return false;
        var rest = source.AsSpan(SourcePrefix.Length);
        var pipe = rest.IndexOf('|');
        if (pipe <= 0 || pipe >= rest.Length - 1) return false;
        var stemText = rest[..pipe].ToString();
        if (!KnownStems.Contains(stemText)) return false;
        var remainder = rest[(pipe + 1)..];
        if (remainder.IndexOf('|') >= 0) return false;
        var keyText = remainder.ToString();
        if (keyText.Length is <= 0 or > 64) return false;
        foreach (var character in keyText)
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                return false;
        stem = stemText;
        fallbackKey = keyText;
        return true;
    }

    /// <summary>Selects the themed subfolder from the real effective CUI appearance:
    /// SuperBright/Bright → light; Dark/SuperDark → dark. Never inferred from theme name/accent.
    /// Undefined enum values are rejected rather than silently collapsing to light.</summary>
    public static string AppearanceFolder(CuiAppearance appearance)
    {
        if (!Enum.IsDefined(appearance))
            throw new ArgumentOutOfRangeException(nameof(appearance), "Undefined CuiAppearance value.");
        return appearance is CuiAppearance.Dark or CuiAppearance.SuperDark ? "dark" : "light";
    }

    /// <summary>The bounded packaged URI for a stem under an explicit appearance folder. Accepts only
    /// one of the known brand stems and exactly "light"/"dark"; traversal, near-matches and any
    /// arbitrary file/network/data shape are rejected.</summary>
    public static string ResourceUri(string stem, string folder)
    {
        if (stem is null || !KnownStems.Contains(stem))
            throw new ArgumentException($"'{stem}' is not one of the owner brand stems.", nameof(stem));
        if (folder is not ("light" or "dark"))
            throw new ArgumentException("The folder must be exactly 'light' or 'dark'.", nameof(folder));
        return "avares://" + AssemblyName + AssetRoot + folder + "/" + stem + ".png";
    }

    /// <summary>The bounded packaged URI for a stem under the current effective appearance.</summary>
    public static string ThemedResourceUri(string stem, Control target) =>
        ResourceUri(stem, AppearanceFolder(DetectAppearance(target)));

    /// <summary>Reads the scoped/global real CUI appearance resource, matching the loader's own
    /// four-mode observation, with a truthful fallback to the global detector. An explicit resource
    /// whose text is not a defined appearance (including numeric text) is rejected, never silently
    /// mapped to light.</summary>
    public static CuiAppearance DetectAppearance(Control target)
    {
        if (target.TryFindResource("CuiAppearance", out var raw))
        {
            if (raw is CuiAppearance direct)
            {
                if (!Enum.IsDefined(direct))
                    throw new ArgumentException("Undefined CuiAppearance enum value.", nameof(raw));
                return direct;
            }
            if (raw is string text)
            {
                foreach (var name in Enum.GetNames<CuiAppearance>())
                    if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
                        return (CuiAppearance)Enum.Parse(typeof(CuiAppearance), name);
                throw new ArgumentException($"Unrecognized CuiAppearance resource text '{text}'.", nameof(raw));
            }
            throw new ArgumentException("The CuiAppearance resource must be a defined appearance.", nameof(raw));
        }
        return CuiThemeScopeApplier.DetectAppearance();
    }

    /// <summary>Honest existing fallback: the app's current catalog icon, rail filled-rounded SVG first
    /// (so folder-keyed apps keep the rail folder), then the shared canonical vector catalog.</summary>
    private static IImage? FallbackFor(string key, Control target) =>
        HomeRailPackagedIcons.Resolve("icon:" + key, target) ?? ResolveCanonical(key, target);

    /// <summary>Shared canonical vector rendering (HavenIconCatalog), mirroring the browser/Spaces
    /// canonical resolver so a missing/unintegrated brand asset keeps the exact generic icon.</summary>
    private static IImage? ResolveCanonical(string key, Control target)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64) return null;
        if (!target.TryFindResource("CuiTextBrush", out var brushResource) || brushResource is not IBrush brush)
            return null;
        var sourceGeometry = HavenIconCatalog.Resolve(key);
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.SetFillRule(sourceGeometry.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
            foreach (var figure in sourceGeometry.Path.Figures)
            {
                writer.BeginFigure(new Point(figure.Start.X, figure.Start.Y), false);
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case HavenLineSegment line:
                            writer.LineTo(new Point(line.End.X, line.End.Y), true); break;
                        case HavenQuadraticBezierSegment quadratic:
                            writer.QuadraticBezierTo(new Point(quadratic.Control.X, quadratic.Control.Y),
                                new Point(quadratic.End.X, quadratic.End.Y), true); break;
                        case HavenCubicBezierSegment cubic:
                            writer.CubicBezierTo(new Point(cubic.Control1.X, cubic.Control1.Y),
                                new Point(cubic.Control2.X, cubic.Control2.Y),
                                new Point(cubic.End.X, cubic.End.Y), true); break;
                        case HavenArcSegment arc:
                            writer.ArcTo(new Point(arc.End.X, arc.End.Y),
                                new Size(arc.Radius.Width, arc.Radius.Height), arc.RotationDegrees, arc.IsLargeArc,
                                arc.SweepDirection == HavenSweepDirection.Clockwise
                                    ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, true); break;
                        default: return null;
                    }
                }
                writer.EndFigure(figure.Closed);
            }
        }
        var image = new DrawingImage(new GeometryDrawing
        {
            Geometry = geometry,
            Pen = new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
        });
        if (sourceGeometry.ViewBox is { } viewBox)
            image.Viewbox = new Rect(viewBox.X, viewBox.Y, viewBox.Width, viewBox.Height);
        return image;
    }

    /// <summary>Bounded icon-key sanitisation, identical to the launcher's existing rule.</summary>
    private static string SafeIconKey(string? key)
    {
        var symbolic = key?.Trim() ?? string.Empty;
        if (symbolic.Length is > 0 and <= 64)
        {
            foreach (var character in symbolic)
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                    return "unknown";
            return symbolic;
        }
        return "unknown";
    }
}
