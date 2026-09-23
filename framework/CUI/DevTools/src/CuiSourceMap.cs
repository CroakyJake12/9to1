namespace Haven.CUI.DevTools;

public readonly record struct CuiSourceSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public bool IsValid => StartLine > 0
                           && StartColumn > 0
                           && EndLine >= StartLine
                           && (EndLine != StartLine || EndColumn >= StartColumn);
}

public sealed record CuiSourceLocation
{
    private CuiSourceLocation(string filePath, CuiSourceSpan span, string? authoredId)
    {
        FilePath = filePath;
        Span = span;
        AuthoredId = authoredId;
    }

    public string FilePath { get; }

    public CuiSourceSpan Span { get; }

    public string? AuthoredId { get; }

    public static CuiSourceLocation Create(string filePath, CuiSourceSpan span, string? authoredId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!string.Equals(Path.GetExtension(filePath), ".cui", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("CUI diagnostics source must be a .cui file; AXAML and other live sources are rejected.", nameof(filePath));
        if (!span.IsValid)
            throw new ArgumentOutOfRangeException(nameof(span), "Source spans use positive, ordered one-based positions.");

        return new CuiSourceLocation(filePath, span, string.IsNullOrWhiteSpace(authoredId) ? null : authoredId);
    }
}

public sealed record CuiSourceMapEntry(ElementId ElementId, CuiSourceLocation Location);

public sealed class CuiSourceMap
{
    private readonly IReadOnlyDictionary<ElementId, CuiSourceLocation> _locations;

    public CuiSourceMap(IEnumerable<CuiSourceMapEntry>? entries = null)
    {
        var locations = new Dictionary<ElementId, CuiSourceLocation>();
        foreach (var entry in entries ?? [])
        {
            ArgumentNullException.ThrowIfNull(entry.Location);
            if (!locations.TryAdd(entry.ElementId, entry.Location))
                throw new ArgumentException($"Duplicate source mapping for element '{entry.ElementId}'.", nameof(entries));
        }

        _locations = DiagnosticsCollections.Copy<ElementId, CuiSourceLocation>(locations);
    }

    public bool TryGetLocation(ElementId elementId, out CuiSourceLocation? location) =>
        _locations.TryGetValue(elementId, out location);

    public CuiSourceLocation? GetLocationOrDefault(ElementId elementId) =>
        _locations.GetValueOrDefault(elementId);
}
