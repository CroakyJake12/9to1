using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces;

public static class SpacesHomeCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Home.SpacesHome.cui";

    public static CuiDocument Load()
    {
        using var stream = typeof(SpacesHomeCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Spaces CUI resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd(), "SpacesHome.cui");
    }
}
