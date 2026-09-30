using System.Text.Json.Serialization;

namespace NineToOne.Launcher;

public sealed record LauncherPlacement(Guid Id, Guid ApplicationId, int Column, int Row, Guid? FolderId = null);
public sealed record LauncherPage(Guid Id, string Name, IReadOnlyList<LauncherPlacement> Items);
public sealed record LauncherFolder(Guid Id, string Name, int Columns, IReadOnlyList<LauncherPlacement> Items);
public sealed record LauncherDock(Guid Id, int Rows, int Columns, IReadOnlyList<LauncherPlacement> Items);
public sealed record LauncherLayout(int SchemaVersion, Guid ActivePageId, int Rows, int Columns,
    IReadOnlyList<LauncherPage> Pages, IReadOnlyList<Guid> HiddenApplications)
{
    public const int CurrentSchema = 5;
    public LauncherPresentation? Presentation { get; init; }
    public LauncherDrawer? Drawer { get; init; }
    public LauncherDock? Dock { get; init; }
    public IReadOnlyList<LauncherFolder> Folders { get; init; } = [];
    [JsonIgnore] public LauncherPage ActivePage => Pages.Single(p => p.Id == ActivePageId);
    public static LauncherLayout Empty(int rows = 5, int columns = 4)
    {
        var page = new LauncherPage(Guid.NewGuid(), "Home", []);
        return new(CurrentSchema, page.Id, rows, columns, [page], []);
    }
    public void Validate()
    {
        if (SchemaVersion != CurrentSchema || Rows is < 3 or > 8 || Columns is < 3 or > 7 || Pages is null || Pages.Count is < 1 or > 64 ||
            Folders is null || Folders.Any(f => f is null) || Folders.Count > 256 || HiddenApplications is null || HiddenApplications.Count > 10000 || HiddenApplications.Any(id => id == Guid.Empty) || HiddenApplications.Distinct().Count() != HiddenApplications.Count)
            throw new InvalidDataException("Launcher layout requires recovery; unsupported data was preserved.");
        Presentation?.Validate();
        var ids = new HashSet<Guid>();
        foreach (var page in Pages)
        {
            if (page is null || page.Id == Guid.Empty || !ids.Add(page.Id) || !ValidName(page.Name) || page.Items is null || page.Items.Count > Rows * Columns)
                throw new InvalidDataException("Launcher page identity, name or capacity is invalid.");
            var cells = new HashSet<(int, int)>();
            foreach (var item in page.Items)
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || !ValidTarget(item) || item.Column < 0 || item.Column >= Columns || item.Row < 0 || item.Row >= Rows || !cells.Add((item.Column, item.Row)))
                    throw new InvalidDataException("Launcher placement identity or grid position is invalid.");
        }
        if (Dock is { } dock)
        {
            if (dock.Id == Guid.Empty || !ids.Add(dock.Id) || dock.Rows is < 1 or > 3 || dock.Columns is < 3 or > 7 || dock.Items is null || dock.Items.Count > dock.Rows * dock.Columns)
                throw new InvalidDataException("Launcher dock geometry is invalid.");
            var cells = new HashSet<(int, int)>();
            foreach (var item in dock.Items)
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || !ValidTarget(item) || item.Column < 0 || item.Column >= dock.Columns || item.Row < 0 || item.Row >= dock.Rows || !cells.Add((item.Column, item.Row)))
                    throw new InvalidDataException("Launcher dock placement is invalid.");
        }
        var folderReferences = Pages.SelectMany(p => p.Items).Concat(Dock?.Items ?? []).Where(i => i.FolderId is not null).Select(i => i.FolderId!.Value).ToArray();
        if (folderReferences.Length != folderReferences.Distinct().Count() || !folderReferences.ToHashSet().SetEquals(Folders.Select(f => f.Id)))
            throw new InvalidDataException("Launcher folder ownership is invalid.");
        if (Folders.Sum(f => f.Items?.Count ?? 0) > 10000) throw new InvalidDataException("Launcher folder capacity is exceeded.");
        foreach (var folder in Folders)
        {
            if (folder is null || folder.Id == Guid.Empty || !ids.Add(folder.Id) || !ValidName(folder.Name) || folder.Columns is < 3 or > 7 || folder.Items is null || folder.Items.Count > 1024)
                throw new InvalidDataException("Launcher folder identity or geometry is invalid.");
            var cells = new HashSet<(int, int)>();
            foreach (var item in folder.Items)
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.ApplicationId == Guid.Empty || item.FolderId is not null || item.Column < 0 || item.Column >= folder.Columns || item.Row < 0 || item.Row >= (1024 + folder.Columns - 1) / folder.Columns || !cells.Add((item.Column, item.Row)))
                    throw new InvalidDataException("Launcher folder member is invalid or nested.");
        }
        Drawer?.Validate(ids);
        if (!Pages.Any(p => p.Id == ActivePageId)) throw new InvalidDataException("Launcher active page is missing.");
    }
    public static LauncherLayout UpgradeKnownSchema(LauncherLayout layout)
    {
        // Versions 1 and 2 had pages/hidden apps and then the dock. Preserve every existing identity.
        if (layout.Drawer is null && ((layout.SchemaVersion == 1 && layout.Dock is null || layout.SchemaVersion == 2) && layout.Folders is { Count: 0 } && layout.Presentation is null ||
            layout.SchemaVersion == 3 && layout.Presentation is null || layout.SchemaVersion == 4))
            layout = layout with { SchemaVersion = CurrentSchema };
        layout.Validate(); return layout;
    }
    private static bool ValidTarget(LauncherPlacement item) => item.FolderId is { } folder ? folder != Guid.Empty && item.ApplicationId == Guid.Empty : item.ApplicationId != Guid.Empty;
    internal static bool ValidName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
}

public sealed record LauncherStoredLayout(long Revision, string AuthorityId, LauncherLayout Current, LauncherLayout? Previous);

/// <summary>Pure typed layout edits. Applications remain references to Home's installed registry.</summary>
public static partial class LauncherLayoutEdits
{
    public static LauncherLayout AddPage(LauncherLayout layout, string name)
    { var page = new LauncherPage(Guid.NewGuid(), name, []); return Checked(layout with { ActivePageId = page.Id, Pages = [.. layout.Pages, page] }); }
    public static LauncherLayout SelectPage(LauncherLayout layout, Guid id) => Checked(layout with { ActivePageId = id });
    public static LauncherLayout RenamePage(LauncherLayout layout, Guid id, string name)
    {
        RequirePage(layout, id);
        return Checked(layout with { Pages = layout.Pages.Select(p => p.Id == id ? p with { Name = name } : p).ToArray() });
    }
    public static LauncherLayout ReorderPage(LauncherLayout layout, Guid id, int delta)
    {
        var pages = layout.Pages.ToList(); var index = pages.FindIndex(p => p.Id == id);
        if (index < 0) throw new InvalidOperationException("This launcher page is no longer available.");
        var next = Math.Clamp(index + Math.Sign(delta), 0, pages.Count - 1);
        (pages[index], pages[next]) = (pages[next], pages[index]); return Checked(layout with { Pages = pages.ToArray() });
    }
    public static LauncherLayout RemovePage(LauncherLayout layout, Guid id)
    {
        var page = RequirePage(layout, id);
        if (layout.Pages.Count == 1 || page.Items.Count != 0) throw new InvalidOperationException("Keep at least one page. Move or remove this page's shortcuts before deleting it.");
        var pages = layout.Pages.Where(p => p.Id != id).ToArray();
        return Checked(layout with { Pages = pages, ActivePageId = layout.ActivePageId == id ? pages[0].Id : layout.ActivePageId });
    }
    public static LauncherLayout AddApplication(LauncherLayout layout, Guid pageId, Guid applicationId)
    {
        var page = RequirePage(layout, pageId);
        var occupied = page.Items.Select(i => (i.Column, i.Row)).ToHashSet();
        var cell = Enumerable.Range(0, layout.Rows).SelectMany(row => Enumerable.Range(0, layout.Columns).Select(column => (Column: column, Row: row)))
            .Where(c => !occupied.Contains(c)).Select(c => ((int Column, int Row)?)c).FirstOrDefault()
            ?? throw new InvalidOperationException("This page is full. Add a page or move a shortcut first.");
        var item = new LauncherPlacement(Guid.NewGuid(), applicationId, cell.Column, cell.Row);
        return Checked(layout with { Pages = layout.Pages.Select(p => p.Id == pageId ? p with { Items = [.. p.Items, item] } : p).ToArray() });
    }
    public static LauncherLayout MovePlacement(LauncherLayout layout, Guid itemId, Guid targetPageId, int column, int row)
    {
        var containers = Containers(layout).ToArray();
        var source = containers.SingleOrDefault(p => p.Items.Any(i => i.Id == itemId)) ?? throw new InvalidOperationException("This shortcut is no longer available.");
        var item = source.Items.Single(i => i.Id == itemId);
        var target = containers.SingleOrDefault(p => p.Id == targetPageId) ?? throw new InvalidOperationException("This launcher target is no longer available.");
        var displaced = target.Items.SingleOrDefault(i => i.Column == column && i.Row == row);
        if (displaced?.Id == item.Id) return layout;
        LauncherPlacement[] Items(LauncherPage p)
        {
            var items = p.Items.Where(i => i.Id != item.Id && i.Id != displaced?.Id).ToList();
            if (p.Id == targetPageId) items.Add(item with { Column = column, Row = row });
            if (p.Id == source.Id && displaced is not null) items.Add(displaced with { Column = item.Column, Row = item.Row });
            return items.ToArray();
        }
        return Checked(layout with { Pages = layout.Pages.Select(p => p with { Items = Items(p) }).ToArray(),
            Dock = layout.Dock is { } dock ? dock with { Items = Items(new(dock.Id, "Dock", dock.Items)) } : null,
            Folders = layout.Folders.Select(f => f with { Items = Items(new(f.Id, f.Name, f.Items)) }).ToArray() });
    }
    public static LauncherLayout RemovePlacement(LauncherLayout layout, Guid itemId)
    {
        var removed = Placements(layout).SingleOrDefault(i => i.Id == itemId);
        if (removed?.FolderId is { } folderId && layout.Folders.Single(f => f.Id == folderId).Items.Count > 0)
            throw new InvalidOperationException("Move or remove this folder's shortcuts before deleting it.");
        if (!Containers(layout).Any(p => p.Items.Any(i => i.Id == itemId))) throw new InvalidOperationException("This shortcut is no longer available.");
        return Checked(layout with { Pages = layout.Pages.Select(p => p with { Items = p.Items.Where(i => i.Id != itemId).ToArray() }).ToArray(),
            Dock = layout.Dock is { } dock ? dock with { Items = dock.Items.Where(i => i.Id != itemId).ToArray() } : null,
            Folders = layout.Folders.Where(f => f.Id != removed?.FolderId).Select(f => f with { Items = f.Items.Where(i => i.Id != itemId).ToArray() }).ToArray() });
    }
    public static LauncherLayout SetHidden(LauncherLayout layout, Guid appId, bool hidden) => Checked(layout with
    { HiddenApplications = hidden ? layout.HiddenApplications.Append(appId).Distinct().ToArray() : layout.HiddenApplications.Where(id => id != appId).ToArray() });
    public static LauncherLayout Reflow(LauncherLayout layout, int rows, int columns)
    {
        if (rows is < 3 or > 8 || columns is < 3 or > 7) throw new ArgumentOutOfRangeException(nameof(rows));
        if (rows == layout.Rows && columns == layout.Columns) return Checked(layout);
        var items = layout.Pages.SelectMany(p => p.Items.OrderBy(i => i.Row).ThenBy(i => i.Column)).ToArray();
        var pages = layout.Pages.ToList(); var needed = Math.Max(1, (items.Length + rows * columns - 1) / (rows * columns));
        while (pages.Count < needed) pages.Add(new(Guid.NewGuid(), $"Page {pages.Count + 1}", []));
        for (var index = 0; index < pages.Count; index++) pages[index] = pages[index] with { Items = items.Skip(index * rows * columns).Take(rows * columns)
            .Select((item, offset) => item with { Column = offset % columns, Row = offset / columns }).ToArray() };
        return Checked(layout with { Rows = rows, Columns = columns, Pages = pages.ToArray() });
    }
    public static LauncherLayout Seed(LauncherLayout layout, IEnumerable<Guid> appIds)
    {
        foreach (var id in appIds.Distinct())
        {
            if (layout.ActivePage.Items.Count == layout.Rows * layout.Columns) layout = AddPage(layout, $"Page {layout.Pages.Count + 1}");
            layout = AddApplication(layout, layout.ActivePageId, id);
        }
        return Checked(layout with { ActivePageId = layout.Pages[0].Id });
    }
    public static LauncherLayout Clone(LauncherLayout layout) => layout with { Drawer = layout.Drawer?.Copy(), Pages = layout.Pages.Select(p => p with { Items = p.Items.ToArray() }).ToArray(), HiddenApplications = layout.HiddenApplications.ToArray(), Dock = layout.Dock is { } dock ? dock with { Items = dock.Items.ToArray() } : null, Folders = layout.Folders.Select(f => f with { Items = f.Items.ToArray() }).ToArray() };
    private static LauncherPage RequirePage(LauncherLayout layout, Guid id) => layout.Pages.SingleOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("This launcher page is no longer available.");
    private static LauncherLayout Checked(LauncherLayout layout) { layout.Validate(); return layout; }
}
