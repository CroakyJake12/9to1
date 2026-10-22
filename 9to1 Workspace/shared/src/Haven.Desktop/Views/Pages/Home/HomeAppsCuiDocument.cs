using System;
using System.IO;
using System.Linq;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Web;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// Loads the authored Home app launcher document and owns the bounded CUI registry it renders
/// against. Each Home surface constructs its own registry, exactly like the Spaces home surface,
/// so no global image resolver is installed and no loader silently gains a wider capability.
///
/// The only resolver registered is the canonical symbolic icon catalog shared with the browser and
/// Spaces surfaces (<c>icon:&lt;bounded-symbolic-key&gt;</c> resolved from maintained vector data).
/// It performs no file, network or arbitrary asset access, and an unrecognised symbolic key keeps
/// the catalog's visible fallback rather than failing the launcher.
/// </summary>
internal static class HomeAppsCuiDocument
{
    public const string ResourceName = "Haven.Desktop.Home.UnifiedApps.cui";

    public static CuiControlRegistry CreateControlRegistry(IHomeBrandObservationCoordinator? coordinator = null)
    {
        var registry = new CuiControlRegistry();
        // The Home surface composes the filled-rounded packaged Home rail SVG family and the
        // owner-app brand PNG identity ahead of the shared stroke catalog, exactly like the Spaces
        // rail. The rail keys render as genuine filled, rounded SVG; a mapped built-in brand app
        // renders its owner PNG; every other icon falls through to the shared canonical catalog. All
        // three share one resolver registration (the CUI registry accepts a single image resolver).
        BrowserCanonicalIcons.Register(registry, (source, target) =>
        {
            // Reconcile the actual rendered target before EVERY resolver branch (rail SVG, brand PNG,
            // missing fallback, generic catalog, or malformed token) so stale branding ownership is
            // always cleaned up and ownership follows the target. Each registry captures its own page
            // binder; different surfaces never register each other's targets.
            coordinator?.ReconcileTarget(target, source);
            return HomeRailPackagedIcons.Resolve(source, target) ?? HomeAppBrandingIcons.Resolve(source, target);
        });
        return registry;
    }

    public static CuiDocument Parse()
    {
        using var stream = typeof(HomeAppsCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"The Home apps document '{ResourceName}' is missing from the desktop assembly.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "UnifiedHomeApps.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home apps document is invalid: " + string.Join("; ",
                parser.Diagnostics.Diagnostics.Where(item => item.Severity == CuiDiagnosticSeverity.Error)
                    .Select(item => item.Code + " " + item.Message)));
        return document;
    }
}
