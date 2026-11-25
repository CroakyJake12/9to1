namespace Haven.Core.Games;

public enum GamesWidgetKind { Panel, Text, Button, Image, Slider }

public sealed record GamesWidgetNode(
    string Id,
    string Name,
    GamesWidgetKind Kind,
    string? Text,
    string? StyleToken,
    string? LayoutReference,
    string? GameplayEventBinding,
    string? ParentId);

public sealed record GamesWidgetDocument
{
    public string ScreenId { get; }
    public int Revision { get; }
    public IReadOnlyList<GamesWidgetNode> Nodes { get; }

    [System.Text.Json.Serialization.JsonConstructor]
    public GamesWidgetDocument(string screenId, int revision, IReadOnlyList<GamesWidgetNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        var snapshot = nodes.ToArray();
        Validate(screenId, snapshot);
        ScreenId = screenId;
        Revision = revision;
        Nodes = Array.AsReadOnly(snapshot);
    }

    public static GamesWidgetDocument Create(string screenId, IEnumerable<GamesWidgetNode> nodes)
        => new(screenId, 1, nodes.ToArray());

    public GamesWidgetDocument Add(GamesWidgetNode node, int expectedRevision)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("stale edit");
        var next = Nodes.Append(node).ToList();
        return new GamesWidgetDocument(ScreenId, checked(Revision + 1), next);
    }

    public GamesWidgetDocument Reparent(string id, string? parentId, int expectedRevision)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("stale edit");
        if (!Nodes.Any(n => n.Id == id)) throw new ArgumentException("Unknown widget.", nameof(id));
        var next = Nodes.Select(n => n.Id == id ? n with { ParentId = parentId } : n).ToList();
        return new GamesWidgetDocument(ScreenId, checked(Revision + 1), next);
    }

    public GamesWidgetDocument Update(string id, string? text, string? styleToken, string? layoutReference, string? gameplayEventBinding, int expectedRevision)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("stale edit");
        if (!Nodes.Any(n => n.Id == id)) throw new ArgumentException("Unknown widget.", nameof(id));
        var next = Nodes.Select(n => n.Id == id ? n with { Text = text, StyleToken = styleToken, LayoutReference = layoutReference, GameplayEventBinding = gameplayEventBinding } : n).ToList();
        return new GamesWidgetDocument(ScreenId, checked(Revision + 1), next);
    }

    public GamesWidgetDocument Reorder(IReadOnlyList<string> orderedIds, int expectedRevision)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("stale edit");
        var byId = Nodes.ToDictionary(n => n.Id);
        var next = orderedIds.Select(id => byId[id]).ToList();
        if (next.Count != Nodes.Count || next.Count != orderedIds.Count) throw new InvalidOperationException("invalid reorder");
        return new GamesWidgetDocument(ScreenId, checked(Revision + 1), next);
    }

    private static void Validate(string screenId, IReadOnlyList<GamesWidgetNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(screenId)) throw new ArgumentException("screen id required");
        if (nodes.Any(n => n is null)) throw new ArgumentException("A widget is missing.");
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != nodes.Count) throw new ArgumentException("duplicate widget id");
        foreach (var n in nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.Name)) throw new ArgumentException("id/name required");
            if (!Enum.IsDefined(n.Kind)) throw new ArgumentException("Unknown widget kind.");
            if (n.GameplayEventBinding is not null && !IsValidEventName(n.GameplayEventBinding)) throw new ArgumentException("invalid event name");
            if (n.ParentId is not null && !ids.Contains(n.ParentId)) throw new ArgumentException("unknown parent");
        }
        var parentById = nodes.ToDictionary(n => n.Id, n => n.ParentId, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            var chain = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = n.Id;
            while (cursor is not null && !visited.Contains(cursor))
            {
                if (!chain.Add(cursor)) throw new ArgumentException("cycle detected");
                cursor = parentById[cursor];
            }
            visited.UnionWith(chain);
        }
    }

    private static bool IsValidEventName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}
