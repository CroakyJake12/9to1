using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces;

public static class SpacesHomeCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Home.SpacesHome.cui";

    /// <summary>The same bounded catalog resolver used by the canonical web host.
    /// Each Spaces host owns a registry; no global resolver or file/network access.</summary>
    public static CuiControlRegistry CreateControlRegistry()
    {
        var registry = new CuiControlRegistry();
        NineToOne.Web.BrowserCanonicalIcons.Register(registry);
        return registry;
    }

    public static CuiDocument Load()
    {
        using var stream = typeof(SpacesHomeCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Spaces CUI resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd(), "SpacesHome.cui");
    }
}
