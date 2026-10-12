using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>Explicit, immutable library composition for the runtime-parsed Settings host.</summary>
public static class CuiThemeCreatorComponents
{
    public const string ImportSource = "framework/CUI/Components/ThemeCreator.cui";
    private static readonly Lazy<CuiDocument> Library = new(() =>
    {
        using var stream = typeof(CuiThemeCreatorComponents).Assembly.GetManifestResourceStream("CakeOS.Cui.ThemeCreator.cui")
            ?? throw new InvalidOperationException("The CUI theme creator library is unavailable.");
        using var reader = new StreamReader(stream);
        return ParseLibrary(reader.ReadToEnd());
    });
    internal static CuiDocument ParseLibrary(string markup)
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(markup, ImportSource);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The CUI theme creator library is invalid.");
        return document;
    }
    public static CuiDocument Compose(CuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Imports.Any(import => import.Source == ImportSource)
            || document.Components.Any(component => component.IsDefinition && component.Name == "ThemeCreatorPanel")) return document;
        return Compose(document, Library.Value);
    }
    internal static CuiDocument Compose(CuiDocument document, CuiDocument library)
    {
        if (!document.Imports.Any(import => import.Source == ImportSource)
            || document.Components.Any(component => component.IsDefinition && component.Name == "ThemeCreatorPanel")) return document;
        return document with { Components = library.Components.Concat(document.Components).ToArray() };
    }
}
