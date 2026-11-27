using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Home inspection of the canonical Specialist definitions. The page owns read/navigation
/// work only; it has no invocation, grant, enable/disable or definition-writing dependency.</summary>
internal sealed class HomeSpecialistsDestinationPage : UserControl, ICuiWritableBindingContext,
    ICuiRepeatItemBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private const string ConfigurationUnavailable = "AI Studio configuration is unavailable in this native workspace.";
    private readonly ICatalogRepository _catalogue;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly Func<HomeSpecialistConfigurationRequest, Task, bool>? _configure;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly CuiControlLoader _loader;
    private readonly CuiViewModel _bindings = new();
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly Control _surface;
    private readonly Button? _filterButton;
    private readonly Control? _inspector;
    private readonly List<IDisposable> _themeObservations = [];
    private AgentDefinition[] _definitions = [];
    private HomeSpecialistRow[] _rows = [];
    private HomeSpecialistRow? _selected;
    private string _query = string.Empty, _status = "Loading configured Specialists…";
    private HomeSpecialistFilter _filter;
    private long _generation, _selectionRevision, _bindingRevision;
    private bool _active, _admitted, _loading, _stopped;
    private int _columns = 1;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private event PropertyChangedEventHandler? _changed;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    { add => _changed += value; remove => _changed -= value; }

    internal HomeSpecialistsDestinationPage(ICatalogRepository catalogue, Func<CancellationToken, Task> admit,
        CuiThemePreferenceContext? theme = null, Func<HomeSpecialistConfigurationRequest, Task, bool>? configure = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _theme = theme; _configure = configure;
        _work = new(StopAsync, CleanupAsync);
        _bindings.PropertyChanged += OnBindingsChanged;
        _loader = new(HomeAppsCuiDocument.CreateControlRegistry());
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
        _bindings.Set("specialists.pagePadding", new Thickness(32, 28, 32, 30));
        _bindings.Set("specialists.headerColumns", "1fr Auto"); _bindings.Set("specialists.headerRows", "Auto");
        _bindings.Set("specialists.actionRow", 0); _bindings.Set("specialists.actionColumn", 1);
        _bindings.Set("specialists.searchWidth", 390d);
        _bindings.Set("specialists.cardRadius", new CornerRadius(16));
        _bindings.Set("specialists.buttonRadius", new CornerRadius(24));
        RefreshBindings();
        using var stream = typeof(HomeSpecialistsDestinationPage).Assembly.GetManifestResourceStream(
            "Haven.Desktop.Views.Pages.Home.HomeSpecialists.cui")
            ?? throw new InvalidDataException("The authored Home Specialists document is missing.");
        using var reader = new StreamReader(stream);
        var loaded = _loader.LoadMarkup(reader.ReadToEnd(), "HomeSpecialists.cui");
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
        {
            _loader.Dispose();
            throw new InvalidDataException("The authored Specialists page could not be rendered: " +
                string.Join("; ", loaded.Diagnostics.Select(item => item.Message)));
        }
        _surface = loaded.Root;
        _loader.WireBindings(_surface); Content = _surface;
        _filterButton = _surface.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.Name == "specialists-filter");
        _inspector = _surface.GetVisualDescendants().OfType<Control>().SingleOrDefault(control => control.Name == "specialist-inspector");
        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        RefreshBindings();
    }

    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal bool IsAlive => !_work.IsRetiring;
    internal Guid? SelectedSpecialistId => _admitted ? _selected?.Definition.Id : null;
    internal string Query => _query;
    internal IReadOnlyList<HomeSpecialistRow> VisibleRows => _rows;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();

    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        _active = true;
        return _work.RunAsync(original => RefreshAsync(original, token), task => _initialization ??= task);
    }

    internal void Deactivate()
    {
        Dispatcher.UIThread.VerifyAccess();
        _active = false; _admitted = false; ++_generation;
        _filterButton?.ContextMenu?.Close();
        RefreshBindings();
    }

    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);

    public bool TrySetValue(string path, object? value)
    {
        if (path != "specialists.query" || !IsAlive || !_active || !_admitted) return false;
        // Search changes presentation of the already admitted snapshot. It reads no additional source.
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsAlive && _active && _admitted);
            original.RunAcceptedPublicationCallback(() => { _query = value?.ToString() ?? string.Empty; RefreshBindings(); });
        });
        return true;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not HomeSpecialistRow row) return false;
        value = path.ToLowerInvariant() switch
        {
            "id" => row.Definition.Id.ToString("D"), "target" => row.Target,
            "name" => row.Definition.Name, "description" => row.Definition.Description,
            "iconsource" => row.IconSource, "status" => row.Status, "origin" => row.Origin,
            "scope" => row.Scope.Summary, "model" => row.ModelPolicy,
            "detailslabel" => row.DetailsLabel, "configurelabel" => row.ConfigureLabel,
            "invitereason" => row.InviteReason, "row" => row.Row, "column" => row.Column,
            "selected" => _selected?.Definition.Id == row.Definition.Id,
            _ => null
        };
        return value is not null;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public bool? IsActionAvailable(string command) => command switch
    {
        "specialists.refresh" => IsAlive && _active,
        "specialists.select" or "specialists.filter" or "specialists.filters" or "specialists.clear-query" or "specialists.close-details" => IsAlive && _active && _admitted && !_loading,
        "specialists.create" => IsAlive && _active && _admitted && !_loading && _configure is not null,
        "specialists.configure" => IsAlive && _active && _admitted && !_loading && _configure is not null && _selected is not null,
        "specialists.invite" => false,
        _ => null
    };

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Specialist action is unavailable.");
        return new(_work.RunAsync(async original =>
        {
            if (command == "specialists.refresh") { await RefreshAsync(original, cancellationToken); return; }
            var generation = _generation;
            var selectsDefinition = command is "specialists.select" or "specialists.configure" or "specialists.close-details";
            var inspectedAtAdmission = _selected;
            var selectionRevision = selectsDefinition ? ++_selectionRevision : _selectionRevision;
            original.BindPublicationGuard(() => IsAlive && _active && _admitted && generation == _generation &&
                (!selectsDefinition || selectionRevision == _selectionRevision));
            CheckTokens(original, cancellationToken);
            try
            {
                await original.AwaitAsync(_admit(original.Token));
                CheckTokens(original, cancellationToken);
                if (!original.IsAcceptedPublicationCurrent) return;
                if (command == "specialists.select")
                {
                    if (parameter is not HomeSpecialistTarget target || !_definitions.Any(definition => definition.Id == target.AgentId))
                    { _status = "This Specialist is no longer in the displayed catalogue. Refresh to continue."; RefreshBindings(); return; }
                    var definitions = await ReadAsync(original, cancellationToken);
                    if (!original.IsAcceptedPublicationCurrent) return;
                    var current = definitions.SingleOrDefault(definition => definition.Id == target.AgentId);
                    original.RunAcceptedPublicationCallback(() =>
                    {
                        _definitions = definitions;
                        _selected = current is null ? null : HomeSpecialistCatalogueProjection.Inspect(current);
                        _status = current is null ? "This Specialist was removed. Its previous definition is no longer selectable." :
                            _selected!.Target != target ? "The definition changed. Review its current instructions and scope." : "Definition selected. No work has started.";
                        RefreshBindings();
                        if (_active && _admitted && _selected is not null) RevealInspection();
                    });
                    return;
                }
                if (command is "specialists.create" or "specialists.configure")
                {
                    HomeSpecialistConfigurationRequest request;
                    if (command == "specialists.create") request = new(null, null, null);
                    else
                    {
                        var inspected = inspectedAtAdmission ?? throw new InvalidOperationException("Inspect a Specialist first.");
                        var definitions = await ReadAsync(original, cancellationToken);
                        var current = definitions.SingleOrDefault(definition => definition.Id == inspected.Definition.Id);
                        if (!original.IsAcceptedPublicationCurrent) return;
                        if (current is null || HomeSpecialistCatalogueProjection.Fingerprint(current) != inspected.Target.Snapshot)
                        {
                            _definitions = definitions;
                            _selected = current is null ? null : HomeSpecialistCatalogueProjection.Inspect(current);
                            _status = "The definition changed. Review its current details before configuration.";
                            RefreshBindings(); return;
                        }
                        request = new(current.Id, current.UpdatedAt, inspected.Target.Snapshot);
                    }
                    original.DemandPublication();
                    var settled = Task.WhenAll(original.Task, WhenActionsIdleAsync());
                    _status = _configure!(request, settled) ? "Opening the canonical AI Studio definition." :
                        "The configuration owner is unavailable. The definition was preserved.";
                    RefreshBindings(); return;
                }
                original.RunAcceptedPublicationCallback(() =>
                {
                    switch (command)
                    {
                        case "specialists.clear-query": _query = string.Empty; break;
                        case "specialists.close-details": _selected = null; break;
                        case "specialists.filter":
                            _filter = (parameter as string) switch
                            {
                                "all" => HomeSpecialistFilter.All, "enabled" => HomeSpecialistFilter.Enabled,
                                "disabled" => HomeSpecialistFilter.Disabled, _ => _filter
                            };
                            break;
                        case "specialists.filters":
                            if (_filterButton is { IsEffectivelyVisible: true, ContextMenu: { } menu }) menu.Open(_filterButton);
                            break;
                    }
                    RefreshBindings();
                });
            }
            catch
            {
                if (IsAlive && generation == _generation && (!selectsDefinition || selectionRevision == _selectionRevision)) ClearUnavailable();
                throw;
            }
        }));
    }

    private async Task<AgentDefinition[]> ReadAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var definitions = await original.AwaitAsync(_catalogue.GetAllAgentsAsync(original.Token));
        await original.AwaitAsync(_admit(original.Token));
        CheckTokens(original, caller);
        return HomeSpecialistCatalogueProjection.Validate(definitions);
    }

    private async Task RefreshAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var generation = ++_generation;
        var selectedId = _selected?.Definition.Id;
        _loading = true; _admitted = false; _status = "Loading configured Specialists…"; RefreshBindings();
        original.BindPublicationGuard(() => IsAlive && _active && generation == _generation);
        try
        {
            CheckTokens(original, token);
            await original.AwaitAsync(_admit(original.Token));
            var definitions = await ReadAsync(original, token);
            if (!original.IsAcceptedPublicationCurrent) return;
            original.RunAcceptedPublicationCallback(() =>
            {
                _definitions = definitions; _admitted = true; _loading = false;
                _selected = selectedId is { } id && definitions.SingleOrDefault(definition => definition.Id == id) is { } selected
                    ? HomeSpecialistCatalogueProjection.Inspect(selected) : null;
                _status = definitions.Length == 0 ? "No Specialists are configured in this catalogue." :
                    "Inspect a definition before choosing its conversation or document context.";
                RefreshBindings();
            });
        }
        catch
        {
            if (IsAlive && generation == _generation) ClearUnavailable();
            throw;
        }
    }

    private void ClearUnavailable()
    {
        _admitted = false; _loading = false; _definitions = []; _selected = null;
        _status = "The Specialist catalogue is unavailable. Refresh after the issue is resolved.";
        RefreshBindings();
    }

    private void RefreshBindings()
    {
        var publication = ++_bindingRevision;
        void Set(string key, object? value)
        {
            if (publication == _bindingRevision) _bindings.Set(key, value);
        }
        _rows = _admitted ? HomeSpecialistCatalogueProjection.Project(_definitions, _query, _filter, _columns) : [];
        var selected = _admitted ? _selected : null;
        Set("specialists.rows", _rows);
        Set("specialists.columns", string.Join(' ', Enumerable.Repeat("1fr", _columns)));
        Set("specialists.gridRows", HomeAppHubCatalogue.AutoRows(_rows.Length, _columns));
        Set("specialists.query", _query);
        Set("specialists.hasQuery", _query.Length != 0);
        Set("specialists.canBrowse", _active && _admitted && !_loading);
        Set("specialists.status", _status);
        Set("specialists.count", _admitted ? $"{_definitions.Length} configured Specialist{(_definitions.Length == 1 ? string.Empty : "s")}" : string.Empty);
        Set("specialists.filterLabel", _filter switch { HomeSpecialistFilter.Enabled => "Enabled", HomeSpecialistFilter.Disabled => "Disabled", _ => "All Specialists" });
        Set("specialists.all", _filter == HomeSpecialistFilter.All);
        Set("specialists.enabled", _filter == HomeSpecialistFilter.Enabled);
        Set("specialists.disabled", _filter == HomeSpecialistFilter.Disabled);
        Set("specialists.empty", _admitted && _rows.Length == 0);
        Set("specialists.emptyTitle", _definitions.Length == 0 ? "No Specialists configured" : "No Specialists found");
        Set("specialists.emptyDetail", _definitions.Length == 0 ? "Specialist definitions are managed in AI Studio." : "Try another search or choose All Specialists.");
        Set("specialists.canCreate", IsActionAvailable("specialists.create") == true);
        Set("specialists.configurationReason", _configure is null ? ConfigurationUnavailable : "Open the canonical AI Studio configuration.");
        Set("specialists.configurationUnavailable", _configure is null);
        Set("specialists.hasSelection", selected is not null);
        Set("specialist.name", selected?.Definition.Name ?? string.Empty);
        Set("specialist.description", selected?.Definition.Description ?? string.Empty);
        Set("specialist.status", selected?.Status ?? string.Empty);
        Set("specialist.origin", selected?.Origin ?? string.Empty);
        Set("specialist.updated", selected?.Updated ?? string.Empty);
        Set("specialist.identity", selected?.Definition.Id.ToString("D") ?? string.Empty);
        Set("specialist.instructions", TextOr(selected?.Definition.Instructions, "No additional instructions are configured."));
        Set("specialist.inputs", TextOr(selected?.Definition.DetectionRules, "No detection rules are configured."));
        Set("specialist.preferred", selected?.ModelPolicy ?? string.Empty);
        Set("specialist.fallback", selected?.FallbackPolicy ?? string.Empty);
        Set("specialist.tools", selected is null ? string.Empty : selected.Scope.Capabilities.Count != 0
            ? string.Join(Environment.NewLine, selected.Scope.Capabilities) : selected.Scope.IsValid && !selected.Scope.HasUninspectedFields ? "No tools configured" : "Tool scope could not be confirmed");
        Set("specialist.sources", selected is null ? string.Empty : selected.Scope.Sources.Count != 0
            ? string.Join(Environment.NewLine, selected.Scope.Sources) : selected.Scope.IsValid && !selected.Scope.HasUninspectedFields ? "No knowledge sources configured" : "Source scope could not be confirmed");
        Set("specialist.policy", selected?.Scope.Note ?? string.Empty);
        Set("specialist.inviteReason", selected?.InviteReason ?? string.Empty);
        Set("specialist.canConfigure", IsActionAvailable("specialists.configure") == true);
        if (publication == _bindingRevision) _loader.RefreshBindings();
    }

    private void RevealInspection()
    {
        _surface.UpdateLayout();
        if (_inspector is { IsVisible: true } details)
            details.BringIntoView(new Rect(0, 0, details.Bounds.Width, Math.Min(1, details.Bounds.Height)));
        _surface.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.Name == "specialist-close-details")?.Focus();
    }

    private static string TextOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private void OnBindingsChanged(object? sender, PropertyChangedEventArgs args) => _changed?.Invoke(this, args);
    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        StopThemeObservation();
        var observer = new ResourceObserver(ApplyTheme);
        foreach (var name in new[] { "CuiEffectiveThemeName", "CuiCardRadius", "CuiControlRadius" })
            _themeObservations.Add(_surface.GetResourceObservable(name).Subscribe(observer));
        ApplyTheme(); ApplyWidth();
    }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args) => StopThemeObservation();
    private void StopThemeObservation()
    { foreach (var observation in _themeObservations) observation.Dispose(); _themeObservations.Clear(); }
    private void ApplyTheme()
    {
        if (!IsAlive) return;
        _surface.TryFindResource("CuiEffectiveThemeName", out var name);
        _surface.TryFindResource("CuiCardRadius", out var card);
        _surface.TryFindResource("CuiControlRadius", out var control);
        var expression = CuiThemeCatalog.FindBuiltIn(name as string);
        _bindings.Set("specialists.cardRadius", expression?.Theme switch
        {
            CuiTheme.Glow => new CornerRadius(22), CuiTheme.Professional => new CornerRadius(16),
            _ => card is CornerRadius value ? value : new CornerRadius(16)
        });
        _bindings.Set("specialists.buttonRadius", expression?.Theme switch
        {
            CuiTheme.Glow => new CornerRadius(40), CuiTheme.Professional => new CornerRadius(24),
            _ => control is CornerRadius value ? value : new CornerRadius(12)
        });
    }
    private sealed class ResourceObserver(Action changed) : IObserver<object?>
    {
        public void OnNext(object? value) => changed();
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }
    private void OnSizeChanged(object? sender, SizeChangedEventArgs args) { if (args.WidthChanged) ApplyWidth(); }
    private void ApplyWidth()
    {
        var scale = _theme?.Current.Accessibility.DisplayScale ?? 1;
        // This surface owns the grid's available space. Its SizeChanged event can
        // run before the containing Window has finished its first arrange.
        var width = Bounds.Width / scale;
        _columns = width <= 650 ? 1 : width <= 1000 ? 2 : 3;
        _bindings.Set("specialists.searchWidth", Math.Min(390, Math.Max(120, Bounds.Width / scale - (width <= 650 ? 32 : 64))));
        _bindings.Set("specialists.pagePadding", width <= 650 ? new Thickness(16, 18, 16, 28) : new Thickness(32, 28, 32, 30));
        _bindings.Set("specialists.headerColumns", width <= 650 ? "1fr" : "1fr Auto");
        _bindings.Set("specialists.headerRows", width <= 650 ? "Auto Auto" : "Auto");
        _bindings.Set("specialists.actionRow", width <= 650 ? 1 : 0);
        _bindings.Set("specialists.actionColumn", width <= 650 ? 0 : 1);
        RefreshBindings();
    }

    private static void CheckTokens(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    { original.Token.ThrowIfCancellationRequested(); caller.ThrowIfCancellationRequested(); }

    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); token.ThrowIfCancellationRequested();
        if (_preparation is null || _preparation.IsCompletedSuccessfully) _preparation = PrepareAsync();
        return _preparation.WaitAsync(token);
    }
    private async Task<bool> PrepareAsync()
    { await _loader.WhenActionsIdleAsync(); await _work.JoinAdmittedOriginalsAsync(); return true; }
    internal Task CloseAndDrainAsync()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); return _work.CloseAndDrainAsync(); }
    private async Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); _active = false; _admitted = false; ++_generation; ++_bindingRevision;
        SizeChanged -= OnSizeChanged; AttachedToVisualTree -= OnAttached; DetachedFromVisualTree -= OnDetached;
        var failures = new List<Exception>();
        try { StopThemeObservation(); } catch (Exception error) { failures.Add(error); }
        _bindings.PropertyChanged -= OnBindingsChanged;
        try { _filterButton?.ContextMenu?.Close(); } catch (Exception error) { failures.Add(error); }
        try { _loader.Dispose(); } catch (Exception error) { failures.Add(error); }
        try { await _loader.WhenActionsIdleAsync(); } catch (Exception error) { failures.Add(error); }
        if (_preparation is { } preparation)
            try { await preparation; } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Specialist page actions did not settle cleanly.", failures);
        _stopped = true;
    }
    private Task CleanupAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_stopped) throw new InvalidOperationException("Join the original Specialist loader before detaching it.");
        _definitions = []; _rows = []; _selected = null; Content = null; return Task.CompletedTask;
    }
}
