using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web;

/// <summary>Responsive state projection into the authored CUI tree. Ordinary chrome,
/// templates and colour/state defaults belong to CUI, never app-level donor Styles.</summary>
internal sealed class BrowserHomePresentation : IDisposable
{
    private readonly Control _root;
    private readonly BrowserHomeContext _context;
    private readonly Dictionary<string, Control> _tiles = new(StringComparer.Ordinal);
    private bool _disposed, _updating;
    private readonly EventHandler<SizeChangedEventArgs> _resized;
    internal BrowserHomePresentation(Control root, BrowserHomeContext context)
    {
        _root = root; _context = context; _resized = (_, _) => Apply();
        root.SizeChanged += _resized; root.KeyDown += KeyDown;
        context.PropertyChanged += Changed; context.DashboardPresentationChanged += Apply;
        Apply();
    }
    private static void Set(Control control, string property, object? value)
    {
        if (CuiLayoutProperties.SetLiteral(control,property,Convert.ToString(value,CultureInfo.InvariantCulture) ?? "",CuiPropertyLevel.Instance,"Home responsive state")) return;
        if (!CuiPropertyPresentation.Set(control, property, value, CuiPropertyLevel.Instance, "Home responsive state"))
            throw new InvalidOperationException($"The canonical CUI property '{property}' is unavailable for Home.");
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => Dispatcher.UIThread.Post(Apply);
    private IEnumerable<Control> AuthoredControls()
    {
        // Active=false removes a canonical cell from the live tree. Retain authored
        // identity so the same control can be activated again without reconstruction.
        var pending = new Stack<Control>();
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        pending.Push(_root);
        while (pending.TryPop(out var control))
        {
            if (!seen.Add(control)) continue;
            yield return control;
            var children = control is CuiContainerControl container
                ? container.AuthoredChildren
                : control.GetLogicalChildren().OfType<Control>();
            foreach (var child in children) pending.Push(child);
        }
    }
    private void KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !_context.DrawerOpen) return;
        _context.DispatchAsync("ToggleDrawer", null).GetAwaiter().GetResult();
        AuthoredControls().OfType<Button>().FirstOrDefault(button => button.Name == "open-navigation")?.Focus(); args.Handled = true;
    }
    private void Apply()
    {
        if (_disposed || _updating) return; _updating = true;
        try
        {
            var controls = AuthoredControls().Where(c => c.Name is not null)
                .GroupBy(c => c.Name!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Control? Find(string id) => controls.GetValueOrDefault(id);
            var width = _root.Bounds.Width > 0 ? _root.Bounds.Width : 1200;
            var compact = width < 760; _context.SetCompact(compact);
            if (Find("home-body") is { } body) { Set(body,"ColumnSizes",compact ? "1fr" : "220 1fr"); Set(body,"RowSizes","1fr"); }
            if (Find("home-content") is { } content) Set(content,"Margin",new Thickness(compact ? 18 : 28));
            if (Find("home-content-frame") is { } frame)
            { Set(frame,"Column",compact ? 0 : 1); Set(frame,"Margin",compact ? new Thickness(0) : new Thickness(0,0,12,12)); Set(frame,"Interactive",!_context.DrawerOpen); }
            if (Find("navigation-rail") is { } rail)
            { Set(rail,"Width",compact ? Math.Min(300,width-48) : 220d); Set(rail,"Layer",20); Set(rail,"Active",!compact || _context.DrawerOpen); }
            if (Find("drawer-dismiss") is { } dismiss)
            { Set(dismiss,"ColumnSpan",compact ? 1 : 2); Set(dismiss,"Layer",10); Set(dismiss,"Active",_context.DrawerOpen); }
            if (Find("open-navigation") is { } menu)
            { Set(menu,"Active",compact); Set(menu,"Content",_context.DrawerOpen ? "Close" : "Menu"); AutomationProperties.SetName(menu,_context.DrawerOpen ? "Close navigation menu" : "Open navigation"); }
            foreach (var button in controls.Values.OfType<Button>().Where(b => b.Name!.StartsWith("nav-",StringComparison.Ordinal)))
            {
                Set(button,"Width",compact ? Math.Min(300,width-48)-36 : 184d);
                var route = button.Name == "nav-home" ? "Dashboard" : button.Name![4..];
                var selected = route.Equals(_context.CurrentRoute,StringComparison.OrdinalIgnoreCase);
                CuiPropertyPresentation.SetSelected(button,selected,"Home navigation");
                if (Find("nav-marker-"+button.Name![4..]) is { } marker) Set(marker,"Active",selected);
                AutomationProperties.SetHelpText(button,selected ? "Current destination" : button.IsEnabled ? "Open "+route : "This destination is not connected in this browser.");
            }
            if (Find("top-bar") is { } header)
            { Set(header,"ColumnSizes","Auto Auto 1fr Auto"); Set(header,"RowSizes",compact ? "Auto Auto" : "Auto"); }
            if (Find("header-search") is { } search)
            { Set(search,"Column",compact ? 0 : 2); Set(search,"Row",compact ? 1 : 0); Set(search,"ColumnSpan",compact ? 4 : 1); Set(search,"Margin",new Thickness(0,compact ? 12 : 0,compact ? 0 : 20,0)); Set(search,"HorizontalAlignment",HorizontalAlignment.Stretch); Set(search,"Interactive",!_context.DrawerOpen); }
            if (Find("header-utilities") is { } utilities) Set(utilities,"Interactive",!_context.DrawerOpen);
            if (Find("home-hero") is { } hero)
            {
                var stacked = width < 1080;
                Set(hero,"ColumnSizes",stacked ? "1fr" : "1fr 240"); Set(hero,"RowSizes",stacked ? "Auto Auto" : "Auto");
                if (Find("hero-copy") is { } copy) Set(copy,"Margin",stacked ? new Thickness(0,0,0,18) : new Thickness(0,0,28,0));
                if (Find("hero-context") is { } context) { Set(context,"Column",stacked ? 0 : 1); Set(context,"Row",stacked ? 1 : 0); }
            }
            foreach (var card in controls.Values.OfType<CuiContainerControl>().Where(c => c.Name!.StartsWith("app-card-",StringComparison.Ordinal)))
            {
                var available = compact ? width-36 : width-220-12-56; var columns = Math.Max(1,(int)Math.Floor(available/220));
                Set(card,"Margin",new Thickness(0,0,12,12)); Set(card,"Width",Math.Max(180,(available-columns*12)/columns));
            }
            if (Find("dashboard-tile-host") is CuiContainerControl host)
            {
                foreach (var id in HomeLocalDashboard.TileIds) if (Find("tile-"+id) is { } tile) _tiles[id]=tile;
                var desired = _context.LocalDashboard.Tiles.OrderByDescending(t=>t.Pinned).Select(t=>t.Id).ToArray();
                var existing = host.AuthoredChildren.Select(child=>child.Name?[5..]).ToArray();
                if (!desired.SequenceEqual(existing))
                    host.ReorderChildren(desired.Where(_tiles.ContainsKey).Select(id=>_tiles[id]).ToArray());
                foreach (var row in _context.LocalDashboard.Tiles) if (_tiles.TryGetValue(row.Id,out var tile)) Set(tile,"Active",!row.Hidden || _context.IsCustomising);
            }
        }
        finally { _updating=false; }
    }
    public void Dispose()
    {
        if(_disposed)return; _disposed=true;
        _root.SizeChanged-=_resized; _root.KeyDown-=KeyDown; _context.PropertyChanged-=Changed; _context.DashboardPresentationChanged-=Apply; _tiles.Clear();
    }
}
