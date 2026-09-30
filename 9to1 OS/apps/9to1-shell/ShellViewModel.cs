using Haven.Application.Go;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using System.ComponentModel;

namespace NineToOne.Os.Shell;

public sealed class ShellViewModel : ICuiWritableBindingContext, ICuiRepeatItemBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged
    { add => _bindings.PropertyChanged += value; remove => _bindings.PropertyChanged -= value; }
    private readonly CuiViewModel _bindings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly SemaphoreSlim _actions = new(1, 1);
    private readonly DispatcherTimer _timer;
    public Func<CancellationToken, Task>? OpenModels { get; set; }
    private ShellConfigurationService? _configuration;
    private GoService? _go;
    private LinuxApplicationLauncher? _launcher;
    private ShellConfigurationSnapshot? _snapshot;
    private CancellationTokenSource? _query;
    private long _queryGeneration;
    private string? _goCategory;
    private Guid? _selectedPageItem;
    public ShellViewModel()
    {
        _lifetimeToken = _lifetime.Token;
        _bindings.Set("PageColumns", "*,*,*,*,*,*,*,*"); _bindings.Set("PageRows", "72,72,72,72,72,72");
        _bindings.Set("PageTitle", "Desktop Page"); _bindings.Set("PagePosition", "Page 1 of 1"); _bindings.Set("PageScope", "Global desktop pages");
        _bindings.GetOrCreateList<DesktopPageItem>("PageItems");
        _bindings.Set("SelectedPageItemLabel", "Select Arrange on a desktop shortcut.");
        _bindings.Set("HasSelectedPageItem", false); _bindings.Set("HasPreview", false);
        _bindings.Set("PageGridColumns", "8"); _bindings.Set("PageGridRows", "6");
        _bindings.Set("SelectedColumn", "1"); _bindings.Set("SelectedRow", "1");
        _bindings.Set("SelectedWidth", "1"); _bindings.Set("SelectedHeight", "1");
        _bindings.Set("GoCategory", "All");
        _bindings.Set("Status", "Checking Home…"); _bindings.Set("Name", ""); _bindings.Set("Query", "");
        _bindings.Set("Thickness", "56"); _bindings.Set("Spacing", "8"); _bindings.Set("Padding", "8"); _bindings.Set("Radius", "12"); _bindings.Set("Opacity", "1");
        _bindings.Set("SpaceTitle", "Desktop Space"); _bindings.Set("LayerTitle", "Main"); _bindings.Set("LayerPosition", "Layer 1 of 1");
        _bindings.Set("LayerHeight", 56); _bindings.Set("LayerPadding", 8); _bindings.Set("LayerRadius", 12); _bindings.Set("LayerOpacity", 1d); _bindings.Set("ItemSpacing", 8);
        _bindings.GetOrCreateList<GoResult>("Results"); _bindings.GetOrCreateList<TaskbarItem>("Items");
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _ = RefreshExpiryAsync());
        _timer.Stop();
    }
    public async Task StartAsync(ShellConfigurationService configuration, GoService go, LinuxApplicationLauncher launcher, CancellationToken ct)
    {
        var snapshot = await configuration.GetAsync(ct);
        await Dispatcher.UIThread.InvokeAsync(() => { _configuration = configuration; _go = go; _launcher = launcher; Populate(snapshot); _timer.Start(); });
        _ = SearchSafelyAsync(_lifetimeToken);
    }
    private async Task RefreshExpiryAsync()
    {
        if (_configuration is null || _snapshot?.Preview is null || _actions.CurrentCount == 0) return;
        try
        {
            var snapshot = await _configuration.GetAsync(_lifetimeToken);
            if (snapshot.Preview is null) Populate(snapshot);
            else _bindings.Set("Status", $"Preview — keep or revert within {Math.Max(0, (int)(snapshot.Preview.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)} seconds.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _bindings.Set("Status", ex.Message); }
    }
    private void Populate(ShellConfigurationSnapshot snapshot)
    {
        _snapshot = snapshot; var config = snapshot.Effective; var space = config.ActiveSpace; var bar = space.Taskbar;
        _bindings.Set("HasPreview", snapshot.Preview is not null);
        var surface = DesktopPageEdits.Effective(config);
        _bindings.Set("PageTitle", "Desktop Page: " + surface.ActivePage.Name);
        _bindings.Set("PagePosition", $"Page {surface.Pages.ToList().FindIndex(p => p.Id == surface.ActivePageId) + 1} of {surface.Pages.Count}");
        _bindings.Set("PageColumns", string.Join(",", Enumerable.Repeat("*", surface.Columns)));
        _bindings.Set("PageRows", string.Join(",", Enumerable.Repeat("72", surface.Rows)));
        _bindings.Set("PageScope", space.DesktopSurface is null ? "Global desktop pages" : "Pages for this Desktop Space");
        _bindings.Set("PageGridColumns", surface.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _bindings.Set("PageGridRows", surface.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
        PopulateSelection(surface);
        var pageItems = _bindings.GetOrCreateList<DesktopPageItem>("PageItems"); pageItems.Clear(); foreach (var pageItem in surface.ActivePage.Items) pageItems.Add(pageItem);
        var layer = bar.Layers.Single(l => l.Id == bar.ActiveLayerId); var p = layer.Presentation;
        _bindings.Set("SpaceTitle", "Desktop Space: " + space.Name); _bindings.Set("LayerTitle", layer.Name);
        _bindings.Set("LayerPosition", $"Layer {bar.Layers.ToList().FindIndex(l => l.Id == layer.Id) + 1} of {bar.Layers.Count}");
        _bindings.Set("LayerHeight", p.Thickness); _bindings.Set("LayerPadding", p.Padding); _bindings.Set("LayerRadius", p.CornerRadius); _bindings.Set("LayerOpacity", p.Opacity); _bindings.Set("ItemSpacing", p.Spacing);
        _bindings.Set("Thickness", p.Thickness.ToString()); _bindings.Set("Spacing", p.Spacing.ToString()); _bindings.Set("Padding", p.Padding.ToString()); _bindings.Set("Radius", p.CornerRadius.ToString()); _bindings.Set("Opacity", p.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var items = _bindings.GetOrCreateList<TaskbarItem>("Items"); items.Clear(); foreach (var item in layer.Items) items.Add(item);
        _bindings.Set("Status", snapshot.Preview is { } preview ? $"Preview — keep or revert within {Math.Max(0, (int)(preview.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)} seconds." : $"Saved Home configuration · revision {snapshot.Stored.Revision}");
    }
    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) => _bindings.TrySetValue(path, value);
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = (item, path) switch
        {
            (GoResult result, "Reference.Id") => result.Reference.Id,
            (GoResult result, "Label") => result.Label,
            (GoResult result, "Category") => result.Category,
            (GoResult result, "OtherActions") => result.Actions.Where(a => a.Id != "Open").Select(a => new ShellGoAction(result, a)).ToArray(),
            (ShellGoAction action, "Id") => action.Action.Id,
            (ShellGoAction action, "Label") => action.Action.Label,
            (GoResult result, "CanOpen") => result.Actions.Any(a => a.Id == "Open"),
            (GoResult result, "CanPinApplication") => result.Reference is { Owner: "Home", Kind: "os.installed-application" } && Guid.TryParse(result.Reference.Id, out var applicationId) && applicationId != Guid.Empty,
            (DesktopPageItem pageItem, "Column") => pageItem.Column,
            (DesktopPageItem pageItem, "Row") => pageItem.Row,
            (DesktopPageItem pageItem, "ColumnSpan") => pageItem.ColumnSpan,
            (DesktopPageItem pageItem, "RowSpan") => pageItem.RowSpan,
            (DesktopPageItem pageItem, "Id") => pageItem.Id,
            (DesktopPageItem pageItem, "Label") => pageItem.Label,
            (DesktopPageItem pageItem, "CanOpen") => pageItem.Kind == DesktopPageItemKind.Application && pageItem.Target is { Owner: "Home", Kind: "os.installed-application" },
            (TaskbarItem entry, "Id") => entry.Id,
            (TaskbarItem entry, "Label") => entry.Label,
            (TaskbarItem entry, "CanOpen") => entry.Kind == TaskbarItemKind.Go || (entry.Kind == TaskbarItemKind.Application && entry.Target is { Owner: "Home", Kind: "os.installed-application" }),
            _ => null
        };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public bool? IsActionAvailable(string command) => _configuration is not null &&
        (command is not ("Keep" or "Revert") || _snapshot?.Preview is not null) &&
        (command is not ("PlaceDesktopItem" or "RemoveSelectedDesktopItem") || SelectedItem() is not null);
    private DesktopPageItem? SelectedItem() => _snapshot is null || _selectedPageItem is null ? null :
        DesktopPageEdits.Effective(_snapshot.Effective).ActivePage.Items.SingleOrDefault(i => i.Id == _selectedPageItem);
    private void PopulateSelection(DesktopSurfaceConfiguration surface)
    {
        var selected = surface.ActivePage.Items.SingleOrDefault(i => i.Id == _selectedPageItem);
        _bindings.Set("HasSelectedPageItem", selected is not null);
        if (selected is null) { _selectedPageItem = null; _bindings.Set("SelectedPageItemLabel", "Select Arrange on a desktop shortcut."); return; }
        _bindings.Set("SelectedPageItemLabel", "Arrange: " + selected.Label);
        _bindings.Set("SelectedColumn", (selected.Column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        _bindings.Set("SelectedRow", (selected.Row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        _bindings.Set("SelectedWidth", selected.ColumnSpan.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _bindings.Set("SelectedHeight", selected.RowSpan.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (_configuration is null || _snapshot is null || _go is null) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        if (command.StartsWith("GoFilter.", StringComparison.Ordinal))
        {
            var category = command[9..];
            if (category is not ("All" or "Apps" or "Desktop Spaces" or "Desktop Pages" or "Taskbar Layers")) return;
            _goCategory = category == "All" ? null : category;
            _bindings.Set("GoCategory", category); await SearchSafelyAsync(request.Token); return;
        }
        if (command is "Search" or "Go") { await SearchSafelyAsync(request.Token); return; }
        await _actions.WaitAsync(request.Token);
        try
        {
            if (command == "InvokeGoAction" && parameter is ShellGoAction ownerAction)
            {
                await _go.InvokeAsync(ownerAction.Result, ownerAction.Action.Id, GoScope(), request.Token);
                Populate(await _configuration.GetAsync(request.Token)); return;
            }
            if (command == "Models" && OpenModels is { } openModels) { await openModels(request.Token); return; }
            if (command == "Open" && parameter is GoResult result) { await _go.InvokeAsync(result, "Open", GoScope(), request.Token); return; }
            if (command == "SelectPageItem" && parameter is DesktopPageItem selected)
            {
                if (!DesktopPageEdits.Effective(_snapshot.Effective).ActivePage.Items.Any(i => i.Id == selected.Id))
                    throw new InvalidOperationException("This shortcut is no longer on the current desktop page.");
                _selectedPageItem = selected.Id; PopulateSelection(DesktopPageEdits.Effective(_snapshot.Effective)); return;
            }
            if (command == "OpenPageItem" && parameter is DesktopPageItem pageItem)
            {
                if (pageItem.Kind != DesktopPageItemKind.Application || pageItem.Target is not { Owner: "Home", Kind: "os.installed-application" } target || !Guid.TryParse(target.Id, out var id) || _launcher is null)
                    throw new InvalidOperationException("This desktop item canonical owner action is unavailable.");
                await _launcher.LaunchCurrentAsync(id, request.Token); return;
            }
            if (command == "OpenItem" && parameter is TaskbarItem item)
            {
                if (item.Kind == TaskbarItemKind.Go) { await SearchSafelyAsync(request.Token); return; }
                if (item.Kind != TaskbarItemKind.Application || item.Target is not { Owner: "Home", Kind: "os.installed-application" } target || !Guid.TryParse(target.Id, out var id) || _launcher is null)
                    throw new InvalidOperationException("This taskbar item's canonical owner action is unavailable.");
                await _launcher.LaunchCurrentAsync(id, request.Token); return;
            }
            if (command == "Keep") { Populate(await _configuration.KeepAsync(_snapshot.Preview?.Id ?? Guid.Empty, request.Token)); return; }
            if (command == "Revert") { Populate(await _configuration.RevertAsync(_snapshot.Preview?.Id ?? Guid.Empty, request.Token)); return; }
            var config = _snapshot.Effective; var name = _bindings.Get("Name")?.ToString() ?? "";
            if (command == "CopySpace")
            {
                var clipboard = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard
                    ?? throw new InvalidOperationException("The platform clipboard is unavailable.");
                await clipboard.SetTextAsync(DesktopSpaceExchange.Export(config.ActiveSpace)).WaitAsync(request.Token);
                _bindings.Set("Status", "Desktop Space copied. Paste it into another 9to1 OS shell to import its configuration."); return;
            }
            if (command == "PasteSpace")
            {
                var clipboard = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard
                    ?? throw new InvalidOperationException("The platform clipboard is unavailable.");
                var text = await clipboard.TryGetTextAsync().WaitAsync(request.Token);
                Populate(await _configuration.PreviewAsync(_snapshot.Stored.Revision, DesktopSpaceExchange.Import(config, text ?? ""), TimeSpan.FromSeconds(30), request.Token)); return;
            }
            if (command is "Pin" or "PinPage" && parameter is GoResult pinSource)
            {
                if (_launcher is null || _goCategory is not (null or "Apps") || pinSource.ProviderId != "os.installed-applications" ||
                    pinSource.Reference is not { Owner: "Home", Kind: "os.installed-application" } ||
                    !Guid.TryParse(pinSource.Reference.Id, out var currentAppId) || !long.TryParse(pinSource.Reference.Revision, out var currentRevision))
                    throw new UnauthorizedAccessException("This application is outside the current Go discovery scope.");
                var canonical = await _launcher.ResolveForReadAsync(currentAppId, currentRevision, request.Token);
                parameter = pinSource with { Label = canonical.Label };
            }
            var candidate = command switch
            {
                "PreviousPage" => DesktopPageEdits.StepPage(config, -1), "NextPage" => DesktopPageEdits.StepPage(config, 1),
                "AddPage" => DesktopPageEdits.AddPage(config, name), "RenamePage" => DesktopPageEdits.RenamePage(config, name), "RemovePage" => DesktopPageEdits.RemovePage(config),
                "MovePageEarlier" => DesktopPageEdits.ReorderPage(config, -1), "MovePageLater" => DesktopPageEdits.ReorderPage(config, 1),
                "SpaceSpecificPages" => DesktopPageEdits.SetSpaceSpecific(config, true), "GlobalPages" => DesktopPageEdits.SetSpaceSpecific(config, false),
                "ResetDesktop" => DesktopPageEdits.ResetSurface(config),
                "PageGridSize" => DesktopPageEdits.GridSize(config, Integer("PageGridColumns"), Integer("PageGridRows")),
                "PlaceDesktopItem" => DesktopPageEdits.ArrangeItem(config, SelectedItem()?.Id ?? throw new InvalidOperationException("Select a desktop shortcut first."),
                    checked(Integer("SelectedColumn") - 1), checked(Integer("SelectedRow") - 1), Integer("SelectedWidth"), Integer("SelectedHeight")),
                "RemoveSelectedDesktopItem" => DesktopPageEdits.RemoveItem(config, SelectedItem()?.Id ?? throw new InvalidOperationException("Select a desktop shortcut first.")),
                "PinPage" when parameter is GoResult pagePin && pagePin.Reference is { Owner: "Home", Kind: "os.installed-application" } && Guid.TryParse(pagePin.Reference.Id, out var pageAppId) => DesktopPageEdits.PinApplication(config, pageAppId, pagePin.Label),
                "RemovePageItem" when parameter is DesktopPageItem removePageItem => DesktopPageEdits.RemoveItem(config, removePageItem.Id),
                "PreviousLayer" => ShellEdits.StepLayer(config, -1), "NextLayer" => ShellEdits.StepLayer(config, 1),
                "AddLayer" => ShellEdits.AddLayer(config, name), "RenameLayer" => ShellEdits.RenameLayer(config, name),
                "RemoveLayer" => ShellEdits.RemoveLayer(config), "MoveLayerEarlier" => ShellEdits.ReorderLayer(config, -1), "MoveLayerLater" => ShellEdits.ReorderLayer(config, 1),
                "PreviousSpace" => ShellEdits.StepSpace(config, -1), "NextSpace" => ShellEdits.StepSpace(config, 1),
                "DuplicateSpace" => ShellEdits.DuplicateSpace(config, name), "RenameSpace" => ShellEdits.RenameSpace(config, name), "RemoveSpace" => ShellEdits.RemoveSpace(config),
                "MoveSpaceEarlier" => ShellEdits.ReorderSpace(config, -1), "MoveSpaceLater" => ShellEdits.ReorderSpace(config, 1),
                "Pin" when parameter is GoResult pin && pin.Reference is { Owner: "Home", Kind: "os.installed-application" } && Guid.TryParse(pin.Reference.Id, out var appId) => ShellEdits.PinApplication(config, appId, pin.Label),
                "Unpin" when parameter is TaskbarItem unpin => ShellEdits.UnpinItem(config, unpin.Id),
                "ResetTaskbar" => ShellEdits.ResetTaskbar(config), "RestorePrevious" => _snapshot.Stored.Previous ?? throw new InvalidOperationException("No previous configuration is available."),
                "SafeDefaults" => ShellConfiguration.Default(), "Presentation" => ShellEdits.Presentation(config, Integer("Thickness"), Integer("Spacing"), Integer("Padding"), Integer("Radius"), Number("Opacity")),
                _ => throw new InvalidOperationException("This shell action is unavailable.")
            };
            Populate(await _configuration.PreviewAsync(_snapshot.Stored.Revision, candidate, TimeSpan.FromSeconds(30), request.Token));
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException or OverflowException or Win32Exception)
        { _bindings.Set("Status", ex.Message); }
        finally { _actions.Release(); }
    }
    private int Integer(string name) => int.Parse(_bindings.Get(name)?.ToString() ?? "", System.Globalization.CultureInfo.InvariantCulture);
    private double Number(string name) => double.Parse(_bindings.Get(name)?.ToString() ?? "", System.Globalization.CultureInfo.InvariantCulture);
    private async Task SearchAsync(CancellationToken ct)
    {
        if (_go is null) return;
        _query?.Cancel();
        using var queryLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);
        _query = queryLifetime; var generation = Interlocked.Increment(ref _queryGeneration);
        var results = _bindings.GetOrCreateList<GoResult>("Results");
        await Dispatcher.UIThread.InvokeAsync(results.Clear);
        var query = new GoQuery(_bindings.Get("Query")?.ToString() ?? "", _goCategory, Scope: GoScope());
        try
        {
            await foreach (var update in _go.QueryAsync(query, queryLifetime.Token))
                await Dispatcher.UIThread.InvokeAsync(() => { if (generation != Interlocked.Read(ref _queryGeneration)) return; if (update.Result is { } result) results.Add(result); if (update.Failure is { } failure) _bindings.Set("Status", update.ProviderId + ": " + failure); });
        }
        finally { if (ReferenceEquals(_query, queryLifetime)) _query = null; }
    }
    private GoScope? GoScope() => _goCategory switch
    {
        "Apps" => new(new HashSet<string>(StringComparer.Ordinal) { "os.installed-applications" }),
        "Desktop Spaces" or "Desktop Pages" or "Taskbar Layers" => new(new HashSet<string>(StringComparer.Ordinal) { ShellNavigationGoProvider.Id }),
        _ => null
    };
    private async Task SearchSafelyAsync(CancellationToken ct)
    {
        try { await SearchAsync(ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { await Dispatcher.UIThread.InvokeAsync(() => _bindings.Set("Status", ex.Message)); }
    }
    public void Dispose() { _timer.Stop(); _lifetime.Cancel(); _query?.Cancel(); _lifetime.Dispose(); }
}
