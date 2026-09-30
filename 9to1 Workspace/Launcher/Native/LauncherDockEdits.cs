namespace NineToOne.Launcher;

public static partial class LauncherLayoutEdits
{
    private static IEnumerable<LauncherPage> Containers(LauncherLayout layout) => (layout.Dock is { } dock
        ? layout.Pages.Append(new(dock.Id, "Dock", dock.Items)) : layout.Pages).Concat(layout.Folders.Select(f => new LauncherPage(f.Id, f.Name, f.Items)));
    public static IEnumerable<LauncherPlacement> Placements(LauncherLayout layout) => Containers(layout).SelectMany(p => p.Items);
    public static LauncherLayout ConfigureDock(LauncherLayout layout, int rows, int columns)
    {
        if (rows is < 1 or > 3 || columns is < 3 or > 7) throw new ArgumentOutOfRangeException(nameof(rows));
        if (layout.Dock is { } unchanged && unchanged.Rows == rows && unchanged.Columns == columns) return Checked(layout);
        var dock = layout.Dock ?? new LauncherDock(Guid.NewGuid(), rows, columns, []);
        if (dock.Items.Count > rows * columns) throw new InvalidOperationException("Move shortcuts out before reducing the dock capacity.");
        return Checked(layout with { Dock = dock with { Rows = rows, Columns = columns,
            Items = dock.Items.OrderBy(i => i.Row).ThenBy(i => i.Column).Select((i, offset) => i with { Column = offset % columns, Row = offset / columns }).ToArray() } });
    }
    public static LauncherLayout RemoveDock(LauncherLayout layout)
    {
        if (layout.Dock?.Items.Count > 0) throw new InvalidOperationException("Move the dock's shortcuts to a page before hiding it.");
        return Checked(layout with { Dock = null });
    }
    public static LauncherLayout AddDockApplication(LauncherLayout layout, Guid applicationId)
    {
        var dock = layout.Dock ?? throw new InvalidOperationException("Configure a dock first.");
        var cell = FreeCell(dock.Items, dock.Rows, dock.Columns);
        return Checked(layout with { Dock = dock with { Items = [.. dock.Items, new(Guid.NewGuid(), applicationId, cell.Column, cell.Row)] } });
    }
    public static LauncherLayout MoveToContainer(LauncherLayout layout, Guid itemId, Guid targetId)
    {
        var dock = layout.Dock;
        var target = Containers(layout).SingleOrDefault(p => p.Id == targetId) ?? throw new InvalidOperationException("This launcher target is no longer available.");
        if (target.Items.Any(i => i.Id == itemId)) return layout;
        var folder = layout.Folders.SingleOrDefault(f => f.Id == targetId);
        if (folder?.Items.Count >= 1024) throw new InvalidOperationException("This folder is full.");
        var cell = FreeCell(target.Items, folder is not null ? (1024 + folder.Columns - 1) / folder.Columns : dock?.Id == targetId ? dock.Rows : layout.Rows,
            folder?.Columns ?? (dock?.Id == targetId ? dock.Columns : layout.Columns));
        return MovePlacement(layout, itemId, targetId, cell.Column, cell.Row);
    }
    private static (int Column, int Row) FreeCell(IReadOnlyList<LauncherPlacement> items, int rows, int columns)
    {
        var occupied = items.Select(i => (i.Column, i.Row)).ToHashSet();
        return Enumerable.Range(0, rows).SelectMany(row => Enumerable.Range(0, columns).Select(column => (Column: column, Row: row)))
            .Where(c => !occupied.Contains(c)).Select(c => ((int Column, int Row)?)c).FirstOrDefault()
            ?? throw new InvalidOperationException("The target is full. Move a shortcut or increase its grid size first.");
    }
}
