using HavenOS.Home.Core;

namespace NineToOne.Launcher;

/// <summary>Android provider locator only; the OS-owned AppWidget ID is a separate device-local binding.</summary>
public sealed record LauncherAndroidWidgetReference(string ProviderComponent, string PlatformProfileId);
/// <summary>Profile-owned geometry and inert owner locator. Neither locator grants rendering or actions.</summary>
public sealed record LauncherWidgetPlacement(Guid Id, Guid PageId, int Column, int Row, int ColumnSpan, int RowSpan,
    string Label, HomeNativeWidgetReference? Native = null, LauncherAndroidWidgetReference? Android = null,
    string? ConfigurationReference = null);

public static partial class LauncherLayoutEdits
{
    public static LauncherLayout AddWidget(LauncherLayout layout, Guid pageId, string label, int columns, int rows,
        HomeNativeWidgetReference? native = null, LauncherAndroidWidgetReference? android = null,
        string? configurationReference = null)
    {
        RequirePage(layout, pageId);
        var cell = FindWidgetCell(layout, pageId, columns, rows);
        return Checked(layout with { Widgets = [.. layout.Widgets,
            new(Guid.NewGuid(), pageId, cell.Column, cell.Row, columns, rows, label, native, android, configurationReference)] });
    }
    public static LauncherLayout MoveWidget(LauncherLayout layout, Guid id, Guid pageId, int column, int row,
        int columns, int rows)
    {
        RequirePage(layout, pageId);
        if (!layout.Widgets.Any(widget => widget.Id == id)) throw new InvalidOperationException("This widget placement is no longer available.");
        return Checked(layout with { Widgets = layout.Widgets.Select(widget => widget.Id == id
            ? widget with { PageId = pageId, Column = column, Row = row, ColumnSpan = columns, RowSpan = rows } : widget).ToArray() });
    }
    public static LauncherLayout RemoveWidget(LauncherLayout layout, Guid id)
    {
        if (!layout.Widgets.Any(widget => widget.Id == id)) throw new InvalidOperationException("This widget placement is no longer available.");
        return Checked(layout with { Widgets = layout.Widgets.Where(widget => widget.Id != id).ToArray() });
    }
    internal static IEnumerable<(int Column, int Row)> WidgetCells(LauncherWidgetPlacement widget)
    {
        for (var row = widget.Row; row < widget.Row + widget.RowSpan; row++)
            for (var column = widget.Column; column < widget.Column + widget.ColumnSpan; column++) yield return (column, row);
    }
    internal static HashSet<(int Column, int Row)> PageOccupied(LauncherLayout layout, Guid pageId)
    {
        var page = RequirePage(layout, pageId);
        return page.Items.Select(item => (item.Column, item.Row))
            .Concat(layout.Widgets.Where(widget => widget.PageId == pageId).SelectMany(WidgetCells)).ToHashSet();
    }
    private static LauncherLayout ReflowWithWidgets(LauncherLayout layout, int rows, int columns)
    {
        if (layout.Widgets.Any(widget => widget.ColumnSpan > columns || widget.RowSpan > rows))
            throw new InvalidOperationException("Resize widgets before reducing the page grid. All placements were preserved.");
        var working = layout with { Rows = rows, Columns = columns, Widgets = [],
            Pages = layout.Pages.Select(page => page with { Items = Array.Empty<LauncherPlacement>() }).ToArray() };
        foreach (var sourcePage in layout.Pages)
        {
            var entries = sourcePage.Items.Select(item => (item.Row, item.Column, item.Id, App: (LauncherPlacement?)item, Widget: (LauncherWidgetPlacement?)null))
                .Concat(layout.Widgets.Where(widget => widget.PageId == sourcePage.Id)
                    .Select(widget => (widget.Row, widget.Column, widget.Id, App: (LauncherPlacement?)null, Widget: (LauncherWidgetPlacement?)widget)))
                .OrderBy(entry => entry.Row).ThenBy(entry => entry.Column).ThenBy(entry => entry.Id);
            foreach (var entry in entries)
            {
                var width = entry.Widget?.ColumnSpan ?? 1; var height = entry.Widget?.RowSpan ?? 1;
                Guid destination = default; (int Column, int Row)? cell = null;
                foreach (var page in working.Pages)
                {
                    cell = TryFindWidgetCell(working, page.Id, width, height);
                    if (cell is not null) { destination = page.Id; break; }
                }
                if (cell is null)
                {
                    if (working.Pages.Count >= 64) throw new InvalidOperationException("This grid would exceed the page limit. All placements were preserved.");
                    var page = new LauncherPage(Guid.NewGuid(), $"Page {working.Pages.Count + 1}", []);
                    working = working with { Pages = [.. working.Pages, page] }; destination = page.Id; cell = (0, 0);
                }
                if (entry.Widget is { } widget)
                    working = working with { Widgets = [.. working.Widgets, widget with { PageId = destination, Column = cell.Value.Column, Row = cell.Value.Row }] };
                else if (entry.App is { } app)
                    working = working with { Pages = working.Pages.Select(page => page.Id == destination
                        ? page with { Items = [.. page.Items, app with { Column = cell.Value.Column, Row = cell.Value.Row }] } : page).ToArray() };
            }
        }
        return Checked(working);
    }
    private static (int Column, int Row) FindWidgetCell(LauncherLayout layout, Guid pageId, int columns, int rows)
        => TryFindWidgetCell(layout, pageId, columns, rows) ?? throw new InvalidOperationException("There is no room for this widget. Move items or add a page first.");
    private static (int Column, int Row)? TryFindWidgetCell(LauncherLayout layout, Guid pageId, int columns, int rows)
    {
        if (columns < 1 || rows < 1 || columns > layout.Columns || rows > layout.Rows)
            throw new InvalidOperationException("This widget does not fit the current page grid.");
        var occupied = PageOccupied(layout, pageId);
        for (var row = 0; row <= layout.Rows - rows; row++)
            for (var column = 0; column <= layout.Columns - columns; column++)
            {
                var available = true;
                for (var y = row; y < row + rows && available; y++)
                    for (var x = column; x < column + columns; x++) if (occupied.Contains((x, y))) { available = false; break; }
                if (available) return (column, row);
            }
        return null;
    }
}
