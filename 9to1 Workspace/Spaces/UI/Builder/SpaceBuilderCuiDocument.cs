using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces.Builder;

public static class SpaceBuilderCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Builder.SpaceBuilder.cui";
    public static CuiDocument Load()
    {
        using var stream = typeof(SpaceBuilderCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The authored Space Builder CUI resource is missing.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd(), "SpaceBuilder.cui");
    }
}
