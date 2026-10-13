using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces;

public static class SpaceBuilderCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Builder.SpaceBuilder.cui";
    public static CuiDocument Load()
    {
        using var stream = typeof(SpaceBuilderCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The Space Builder interface is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "SpaceBuilder.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException("The Space Builder interface could not be parsed: " +
                string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
}
