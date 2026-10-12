using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>Explicit, in-process composition of the canonical app header controls.</summary>
public static class CuiAppComponents
{
    public const string ImportSource = "framework/CUI/Components/App.cui";
    private static readonly Lazy<CuiDocument> Library = new(() =>
    {
        using var stream = typeof(CuiAppComponents).Assembly.GetManifestResourceStream("CakeOS.Cui.App.cui")
            ?? throw new InvalidOperationException("The shared CUI app library is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), ImportSource);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The shared CUI app library is invalid.");
        return document;
    });
    public static CuiDocument Compose(CuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Imports.Any(import => import.Source == ImportSource)
            || document.Components.Any(component => component.IsDefinition && component.Name == "AppAccentMenu")) return document;
        return document with { Components = Library.Value.Components.Concat(document.Components).ToArray() };
    }
}
