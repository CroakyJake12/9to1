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
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cui",
        ".cui-theme",
        ".cui-prefab"
    };

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
        if (!SourceExtensions.Contains(Path.GetExtension(filePath)))
            throw new ArgumentException("CUI source locations must refer to .cui, .cui-theme, or .cui-prefab files; AXAML and other live sources are rejected.", nameof(filePath));
        if (!span.IsValid)
            throw new ArgumentOutOfRangeException(nameof(span), "Source spans use positive, ordered one-based positions.");

        return new CuiSourceLocation(filePath, span, string.IsNullOrWhiteSpace(authoredId) ? null : authoredId);
    }
}

public sealed record CuiSourceMapEntry(ElementId ElementId, CuiSourceLocation Location, bool HasStableIdentity = false);

public sealed class CuiSourceMap
{
    private readonly IReadOnlyDictionary<ElementId, CuiSourceLocation> _locations;
    private readonly IReadOnlySet<ElementId> _stableIdentities;

    public CuiSourceMap(IEnumerable<CuiSourceMapEntry>? entries = null)
    {
        var locations = new Dictionary<ElementId, CuiSourceLocation>();
        var stableIdentities = new HashSet<ElementId>();
        foreach (var entry in entries ?? [])
        {
            ArgumentNullException.ThrowIfNull(entry.Location);
            if (!locations.TryAdd(entry.ElementId, entry.Location))
                throw new ArgumentException($"Duplicate source mapping for element '{entry.ElementId}'.", nameof(entries));
            if (entry.HasStableIdentity)
                stableIdentities.Add(entry.ElementId);
        }

        _locations = DiagnosticsCollections.Copy<ElementId, CuiSourceLocation>(locations);
        _stableIdentities = stableIdentities;
    }

    /// <summary>Creates source mappings from the canonical CUI document while preserving authored IDs.</summary>
    public static CuiSourceMap FromDocument(CakeOS.Cui.CuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entries = new List<CuiSourceMapEntry>();

        void AddChildren(IReadOnlyList<CakeOS.Cui.CuiComponent> children, string parentIdentity)
        {
            var typeCounts = children.GroupBy(child => child.Type, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var stableCounts = children.GroupBy(child => child.StableId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var ambiguousOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var child in children)
            {
                var componentIdentity = child.AuthoredId is not null
                    ? $"id:{Uri.EscapeDataString(child.AuthoredId)}"
                    : typeCounts[child.Type] == 1
                        ? $"type:{Uri.EscapeDataString(child.Type)}"
                        : stableCounts[child.StableId] == 1
                            ? child.StableId
                            : GetAmbiguousIdentity(child, ambiguousOrdinals);
                var isUnambiguous = child.AuthoredId is not null
                                    || typeCounts[child.Type] == 1
                                    || stableCounts[child.StableId] == 1;
                var identity = $"{parentIdentity}/{componentIdentity}";

                var id = new ElementId(identity);
                var span = child.Span;
                entries.Add(new CuiSourceMapEntry(id,
                    CuiSourceLocation.Create(span.SourceName,
                        new CuiSourceSpan(span.Start.Line, span.Start.Column, span.End.Line, span.End.Column), child.Name),
                    isUnambiguous));
                AddChildren(child.Children, identity);
                AddChildren(child.ElseChildren, identity + "/else");
            }
        }

        AddChildren(document.Components, "document");
        foreach (var template in document.Templates.Values)
            AddChildren(template.Content, $"template:{template.Name}");
        return new CuiSourceMap(entries);
    }

    private static string GetAmbiguousIdentity(
        CakeOS.Cui.CuiComponent component,
        Dictionary<string, int> ordinals)
    {
        ordinals.TryGetValue(component.StableId, out var ordinal);
        ordinals[component.StableId] = ordinal + 1;
        return $"ambiguous:{Uri.EscapeDataString(component.Type)}:{ordinal}";
    }

    public bool TryGetLocation(ElementId elementId, out CuiSourceLocation? location) =>
        _locations.TryGetValue(elementId, out location);

    public CuiSourceLocation? GetLocationOrDefault(ElementId elementId) =>
        _locations.GetValueOrDefault(elementId);

    public CuiSourceLocation? GetLocationByAuthoredId(string? authoredId)
    {
        if (string.IsNullOrWhiteSpace(authoredId)) return null;
        var matches = _locations.Values.Where(location =>
            string.Equals(location.AuthoredId, authoredId, StringComparison.Ordinal)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Returns true only when the mapping can safely identify the same authored element across revisions.</summary>
    public bool HasStableIdentity(ElementId elementId) => _stableIdentities.Contains(elementId);

    public IReadOnlyList<CuiSourceMapEntry> Entries => Array.AsReadOnly(_locations
        .Select(pair => new CuiSourceMapEntry(pair.Key, pair.Value, _stableIdentities.Contains(pair.Key)))
        .ToArray());
}
