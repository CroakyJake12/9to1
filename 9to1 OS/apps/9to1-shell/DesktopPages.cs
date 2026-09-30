using System.Security.Cryptography;
using System.Text;

namespace NineToOne.Os.Shell;

public enum DesktopPageItemKind { Application, File, Folder, Shelf, Shortcut, Widget }
public sealed record DesktopPageItem(Guid Id, DesktopPageItemKind Kind, string Label, ShellEntityReference Target,
    int Column, int Row, int ColumnSpan = 1, int RowSpan = 1);
public sealed record DesktopPage(Guid Id, string Name, IReadOnlyList<DesktopPageItem> Items);
public sealed record DesktopSurfaceConfiguration(Guid Id, Guid ActivePageId, int Columns, int Rows, IReadOnlyList<DesktopPage> Pages)
{
    [System.Text.Json.Serialization.JsonIgnore] public DesktopPage ActivePage => Pages.Single(p => p.Id == ActivePageId);
    public static DesktopSurfaceConfiguration Default() => Create(Guid.NewGuid(), Guid.NewGuid());
    internal static DesktopSurfaceConfiguration FromLegacy(Guid spaceId) => Create(StableId(spaceId, "surface"), StableId(spaceId, "page"));
    private static DesktopSurfaceConfiguration Create(Guid surfaceId, Guid pageId) => new(surfaceId, pageId, 8, 6, [new(pageId, "Desktop", [])]);
    private static Guid StableId(Guid identity, string kind) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"9to1.shell.pages.v2:{identity:D}:{kind}")).AsSpan(0, 16));
}

/// <summary>Pages are independent of Desktop Spaces and Virtual Desktops; items are references, never cloned app objects.</summary>
public static class DesktopPageEdits
{
    public static DesktopSurfaceConfiguration Effective(ShellConfiguration configuration) =>
        configuration.ActiveSpace.DesktopSurface ?? configuration.GlobalDesktopSurface ?? throw new InvalidDataException("Desktop surface requires recovery.");
    public static ShellConfiguration SetSpaceSpecific(ShellConfiguration configuration, bool specific)
    {
        configuration.Validate();
        if (specific == (configuration.ActiveSpace.DesktopSurface is not null)) return configuration;
        return configuration with { Spaces = configuration.Spaces.Select(s => s.Id == configuration.ActiveSpaceId
            ? s with { DesktopSurface = specific ? Duplicate(Effective(configuration)) : null } : s).ToArray() };
    }
    public static ShellConfiguration AddPage(ShellConfiguration configuration, string name) => Change(configuration, surface =>
    {
        var page = new DesktopPage(Guid.NewGuid(), name, []);
        return surface with { ActivePageId = page.Id, Pages = [.. surface.Pages, page] };
    });
    public static ShellConfiguration RenamePage(ShellConfiguration configuration, string name) => Change(configuration, surface => surface with
    { Pages = surface.Pages.Select(p => p.Id == surface.ActivePageId ? p with { Name = name } : p).ToArray() });
    public static ShellConfiguration RemovePage(ShellConfiguration configuration) => Change(configuration, surface =>
    {
        if (surface.Pages.Count == 1) throw new InvalidOperationException("A desktop surface requires at least one page.");
        var index = surface.Pages.ToList().FindIndex(p => p.Id == surface.ActivePageId);
        var pages = surface.Pages.Where(p => p.Id != surface.ActivePageId).ToArray();
        return surface with { ActivePageId = pages[Math.Min(index, pages.Length - 1)].Id, Pages = pages };
    });
    public static ShellConfiguration StepPage(ShellConfiguration configuration, int direction) => Change(configuration, surface => surface with
    { ActivePageId = surface.Pages[Math.Clamp(surface.Pages.ToList().FindIndex(p => p.Id == surface.ActivePageId) + Math.Sign(direction), 0, surface.Pages.Count - 1)].Id });
    public static ShellConfiguration ReorderPage(ShellConfiguration configuration, int direction) => Change(configuration, surface =>
    {
        var pages = surface.Pages.ToList(); var index = pages.FindIndex(p => p.Id == surface.ActivePageId);
        var next = Math.Clamp(index + Math.Sign(direction), 0, pages.Count - 1);
        (pages[index], pages[next]) = (pages[next], pages[index]); return surface with { Pages = pages.ToArray() };
    });
    public static ShellConfiguration PinApplication(ShellConfiguration configuration, Guid applicationId, string label) => Change(configuration, surface =>
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("A canonical installed application is required.");
        var target = new ShellEntityReference("Home", "os.installed-application", applicationId.ToString("D"));
        if (surface.ActivePage.Items.Any(i => i.Kind == DesktopPageItemKind.Application && i.Target == target)) return surface;
        var occupied = surface.ActivePage.Items.SelectMany(i => Enumerable.Range(i.Row, i.RowSpan)
            .SelectMany(row => Enumerable.Range(i.Column, i.ColumnSpan).Select(column => (column, row)))).ToHashSet();
        var cell = Enumerable.Range(0, surface.Rows).SelectMany(row => Enumerable.Range(0, surface.Columns).Select(column => (column, row)))
            .Where(c => !occupied.Contains(c)).Select(c => ((int Column, int Row)?)c).FirstOrDefault();
        if (cell is null) throw new InvalidOperationException("This desktop page is full. Add a page or remove a shortcut.");
        var item = new DesktopPageItem(Guid.NewGuid(), DesktopPageItemKind.Application, label, target, cell.Value.Column, cell.Value.Row);
        return surface with { Pages = surface.Pages.Select(p => p.Id == surface.ActivePageId ? p with { Items = [.. p.Items, item] } : p).ToArray() };
    });
    public static ShellConfiguration RemoveItem(ShellConfiguration configuration, Guid id) => Change(configuration, surface => surface with
    { Pages = surface.Pages.Select(p => p.Id == surface.ActivePageId ? p with { Items = p.Items.Where(i => i.Id != id).ToArray() } : p).ToArray() });
    public static ShellConfiguration ResetSurface(ShellConfiguration configuration) => Change(configuration, _ => DesktopSurfaceConfiguration.Default());
    private static ShellConfiguration Change(ShellConfiguration configuration, Func<DesktopSurfaceConfiguration, DesktopSurfaceConfiguration> edit)
    {
        configuration.Validate(); var nextSurface = edit(Effective(configuration));
        var next = configuration.ActiveSpace.DesktopSurface is null
            ? configuration with { GlobalDesktopSurface = nextSurface }
            : configuration with { Spaces = configuration.Spaces.Select(s => s.Id == configuration.ActiveSpaceId ? s with { DesktopSurface = nextSurface } : s).ToArray() };
        next.Validate(); return next;
    }
    internal static DesktopSurfaceConfiguration Duplicate(DesktopSurfaceConfiguration source)
    {
        var pages = source.Pages.Select(p => p with { Id = Guid.NewGuid(), Items = p.Items.Select(i => i with { Id = Guid.NewGuid() }).ToArray() }).ToArray();
        return source with { Id = Guid.NewGuid(), ActivePageId = pages[source.Pages.ToList().FindIndex(p => p.Id == source.ActivePageId)].Id, Pages = pages };
    }
    internal static DesktopSurfaceConfiguration Clone(DesktopSurfaceConfiguration source) => source with
    { Pages = source.Pages.Select(p => p with { Items = p.Items.ToArray() }).ToArray() };
}
