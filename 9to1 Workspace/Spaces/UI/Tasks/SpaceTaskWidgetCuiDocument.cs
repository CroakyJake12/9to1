using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces.Tasks;

public static class SpaceTaskWidgetCuiDocument
{
    public static CuiDocument Load()
    {
        using var stream = typeof(SpaceTaskWidgetCuiDocument).Assembly.GetManifestResourceStream(
            "HavenOS.Apps.Spaces.UI.Tasks.SpaceTaskWidget.cui")
            ?? throw new InvalidOperationException("The Space task widget resource is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "SpaceTaskWidget.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Space task widget resource is invalid.");
        return document;
    }
}
