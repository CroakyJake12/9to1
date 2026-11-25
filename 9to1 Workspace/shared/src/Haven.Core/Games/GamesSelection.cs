namespace Haven.Core.Games;

/// <summary>The kind of selectable identity. Hierarchy, viewport, components and assets all reference the same underlying IDs, so a selection is
/// expressed by (Kind, ID) where the ID is the shared canonical identity across views.</summary>
public enum GamesSelectableKind { Entity, Component, Asset, Graph }

/// <summary>A single selected identity. Equality is by (Kind, ID); the ID is the same value used by hierarchy/viewport/components/assets so selection is shared.</summary>
public sealed record GamesSelectionItem(GamesSelectableKind Kind, Guid ID)
{
    public GamesSelectionItem Capture()
    {
        if (ID == Guid.Empty) throw new InvalidDataException("Games selection identity is missing.");
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException("Games selection kind is unsupported.");
        return this;
    }
}

/// <summary>The shared cross-view selection context. It holds the currently selected identities as stable IDs so hierarchy, viewport, components and
/// assets agree on what is selected. Revision-aware immutable transactions never mutate the original.</summary>
public sealed record GamesSelectionContext(int Revision, IReadOnlyList<GamesSelectionItem> Selected)
{
    public const int MaximumSelected = 1024;

    public GamesSelectionContext Capture()
    {
        if (Revision < 1) throw new InvalidDataException("Games selection revision must be positive.");
        var items = (Selected ?? []).Select(i => i?.Capture() ?? throw new InvalidDataException("A games selection item is missing.")).ToArray();
        if (items.Length > MaximumSelected) throw new InvalidDataException("Games selection exceeds the configured limit.");
        if (items.Select(i => (i.Kind, i.ID)).Distinct().Count() != items.Length) throw new InvalidOperationException("GamesDuplicateSelection");
        return this with { Selected = items };
    }

    public static GamesSelectionContext Empty() => new(1, Array.Empty<GamesSelectionItem>()).Capture();

    private GamesSelectionContext Next(int expectedRevision, IReadOnlyList<GamesSelectionItem> selected)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesSelectionRevisionConflict");
        return new GamesSelectionContext(checked(Revision + 1), selected).Capture();
    }

    /// <summary>Select a single identity, replacing any prior selection (single-selection semantics for the primary view).</summary>
    public GamesSelectionContext Select(GamesSelectableKind kind, Guid id, int expectedRevision)
        => Next(expectedRevision, [new GamesSelectionItem(kind, id)]);

    /// <summary>Add to a multi-selection. Re-adding an already-selected identity is a no-op that still advances revision only if changed.</summary>
    public GamesSelectionContext Add(GamesSelectionItem item, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(item);
        var captured = item.Capture();
        if (Selected.Any(existing => existing.Kind == captured.Kind && existing.ID == captured.ID)) return this;
        return Next(expectedRevision, [.. Selected, captured]);
    }

    /// <summary>Toggle membership of an identity within the selection.</summary>
    public GamesSelectionContext Toggle(GamesSelectionItem item, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(item);
        var captured = item.Capture();
        bool present = Selected.Any(existing => existing.Kind == captured.Kind && existing.ID == captured.ID);
        var selected = present
            ? Selected.Where(existing => !(existing.Kind == captured.Kind && existing.ID == captured.ID)).ToArray()
            : [.. Selected, captured];
        return Next(expectedRevision, selected);
    }

    public GamesSelectionContext Clear(int expectedRevision) => Next(expectedRevision, Array.Empty<GamesSelectionItem>());

    /// <summary>Whether an identity is currently selected. Views sharing this context agree on selection by shared ID.</summary>
    public bool IsSelected(GamesSelectableKind kind, Guid id) => Selected.Any(item => item.Kind == kind && item.ID == id);
}
