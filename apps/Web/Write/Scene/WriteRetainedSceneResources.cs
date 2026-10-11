using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace NineToOne.Web.Write;

/// <summary>The specialised retained engine consumes the same CUI-owned resources as its surrounding chrome.</summary>
public static class WriteRetainedSceneResources
{
    public static void Register(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        // The retained renderer uses semantic brushes; its native-control resolver
        // creates no donor controls. Importing the former AXAML template dictionary
        // therefore introduced a second visual authority, not an engine dependency.
        if (!application.Styles.OfType<CuiComponentStyles>().Any()) application.Styles.Add(new CuiComponentStyles());
        CuiThemeResourceApplier.Apply(CuiSurfacePaletteCatalog.For("Home", CuiThemeScopeApplier.DetectAppearance()));
    }
}
