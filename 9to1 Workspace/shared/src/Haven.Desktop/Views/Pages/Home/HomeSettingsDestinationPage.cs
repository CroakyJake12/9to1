using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application.Haven;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Only an explicit false current-owner predicate produces this expected denial.
/// Actual Home, storage and caller faults keep their original cause and Task status.</summary>
internal sealed class HomeSettingsAuthorityRefusal(string message) : Exception(message);

/// <summary>The same canonical Home Appearance fragment and native preference owner used by
/// standalone Home. This tab borrows the source/context; it never disposes either or owns a file.</summary>
internal sealed class HomeSettingsDestinationPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly NativeHomeThemePreferences _source;
    private readonly NativeHomeThemeCreator _creator;
    private BrowsePageCommandSlot? _pageCommands;
    private BrowsePageCommand? _saveTheme;
    private readonly NativeHomeAccentEditor _accentEditor;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly CuiControlLoader _loader = new(HomeAppsCuiDocument.CreateControlRegistry());
    private readonly CuiViewModel _bindings = new();
    private readonly DesktopOriginalWorkLifetime _work;
    private sealed record CategoryRow(string Key, string? Parent, string Label, string Subtitle,
        string? Icon, bool Expanded, bool Selected);
    private CategoryRow[] _categoryRows = [];
    private readonly Dictionary<string, bool> _expanded = new();
    private readonly Dictionary<string, bool> _searchExpanded = new();
    private string _typeahead = string.Empty;
    private long _lastType;
    private IReadOnlyList<HomeSettingsCatalog.Link> _links = [];
    private IReadOnlyDictionary<string, HomeAppItem> _nativeApps = new Dictionary<string, HomeAppItem>();
    private readonly TextBox _search = new();
    private string _category = "theme";
    private string _initialCategory = "theme";
    private string? _initialAnchor;
    private bool _initialAnchorPending;
    private bool _openInitialCreator;
    private bool _initialDirectoryReveal;
    private bool? _narrowLayout;
    private long _directoryRevealVersion;
    private string _query = string.Empty;
    private Avalonia.Vector _offsetBeforeSearch;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private bool _active, _unavailable, _stopped;
    private string? _creatorReturnCategory;
    private int _busyCount;
    private bool IsBusy => _busyCount != 0;
    private long _generation;
    private string? _status;
    internal sealed record AdmissionObservation(Task? Source, Exception Cause, CancellationToken RequestedToken);
    private readonly List<AdmissionObservation> _admissionObservations = [];
    internal IReadOnlyList<AdmissionObservation> AdmissionObservations => _admissionObservations.ToArray();

    internal HomeSettingsDestinationPage(NativeHomeThemePreferences source, Func<CancellationToken, Task> admit)
    {
        Dispatcher.UIThread.VerifyAccess();
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _work = new(StopAsync, CleanupAsync);
        _loader.SetThemePreferenceContext(source.Context);
        _loader.SetBindingContext(_bindings); _loader.SetActionDispatcher(this);
        _loader.BindAuthoredControl("home-settings-search", _ => _search);
        _loader.BindAuthoredControl("home-settings-directory-item", _ =>
        {
            var button = new Button();
            button.AttachedToVisualTree += OnDirectoryItemAttached;
            return button;
        });
        NativeHomeThemePreferences.BindAppearanceLayout(_loader, _bindings);
        _creator = new(source, _bindings, () => IsActionAvailable("ApplyHomeAppearance") == true,
            (change, token) => DispatchAsync("ApplyHomeAppearance", change, token).AsTask());
        _creator.Register(_loader);
        _accentEditor = new(source, _bindings,
            (change, token) => DispatchAsync("ApplyHomeAppearance", change, token).AsTask());
        _accentEditor.Register(_loader);
        ResetExpansion();
        SetBrowseStatus(string.Empty);
        PublishCategory();
        ApplyResponsiveLayout(double.PositiveInfinity);
        var loaded = _loader.TryLoad(LoadSettingsDocument());
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Home Settings could not be rendered: " + string.Join("; ",
                loaded.Diagnostics.Select(item => item.Code + " " + item.Message)));
        _loader.WireBindings(loaded.Root); Content = loaded.Root;
        PublishCategory();
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        AddHandler(KeyDownEvent, OnCategoryKeyDown, RoutingStrategies.Tunnel);
        _search.TextChanged += (_, _) => FilterCategories();
        _bindings.PropertyChanged += NativeHomeThemePreferences.UpdateDisplayReadout;
        _bindings.PropertyChanged += OnCreatorOpenChanged;
        _source.Changed += OnSourceChanged;
        RefreshBindings();
    }

    internal bool IsAlive => !_work.IsRetiring;
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal Task WhenActionsIdleAsync() => Task.WhenAll(_loader.WhenActionsIdleAsync(), _creator.WhenIdleAsync(),
        _accentEditor.WhenIdleAsync());
    internal NativeHomeThemePreferences.SettingsWriteOutcome? LastWrite { get; private set; }
    internal Func<bool, CancellationToken, Task>? NavigateHomeAsync { get; set; }
    internal Func<AppContentAddress, CancellationToken, Task<bool>>? NavigateContentAsync { get; set; }
    internal Func<AppContentAddress, bool>? ContentAvailable { get; set; }
    internal bool NewTabCustomisationAvailable { get; set; }
    internal Func<ModeDefinition, bool>? NativeAppAvailable { get; set; }
    internal Func<HomeSettingsOwner, CancellationToken, Task<HavenSharedOpenResult>>? OpenOwnerAsync { get; set; }
    private bool CreatorOpen => _bindings.Get("HomeThemeCreatorOpen") is true;
    internal string CategoryTitle => _category switch
    {
        "theme" when CreatorOpen => "Create theme",
        "theme" => "Theme and accent preferences",
        _ => HomeSettingsCatalog.Get(_category).Label
    };

    private void OnCreatorOpenChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "HomeThemeCreatorOpen") return;
        if (!CreatorOpen && _creatorReturnCategory is { } origin)
        {
            _creatorReturnCategory = null;
            if (_active && IsAlive && _category == "theme")
            {
                SelectCategory(origin, scroll: origin != "theme-options");
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_active || !IsAlive || _category != origin || _pageCommands is { IsHosted: true, IsSelected: false }) return;
                    if (origin == "theme-options")
                        this.GetVisualDescendants().OfType<Button>()
                            .SingleOrDefault(item => item.Name == "home-theme-options-create")?.Focus(NavigationMethod.Tab);
                    else FocusDirectoryItem(origin);
                }, DispatcherPriority.Loaded);
                return;
            }
        }
        if (_active && IsAlive && _category == "theme") CategoryChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetInitialRoute(AppContentAddress address)
    {
        if (address.ResourceSegments is not ["settings", var route]) return;
        _initialCategory = HomeSettingsCatalog.ResolveRoute(route)
            ?? throw new InvalidOperationException("The requested Settings destination was not admitted.");
        _initialAnchor = route == "appearance-palette" ? "home-accent-settings" : null;
        _openInitialCreator = route == "custom-theme";
    }

    internal void SetNativeApps(IEnumerable<ModeDefinition> modes)
    {
        _nativeApps = modes.Where(mode => NativeAppAvailable?.Invoke(mode) == true)
            .ToDictionary(mode => mode.Key, mode => new HomeAppItem(mode.Key, mode.Name, mode.Description, mode.IconKey,
                CanonicalId: mode.Id, Source: mode.Source), StringComparer.OrdinalIgnoreCase);
        PublishCategory();
    }
    internal event EventHandler? CategoryChanged;

    internal void AttachPageCommands(BrowsePageCommandSlot slot)
    {
        _pageCommands = slot;
        slot.HostChanged += OnCommandHostChanged;
        _creator.DraftChanged += OnDraftChanged;
        _creator.SetShellHosted(slot.IsHosted, slot.IsSelected);
        UpdatePageCommand();
    }

    private void OnCommandHostChanged(object? sender, EventArgs args) =>
        _creator.SetShellHosted(_pageCommands!.IsHosted, _pageCommands.IsSelected);
    private void OnDraftChanged(object? sender, EventArgs args) => UpdatePageCommand();
    private void UpdatePageCommand()
    {
        if (_pageCommands is null) return;
        if (_active && IsAlive && _category == "theme" && CreatorOpen)
            _saveTheme ??= new("Save theme", _creator.SaveFromShellAsync);
        else _saveTheme = null;
        _pageCommands.Update(_saveTheme, _saveTheme is not null && _creator.CanSave);
    }

    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_active)
        {
            _query = string.Empty;
            _search.Text = string.Empty;
            ResetExpansion();
            SetBrowseStatus(string.Empty);
            _initialAnchorPending = _initialAnchor is not null;
            _initialDirectoryReveal = true;
            SelectCategory(_initialCategory, scroll: false);
        }
        _active = true;
        return _work.RunAsync(original => RefreshAsync(original, token), actual => _initialization ??= actual);
    }
    internal void Deactivate()
    { Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation; UpdatePageCommand(); }

    private void OpenThemeCreator()
    {
        if (_category != "theme")
        {
            _creatorReturnCategory = _category;
            _search.Text = string.Empty;
            SelectCategory("theme", scroll: false, notify: CreatorOpen);
        }
        _creator.Open();
    }

    internal void FocusCategory() => FocusDirectoryItem(_category);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        if (_openInitialCreator && _active && _category == _initialCategory)
        {
            _openInitialCreator = false;
            if (IsActionAvailable("SettingsOptionsCreateTheme") == true) OpenThemeCreator();
        }
        if (_active && _initialDirectoryReveal)
        {
            _initialDirectoryReveal = false;
            RevealSelectedDirectoryItem();
        }
        if (_active && _category == _initialCategory && _initialAnchorPending && _initialAnchor is { } anchor)
        {
            _initialAnchorPending = false;
            SelectCategory(_category, anchor);
        }
    }

    public bool? IsActionAvailable(string command) => IsAlive && _active && (command switch
    {
        "SettingsBack" or "SettingsOpenDashboard" => NavigateHomeAsync is not null,
        "SettingsDirectory" or "SettingsDirectoryLink" or "SettingsTheme" or "SettingsThemeOptions" or "SettingsLayout" or "SettingsAccessibility" => true,
        "SettingsClearSearch" => _query.Length != 0,
        "SettingsOptionsAccent" or "SettingsOptionsAccessibility" => _category == "theme-options",
        "SettingsOptionsCreateTheme" => _category == "theme-options" && !IsBusy && !_unavailable,
        "SettingsOpenBrowseGeneral" => NavigateContentAsync is not null && _category == "haven-general",
        "SettingsOpenBrowseBookmarks" => NavigateContentAsync is not null && _category == "haven-bookmarks",
        "SettingsOpenBrowseHistory" => NavigateContentAsync is not null && _category == "haven-history",
        "ApplyHomeAppearance" or "RefreshHomeAppearance" or "HomeCreateTheme" => !IsBusy && !_unavailable,
        _ => false
    });

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_work.IsRetiring) return ValueTask.CompletedTask; // Already queued presentation acquires no new business work.
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Home Settings action is unavailable.");
        switch (command)
        {
            case "SettingsBack":
            case "SettingsOpenDashboard":
                return new(NavigateHomeAsync!(command == "SettingsOpenDashboard", cancellationToken));
            case "SettingsClearSearch":
                ClearSearch();
                return ValueTask.CompletedTask;
            case "SettingsDirectory":
                if (parameter is string selected && _categoryRows.FirstOrDefault(row => row.Key == selected) is { } row)
                {
                    if (row.Parent is null) SetGroupExpanded(row.Key, !row.Expanded);
                    else SelectCategory(row.Key);
                }
                return ValueTask.CompletedTask;
            case "SettingsDirectoryLink":
                return parameter is HomeSettingsCatalog.Link link && _links.Contains(link) && LinkIsAvailable(link)
                    ? OpenDirectoryLinkAsync(link, cancellationToken) : ValueTask.CompletedTask;
            case "SettingsTheme":
            case "SettingsLayout":
            case "SettingsAccessibility":
                SelectCategory(command["Settings".Length..].ToLowerInvariant());
                return ValueTask.CompletedTask;
            case "SettingsThemeOptions":
                SelectCategory("theme-options");
                return ValueTask.CompletedTask;
            case "SettingsOptionsAccent":
                OpenRelatedCategory("theme", "home-accent-settings");
                return ValueTask.CompletedTask;
            case "SettingsOptionsAccessibility":
                OpenRelatedCategory("accessibility");
                return ValueTask.CompletedTask;
            case "SettingsOptionsCreateTheme":
            case "HomeCreateTheme":
                OpenThemeCreator();
                return ValueTask.CompletedTask;
            case "SettingsOpenBrowseGeneral":
                return OpenDirectoryLinkAsync(BrowseLink(BrowserNativePage.Settings), cancellationToken);
            case "SettingsOpenBrowseBookmarks":
                return OpenDirectoryLinkAsync(BrowseLink(BrowserNativePage.Bookmarks), cancellationToken);
            case "SettingsOpenBrowseHistory":
                return OpenDirectoryLinkAsync(BrowseLink(BrowserNativePage.History), cancellationToken);
        }
        parameter = NativeHomeThemePreferences.CaptureDisplayDraft(parameter, _bindings);
        return new(_work.RunAsync(async original =>
        {
            if (command == "RefreshHomeAppearance")
            {
                NativeHomeThemePreferences.ReloadDisplayDraft(_bindings);
                await RefreshAsync(original, cancellationToken);
                return;
            }
            var generation = _generation;
            original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
            cancellationToken.ThrowIfCancellationRequested();
            NativeHomeThemePreferences.SettingsWriteOutcome? write = null;
            _busyCount++;
            try
            {
                RefreshBindings();

                if (!await original.AwaitAsync(IsAdmittedAsync(original.Token)))
                { Deny(original, false); return; }
                if (!original.IsAcceptedPublicationCurrent) return;
                cancellationToken.ThrowIfCancellationRequested();
                write = LastWrite = _source.CreateSettingsWriteOutcome();
                // The preference owner retains its actual business lifetime. A tab's private
                // retirement token is not permission to cancel a global atomic preference write.
                var receipt = await original.AwaitAsync(write.ApplyAsync(parameter));
                if (!original.IsAcceptedPublicationCurrent) return;
                if (!await original.AwaitAsync(IsAdmittedAsync(original.Token)))
                { Deny(original, receipt.Succeeded); return; }
                original.RunAcceptedPublicationCallback(() =>
                {
                    _status = receipt.Succeeded ? "Appearance saved." : receipt.Diagnostic ??
                        "Appearance changed. Refresh before choosing again.";
                    RefreshBindings();
                });
            }
            catch (Exception cause)
            {
                if (write?.RetainPostCommitFailure(null, cause) == true)
                {
                    // AwaitAsync already retains an actual failed source. Preserve the confirmed
                    // save outcome independently; no UI observer can replay Set or invent failure.
                    original.Retain(cause);
                    Deny(original, true);
                    return;
                }
                if (original.IsAcceptedPublicationCurrent) Deny(original, false);
                throw;
            }
            finally
            {
                _busyCount--;
                if (original.IsAcceptedPublicationCurrent)
                    try { original.RunAcceptedPublicationCallback(RefreshBindings); }
                    catch (Exception cause)
                    {
                        if (write?.RetainPostCommitFailure(null, cause) != true) throw;
                        original.Retain(cause); _unavailable = true;
                        _status = NativeHomeThemePreferences.SettingsWriteOutcome.SavedObservationDiagnostic;
                    }
            }
        }));
    }

    private static HomeSettingsCatalog.Link BrowseLink(BrowserNativePage page) =>
        new(string.Empty, string.Empty, string.Empty, Address: AppContentAddress.Parse(BrowserNativePages.Address(page).AbsoluteUri));

    private bool LinkIsAvailable(HomeSettingsCatalog.Link link) =>
        link.Category is not null
        || link.Owner.HasValue && OpenOwnerAsync is not null
        || link.Address is { } address && NavigateContentAsync is not null && ContentAvailable?.Invoke(address) != false
           && (!HomeSettingsCatalog.IsNewTabCustomisationAddress(address) || NewTabCustomisationAvailable)
        || link.App is { } app && NavigateContentAsync is not null && _nativeApps.ContainsKey(app);

    private ValueTask OpenDirectoryLinkAsync(HomeSettingsCatalog.Link link, CancellationToken token)
    {
        if (link.Category is { } related)
        {
            OpenRelatedCategory(related);
            return ValueTask.CompletedTask;
        }

        var generation = _generation;
        var category = _category;
        return new(_work.RunAsync(async original =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, token);
            original.BindPublicationGuard(() => _active && IsAlive && generation == _generation && category == _category);
            linked.Token.ThrowIfCancellationRequested();
            var admitted = await original.AwaitAsync(IsAdmittedAsync(linked.Token));
            if (!original.IsAcceptedPublicationCurrent) return;
            if (!admitted)
            {
                original.RunAcceptedPublicationCallback(() => SetBrowseStatus("Reopen Settings for the current profile."));
                return;
            }
            linked.Token.ThrowIfCancellationRequested();
            var status = string.Empty;
            if (link.Owner is { } owner && OpenOwnerAsync is { } open)
            {
                var receipt = await original.AwaitAsync(open(owner, linked.Token));
                if (!receipt.Opened) status = receipt.Message ?? "This setting is currently unavailable.";
            }
            else
            {
                var address = link.Address;
                if (link.App is { } app && _nativeApps.ContainsKey(app)) address = HomeLaunchRouteResolver.Resolve(app);
                if (address is null || NavigateContentAsync is not { } navigate ||
                    !await original.AwaitAsync(navigate(address, linked.Token)))
                    status = "Haven could not open this destination. Try again.";
            }
            original.RunAcceptedPublicationCallback(() => SetBrowseStatus(status));
        }));
    }

    private void SetBrowseStatus(string status)
    {
        _bindings.Set("SettingsBrowseStatus", status);
        _bindings.Set("SettingsHasBrowseStatus", status.Length != 0);
    }

    private static CuiDocument LoadSettingsDocument()
    {
        using var stream = typeof(HomeSettingsDestinationPage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Settings.cui")
            ?? throw new InvalidDataException("The Home Settings document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "HomeSettings.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home Settings document is invalid: " + string.Join("; ",
                parser.Diagnostics.Diagnostics.Select(item => item.Code + " " + item.Message)));
        var home = HavenOS.Home.HomeCuiSurface.LoadDefault().Document;
        var appearance = home.Components
            .SelectMany(component => component.DescendantsAndSelf())
            .Single(component => component.AuthoredId == "home-appearance-settings");
        return document with
        {
            Actions = home.Actions.Concat(document.Actions).ToDictionary(action => action.Key, action => action.Value, StringComparer.Ordinal),
            Components = document.Components.Select(IncludeAppearance).ToArray()
        };

        CuiComponent IncludeAppearance(CuiComponent component) => new(component.Type, component.Name,
            component.Classes, component.Properties, component.Actions,
            component.AuthoredId == "home-settings-appearance-host" ? [appearance] : component.Children.Select(IncludeAppearance).ToArray(),
            component.Condition, component.List, component.Span, component.DefaultTheme, component.Repeat,
            component.ElseChildren, component.Groups, component.IsDefinition, component.PropertyRegion)
        { Text = component.Text, TextParts = component.TextParts };
    }

    private void ApplyResponsiveLayout(double width)
    {
        var narrow = width < 790;
        var orientationChanged = _narrowLayout.HasValue && _narrowLayout.Value != narrow;
        _narrowLayout = narrow;
        _bindings.Set("SettingsColumns", narrow ? "1fr" : width >= 1120 ? "276 1fr" : "232 1fr");
        _bindings.Set("SettingsRows", narrow ? "Auto 1fr" : "1fr");
        _bindings.Set("SettingsContentRow", narrow ? 1 : 0);
        _bindings.Set("SettingsContentColumn", narrow ? 0 : 1);
        _bindings.Set("SettingsNavHeight", narrow ? 176d : double.PositiveInfinity);
        _bindings.Set("SettingsOptionsPadding", narrow ? "14 20 14 24" : "48 25 36 32");
        _bindings.Set("SettingsOptionsRows", narrow ? "Auto Auto" : "Auto");
        _bindings.Set("SettingsOptionsColumns", narrow ? "1fr" : "1fr Auto");
        _bindings.Set("SettingsOptionsControlRow", narrow ? 1 : 0);
        _bindings.Set("SettingsOptionsControlColumn", narrow ? 0 : 1);
        if (orientationChanged) RevealSelectedDirectoryItem();
    }

    private bool Matches(string terms) => terms.Contains(_query, StringComparison.OrdinalIgnoreCase);

    private ScrollViewer? CategoryScroll() => this.GetVisualDescendants().OfType<CuiContainerControl>()
        .FirstOrDefault(control => control.Name == "home-settings-categories")?.Child as ScrollViewer;

    private void ClearSearch()
    {
        _search.Text = string.Empty;
        _search.Focus(NavigationMethod.Tab);
    }

    private void FilterCategories()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        if (_query == query) return;
        if (_query.Length == 0) _offsetBeforeSearch = CategoryScroll()?.Offset ?? default;
        _query = query;
        _searchExpanded.Clear();
        PublishCategory();
        Dispatcher.UIThread.Post(() =>
        {
            if (query == _query && CategoryScroll() is { } scroll)
                scroll.Offset = query.Length == 0 ? _offsetBeforeSearch : default;
        }, DispatcherPriority.Loaded);
    }

    private string[] SearchTerms() =>
        _query.Split([' ', '&', '/', '-', ',', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IncludesTerms(string text, string[] terms) =>
        terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string SearchText(HomeSettingsCatalog.Destination item) => $"{item.Label} {item.Keywords} {item.Description}";

    private void PublishCategory()
    {
        var rows = new List<CategoryRow>();
        var terms = SearchTerms();
        bool Includes(string text) => IncludesTerms(text, terms);

        foreach (var group in HomeSettingsCatalog.Groups)
        {
            var sections = new List<CategoryRow>();
            var groupMatches = Includes($"{group.Label} {group.Subtitle}");
            foreach (var section in HomeSettingsCatalog.Children(group.Key))
            {
                var includeSectionChildren = groupMatches || Includes(section.Label);
                var sectionMatches = includeSectionChildren || Includes(SearchText(section));
                var children = HomeSettingsCatalog.Children(section.Key)
                    .Where(child => includeSectionChildren || Includes($"{section.Label} {SearchText(child)}")).ToArray();
                if (!sectionMatches && children.Length == 0) continue;
                sections.Add(DestinationRow(section));
                sections.AddRange(children.Select(DestinationRow));
            }
            if (sections.Count == 0) continue;
            var expanded = IsGroupExpanded(group.Key);
            rows.Add(new(group.Key, null, group.Label, group.Subtitle, null, expanded, false));
            if (expanded) rows.AddRange(sections);
        }
        _categoryRows = rows.ToArray();
        _bindings.Set("SettingsDirectoryRows", _categoryRows.Select(row => new Dictionary<string, object?>
        {
            ["Key"] = row.Key, ["Label"] = row.Label, ["Subtitle"] = row.Subtitle, ["Icon"] = row.Icon,
            ["HasIcon"] = row.Icon is not null, ["IsGroup"] = row.Parent is null, ["Selected"] = row.Selected,
            ["AccessibleName"] = row.Parent is null ? $"{row.Label}, {row.Subtitle}" : row.Label,
            ["Height"] = row.Parent is null ? 52d : row.Icon is null ? 33d : 39d,
            ["Weight"] = row.Parent is null || row.Selected ? 750 : row.Icon is null ? 600 : 650,
            ["Indent"] = row.Parent is not null && row.Icon is null ? "24 0 0 0" : "0 0 0 0",
            ["Connector"] = row.Parent is not null && row.Icon is null ? "1 0 0 0" : "0",
            ["Chevron"] = row.Expanded ? "icon:chevron-down" : "icon:chevron-right"
        }).ToArray());
        _bindings.Set("SettingsHasQuery", _query.Length != 0);
        _bindings.Set("SettingsNoMatches", rows.Count == 0);
        _bindings.Set("SettingsShowsAppearance", _category is "theme" or "accessibility");
        _bindings.Set("SettingsLayoutSelected", _category == "layout");
        _bindings.Set("SettingsAppearanceSelected", _category == "appearance");
        _bindings.Set("SettingsThemeOptionsSelected", _category == "theme-options");
        _bindings.Set("SettingsHavenGeneralSelected", _category == "haven-general" && NavigateContentAsync is not null);
        _bindings.Set("SettingsHavenBookmarksSelected", _category == "haven-bookmarks" && NavigateContentAsync is not null);
        _bindings.Set("SettingsHavenHistorySelected", _category == "haven-history" && NavigateContentAsync is not null);
        _bindings.Set("SettingsCatalogSelected", _category is not ("appearance" or "theme" or "theme-options" or "layout" or "accessibility"));

        var item = HomeSettingsCatalog.Get(_category);
        var scope = HomeSettingsCatalog.GroupFor(item).Scope;
        AutomationProperties.SetName(this, $"{scope} settings, {item.Label}");
        _bindings.Set("SettingsCatalogScope", scope);
        _bindings.Set("SettingsCatalogTitle", item.Label);
        _bindings.Set("SettingsCatalogDescription", item.Description);
        var globalGeneral = item.Key == "global-general";
        _bindings.Set("SettingsGlobalGeneralSelected", globalGeneral);
        _bindings.Set("SettingsShowCatalogDescription", !globalGeneral);
        _bindings.Set("SettingsCatalogRowHeight", globalGeneral ? 76d : 64d);
        _bindings.Set("SettingsCatalogAvailability", item.Availability);
        _bindings.Set("SettingsHasCatalogAvailability", item.Availability is not null);
        _links = HomeSettingsCatalog.Children(item.Key).Select(child => HomeSettingsCatalog.Related(child.Key))
            .Concat(item.Actions ?? []).ToArray();
        _bindings.Set("SettingsCatalogLinks", _links.Select((link, index) =>
        {
            var related = link.Category is { } key ? HomeSettingsCatalog.Get(key) : null;
            var label = related?.Label ?? link.Label;
            var available = LinkIsAvailable(link);
            return new Dictionary<string, object?>
            {
                ["Key"] = $"{_category}-{index}", ["Target"] = link, ["Label"] = label, ["Action"] = link.Action,
                ["Description"] = link.Description.Length > 0 ? link.Description : related?.Description,
                ["AccessibleName"] = $"{link.Action} {label}", ["Available"] = available, ["Unavailable"] = !available
            };
        }).ToArray());
        Dispatcher.UIThread.Post(UpdateDirectoryAutomation, DispatcherPriority.Loaded);

        CategoryRow DestinationRow(HomeSettingsCatalog.Destination entry)
        {
            var isSection = !HomeSettingsCatalog.TryGet(entry.Parent, out _);
            var icon = entry.App is { } app && _nativeApps.TryGetValue(app, out var registered)
                ? HomeAppBrandingIcons.SourceFor(registered.IconKey, registered.CanonicalId, registered.Source)
                : "icon:" + entry.Icon;
            return new(entry.Key, entry.Parent, entry.Label, string.Empty, isSection ? icon : null, false, entry.Key == _category);
        }
    }

    internal static string DirectoryItemId(string key) => $"id:home-settings-directory|key:{key.Length}:{key}/id:home-settings-directory-item";

    private Dictionary<string, Button> DirectoryButtons()
    {
        var available = this.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Name == "home-settings-directory-item")
            .ToDictionary(button => CuiRuntimeIdentity.GetStableId(button)!);
        return _categoryRows.Where(row => available.ContainsKey(DirectoryItemId(row.Key)))
            .ToDictionary(row => row.Key, row => available[DirectoryItemId(row.Key)]);
    }

    private void OnDirectoryItemAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is not Button button || !IsAlive) return;
        var id = CuiRuntimeIdentity.GetStableId(button);
        var row = _categoryRows.FirstOrDefault(row => DirectoryItemId(row.Key) == id);
        if (row is not null) SetDirectoryAutomation(button, row);
    }

    private void UpdateDirectoryAutomation()
    {
        if (!IsAlive) return;
        var controls = DirectoryButtons();
        foreach (var row in _categoryRows)
            if (controls.TryGetValue(row.Key, out var button))
                SetDirectoryAutomation(button, row);
    }

    private static void SetDirectoryAutomation(Button button, CategoryRow row)
    {
        AutomationProperties.SetAutomationId(button, "home-settings-" + row.Key);
        AutomationProperties.SetItemStatus(button, row.Parent is null
            ? row.Expanded ? "Expanded" : "Collapsed"
            : row.Selected ? "Current page" : null);
    }

    private void RevealSelectedDirectoryItem()
    {
        var version = ++_directoryRevealVersion;
        var category = _category;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active || !IsAlive || !this.IsAttachedToVisualTree() || version != _directoryRevealVersion ||
                _category != category) return;
            if (DirectoryButtons().TryGetValue(category, out var button)) button.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void FocusDirectoryItem(string key)
    {
        if (!DirectoryButtons().TryGetValue(key, out var button)) return;
        button.Focus(NavigationMethod.Directional);
        button.BringIntoView();
    }

    private void OpenRelatedCategory(string category, string? anchor = null)
    {
        _search.Text = string.Empty;
        SelectCategory(category, anchor);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active || !IsAlive || _category != category) return;
            FocusDirectoryItem(category);
        }, DispatcherPriority.Loaded);
    }

    private void SelectCategory(string category, string? requestedAnchor = null, bool scroll = true, bool notify = true)
    {
        var changed = _category != category;
        _category = category;
        _creator.SetCategoryPresentation(category == "theme");
        UpdatePageCommand();
        var item = HomeSettingsCatalog.Get(category);
        _expanded[HomeSettingsCatalog.GroupFor(item).Key] = true;
        if (changed) SetBrowseStatus(string.Empty);
        PublishCategory();
        if (changed && notify) CategoryChanged?.Invoke(this, EventArgs.Empty);
        if (!scroll || category is not ("theme" or "accessibility")) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active || !IsAlive || _category != category || !this.IsAttachedToVisualTree()) return;
            var appearance = this.GetVisualDescendants().OfType<CuiContainerControl>()
                .SingleOrDefault(control => control.Name == "home-appearance-settings");
            if (appearance is null) return;
            var anchor = requestedAnchor ?? (category == "accessibility" ? "home-appearance-accessibility"
                : _query.Length == 0 ? null
                : Matches("Accent palette") || Matches("Override app accents") ? "home-accent-settings"
                : Matches("Brightness") || Matches("Colour contrast") ? "home-display-settings" : null);
            if (anchor is null)
                appearance.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToHome();
            else
                appearance.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == anchor)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void OnCategoryKeyDown(object? sender, KeyEventArgs args)
    {
        if (!_active || args.KeyModifiers != KeyModifiers.None) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var controls = DirectoryButtons();
        var visible = _categoryRows.Where(row => controls.ContainsKey(row.Key)).ToArray();
        var index = Array.FindIndex(visible, row => ReferenceEquals(controls[row.Key], focused));
        if (_query.Length > 0 && args.Key == Key.Escape && (focused == _search || index >= 0))
        {
            ClearSearch();
            args.Handled = true;
            return;
        }
        if (focused == _search && args.Key is Key.Down or Key.Enter)
        {
            var results = visible.Where(row => row.Parent is not null).ToArray();
            var exact = results.Where(row => _query.Equals(row.Label, StringComparison.OrdinalIgnoreCase)).ToArray();
            var currentGroup = HomeSettingsCatalog.GroupFor(HomeSettingsCatalog.Get(_category)).Key;
            var terms = SearchTerms();
            var result = exact.FirstOrDefault(row => HomeSettingsCatalog.GroupFor(HomeSettingsCatalog.Get(row.Key)).Key == currentGroup)
                ?? exact.FirstOrDefault()
                ?? results.FirstOrDefault(row => IncludesTerms(SearchText(HomeSettingsCatalog.Get(row.Key)), terms))
                ?? results.FirstOrDefault(row => HomeSettingsCatalog.TryGet(row.Parent!, out _))
                ?? results.FirstOrDefault();
            if (result is null) return;
            args.Handled = true;
            FocusDirectoryItem(result.Key);
            if (args.Key == Key.Enter) controls[result.Key].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }
        if (index < 0) return;
        var current = visible[index];
        if (current.Parent is null &&
            (args.Key == Key.Left && current.Expanded || args.Key == Key.Right && !current.Expanded))
        {
            SetGroupExpanded(current.Key, args.Key == Key.Right);
            args.Handled = true;
            return;
        }
        var next = args.Key switch
        {
            Key.Up => visible[Math.Max(0, index - 1)],
            Key.Down => visible[Math.Min(visible.Length - 1, index + 1)],
            Key.Home => visible[0],
            Key.End => visible[^1],
            Key.Left => visible.FirstOrDefault(row => row.Key == current.Parent),
            Key.Right => visible.FirstOrDefault(row => row.Parent == current.Key),
            _ => FindTypeahead(visible, index, args.KeySymbol)
        };
        if (next is null) return;
        args.Handled = true;
        FocusDirectoryItem(next.Key);
    }

    private CategoryRow? FindTypeahead(CategoryRow[] visible, int index, string? symbol)
    {
        if (symbol is not { Length: 1 } || !char.IsLetterOrDigit(symbol[0])) return null;
        var now = Environment.TickCount64;
        _typeahead = now - _lastType <= 700 ? _typeahead + symbol : symbol;
        _lastType = now;
        var ordered = visible.Skip(index + 1).Concat(visible.Take(index + 1)).ToArray();
        var match = ordered.FirstOrDefault(row => row.Label.StartsWith(_typeahead, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;
        _typeahead = symbol;
        return ordered.FirstOrDefault(row => row.Label.StartsWith(symbol, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsGroupExpanded(string group) =>
        _query.Length == 0 ? _expanded[group] : _searchExpanded.GetValueOrDefault(group, true);

    private void SetGroupExpanded(string group, bool expanded)
    {
        var expansion = _query.Length == 0 ? _expanded : _searchExpanded;
        expansion[group] = expanded;
        PublishCategory();
    }

    private void ResetExpansion()
    {
        foreach (var group in HomeSettingsCatalog.Groups)
            _expanded[group.Key] = group.InitiallyExpanded;
        _searchExpanded.Clear();
    }

    private async Task<bool> IsAdmittedAsync(CancellationToken token)
    {
        // Only the owning producer's explicit denial is a normal unavailable result. Real
        // source/caller cancellation and IO exceptions keep the SAME original failed task.
        Task actual;
        try { actual = _admit(token) ?? throw new InvalidOperationException("The original Home Settings admission returned no Task."); }
        catch (OperationCanceledException cause)
        {
            _admissionObservations.Add(new(null, cause, token));
            throw new AggregateException("The Home Settings authority callback faulted before returning its source.", cause);
        }
        try { await actual; return true; }
        catch (Exception cause)
        {
            // Capture the raw terminal payload once: Task.Exception need not return the same
            // wrapper on repeated reads. Only a sole actual owner refusal is a quiet denial.
            var terminal = actual.IsFaulted ? actual.Exception : null;
            var failures = terminal?.InnerExceptions;
            if (cause is HomeSettingsAuthorityRefusal && failures is { Count: 1 } &&
                failures[0] is HomeSettingsAuthorityRefusal) return false;
            // Await exposes only one cause. A refusal sharing its raw Task with an I/O or
            // cancellation failure must preserve the entire terminal payload.
            var observed = failures is { Count: > 1 } ? terminal! : cause;
            _admissionObservations.Add(new(actual, observed, token));
            if (actual.IsFaulted && (cause is OperationCanceledException || failures is { Count: > 1 }))
                throw new AggregateException("The original Home Settings authority source faulted.", failures!);
            throw;
        }
    }

    private async Task RefreshAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var generation = ++_generation;
        original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
        caller.ThrowIfCancellationRequested();
        _busyCount++;
        try
        {
            RefreshBindings();

            if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
            if (!original.IsAcceptedPublicationCurrent) return;
            // Borrow, do not repoint or dispose, the SAME live source/context used by Home chrome.
            await original.AwaitAsync(_source.RefreshAsync());
            if (!original.IsAcceptedPublicationCurrent) return;
            caller.ThrowIfCancellationRequested();
            if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
            original.RunAcceptedPublicationCallback(() => { _unavailable = false; _status = _source.Diagnostic; });
        }
        catch
        {
            if (original.IsAcceptedPublicationCurrent) Deny(original, false);
            throw;
        }
        finally
        {
            _busyCount--;
            if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings);
        }
    }

    private void Deny(DesktopOriginalWorkLifetime.Original original, bool saved)
    {
        if (!original.IsAcceptedPublicationCurrent) return;
        try
        {
            original.RunAcceptedPublicationCallback(() =>
            {
                _unavailable = true;
                _status = saved ? NativeHomeThemePreferences.SettingsWriteOutcome.SavedObservationDiagnostic
                    : "Appearance is unavailable. Reopen Home for the current profile.";
                RefreshBindings();
            });
        }
        catch (Exception cause) when (saved)
        {
            LastWrite?.RetainPostCommitFailure(null, cause);
            original.Retain(cause); // The confirmed save remains distinguishable from its broken observer.
        }
    }
    private void RefreshBindings()
    {
        UpdatePageCommand();
        Dispatcher.UIThread.VerifyAccess();
        if (_active && !_unavailable)
            _source.ApplyEditorBindings(_bindings);
        else
        {
            _bindings.Set("HomeAppearanceScope", "Applies to this device profile.");
            _bindings.Set("HomeThemeChoices", Array.Empty<object>());
            _bindings.Set("HomeAppearanceChoices", Array.Empty<object>());
            _bindings.Set("HomeModeChoices", Array.Empty<object>());
            _bindings.Set("HomeDisplayTarget", null);
            _bindings.Set("HomeAccessibilityChoices", Array.Empty<object>());
            _bindings.Set("HomeAccentModeChoices", Array.Empty<object>());
            _bindings.Set("HomeGlobalAccentChoices", Array.Empty<object>());
            _bindings.Set("HomeAccentPerApp", false);
            _bindings.Set("HomeAccentEditorTarget", null);
            _bindings.Set("HomeAccentResetTarget", null);
            _bindings.Set("HomeAccentCanReset", false);
        }
        var available = _active && IsAlive && !IsBusy && !_unavailable;
        _bindings.Set("HomeAppearanceAvailable", available);
        _bindings.Set("HomeAppearanceCanRefresh", available);
        _bindings.Set("SettingsHasAppearanceStatus", !string.IsNullOrWhiteSpace(_status));
        if (_status is not null) _bindings.Set("HomeAppearanceStatus", _status);
        _loader.RefreshBindings();
    }

    private void OnSourceChanged(object? sender, EventArgs args)
    {
        if (_work.IsRetiring) return;
        Task actual;
        try
        {
            actual = _work.RunAsync(async original =>
            {
                var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(async () =>
                {
                    if (!_active || IsBusy || _unavailable) return;
                    var generation = _generation;
                    original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
                    if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
                    if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings);
                }).GetTask();
                var callback = await original.AwaitAsync(dispatch).ConfigureAwait(false);
                await original.AwaitAsync(callback).ConfigureAwait(false);
            });
        }
        catch (ObjectDisposedException) when (_work.IsRetiring) { return; }
        ObserveNotification(actual);
    }
    private static async void ObserveNotification(Task actual)
    {
        try { await actual; }
        catch (Exception cause) { System.Diagnostics.Trace.TraceError("Retained Home Appearance notification failed: {0}", cause); }
    }
    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _work.DemandExternalClose(); token.ThrowIfCancellationRequested();
        if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
        return _preparation = _work.RunAsync(_ => Task.FromResult(true));
    }
    internal Task CloseAndDrainAsync() => _work.CloseAndDrainAsync();
    private async Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation;
        _source.Changed -= OnSourceChanged;
        _bindings.PropertyChanged -= OnCreatorOpenChanged;
        if (_pageCommands is { } slot)
        {
            _creator.DraftChanged -= OnDraftChanged;
            slot.HostChanged -= OnCommandHostChanged;
            slot.Update(null, false);
        }
        // Seal page admission first. Let admitted CUI actions acknowledge their same global
        // writes before disposing this loader's caller token. The shared source remains live.
        var failures = new List<Exception>();
        async Task Join(Task actual)
        {
            try { await actual; }
            catch (Exception cause)
            {
                foreach (var error in actual.Exception?.InnerExceptions ?? new[] { cause }.AsEnumerable())
                    if (!failures.Any(known => ReferenceEquals(known, error))) failures.Add(error);
            }
        }
        // A failed action is still a terminal acknowledgement. Join it, retain its causes,
        // then join the loader's actual disposal/late-dispatch receipts as the production host does.
        await Join(_creator.CloseAndDrainAsync());
        await Join(_accentEditor.CloseAndDrainAsync());
        await Join(_loader.WhenActionsIdleAsync());
        var disposed = false;
        try { _loader.Dispose(); disposed = true; } catch (Exception cause) { failures.Add(cause); }
        await Join(_loader.WhenActionsIdleAsync());
        _stopped = disposed;
        if (failures.Count != 0) throw new AggregateException("Original Home Settings actions or loader disposal failed.", failures);
    }
    private Task CleanupAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_stopped) throw new InvalidOperationException("The original Settings actions must settle before detachment.");
        NavigateHomeAsync = null;
        NavigateContentAsync = null;
        ContentAvailable = null;
        NativeAppAvailable = null;
        OpenOwnerAsync = null;
        Content = null; return Task.CompletedTask;
    }
}
