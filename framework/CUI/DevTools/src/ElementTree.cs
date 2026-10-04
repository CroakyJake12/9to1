namespace Haven.CUI.DevTools;

public sealed class CuiElementNode
{
    public CuiElementNode(
        ElementId id,
        string type,
        string? name = null,
        IEnumerable<string>? classes = null,
        string? text = null,
        IEnumerable<CuiElementNode>? children = null,
        string? automationId = null,
        string? accessibleName = null,
        string? role = null,
        bool isFocusable = false,
        bool isHidden = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Id = id;
        Type = type;
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
        Classes = DiagnosticsCollections.Copy(classes?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal));
        Text = text;
        Children = DiagnosticsCollections.Copy(children);
        AutomationId = string.IsNullOrWhiteSpace(automationId) ? null : automationId;
        AccessibleName = string.IsNullOrWhiteSpace(accessibleName) ? null : accessibleName;
        Role = string.IsNullOrWhiteSpace(role) ? null : role;
        IsFocusable = isFocusable;
        IsHidden = isHidden;
    }

    public ElementId Id { get; }

    public string Type { get; }

    public string? Name { get; }

    public IReadOnlyList<string> Classes { get; }

    public string? Text { get; }

    public IReadOnlyList<CuiElementNode> Children { get; }

    public string? AutomationId { get; }

    public string? AccessibleName { get; }

    public string? Role { get; }

    public bool IsFocusable { get; }

    public bool IsHidden { get; }
}

public sealed record CuiElementSearchResult(
    ElementId ElementId,
    string Label,
    string Selector,
    IReadOnlyList<ElementId> Path,
    int Score);

/// <summary>A semantic selector. Every supplied field must match the same element.</summary>
public sealed record CuiElementSelector(
    string? Id = null,
    string? Role = null,
    string? AccessibleName = null,
    string? Type = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Id)
                           && string.IsNullOrWhiteSpace(Role)
                           && string.IsNullOrWhiteSpace(AccessibleName)
                           && string.IsNullOrWhiteSpace(Type);
}

public sealed class CuiElementTree
{
    private readonly IReadOnlyDictionary<ElementId, CuiElementNode> _nodes;
    private readonly IReadOnlyDictionary<ElementId, ElementId> _parents;
    private readonly List<ElementId> _order;

    public CuiElementTree(CuiElementNode root, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));

        Root = root;
        Revision = revision;
        var nodes = new Dictionary<ElementId, CuiElementNode>();
        var parents = new Dictionary<ElementId, ElementId>();
        var order = new List<ElementId>();
        var active = new HashSet<CuiElementNode>(ReferenceEqualityComparer.Instance);
        Index(root, null, nodes, parents, order, active);
        _nodes = DiagnosticsCollections.Copy<ElementId, CuiElementNode>(nodes);
        _parents = DiagnosticsCollections.Copy<ElementId, ElementId>(parents);
        _order = order;
    }

    public CuiElementNode Root { get; }

    public long Revision { get; }

    public int Count => _nodes.Count;

    public bool Contains(ElementId id) => _nodes.ContainsKey(id);

    public CuiElementNode? GetOrDefault(ElementId id) => _nodes.GetValueOrDefault(id);

    public IReadOnlyList<CuiElementNode> Find(CuiElementSelector selector, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (selector.IsEmpty) throw new ArgumentException("A semantic selector must specify at least one field.", nameof(selector));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        return Array.AsReadOnly(_order.Select(id => _nodes[id])
            .Where(node => Matches(node, selector))
            .Take(limit)
            .ToArray());
    }

    public IReadOnlyList<ElementId> GetPath(ElementId id)
    {
        if (!_nodes.ContainsKey(id)) return [];
        var path = new Stack<ElementId>();
        var current = id;
        path.Push(current);
        while (_parents.TryGetValue(current, out var parent))
        {
            path.Push(parent);
            current = parent;
        }

        return Array.AsReadOnly(path.ToArray());
    }

    public string BuildSelector(ElementId id)
    {
        var path = GetPath(id);
        if (path.Count == 0) return string.Empty;
        return string.Join(" > ", path.Select(part => BuildSelectorPart(_nodes[part])));
    }

    public IReadOnlyList<CuiElementSearchResult> Search(string? query, int limit = 100)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        var terms = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<CuiElementSearchResult>();
        foreach (var id in _order)
        {
            var node = _nodes[id];
            var searchable = $"{node.Type} {node.Name} {node.AutomationId} {node.AccessibleName} {node.Role} {string.Join(' ', node.Classes)} {node.Text}";
            if (terms.Any(term => !searchable.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;

            var score = terms.Sum(term => Score(node, term));
            matches.Add(new CuiElementSearchResult(id, BuildLabel(node), BuildSelector(id), GetPath(id), score));
        }

        return Array.AsReadOnly(matches
            .OrderByDescending(result => result.Score)
            .ThenBy(result => _order.IndexOf(result.ElementId))
            .Take(limit)
            .ToArray());
    }

    private static void Index(
        CuiElementNode node,
        ElementId? parent,
        Dictionary<ElementId, CuiElementNode> nodes,
        Dictionary<ElementId, ElementId> parents,
        List<ElementId> order,
        HashSet<CuiElementNode> active)
    {
        if (!active.Add(node))
            throw new ArgumentException("The element tree contains a cycle.", nameof(node));
        if (!nodes.TryAdd(node.Id, node))
            throw new ArgumentException($"Duplicate element id '{node.Id}'.", nameof(node));

        order.Add(node.Id);
        if (parent is { } parentId) parents.Add(node.Id, parentId);
        foreach (var child in node.Children)
            Index(child, node.Id, nodes, parents, order, active);
        active.Remove(node);
    }

    private static int Score(CuiElementNode node, string term)
    {
        if (string.Equals(node.Name, term, StringComparison.OrdinalIgnoreCase)) return 100;
        if (string.Equals(node.AutomationId, term, StringComparison.OrdinalIgnoreCase)) return 100;
        if (string.Equals(node.AccessibleName, term, StringComparison.OrdinalIgnoreCase)) return 95;
        if (string.Equals(node.Role, term, StringComparison.OrdinalIgnoreCase)) return 85;
        if (string.Equals(node.Type, term, StringComparison.OrdinalIgnoreCase)) return 80;
        if (node.Classes.Any(item => string.Equals(item, term, StringComparison.OrdinalIgnoreCase))) return 60;
        return 10;
    }

    private static string BuildSelectorPart(CuiElementNode node)
    {
        var name = node.Name is null ? string.Empty : $"#{node.Name}";
        var classes = node.Classes.Count == 0 ? string.Empty : $".{string.Join('.', node.Classes)}";
        return node.Type + name + classes;
    }

    private static string BuildLabel(CuiElementNode node)
    {
        var selector = BuildSelectorPart(node);
        var label = node.AccessibleName ?? node.Text;
        if (string.IsNullOrWhiteSpace(label)) return selector;
        var normalized = string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"{selector} \"{(normalized.Length <= 44 ? normalized : normalized[..41] + "…")}\"";
    }

    private static bool Matches(CuiElementNode node, CuiElementSelector selector) =>
        (string.IsNullOrWhiteSpace(selector.Id)
         || string.Equals(node.AutomationId, selector.Id, StringComparison.Ordinal)
         || string.Equals(node.Name, selector.Id, StringComparison.Ordinal))
        && (string.IsNullOrWhiteSpace(selector.Role)
            || string.Equals(node.Role, selector.Role, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(selector.AccessibleName)
            || string.Equals(node.AccessibleName, selector.AccessibleName, StringComparison.Ordinal))
        && (string.IsNullOrWhiteSpace(selector.Type)
            || string.Equals(node.Type, selector.Type, StringComparison.OrdinalIgnoreCase));
}
