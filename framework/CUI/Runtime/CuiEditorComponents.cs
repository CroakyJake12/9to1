using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>Explicit composition of the shared canonical editor library for runtime-parsed hosts.</summary>
public static class CuiEditorComponents
{
    public const string ImportSource = "framework/CUI/Components/Editor.cui";
    private static readonly Lazy<CuiDocument> Library = new(() =>
    {
        using var stream = typeof(CuiEditorComponents).Assembly.GetManifestResourceStream("CakeOS.Cui.Editor.cui")
            ?? throw new InvalidOperationException("The shared CUI editor library is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), ImportSource);
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The shared CUI editor library is invalid.");
        return document;
    });
    public static CuiDocument Compose(CuiDocument document)
    {
        if (!document.Imports.Any(import => import.Source == ImportSource) ||
            document.Components.Any(component => component.IsDefinition && component.Name == "EditorShell")) return document;
        return document with { Components = Library.Value.Components.Concat(document.Components).ToArray() };
    }
}
