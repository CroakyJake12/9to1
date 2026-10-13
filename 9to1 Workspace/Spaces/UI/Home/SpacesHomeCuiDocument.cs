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
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "SpacesHome.cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException("The canonical Spaces interface could not be parsed: " +
                string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
}
