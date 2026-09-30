namespace NineToOne.Launcher;

public static partial class LauncherLayoutEdits
{
    public static Guid ContainerForPlacement(LauncherLayout layout, Guid itemId) => Containers(layout).SingleOrDefault(p => p.Items.Any(i => i.Id == itemId))?.Id
        ?? throw new InvalidOperationException("This shortcut is no longer available.");
    public static LauncherLayout CreateFolder(LauncherLayout layout, Guid pageId, string name)
    {
        var page = RequirePage(layout, pageId); var cell = FreeCell(page.Items, layout.Rows, layout.Columns);
        var folder = new LauncherFolder(Guid.NewGuid(), name, 4, []);
        var placement = new LauncherPlacement(Guid.NewGuid(), Guid.Empty, cell.Column, cell.Row, folder.Id);
        return Checked(layout with { Folders = [.. layout.Folders, folder],
            Pages = layout.Pages.Select(p => p.Id == pageId ? p with { Items = [.. p.Items, placement] } : p).ToArray() });
    }
    public static LauncherLayout ConfigureFolder(LauncherLayout layout, Guid id, string name, int columns)
    {
        var folder = layout.Folders.SingleOrDefault(f => f.Id == id) ?? throw new InvalidOperationException("This folder is no longer available.");
        if (columns is < 3 or > 7) throw new ArgumentOutOfRangeException(nameof(columns));
        var changed = folder with { Name = name, Columns = columns, Items = columns == folder.Columns ? folder.Items.ToArray()
            : folder.Items.OrderBy(i => i.Row).ThenBy(i => i.Column).Select((item, index) => item with { Column = index % columns, Row = index / columns }).ToArray() };
        return Checked(layout with { Folders = layout.Folders.Select(f => f.Id == id ? changed : f).ToArray() });
    }
}
