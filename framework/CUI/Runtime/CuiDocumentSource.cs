using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>Parses immutable authored CUI once, optionally when its surface is first requested.</summary>
public sealed class CuiDocumentSource
{
    private readonly Lazy<CuiDocument> _document;
    public CuiDocument Document => _document.Value;
    public bool IsLoaded => _document.IsValueCreated;

    public CuiDocumentSource(string markup, string source, bool deferParsing = false)
    {
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        _document = new(() =>
        {
            var parser = new CuiRichParser();
            var document = parser.Parse(markup, source);
            if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException($"The CUI document {source} is invalid.");
            return document;
        });
        if (!deferParsing) _ = Document;
    }
}
