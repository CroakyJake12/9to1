using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces.Builder;

public static class SpaceDetailsCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Builder.SpaceDetails.cui";
    public static CuiDocument Load()
    {
        using var stream = typeof(SpaceDetailsCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The authored Space details CUI resource is missing.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd(), "SpaceDetails.cui");
    }
}
