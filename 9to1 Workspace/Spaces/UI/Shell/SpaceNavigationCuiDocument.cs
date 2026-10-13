using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces;

public static class SpaceNavigationCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Shell.SpaceNavigation.cui";
    public static CuiDocument Load()
    {
        using var stream = typeof(SpaceNavigationCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Spaces resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var result = parser.Parse(reader.ReadToEnd(), "SpaceNavigation.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return result;
    }
}
