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
    public Func<Haven.Application.AuthenticatedResourceActor, CancellationToken, CancellationToken, Task>? OpenFiles { get; set; }
    public Func<CancellationToken, Task>? OpenModels { get; set; }
    public Func<CancellationToken, CancellationToken, Task>? OpenDulche { get; set; }
    private ShellConfigurationService? _configuration;
    private GoService? _go;
    private ShellGoSearchOwner? _goSearchOwner;
    private readonly Dictionary<GoResult, ShellGoSearchRequest> _displayedGo = new(ReferenceEqualityComparer.Instance);
    private LinuxApplicationLauncher? _launcher;
    private ShellConfigurationSnapshot? _snapshot;
    private CancellationTokenSource? _query;
    private string? _goCategory;
    private string _goHomeView = "Pinned";
    private GoHomeConfiguration? _displayedGoHome;
    private readonly List<(GoHomeSectionKind Section, GoResult Result)> _goSectionResults = [];
    private Guid? _selectedPageItem;
    public ShellViewModel()
    {
        _lifetimeToken = _lifetime.Token;
        _bindings.Set("GoQueryState", "Idle"); _bindings.Set("GoQuerySummary", "Enter a query or open a Go section.");
        _bindings.Set("GoGroup", ""); _bindings.Set("ShowGroupedGoResults", false); _bindings.Set("ShowLinearGoResults", true);
        _bindings.GetOrCreateList<GoHomeResultGroup>("GoResultGroups");
        _bindings.GetOrCreateList<GoResult>("LinearGoResults");
        _bindings.Set("PageColumns", "*,*,*,*,*,*,*,*"); _bindings.Set("PageRows", "72,72,72,72,72,72");
        _bindings.Set("PageTitle", "Desktop Page"); _bindings.Set("PagePosition", "Page 1 of 1"); _bindings.Set("PageScope", "Global desktop pages");
        _bindings.GetOrCreateList<DesktopPageItem>("PageItems");
        _bindings.Set("SelectedPageItemLabel", "Select Arrange on a desktop shortcut.");
        _bindings.Set("HasSelectedPageItem", false); _bindings.Set("HasPreview", false);
        _bindings.Set("PageGridColumns", "8"); _bindings.Set("PageGridRows", "6");
        _bindings.Set("SelectedColumn", "1"); _bindings.Set("SelectedRow", "1");
        _bindings.Set("SelectedWidth", "1"); _bindings.Set("SelectedHeight", "1");
        _bindings.Set("GoHomeView", "Pinned");
        _bindings.Set("GoHomeAvailability", "Pins from your current taskbar and desktop page.");
        _bindings.Set("HasRecentOwner", false); _bindings.Set("HasSuggestedOwner", false);
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
        await Dispatcher.UIThread.InvokeAsync(() => { if (!ReferenceEquals(_configuration, configuration)) { _goSearchOwner?.Invalidate(); _goSearchOwner = new(configuration); } _configuration = configuration; _go = go; _launcher = launcher; Populate(snapshot); _timer.Start(); });
        _ = SearchSafelyAsync(_lifetimeToken);
    }
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (_configuration is null) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);
        var snapshot = await _configuration.GetAsync(request.Token);
        await Dispatcher.UIThread.InvokeAsync(() => { if (!request.IsCancellationRequested) Populate(snapshot); });
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
        if (_snapshot is { } shown && shown.Stored.AuthorityId == snapshot.Stored.AuthorityId && shown.Stored.SessionActor == snapshot.Stored.SessionActor &&
            (shown.Stored.Revision > snapshot.Stored.Revision || shown.Stored.Revision == snapshot.Stored.Revision && shown.IntentGeneration > snapshot.IntentGeneration)) return;
        if (_snapshot is { } original && (original.Stored.AuthorityId != snapshot.Stored.AuthorityId || original.Stored.SessionActor != snapshot.Stored.SessionActor))
        {
            _goSearchOwner?.Invalidate(); _query?.Cancel();
            _bindings.GetOrCreateList<GoResult>("Results").Clear(); _displayedGo.Clear();
        }
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
        PopulateGoPresentation(snapshot.Effective.EffectiveGoHome);
        var items = _bindings.GetOrCreateList<TaskbarItem>("Items"); items.Clear(); foreach (var item in layer.Items) items.Add(item);
        _bindings.Set("Status", snapshot.Preview is { } preview ? $"Preview — keep or revert within {Math.Max(0, (int)(preview.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)} seconds." : $"Saved Home configuration · revision {snapshot.Stored.Revision}");
    }
    private void PopulateGoPresentation(GoHomeConfiguration home)
    {
        _displayedGoHome = home;
        _bindings.Set("GoLayout", home.Layout.ToString());
        _bindings.Set("GoHomeWidth", home.Layout switch { GoHomeLayout.CompactSearch => 160, GoHomeLayout.StartMenu => 210, _ => 280 });
        var all = _bindings.GetOrCreateList<GoHomeSection>("GoHomeSettings"); all.Clear();
        var visible = _bindings.GetOrCreateList<GoHomeSection>("GoVisibleSections"); visible.Clear();
        foreach (var section in home.Sections) { all.Add(section); if (section.Visible) visible.Add(section); }
    }
    private static string GoSectionLabel(GoHomeSectionKind kind) => kind == GoHomeSectionKind.AllApps ? "All Apps" : kind.ToString();
    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) => _bindings.TrySetValue(path, value);
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = (item, path) switch
        {
            (GoHomeResultGroup group, "Id") => group.Id,
            (GoHomeResultGroup group, "Title") => group.Title,
            (GoHomeResultGroup group, "Availability") => group.Availability,
            (GoHomeResultGroup group, "FontSize") => group.FontSize,
            (GoHomeResultGroup group, "Results") => group.Results,
            (GoHomeResultGroup group, "Column") => group.Column,
            (GoHomeResultGroup group, "Row") => group.Row,
            (GoHomeSection section, "Id") => section.Kind.ToString(),
            (GoHomeSection section, "Label") => GoSectionLabel(section.Kind),
            (GoHomeSection section, "Group") => section.Group ?? "",
            (GoHomeSection section, "CanOpen") => section.Kind is GoHomeSectionKind.Pinned or GoHomeSectionKind.AllApps,
            (GoHomeSection section, "FontSize") => section.Size switch { GoHomeSectionSize.Compact => 12, GoHomeSectionSize.Standard => 16, _ => 22 },
            (GoHomeSection section, "EarlierLabel") => "Move " + GoSectionLabel(section.Kind) + " earlier",
            (GoHomeSection section, "LaterLabel") => "Move " + GoSectionLabel(section.Kind) + " later",
            (GoHomeSection section, "VisibilityLabel") => (section.Visible ? "Hide " : "Show ") + GoSectionLabel(section.Kind),
            (GoHomeSection section, "SizeLabel") => "Resize " + GoSectionLabel(section.Kind) + " (" + section.Size + ")",
            (GoHomeSection section, "GroupLabel") => "Group " + GoSectionLabel(section.Kind),
            (GoResult result, "Reference.Id") => result.Reference.Id,
            (GoResult result, "CanonicalDisplayKey") => ShellGoDisplayKey.Create(result),
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
        var expected = _snapshot;
        var originalConfiguration = _configuration;
        var selectedItemId = SelectedItem()?.Id;
        var requestedGoGroup = _bindings.Get("GoGroup")?.ToString() ?? "";
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        if (command.StartsWith("GoHome.", StringComparison.Ordinal))
        {
            var view = command[7..];
            if (view is not ("Pinned" or "All Apps" or "Recent" or "Suggested")) return;
            _goHomeView = view; _bindings.Set("Query", "");
            await SearchSafelyAsync(request.Token); return;
        }
        if (command.StartsWith("GoFilter.", StringComparison.Ordinal))
        {
            var category = command[9..];
            if (category is not ("All" or "Apps" or "Desktop Spaces" or "Desktop Pages" or "Taskbar Layers")) return;
            _goHomeView = "Search";
            _goCategory = category == "All" ? null : category;
            _bindings.Set("GoCategory", category); await SearchSafelyAsync(request.Token); return;
        }
        if (command is "Search" or "Go") { await SearchSafelyAsync(request.Token); return; }
        await _actions.WaitAsync(request.Token);
        try
        {
            if (command == "OpenGoHomeSection" && parameter is GoHomeSection opening)
            {
                if (!ReferenceEquals(expected, _snapshot) || _displayedGoHome is null || !_displayedGoHome.Sections.Any(s => ReferenceEquals(s, opening)) || !opening.Visible)
                    throw new UnauthorizedAccessException("Go home changed; choose the current section.");
                if (opening.Kind is not (GoHomeSectionKind.Pinned or GoHomeSectionKind.AllApps))
                    throw new InvalidOperationException("This Go section owner is unavailable.");
                if (!await _configuration.IsCurrentSessionAsync(expected.Stored, request.Token) || !ReferenceEquals(expected, _snapshot))
                    throw new UnauthorizedAccessException("The original Go home session or displayed surface changed.");
                _goHomeView = GoSectionLabel(opening.Kind); _bindings.Set("Query", "");
                await SearchSafelyAsync(request.Token); return;
            }
            if (command is "GoSectionEarlier" or "GoSectionLater" or "GoSectionVisibility" or "GoSectionSize" or "GoSectionGroup")
            {
                if (parameter is not GoHomeSection section || !ReferenceEquals(expected, _snapshot) ||
                    _displayedGoHome is null || !_displayedGoHome.Sections.Any(s => ReferenceEquals(s, section)))
                    throw new UnauthorizedAccessException("Choose an original displayed Go section.");
                var home = _displayedGoHome!;
                var position = home.Sections.ToList().FindIndex(s => ReferenceEquals(s, section));
                var sectionCandidate = command switch
                {
                    "GoSectionEarlier" => GoHomeEdits.Move(expected.Effective, section.Kind, Math.Max(0, position - 1)),
                    "GoSectionLater" => GoHomeEdits.Move(expected.Effective, section.Kind, Math.Min(3, position + 1)),
                    "GoSectionVisibility" => GoHomeEdits.Present(expected.Effective, section.Kind, !section.Visible, section.Size, section.Group),
                    "GoSectionSize" => GoHomeEdits.Present(expected.Effective, section.Kind, section.Visible,
                        section.Size switch { GoHomeSectionSize.Compact => GoHomeSectionSize.Standard, GoHomeSectionSize.Standard => GoHomeSectionSize.Large, _ => GoHomeSectionSize.Compact }, section.Group),
                    _ => GoHomeEdits.Present(expected.Effective, section.Kind, section.Visible, section.Size,
                        string.IsNullOrWhiteSpace(requestedGoGroup) ? null : requestedGoGroup)
                };
                Populate(await _configuration.PreviewAsync(expected.Stored, sectionCandidate, TimeSpan.FromSeconds(30), request.Token));
                await SearchSafelyAsync(request.Token); return;
            }
            if (command.StartsWith("GoLayout.", StringComparison.Ordinal))
            {
                if (!ReferenceEquals(expected, _snapshot)) throw new UnauthorizedAccessException("The displayed Go home changed; choose the current layout.");
                var layout = command[9..] switch { "CompactSearch" => GoHomeLayout.CompactSearch, "StartMenu" => GoHomeLayout.StartMenu, "Dashboard" => GoHomeLayout.Dashboard,
                    _ => throw new InvalidOperationException("This Go layout is unavailable.") };
                var layoutCandidate = GoHomeEdits.Configure(expected.Effective, expected.Effective.EffectiveGoHome with { Layout = layout });
                Populate(await _configuration.PreviewAsync(expected.Stored, layoutCandidate, TimeSpan.FromSeconds(30), request.Token));
                await SearchSafelyAsync(request.Token); return;
            }
            if (command == "InvokeGoAction" && parameter is ShellGoAction ownerAction)
            {
                await InvokeDisplayedGoAsync(ownerAction.Result, ownerAction.Action.Id, request.Token);
                Populate(await _configuration.GetAsync(request.Token)); return;
            }
            if (command == "Dulche" && OpenDulche is { } openDulche) { await openDulche(request.Token, _lifetimeToken); return; }
            if (command == "Models" && OpenModels is { } openModels) { await openModels(request.Token); return; }
            if (command == "Files" && OpenFiles is { } openFiles)
            {
                var originalActor = expected.Stored.SessionActor ?? throw new UnauthorizedAccessException("The current Home shell session is unavailable.");
                if (!await _configuration.IsCurrentSessionAsync(expected.Stored, request.Token))
                    throw new UnauthorizedAccessException("Home changed; refresh the shell before opening Files.");
                await openFiles(originalActor, request.Token, _lifetimeToken);
                if (!await _configuration.IsCurrentSessionAsync(expected.Stored, request.Token))
                    throw new UnauthorizedAccessException("The original Home session changed while opening Files.");
                return;
            }
            if (command == "Open" && parameter is GoResult result) { await InvokeDisplayedGoAsync(result, "Open", request.Token); return; }
            if (command == "SelectPageItem" && parameter is DesktopPageItem selected)
            {
                if (!DesktopPageEdits.Effective(expected.Effective).ActivePage.Items.Any(i => i.Id == selected.Id))
                    throw new InvalidOperationException("This shortcut is no longer on the current desktop page.");
                _selectedPageItem = selected.Id; PopulateSelection(DesktopPageEdits.Effective(expected.Effective)); return;
            }
            if (command == "OpenPageItem" && parameter is DesktopPageItem pageItem)
            {
                if (!ReferenceEquals(expected, _snapshot) || !DesktopPageEdits.Effective(expected.Effective).ActivePage.Items.Any(i => ReferenceEquals(i, pageItem)))
                    throw new UnauthorizedAccessException("Choose an original displayed desktop shortcut.");
                if (pageItem.Kind != DesktopPageItemKind.Application || pageItem.Target is not { Owner: "Home", Kind: "os.installed-application" } target ||
                    !Guid.TryParse(target.Id, out var id) || _launcher is null || expected.Stored.SessionActor is not { } originalActor)
                    throw new InvalidOperationException("This desktop item's canonical owner action is unavailable.");
                await _launcher.LaunchCurrentForActorAsync(id, originalActor,
                    token => IsOriginalShortcutCurrentAsync(originalConfiguration, expected, token), request.Token); return;
            }
            if (command == "OpenItem" && parameter is TaskbarItem item)
            {
                var layer = expected.Effective.ActiveSpace.Taskbar.Layers.Single(l => l.Id == expected.Effective.ActiveSpace.Taskbar.ActiveLayerId);
                if (!ReferenceEquals(expected, _snapshot) || !layer.Items.Any(i => ReferenceEquals(i, item)))
                    throw new UnauthorizedAccessException("Choose an original displayed taskbar item.");
                if (item.Kind == TaskbarItemKind.Go)
                {
                    if (!await IsOriginalShortcutCurrentAsync(originalConfiguration, expected, request.Token))
                        throw new UnauthorizedAccessException("The original taskbar session changed.");
                    await SearchSafelyAsync(request.Token); return;
                }
                if (item.Kind != TaskbarItemKind.Application || item.Target is not { Owner: "Home", Kind: "os.installed-application" } target ||
                    !Guid.TryParse(target.Id, out var id) || _launcher is null || expected.Stored.SessionActor is not { } originalActor)
                    throw new InvalidOperationException("This taskbar item's canonical owner action is unavailable.");
                await _launcher.LaunchCurrentForActorAsync(id, originalActor,
                    token => IsOriginalShortcutCurrentAsync(originalConfiguration, expected, token), request.Token); return;
            }
            if (command == "Keep") { Populate(await _configuration.KeepAsync(expected.Preview?.Id ?? Guid.Empty, request.Token)); return; }
            if (command == "Revert") { Populate(await _configuration.RevertAsync(expected.Preview?.Id ?? Guid.Empty, request.Token)); return; }
            var config = expected.Effective; var name = _bindings.Get("Name")?.ToString() ?? "";
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
                Populate(await _configuration.PreviewAsync(expected.Stored, DesktopSpaceExchange.Import(config, text ?? ""), TimeSpan.FromSeconds(30), request.Token)); return;
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
                "PlaceDesktopItem" => DesktopPageEdits.ArrangeItem(config, selectedItemId ?? throw new InvalidOperationException("Select a desktop shortcut first."),
                    checked(Integer("SelectedColumn") - 1), checked(Integer("SelectedRow") - 1), Integer("SelectedWidth"), Integer("SelectedHeight")),
                "RemoveSelectedDesktopItem" => DesktopPageEdits.RemoveItem(config, selectedItemId ?? throw new InvalidOperationException("Select a desktop shortcut first.")),
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
                "ResetTaskbar" => ShellEdits.ResetTaskbar(config), "RestorePrevious" => expected.Stored.Previous ?? throw new InvalidOperationException("No previous configuration is available."),
                "SafeDefaults" => ShellConfiguration.Default(), "Presentation" => ShellEdits.Presentation(config, Integer("Thickness"), Integer("Spacing"), Integer("Padding"), Integer("Radius"), Number("Opacity")),
                _ => throw new InvalidOperationException("This shell action is unavailable.")
            };
            Populate(await _configuration.PreviewAsync(expected.Stored, candidate, TimeSpan.FromSeconds(30), request.Token));
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException or OverflowException or Win32Exception)
        { _bindings.Set("Status", ex.Message); }
        finally { _actions.Release(); }
    }
    private async ValueTask<bool> IsOriginalShortcutCurrentAsync(ShellConfigurationService configuration,
        ShellConfigurationSnapshot expected, CancellationToken ct)
    {
        if (!ReferenceEquals(_configuration, configuration) || !ReferenceEquals(_snapshot, expected)) return false;
        if (!await configuration.IsCurrentSessionAsync(expected.Stored, ct)) return false;
        var current = await configuration.GetAsync(ct);
        if (!ReferenceEquals(_configuration, configuration) || !ReferenceEquals(_snapshot, expected) ||
            current.Stored.SessionActor != expected.Stored.SessionActor || current.Stored.AuthorityId != expected.Stored.AuthorityId ||
            current.Stored.Revision != expected.Stored.Revision || current.IntentGeneration != expected.IntentGeneration ||
            current.Preview?.Id != expected.Preview?.Id) return false;
        return await configuration.IsCurrentSessionAsync(expected.Stored, ct) && ReferenceEquals(_snapshot, expected) && ReferenceEquals(_configuration, configuration);
    }
    private int Integer(string name) => int.Parse(_bindings.Get(name)?.ToString() ?? "", System.Globalization.CultureInfo.InvariantCulture);
    private double Number(string name) => double.Parse(_bindings.Get(name)?.ToString() ?? "", System.Globalization.CultureInfo.InvariantCulture);
    private async Task InvokeDisplayedGoAsync(GoResult result, string actionId, CancellationToken ct)
    {
        var owner = _goSearchOwner;
        if (owner is null || _go is null || !_displayedGo.TryGetValue(result, out var displayed) ||
            displayed.Original.SessionActor is not { } originalActor || !await owner.IsCurrentAsync(displayed, ct) ||
            !_displayedGo.TryGetValue(result, out var retained) || !ReferenceEquals(retained, displayed) || !owner.IsDisplayed(displayed))
            throw new UnauthorizedAccessException("This result is no longer in the original displayed Go search. Search again.");
        // Optional owning port defaults denied; never fall back to a provider's current-only invocation.
        await _go.InvokeForActorAsync(result, actionId, originalActor, displayed.Query.Scope, ct);
    }

    private async Task SearchAsync(CancellationToken ct)
    {
        if (_go is null || _goSearchOwner is null || _snapshot is null) return;
        // Capture the displayed original session and every query input before the first await.
        var owner = _goSearchOwner;
        var text = _bindings.Get("Query")?.ToString() ?? "";
        var presentation = _snapshot.Effective.EffectiveGoHome.Detached();
        var homeView = !string.IsNullOrWhiteSpace(text) ? "Search" : presentation.Layout == GoHomeLayout.Dashboard ? "Dashboard" : _goHomeView;
        if (homeView != "Search" && homeView != "Dashboard" && !presentation.Sections.Any(s => s.Visible && GoSectionLabel(s.Kind) == homeView))
            homeView = presentation.Sections.Where(s => s.Visible && s.Kind is GoHomeSectionKind.Pinned or GoHomeSectionKind.AllApps).Select(s => GoSectionLabel(s.Kind)).FirstOrDefault() ?? "NoSections";
        var scope = homeView is "Pinned" or "All Apps" or "Dashboard" or "NoSections" ? new GoScope(new HashSet<string>(StringComparer.Ordinal) { "os.installed-applications" }) : GoScope();
        var request = owner.Begin(_snapshot.Stored, new GoQuery(text, homeView == "Search" ? _goCategory : "Apps", Scope: scope));
        _query?.Cancel();
        using var queryLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);
        _query = queryLifetime;
        var results = _bindings.GetOrCreateList<GoResult>("Results");
        var reportedCompletion = false;
        var unavailable = homeView is "Recent" or "Suggested" or "NoSections" ||
            homeView == "Dashboard" && presentation.Sections.Any(s => s.Visible && s.Kind is GoHomeSectionKind.Recent or GoHomeSectionKind.Suggested);
        try
        {
            if (!await owner.IsCurrentAsync(request, queryLifetime.Token))
            {
                await Dispatcher.UIThread.InvokeAsync(() => { if (owner.IsDisplayed(request)) { results.Clear(); _displayedGo.Clear(); _goSectionResults.Clear(); _bindings.GetOrCreateList<GoHomeResultGroup>("GoResultGroups").Clear(); _bindings.GetOrCreateList<GoResult>("LinearGoResults").Clear(); _bindings.Set("Status", "The original Home session changed. Reopen Go."); _bindings.Set("GoQueryState", "Unavailable"); _bindings.Set("GoQuerySummary", "The original Home session changed. Reopen Go."); owner.Invalidate(); } });
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() => { if (owner.IsDisplayed(request) && !queryLifetime.IsCancellationRequested)
            {
                results.Clear(); _displayedGo.Clear(); _goSectionResults.Clear(); _bindings.GetOrCreateList<GoHomeResultGroup>("GoResultGroups").Clear(); _bindings.GetOrCreateList<GoResult>("LinearGoResults").Clear(); _bindings.Set("GoHomeView", homeView);
                _bindings.Set("GoQueryState", "Searching"); _bindings.Set("GoQuerySummary", "Searching available owners…");
                var grouped = homeView != "Search" && presentation.Layout != GoHomeLayout.CompactSearch;
                _bindings.Set("GoResultColumns", presentation.Layout == GoHomeLayout.Dashboard ? "*,*" : "*");
                _bindings.Set("ShowGroupedGoResults", grouped); _bindings.Set("ShowLinearGoResults", !grouped);
                PublishGoResultGroups(presentation, homeView);
                _bindings.Set("GoHomeAvailability", homeView switch {
                    "Pinned" => "Pins from your current taskbar and desktop page.",
                    "Recent" => "Recent items aren't available yet.",
                    "Suggested" => "Suggested items aren't available yet.",
                    "All Apps" => "Installed applications available to your current profile.",
                    _ => "" });
            } });
            await foreach (var sectionUpdate in ReadGoHomeUpdatesAsync(request, homeView, presentation, queryLifetime.Token))
            {
                if (!await owner.IsCurrentAsync(request, queryLifetime.Token))
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { if (owner.IsDisplayed(request)) { results.Clear(); _displayedGo.Clear(); _goSectionResults.Clear(); _bindings.GetOrCreateList<GoHomeResultGroup>("GoResultGroups").Clear(); _bindings.GetOrCreateList<GoResult>("LinearGoResults").Clear(); _bindings.Set("Status", "The original Home session changed. Reopen Go."); _bindings.Set("GoQueryState", "Unavailable"); _bindings.Set("GoQuerySummary", "The original Home session changed. Reopen Go."); owner.Invalidate(); } });
                    return;
                }
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!owner.IsDisplayed(request) || queryLifetime.IsCancellationRequested) return;
                    var update = sectionUpdate.Update;
                    if (update.Result is { } result) { results.Add(result); _displayedGo[result] = request;
                        if (homeView == "Search" || presentation.Layout == GoHomeLayout.CompactSearch) _bindings.GetOrCreateList<GoResult>("LinearGoResults").Add(result);
                        if (sectionUpdate.Section is { } section) _goSectionResults.Add((section, result)); PublishGoResultGroups(presentation, homeView); }
                    reportedCompletion |= update.Complete;
                    unavailable |= update.Failure is not null;
                    _bindings.Set("GoQueryState", unavailable ? (results.Count > 0 ? "Partial" : "Unavailable") : (results.Count > 0 ? "Streaming" : "Searching"));
                    _bindings.Set("GoQuerySummary", unavailable ? (results.Count > 0 ? "Available results are shown; some owners are unavailable." : "Some owners are unavailable. This is not an empty result.") : (results.Count > 0 ? "Results are arriving; other owners may still be searching." : "Searching available owners…"));
                    if (update.Failure is { } failure) _bindings.Set("Status", update.ProviderId + ": " + failure);
                });
            }
            if (!await owner.IsCurrentAsync(request, queryLifetime.Token))
            {
                await Dispatcher.UIThread.InvokeAsync(() => { if (owner.IsDisplayed(request)) { results.Clear(); _displayedGo.Clear(); _goSectionResults.Clear(); _bindings.GetOrCreateList<GoHomeResultGroup>("GoResultGroups").Clear(); _bindings.GetOrCreateList<GoResult>("LinearGoResults").Clear(); _bindings.Set("GoQueryState", "Unavailable"); _bindings.Set("GoQuerySummary", "The original Home session changed. Reopen Go."); owner.Invalidate(); } });
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!owner.IsDisplayed(request) || queryLifetime.IsCancellationRequested) return;
                var known = reportedCompletion || results.Count > 0;
                var state = unavailable ? (results.Count > 0 ? "Partial" : "Unavailable") : !known ? "Unavailable" : results.Count == 0 ? "Empty" : "Complete";
                _bindings.Set("GoQueryState", state);
                _bindings.Set("GoQuerySummary", state switch {
                    "Partial" => "Available results are shown; some owners are unavailable.",
                    "Unavailable" => "This query cannot confirm an empty result because an owner or section is unavailable.",
                    "Empty" => "No matching items were returned by the available owners.",
                    _ => results.Count + (results.Count == 1 ? " result is available." : " results are available.") });
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (owner.IsDisplayed(request) && !queryLifetime.IsCancellationRequested) { results.Clear(); _displayedGo.Clear(); _goSectionResults.Clear(); _bindings.GetOrCreateList<GoHomeResultGroup>("GoResultGroups").Clear(); _bindings.GetOrCreateList<GoResult>("LinearGoResults").Clear(); _bindings.Set("Status", ex.Message); _bindings.Set("GoQueryState", "Unavailable"); _bindings.Set("GoQuerySummary", "The query is unavailable; no empty-result claim can be made."); } });
        }
        finally { if (ReferenceEquals(_query, queryLifetime)) _query = null; }
    }
    private void PublishGoResultGroups(GoHomeConfiguration presentation, string view)
    {
        var groups = _bindings.GetOrCreateList<GoHomeResultGroup>("GoResultGroups"); groups.Clear();
        foreach (var group in GoHomeResultPresentation.Create(presentation, _goSectionResults, view)) groups.Add(group);
    }
    private sealed record GoSectionUpdate(GoHomeSectionKind? Section, GoUpdate Update);
    private async IAsyncEnumerable<GoSectionUpdate> ReadGoHomeUpdatesAsync(ShellGoSearchRequest request, string homeView, GoHomeConfiguration presentation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (_configuration is null || _go is null || homeView is "Recent" or "Suggested" or "NoSections") yield break;
        if (homeView == "Search")
        { await foreach (var update in _go.QueryForActorAsync(request.Query, request.Original.SessionActor ?? throw new UnauthorizedAccessException("The displayed Go actor is unavailable."), ct)) yield return new(null, update); yield break; }
        foreach (var section in presentation.Sections.Where(s => s.Visible && (homeView == "Dashboard" || GoSectionLabel(s.Kind) == homeView)))
        {
            if (section.Kind == GoHomeSectionKind.Pinned)
            {
                var pinned = await new ShellGoPinnedHome(_configuration, _go).ReadAsync(request.Original, ct);
                foreach (var result in pinned) yield return new(section.Kind, new(result.ProviderId, result, false, null));
                yield return new(section.Kind, new("os.installed-applications", null, true, null));
            }
            else if (section.Kind == GoHomeSectionKind.AllApps)
            { await foreach (var update in new ShellGoAllAppsHome(_configuration, _go).ReadAsync(request.Original, ct)) yield return new(section.Kind, update); }
        }
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
    }
    public void Dispose() { _goSearchOwner?.Invalidate(); _timer.Stop(); _lifetime.Cancel(); _query?.Cancel(); _lifetime.Dispose(); }
}
