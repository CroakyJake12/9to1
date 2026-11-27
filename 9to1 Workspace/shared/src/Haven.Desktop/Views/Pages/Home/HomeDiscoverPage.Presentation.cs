using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.Services;
using HavenOS.Home.Discover;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class HomeDiscoverPage : ICuiWritableBindingContext, ICuiRepeatItemBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private sealed record ProviderChoice(string? Id, string Name)
    { public override string ToString() => Name; }

    private readonly ComboBox _providerFilter = new();
    private readonly ComboBox _installFilter = new()
    {
        ItemsSource = new[] { "Any availability", "Installed", "Available", "Downloading", "Failed", "Unverified" },
        SelectedIndex = 0
    };
    private readonly ProviderChoice _allProviders = new(null, "All providers");
    private const int MaximumObservedModels = 200;
    private long _observedModelCount;
    private HomeDiscoverModel[] _catalogueModels = [], _visibleModels = [];
    private HomeDiscoverModel? _inspectedModel;
    private Func<CancellationToken, Task>? _admit;
    private Func<CancellationToken, Task<bool>>? _openModelPicker;
    private Task<bool>? _preparation;
    private string _view = "discover", _detailsOrigin = "discover", _catalogueSource = "local";
    private bool _active, _updatingProviderFilter, _filtersExpanded, _settingQuickFilter;
    private string? _capabilityFilter;
    private readonly List<IDisposable> _themeObservations = [];
    private event PropertyChangedEventHandler? _presentationChanged;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    { add => _presentationChanged += value; remove => _presentationChanged -= value; }

    internal string PageTitle => _view switch
    {
        "mine" => "My models", "local-details" => "Model details", "details" => "Model details",
        "catalogue" => "Find and install models", "compare" => "Compare models", _ => "Discover"
    };
    internal event EventHandler? PageTitleChanged;
    internal IReadOnlyList<HomeDiscoverModel> VisibleModels => _visibleModels;
    internal string CurrentView => _view;

    internal void BindOriginalAdmission(Func<CancellationToken, Task>? admit)
    {
        if (_active || _generation != 0) throw new InvalidOperationException("Bind the original Home admission before activating Models.");
        _admit = admit;
    }

    private void InitializePresentation()
    {
        if (_dulche is null) _catalogueSource = "huggingface";
        _bindings.PropertyChanged += OnDiscoverBindingChanged;
        _bindings.Set("models.navigationRadius", new CornerRadius(12));
        _providerFilter.ItemsSource = new[] { _allProviders };
        _providerFilter.SelectedItem = _allProviders;
        _loader.BindAuthoredControl("discover-provider", _ => _providerFilter);
        _loader.BindAuthoredControl("discover-install-state", _ => _installFilter);
        _providerFilter.SelectionChanged += (_, _) => { if (!_updatingProviderFilter) RefreshPresentation(); };
        _installFilter.SelectionChanged += (_, _) => RefreshPresentation();
        RefreshPresentation();
    }

    private void OnDiscoverBindingChanged(object? sender, PropertyChangedEventArgs args) =>
        _presentationChanged?.Invoke(this, args);

    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) => false;

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        if (TryGetSavedModelValue(item, path, out value)) return true;
        value = null;
        if (item is not HomeDiscoverModel model) return false;
        value = path.ToLowerInvariant() switch
        {
            "key" => model.Identity.StableKey, "target" => model,
            "name" => model.DisplayName ?? model.Name,
            "monogram" => ModelMonogram(model),
            "provider" => model.Identity.ProviderId,
            "location" => LocationLabel(model.Locality),
            "availability" => AvailabilityLabel(model.InstallState),
            "connection" => ConnectionLabel(model.ConnectionState),
            "routesummary" => model.Identity.ProviderId + " · " + LocationLabel(model.Locality) + " · " +
                (model.ContextLimit is { } capacity ? $"{capacity:N0} token context" : "Context unreported"),
            "needsattention" => model.ConnectionState != HomeDiscoverConnectionState.Connected,
            "attention" => ConnectionLabel(model.ConnectionState),
            "capabilities" => model.Capabilities is { Count: > 0 }
                ? string.Join(" · ", model.Capabilities.Order(StringComparer.OrdinalIgnoreCase)) : "Capabilities not reported",
            "requirements" => model.ContextLimit is { } context
                ? $"Reported context capacity: {context:N0} tokens" : "Context capacity not reported",
            "detailslabel" => "Details for " + (model.DisplayName ?? model.Name),
            "comparelabel" => SelectedModels().Any(selected => selected.Identity == model.Identity) ? "Remove from comparison" : "Compare",
            "selected" => SelectedModels().Any(selected => selected.Identity == model.Identity),
            _ => null
        };
        return value is not null;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public bool? IsActionAvailable(string command) => command switch
    {
        "models.discover" or "models.mine" or "models.back" or "models.compare" or
        "models.details" or "models.compare-selection" or "models.catalogue-local" or "models.catalogue-huggingface" or
        "models.filter" or "models.filters" => !_disposed && !_work.IsRetiring && _active,
        "models.picker" => !_disposed && !_work.IsRetiring && _active && _openModelPicker is not null,
        _ => ModelPreferenceActionAvailable(command)
    };

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        if (command is "models.save" or "models.remove-saved" or "models.saved-details" or "models.use")
            return DispatchModelPreferenceAsync(command, parameter, cancellationToken);
        return new(RunDiscoverAsync(async token =>
        {
            if (_disposed || !_active) return;
            switch (command)
            {
                case "models.discover": ShowView("discover"); break;
                case "models.mine": ShowView("mine"); break;
                case "models.back": ShowView(_detailsOrigin); break;
                case "models.compare": ShowView("compare"); break;
                case "models.filters": _filtersExpanded = !_filtersExpanded; RefreshPresentation(); break;
                case "models.filter":
                    if (parameter is string filter) SelectQuickFilter(filter);
                    break;
                case "models.catalogue-local":
                    if (_dulche is null) return;
                    _catalogueSource = "local"; ShowView("catalogue"); break;
                case "models.catalogue-huggingface":
                    _catalogueSource = "huggingface"; ShowView("catalogue");
                    if (!_huggingFaceActivated)
                    {
                        await _huggingFace.ActivateAsync(token);
                        if (!_disposed && _active && _view == "catalogue") _huggingFaceActivated = true;
                    }
                    break;
                case "models.details":
                    if (parameter is not HomeDiscoverModel detail || !_catalogueModels.Any(model => ReferenceEquals(model, detail)))
                    { SetDiscoverValue(_status, "Text", "This catalogue entry changed. Search again to inspect it."); return; }
                    _detailsOrigin = _view == "mine" ? "mine" : "discover";
                    _inspectedModel = detail; _modelDetailsEpoch++;
                    if (detail.Identity.ProviderId == DulcheSocketModelProvider.ProviderId &&
                        _dulche?.TryShowModel(detail.Identity.ModelId) == true)
                        ShowView("local-details");
                    else ShowView("details");
                    break;
                case "models.compare-selection":
                    if (parameter is not HomeDiscoverModel selected || !_catalogueModels.Any(model => ReferenceEquals(model, selected))) return;
                    if (_models.SelectedItems?.Contains(selected) == true) _models.SelectedItems.Remove(selected);
                    else if (SelectedModels().Length < 20) _models.SelectedItems?.Add(selected);
                    else SetDiscoverValue(_status, "Text", "Compare up to 20 models at a time.");
                    RefreshPresentation();
                    break;
                case "models.picker":
                    if (_openModelPicker is { } open && !await open(token))
                        SetDiscoverValue(_status, "Text", "The shared model picker is unavailable in this workspace.");
                    break;
            }
        }, cancellationToken));
    }

    private Task OpenCatalogueAsync() => RunDiscoverAsync(async token =>
    {
        if (_disposed || !_active) return;
        ShowView("catalogue");
        if (_catalogueSource == "huggingface" && !_huggingFaceActivated)
        {
            await _huggingFace.ActivateAsync(token);
            if (!_disposed && _active && _view == "catalogue") _huggingFaceActivated = true;
        }
    }, CancellationToken.None);

    private void OnLocalModelDetails(object? sender, EventArgs args)
    {
        if (_disposed || !_active) return;
        _detailsOrigin = "mine";
        var matches = _catalogueModels.Where(model => model.Identity.ProviderId == DulcheSocketModelProvider.ProviderId &&
            model.Identity.ModelId == _dulche?.CurrentModelId).ToArray();
        _inspectedModel = matches.Length == 1 ? matches[0] : null;
        _modelDetailsEpoch++;
        ShowView("local-details");
    }

    private void ShowView(string view)
    {
        if (_view != view) _modelDetailsEpoch++;
        _view = view;
        if (view == "mine") _dulche?.ShowLibrary();
        else if (view == "catalogue") _dulche?.ShowCatalogue();
        RefreshPresentation();
        ApplyDiscoverWidth();
        PageTitleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishCatalogue(HavenOS.Home.Discover.HomeDiscoverPage page)
    {
        // The page is filtered, but these provider snapshots belong to the SAME
        // completed observation. Keep a bounded catalogue from that observation so
        // clearing a local filter can restore other models without a provider read.
        // Requested matches take priority at the bound; never merge an older revision.
        _observedModelCount = page.Providers.Sum(provider => (long)provider.Models.Count);
        _catalogueModels = page.Models.Concat(page.Providers.SelectMany(provider => provider.Models))
            .DistinctBy(model => model.Identity.StableKey, StringComparer.Ordinal)
            .Take(MaximumObservedModels)
            .OrderBy(model => model.Identity.StableKey, StringComparer.Ordinal).ToArray();
        _models.ItemsSource = _catalogueModels;
        if (_inspectedModel is { } inspected)
            _inspectedModel = _catalogueModels.SingleOrDefault(model => model.Identity == inspected.Identity);
        var selectedProvider = (_providerFilter.SelectedItem as ProviderChoice)?.Id;
        _updatingProviderFilter = true;
        try
        {
            var providers = new[] { _allProviders }.Concat(page.Providers.OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(provider => new ProviderChoice(provider.ProviderId, provider.DisplayName))).ToArray();
            _providerFilter.ItemsSource = providers;
            _providerFilter.SelectedItem = providers.FirstOrDefault(provider => provider.Id == selectedProvider) ?? _allProviders;
        }
        finally { _updatingProviderFilter = false; }
        RefreshPresentation();
    }

    private HomeDiscoverQuery CurrentQuery()
    {
        var provider = (_providerFilter.SelectedItem as ProviderChoice)?.Id;
        return new(_search.Text,
            _category.SelectedIndex > 0 ? (HomeDiscoverCategory?)(_category.SelectedIndex - 1) : null,
            _locality.SelectedIndex > 0 ? (HomeDiscoverLocality?)_locality.SelectedIndex : null,
            SelectedInstallState(), RequiredCapabilities: _capabilityFilter is null ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _capabilityFilter },
            ProviderIds: provider is null ? null : new HashSet<string>(StringComparer.Ordinal) { provider }, PageSize: MaximumObservedModels);
    }

    private void SelectQuickFilter(string filter)
    {
        var category = filter switch { "all" or "code" or "vision" => 0, "chat" => 1, "image" => 2, "speech" => 3, "video" => 5, _ => -1 };
        if (category < 0) return;
        _settingQuickFilter = true;
        try { _category.SelectedIndex = category; }
        finally { _settingQuickFilter = false; }
        // These are reported capabilities, never inferred from a model name or its
        // ability to generate text. Image generation and image understanding stay distinct.
        _capabilityFilter = filter switch { "code" => "Code", "vision" => "Vision", _ => null };
        RefreshPresentation();
    }

    private HomeDiscoverInstallState? SelectedInstallState() => _installFilter.SelectedIndex switch
    {
        1 => HomeDiscoverInstallState.Installed, 2 => HomeDiscoverInstallState.Available,
        3 => HomeDiscoverInstallState.Pulling, 4 => HomeDiscoverInstallState.Failed,
        5 => HomeDiscoverInstallState.Unknown, _ => null
    };

    private void RefreshPresentation()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        var provider = (_providerFilter.SelectedItem as ProviderChoice)?.Id;
        var category = _category.SelectedIndex > 0 ? (HomeDiscoverCategory?)(_category.SelectedIndex - 1) : null;
        var locality = _locality.SelectedIndex > 0 ? (HomeDiscoverLocality?)_locality.SelectedIndex : null;
        var install = SelectedInstallState();
        _visibleModels = _catalogueModels.Where(model =>
            (query.Length == 0 || string.Join(' ', model.Name, model.DisplayName, model.Identity.ProviderId, model.Identity.ModelId,
                model.Origin, model.Architecture, model.ParameterInformation, model.QuantizationOrFormat,
                model.EntitlementClass, model.CostClass, model.PrivacyClass, model.Residency,
                model.Categories is null ? "" : string.Join(' ', model.Categories),
                model.Capabilities is null ? "" : string.Join(' ', model.Capabilities)).Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (provider is null || provider == model.Identity.ProviderId) &&
            (category is null || model.Categories?.Contains(category.Value) == true) &&
            (_capabilityFilter is null || model.Capabilities?.Contains(_capabilityFilter) == true) &&
            (locality is null || locality == model.Locality) && (install is null || install == model.InstallState) &&
            (_view != "mine" || model.InstallState == HomeDiscoverInstallState.Installed &&
                (_dulche is null || model.Identity.ProviderId != DulcheSocketModelProvider.ProviderId))).ToArray();
        _bindings.Set("models.rows", _visibleModels);
        _bindings.Set("models.discover", _view == "discover");
        _bindings.Set("models.filtersExpanded", _filtersExpanded);
        var extraFilters = (provider is null ? 0 : 1) + (install is null ? 0 : 1) + (_category.SelectedIndex == 4 ? 1 : 0);
        _bindings.Set("models.moreFiltersLabel", extraFilters == 0 ? "More filters" : $"More filters ({extraFilters})");
        _bindings.Set("models.filterAll", _category.SelectedIndex == 0 && _capabilityFilter is null);
        _bindings.Set("models.filterChat", _category.SelectedIndex == 1 && _capabilityFilter is null);
        _bindings.Set("models.filterImage", _category.SelectedIndex == 2 && _capabilityFilter is null);
        _bindings.Set("models.filterSpeech", _category.SelectedIndex == 3 && _capabilityFilter is null);
        _bindings.Set("models.filterVideo", _category.SelectedIndex == 5 && _capabilityFilter is null);
        _bindings.Set("models.filterCode", _capabilityFilter == "Code");
        _bindings.Set("models.filterVision", _capabilityFilter == "Vision");
        _bindings.Set("models.mine", _view == "mine");
        _bindings.Set("models.catalogue", _view == "catalogue");
        _bindings.Set("models.comparison", _view == "compare");
        _bindings.Set("models.details", _view == "details");
        _bindings.Set("models.localDetails", _view == "local-details");
        _bindings.Set("models.modelActionsVisible", _view is "details" or "local-details");
        _bindings.Set("models.hasDulche", _dulche is not null);
        _bindings.Set("models.dulcheVisible", _dulche is not null && (_view is "mine" or "local-details" || _view == "catalogue" && _catalogueSource == "local"));
        _bindings.Set("models.localCatalogue", _catalogueSource == "local");
        _bindings.Set("models.huggingFaceCatalogue", _catalogueSource == "huggingface");
        _bindings.Set("models.huggingFaceVisible", _view == "catalogue" && _catalogueSource == "huggingface");
        _bindings.Set("models.cardsVisible", _view == "discover" || _view == "mine" && _visibleModels.Length > 0);
        _bindings.Set("models.empty", _view == "discover" && _visibleModels.Length == 0 || _view == "mine" && _dulche is null && _visibleModels.Length == 0);
        _bindings.Set("models.title", _view == "discover" ? "Models" : PageTitle);
        _bindings.Set("models.headingVisible", _view is not ("details" or "local-details"));
        _bindings.Set("models.navigationVisible", _view is not ("details" or "local-details"));
        _bindings.Set("models.subtitle", _view switch
        {
            "mine" => "Your configured and installed models.",
            "details" => "Capabilities and requirements reported by the provider.",
            "local-details" => "Inspect the selected model, then choose an action.",
            "catalogue" => "Search sources, inspect files and manage transfers.",
            "compare" => "Compare reported metadata for the models you selected.",
            _ => "Find and manage models."
        });
        _bindings.Set("models.count", $"{_visibleModels.Length} model{(_visibleModels.Length == 1 ? "" : "s")}");
        _bindings.Set("models.compareCount", SelectedModels().Length == 0 ? "Compare models" : $"Compare ({SelectedModels().Length})");
        _bindings.Set("models.canChoose", _openModelPicker is not null && _active && !_disposed && !_work.IsRetiring);
        _bindings.Set("models.pickerReason", _openModelPicker is null ? "The shared model picker is unavailable in this view." : "Choose the model for the intended role in the shared picker.");
        _bindings.Set("models.backVisible", _view is "details" or "local-details" or "compare" or "catalogue");
        _bindings.Set("models.hasInspected", _inspectedModel is not null);
        _bindings.Set("model.name", _inspectedModel?.DisplayName ?? _inspectedModel?.Name ?? "Model no longer present");
        _bindings.Set("model.metadata", _inspectedModel is { } inspected ? Describe(inspected) : "Refresh discovery to inspect a current catalogue entry.");
        _bindings.Set("model.fit", _inspectedModel?.HardwareFitEstimate ?? "Not checked by this catalogue. Inspect the local runtime before loading.");
        _bindings.Set("model.capabilities", _inspectedModel?.Capabilities is { Count: > 0 } capabilities
            ? string.Join(" · ", capabilities.Order(StringComparer.OrdinalIgnoreCase)) : "The provider has not reported capabilities for this model.");
        _bindings.Set("models.providerHeading", _view == "mine" ? _dulche is null ? "Installed models" : "Other installed models" : "Model catalogue");
        RefreshModelPreferences();
    }

    private void OnDiscoverSizeChanged(object? sender, SizeChangedEventArgs args) { if (args.WidthChanged) ApplyDiscoverWidth(); }
    private void ApplyDiscoverWidth()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : 1000;
        var narrow = width < 620;
        var details = _view is "details" or "local-details";
        var padding = details ? 16d : narrow ? 18d : 32d;
        _bindings.Set("models.padding", new Thickness(padding, details ? 16 : narrow ? 20 : 28, padding, details ? 16 : 24));
        _bindings.Set("models.spacing", details ? 8d : 16d);
        _bindings.Set("models.headingSize", narrow ? 28d : 36d);
        _bindings.Set("models.headerColumns", narrow ? "1fr" : "1fr Auto");
        _bindings.Set("models.headerRows", narrow ? "Auto Auto" : "Auto");
        _bindings.Set("models.headerActionColumn", narrow ? 0 : 1);
        _bindings.Set("models.headerActionRow", narrow ? 1 : 0);
        var stackedSearch = width < 760;
        _bindings.Set("models.searchColumns", stackedSearch ? "1fr" : "1fr Auto");
        _bindings.Set("models.searchRows", stackedSearch ? "Auto Auto" : "Auto");
        _bindings.Set("models.searchActionColumn", stackedSearch ? 0 : 1);
        _bindings.Set("models.searchActionRow", stackedSearch ? 1 : 0);
        _bindings.Set("models.cardColumns", narrow ? "1fr" : "1fr Auto");
        _bindings.Set("models.catalogueCardColumns", narrow ? "48 1fr" : "48 1fr Auto");
        _bindings.Set("models.catalogueActionColumn", narrow ? 1 : 2);
        _bindings.Set("models.cardRows", narrow ? "Auto Auto" : "Auto");
        _bindings.Set("models.cardActionColumn", narrow ? 0 : 1);
        _bindings.Set("models.cardActionRow", narrow ? 1 : 0);
    }

    private void OnDiscoverAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        StopDiscoverThemeObservation();
        if (Content is not Control surface) return;
        var observer = new DiscoverThemeObserver(ApplyDiscoverTheme);
        foreach (var key in new[] { "CuiEffectiveThemeName", "CuiControlRadius" })
            _themeObservations.Add(surface.GetResourceObservable(key).Subscribe(observer));
        ApplyDiscoverTheme();
    }

    private void OnDiscoverDetached(object? sender, VisualTreeAttachmentEventArgs args) => StopDiscoverThemeObservation();
    private void StopDiscoverThemeObservation()
    { foreach (var observation in _themeObservations) observation.Dispose(); _themeObservations.Clear(); }

    private void ApplyDiscoverTheme()
    {
        if (_disposed || Content is not Control surface) return;
        surface.TryFindResource("CuiEffectiveThemeName", out var name);
        surface.TryFindResource("CuiControlRadius", out var control);
        _bindings.Set("models.navigationRadius", CuiThemeCatalog.FindBuiltIn(name as string)?.Theme == CuiTheme.Glow
            ? new CornerRadius(999) : control is CornerRadius radius ? radius : new CornerRadius(12));
    }

    private sealed class DiscoverThemeObserver(Action changed) : IObserver<object?>
    {
        public void OnNext(object? value) => changed();
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }

    private static string ModelMonogram(HomeDiscoverModel model)
    {
        var name = (model.DisplayName ?? model.Name).Trim();
        return name.Length == 0 ? "?" : StringInfo.GetNextTextElement(name).ToUpperInvariant();
    }

    private static string LocationLabel(HomeDiscoverLocality value) => value switch
    { HomeDiscoverLocality.Local => "On this device", HomeDiscoverLocality.Cloud => "Cloud", HomeDiscoverLocality.Remote => "Remote device", _ => "Location unverified" };
    private static string AvailabilityLabel(HomeDiscoverInstallState value) => value switch
    { HomeDiscoverInstallState.Installed => "Installed", HomeDiscoverInstallState.Available => "Available", HomeDiscoverInstallState.Pulling => "Downloading", HomeDiscoverInstallState.Connecting => "Connecting", HomeDiscoverInstallState.Connected => "Provider connected", HomeDiscoverInstallState.Failed => "Unavailable", _ => "Installation unverified" };
    private static string ConnectionLabel(HomeDiscoverConnectionState value) => value switch
    { HomeDiscoverConnectionState.Connected => "Provider reachable", HomeDiscoverConnectionState.Disconnected => "Provider disconnected", HomeDiscoverConnectionState.NeedsAuthentication => "Sign-in required", HomeDiscoverConnectionState.Unavailable => "Provider unavailable", _ => "Connection unverified" };
}
