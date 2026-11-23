using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>The same Home views serve Home and New Tab. This is a content page,
/// not another shell or tab/session authority. Services remain owned by composition.</summary>
internal sealed partial class UnifiedHomePage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private static readonly AppContentAddress DashboardAddress = AppContentAddress.Parse("browse://home/dashboard");

    private readonly BrowseAppContentRequest _request;
    private readonly IModeRegistry _modes;
    private readonly Func<NewDashboardPage> _createDashboard;
    private readonly Func<HomeDiscoverPage> _createDiscover;
    private readonly Func<HomeEventsDestinationPage>? _createEvents;
    private readonly Func<HomeSettingsDestinationPage>? _createSettings;
    private readonly Func<HomeLibraryDestinationPage>? _createLibrary;
    private readonly Func<BrowseNavigationIntent, Task, bool> _queue;
    private readonly Func<string, CancellationToken, Task<AppContentAddress>> _prepareAiDraft;
    private readonly bool _aiAvailable;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly CuiControlLoader _loader = new(HomeAppsCuiDocument.CreateControlRegistry());
    private CuiControlLoader _appsLoader = new(HomeAppsCuiDocument.CreateControlRegistry());
    private readonly Border _viewport = new();
    private readonly TextBlock _heading = new(), _status = new();
    private readonly TextBlock _newTabTime = new(), _newTabDate = new();
    private readonly TextBlock _newTabAiUnavailable = new();
    private readonly TextBox _search = new();
    private readonly Button _newTabGo = new(), _newTabAskAi = new();
    private readonly DispatcherTimer? _newTabClock;
    private readonly HomeAppsCuiBindings _appsBindings;
    private readonly HomeRailCuiBindings _railBindings;
    private readonly CuiKeyboardTooltipHost _keyboardTooltips;
    private HomeAppsViewState _appsState = HomeAppsViewState.Empty;
    private IReadOnlyList<HomeAppItem> _appsCatalogue = [];
    private CuiContainerControl? _searchRow;
    private CuiContainerControl? _newTabShortcuts;
    private (string Query, AppContentAddress Address)? _preparedAiDraft;
    private bool _preparingAiDraft;
    private NewDashboardPage? _dashboard;
    private HomeDiscoverPage? _discover;
    private HomeEventsDestinationPage? _events;
    private HomeSettingsDestinationPage? _settings;
    private HomeLibraryDestinationPage? _library;
    private Control? _appsView;
    private string _section;
    private string _settingsReturnSection = "apps";
    private long _selection;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private readonly List<Task> _stopSources = [];
    private bool _stopped;
    private bool _stopping;

    internal UnifiedHomePage(BrowseAppContentRequest request, IModeRegistry modes,
        Func<NewDashboardPage> createDashboard, Func<HomeDiscoverPage> createDiscover,
        Func<BrowseNavigationIntent, Task, bool> queue,
        Func<string, CancellationToken, Task<AppContentAddress>> prepareAiDraft, bool aiAvailable = true,
        Func<HomeEventsDestinationPage>? createEvents = null,
        Func<HomeSettingsDestinationPage>? createSettings = null,
        Func<HomeLibraryDestinationPage>? createLibrary = null,
        NewTabLayoutPreferences? newTabLayout = null, NewTabWidgetSources? newTabWidgets = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _request = request; _modes = modes; _createDashboard = createDashboard;
        _createDiscover = createDiscover; _queue = queue; _createEvents = createEvents; _createSettings = createSettings; _createLibrary = createLibrary; _prepareAiDraft = prepareAiDraft; _aiAvailable = aiAvailable;
        _section = request.Address.Authority switch
        {
            "newtab" => "newtab",
            "home" when HomeSettingsCatalog.IsSettingsAddress(request.Address) => "settings",
            _ when request.Address.ResourceSegments.Count == 1 &&
                request.Address.ResourceSegments[0] is "dashboard" or "models" or "events" or "library" => request.Address.ResourceSegments[0],
            _ => "apps"
        };
        if (_section == "newtab")
        {
            _newTabClock = new DispatcherTimer(DispatcherPriority.Background);
            _newTabClock.Tick += OnNewTabClockTick;
        }
        _work = new(StopAsync, CleanupAsync);
        _appsBindings = new(() => _appsState, DispatchAppsCommandAsync);
        // The apps surface uses a per-page captured registry: its image resolver reconciles each actual
        // rendered brand target against THIS page's bindings, so multiple Home/New Tab pages never
        // register each other's targets. The loader is ( after the bindings exist so the coordinator
        // can be captured. All brand observations are retired before loader teardown (see StopAsync).
        _appsLoader = new(HomeAppsCuiDocument.CreateControlRegistry(_appsBindings));
        _railBindings = new(() => _section);
        using var stream = typeof(UnifiedHomePage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Unified.cui")
            ?? throw new InvalidDataException("The shared Home document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "UnifiedHome.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The shared Home document is invalid. " + string.Join(" ", parser.Diagnostics.Diagnostics));
        _loader.SetActionDispatcher(this);
        _loader.SetBindingContext(_railBindings);
        _loader.BindAuthoredControl("home-search-row", _ => _searchRow = new("Vertical Stack"));
        _loader.BindAuthoredControl("home-content", _ => _viewport);
        _loader.BindAuthoredControl("home-heading", _ => _heading);
        _loader.BindAuthoredControl("home-status", _ => _status);
        _loader.BindAuthoredControl("home-search", _ => _search);
        _loader.BindAuthoredControl("home-submit", _ => _newTabGo);
        _loader.BindAuthoredControl("home-newtab-ask-ai", _ => _newTabAskAi);
        _loader.BindAuthoredControl("home-newtab-ai-unavailable", _ => _newTabAiUnavailable);
        _loader.BindAuthoredControl("home-newtab-time", _ => _newTabTime);
        _loader.BindAuthoredControl("home-newtab-date", _ => _newTabDate);
        _loader.BindAuthoredControl("home-newtab-shortcuts", _ => _newTabShortcuts = new("Wrap"));
        RegisterNewTabCustomisation(newTabLayout);
        RegisterNewTabWidgets(newTabWidgets, newTabLayout?.CanEdit == true);
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The shared Home document could not be rendered. " + string.Join(" ", loaded.Diagnostics));
        _loader.WireBindings(loaded.Root);
        MountNewTabWidgets();
        // Bounded canonical CUI keyboard-focus tooltip capability: authored rail tooltips gain real
        // keyboard-focus visibility (same retained popup/transparency seam) for this Home surface.
        _keyboardTooltips = CuiKeyboardTooltipHost.Attach(loaded.Root);
        Set(_newTabAiUnavailable, "IsVisible", !_aiAvailable);
        RefreshNewTabSearchActions();
        Content = loaded.Root;
        SizeChanged += (_, args) => _railBindings.SetViewportWidth(args.NewSize.Width);
        if (_newTabClock is not null && _newTabShortcuts is not null)
            _newTabShortcuts.SizeChanged += OnNewTabShortcutsSizeChanged;
        _search.TextChanged += OnSearchTextChanged;
        _search.KeyDown += OnSearchKey;
    }

    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal bool IsAlive => !_work.IsRetiring && !_stopping;
    internal string? SettingsTitle => _section == "settings" ? _settings?.CategoryTitle : null;
    internal event EventHandler? SettingsTitleChanged;
    internal Task WhenActionsIdleAsync() => Task.WhenAll(
        _loader.WhenActionsIdleAsync(), _appsLoader.WhenActionsIdleAsync(),
        WhenNewTabCustomisationActionsIdleAsync(), WhenWidgetActionsIdleAsync());

    internal Task InitializeAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _initialization ?? _work.RunAsync(async original =>
        {
            await SelectAsync(_section, original, cancellationToken);
            if (original.IsAcceptedPublicationCurrent && HomeSettingsCatalog.IsNewTabCustomisationAddress(_request.Address) &&
                NewTabCustomisationActionAvailable(NewTabCustomisationPrefix + "open"))
                OpenNewTabCustomisation();
        }, task => _initialization = task);
    }

    public bool? IsActionAvailable(string command) => IsAlive && (
        command.StartsWith(NewTabCustomisationPrefix, StringComparison.Ordinal) ? NewTabCustomisationActionAvailable(command) :
        command.StartsWith(widgetActionPrefix, StringComparison.Ordinal) ? IsNewTabWidgetActionAvailable(command) :
        command == "home.events"
        ? _createEvents is not null : command == "home.settings" ? _createSettings is not null :
        command == "home.library" ? _createLibrary is not null :
        command == "home.ask-ai" ? _aiAvailable && _section == "newtab" : command is
        "home.apps" or "home.dashboard" or "home.models" or "home.newtab" or "home.submit" or
        "home.library" or "home.events" or "home.mesh" or "home.settings" or "home.account" or
        "home.notifications" or "home.open-app" or "home.set-app-query" or "home.clear-app-query");

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Home action is unavailable.");
        return new(_work.RunAsync(async original =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            switch (command)
            {
                case var _ when command.StartsWith(NewTabCustomisationPrefix, StringComparison.Ordinal):
                    await DispatchNewTabCustomisationAsync(command, parameter, original, linked.Token); break;
                case var _ when command.StartsWith(widgetActionPrefix, StringComparison.Ordinal):
                    await DispatchNewTabWidgetAsync(command, parameter, original, linked.Token); break;
                case "home.dashboard":
                    await OpenDashboardAsync(original, linked.Token);
                    break;
                case "home.apps" when _request.Address == DashboardAddress:
                    Queue(AppContentAddress.Parse("browse://home"), original);
                    break;
                case "home.models" when _request.Address == DashboardAddress:
                    Queue(AppContentAddress.Parse("browse://home/models"), original);
                    break;
                case "home.apps": case "home.models":
                    await SelectAsync(command[5..], original, linked.Token); break;
                case "home.events":
                    if (_createEvents is null) throw new InvalidOperationException("The Home Events source is unavailable.");
                    await SelectAsync("events", original, linked.Token); break;
                case "home.settings":
                    if (_createSettings is null) throw new InvalidOperationException("The Home Settings owner is unavailable.");
                    if (_request.Address.Authority == "newtab") Queue(AppContentAddress.Parse("browse://home/settings"), original, false);
                    else
                    {
                        if (_section != "settings") _settingsReturnSection = _section;
                        await SelectAsync("settings", original, linked.Token);
                        if (original.IsAcceptedPublicationCurrent)
                            FocusAfterSelection(() => _settings?.FocusCategory());
                    }
                    break;
                case "home.library":
                    if (_createLibrary is null) throw new InvalidOperationException("The Home Library owner is unavailable.");
                    if (_request.Address.Authority == "newtab") Queue(AppContentAddress.Parse("browse://home/library"), original, false);
                    else await SelectAsync("library", original, linked.Token);
                    break;
                case "home.newtab":
                    if (_request.Address.Authority == "newtab") await SelectAsync("newtab", original, linked.Token);
                    else Queue(AppContentAddress.Parse("browse://newtab"), original, true);
                    break;
                case "home.submit": SubmitSearch(original, linked.Token); break;
                case "home.ask-ai": await AskAiAsync(original, linked.Token); break;
                case "home.open-app":
                    if (TryResolveAppRoute(parameter as string, out var appAddress)) Queue(appAddress, original);
                    else UpdateAppsState("That app is no longer in this profile. Open it again from the current list.");
                    break;
                case "home.set-app-query": SetAppsQuery(parameter as string); break;
                case "home.clear-app-query": SetAppsQuery(string.Empty); break;
                case "home.mesh": Queue(AppContentAddress.Parse("9to1://mesh"), original); break;
                default: Queue(AppContentAddress.Parse("9to1://home/" + command[5..]), original); break;
            }
        }));
    }

    private static string HeadingFor(string section) => section switch
    {
        "newtab" => "New Tab", "models" => "Models / Discover", "dashboard" => "Dashboard",
        "events" => "Events", "settings" => "Settings", "library" => "Library", _ => "Apps"
    };

    private async Task SelectAsync(string section, DesktopOriginalWorkLifetime.Original original, CancellationToken token,
        string? focusRailButton = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        var generation = ++_selection;
        original.BindPublicationGuard(() => IsAlive && generation == _selection);
        original.DemandPublication();
        // The authored launcher is prepared BEFORE any previous surface is touched. A failed
        // publication must not leave the prior section deactivated, its search row hidden or its
        // heading relabelled: the page stays exactly as it was, with the real diagnostic reported.
        Control? prepared = null;
        if (section == "apps")
        {
            try { prepared = _appsView ??= BuildAppsView(); }
            catch (Exception failure)
            {
                Set(_status, "Text", "The Apps launcher could not be published: " + failure.Message);
                original.Retain(failure);
                return;
            }
        }
        if (section == "events")
        {
            if (_events is null)
                original.RunAcceptedPublicationCallback(() => _events = (_createEvents
                    ?? throw new InvalidOperationException("The Home Events source is unavailable."))());
            if (!original.IsAcceptedPublicationCurrent) return;
            var events = _events ?? throw new InvalidOperationException("The Home Events owner was not acquired.");
            await original.AwaitAsync(events.ActivateAsync(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            prepared = events;
        }
        if (section == "settings")
        {
            if (_settings is null)
                original.RunAcceptedPublicationCallback(() =>
                {
                    _settings = (_createSettings ?? throw new InvalidOperationException("The Home Settings owner is unavailable."))();
                    _settings.SetInitialRoute(_request.Address);
                    _settings.NavigateHomeAsync = NavigateFromSettingsAsync;
                    _settings.NavigateContentAsync = NavigateContentFromSettingsAsync;
                    _settings.ContentAvailable = address => address.Authority != "home" ||
                        (address.ResourceSegments.FirstOrDefault() switch
                        {
                            "events" => _createEvents is not null,
                            "library" => _createLibrary is not null,
                            _ => true
                        });
                    _settings.CategoryChanged += OnSettingsCategoryChanged;
                });
            if (!original.IsAcceptedPublicationCurrent) return;
            var settings = _settings ?? throw new InvalidOperationException("The Home Settings owner was not acquired.");
            await original.AwaitAsync(settings.ActivateAsync(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            if (settings.NativeAppAvailable is not null)
            {
                var modes = await original.AwaitAsync(_modes.GetModesAsync(token));
                if (!original.IsAcceptedPublicationCurrent) return;
                original.RunAcceptedPublicationCallback(() => settings.SetNativeApps(modes));
            }
            prepared = settings;
        }
        if (section == "library")
        {
            if (_library is null)
                original.RunAcceptedPublicationCallback(() => _library = (_createLibrary
                    ?? throw new InvalidOperationException("The Home Library owner is unavailable."))());
            if (!original.IsAcceptedPublicationCurrent) return;
            var library = _library ?? throw new InvalidOperationException("The Home Library owner was not acquired.");
            await original.AwaitAsync(library.ActivateAsync(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            prepared = library;
        }
        _dashboard?.Deactivate(); _discover?.Deactivate();
        if (section != "events") _events?.Deactivate();
        if (section != "settings") _settings?.Deactivate();
        if (section != "library") _library?.Deactivate();
        var settingsVisibilityChanged = (_section == "settings") != (section == "settings");
        _section = section;
        ApplyNewTabBackground();
        _railBindings.Refresh();
        if (focusRailButton is not null)
            FocusAfterSelection(() => this.GetVisualDescendants().OfType<Button>()
                .SingleOrDefault(button => button.Name == focusRailButton)?.Focus(NavigationMethod.Tab));
        Set(_searchRow!, "IsVisible", section == "newtab");
        RefreshNewTabClock();
        _viewport.IsVisible = section != "newtab";
        Set(_heading, "Text", HeadingFor(section));
        Set(_heading, "IsVisible", section is not ("settings" or "newtab"));
        Set(_status, "IsVisible", section != "settings");
        Set(_status, "Text", string.Empty);
        if (settingsVisibilityChanged) SettingsTitleChanged?.Invoke(this, EventArgs.Empty);
        if (section == "apps")
        {
            _viewport.Child = prepared!;
            var modes = await original.AwaitAsync(_modes.GetModesAsync(token));
            if (!IsAlive || generation != _selection) return;
            original.DemandPublication();
            // Canonical catalogue ownership: only enabled modes the shared route policy still admits
            // as standalone applications are projected, each with its registered identity, name and
            // icon key. Obsolete standalone identities stay resolvable through the route policy and
            // are never deleted here.
            _appsCatalogue = modes.Where(mode => mode.IsEnabled && !HavenAppRoutePolicy.IsDeprecatedStandalone(mode))
                .OrderBy(mode => mode.Name, StringComparer.OrdinalIgnoreCase)
                .Select(mode => new HomeAppItem(mode.Key, mode.Name, mode.Description, mode.IconKey,
                    CanonicalId: mode.Id, Source: mode.Source))
                .ToArray();
            UpdateAppsState();
        }
        else if (section is "events" or "settings" or "library")
        {
            _viewport.Child = prepared!;
        }
        else if (section == "newtab")
        {
            // New Tab owns its search surface and never creates or activates Dashboard.
            _viewport.Child = null;
            await RefreshNewTabLayoutAsync(original, token);
            if (original.IsAcceptedPublicationCurrent) await RefreshNewTabWidgetsAsync(original, token);
        }
        else if (section == "models")
        {
            if (_discover is null)
            {
                original.RunAcceptedPublicationCallback(() =>
                {
                    _discover = _createDiscover();
                    _discover.ManageProvidersRequested += OnManageProviders;
                });
            }
            if (!original.IsAcceptedPublicationCurrent) return;
            var discover = _discover ?? throw new InvalidOperationException("The model catalogue owner was not acquired.");
            _viewport.Child = discover;
            await original.AwaitAsync(discover.ActivateAsync(token));
        }
        else
        {
            if (_dashboard is null)
            {
                original.RunAcceptedPublicationCallback(() =>
                {
                    _dashboard = _createDashboard();
                    _dashboard.ModeRequested += OnDashboardApp;
                    _dashboard.ManageAppsRequested += OnManageApps;
                    _dashboard.DiscoverRequested += OnDiscover;
                    _dashboard.ConversationRequested += OnConversation;
                });
            }
            if (!original.IsAcceptedPublicationCurrent) return;
            var dashboard = _dashboard ?? throw new InvalidOperationException("The dashboard owner was not acquired.");
            _viewport.Child = dashboard;
            await original.AwaitAsync(dashboard.ActivateAsync(token));
        }
        if (!IsAlive || generation != _selection) return;
        original.DemandPublication();
    }

    private void OnSettingsCategoryChanged(object? sender, EventArgs args) =>
        SettingsTitleChanged?.Invoke(this, EventArgs.Empty);

    private Task OpenDashboardAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token,
        string? focusRailButton = null)
    {
        token.ThrowIfCancellationRequested();
        if (_request.Address == DashboardAddress)
            return _section == "dashboard" ? Task.CompletedTask : SelectAsync("dashboard", original, token, focusRailButton);

        Queue(DashboardAddress, original, newTab: true);
        return Task.CompletedTask;
    }

    private Task NavigateFromSettingsAsync(bool openDashboard, CancellationToken token)
    {
        if (!IsAlive || _section != "settings") return Task.CompletedTask;
        return _work.RunAsync(async original =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, token);
            if (openDashboard) await OpenDashboardAsync(original, linked.Token, focusRailButton: "home-dashboard");
            else await SelectAsync(_settingsReturnSection, original, linked.Token, focusRailButton: "home-settings");
        });
    }

    private Task<bool> NavigateContentFromSettingsAsync(AppContentAddress address, CancellationToken token)
    {
        if (!IsAlive || _section != "settings" || _settings is not { } settings) return Task.FromResult(false);
        var selection = _selection;
        return _work.RunAsync(original =>
        {
            original.BindPublicationGuard(() => IsAlive && _section == "settings" && selection == _selection);
            token.ThrowIfCancellationRequested();
            // The Browse driver, outside this callback, joins the click's owning Settings loader
            // before making a new tab visible. Settings remains the existing source tab.
            return Task.FromResult(Queue(address, original, newTab: true, additionalSettlement: settings.WhenActionsIdleAsync()));
        });
    }

    private void FocusAfterSelection(Action focus)
    {
        var selection = _selection;
        // The ContentPresenter attaches Home destinations during layout. Only transfer focus
        // once the destination is attached and this is still the latest section change.
        Dispatcher.UIThread.Post(() =>
        {
            if (IsAlive && _selection == selection && IsEffectivelyVisible && this.IsAttachedToVisualTree()) focus();
        }, DispatcherPriority.Loaded);
    }

    private Control BuildAppsView()
    {
        var document = HomeAppsCuiDocument.Parse();
        // The authored launcher is rendered from the document itself. The search field, the wrapped
        // icon tiles and the empty states are CUI elements bound through HomeAppsCuiBindings, so no
        // legacy list control is substituted for the authored surface. The loader owns a bounded
        // registry, so its Image sources resolve only through the canonical symbolic icon catalog.
        _appsLoader.SetBindingContext(_appsBindings);
        _appsLoader.SetActionDispatcher(_appsBindings);
        var loaded = _appsLoader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home apps document could not be rendered: " + string.Join("; ",
                loaded.Diagnostics.Where(item => item.Severity == CuiDiagnosticSeverity.Error)
                    .Select(item => item.Code + " " + item.Message)));
        _appsLoader.WireBindings(loaded.Root);
        return loaded.Root;
    }

    private ValueTask DispatchAppsCommandAsync(string command, object? parameter, CancellationToken token) =>
        IsAlive ? DispatchAsync(command, parameter, token) : ValueTask.CompletedTask;

    private void UpdateAppsState(string? status = null)
    {
        var query = _appsState.Query;
        var launcher = HomeCreateGroup.BuildLauncher(_appsCatalogue);
        var group = HomeCreateGroup.Build(_appsCatalogue);
        var groupVisible = HomeCreateGroup.ResolveGroupVisible(group, query);
        _appsState = new HomeAppsViewState(
            _appsCatalogue,
            HomeCreateGroup.FilterLauncher(launcher, group, query),
            query,
            status ?? (_appsCatalogue.Count == 0
                ? "No apps are available in this profile."
                : "Select an app to open it in this tab."),
            launcher,
            group,
            groupVisible);
        _appsBindings.Refresh();
    }

    private void SetAppsQuery(string? query)
    {
        var text = (query ?? string.Empty).Trim();
        var launcher = HomeCreateGroup.BuildLauncher(_appsCatalogue);
        var group = HomeCreateGroup.Build(_appsCatalogue);
        _appsState = _appsState with
        {
            Query = text,
            Visible = HomeCreateGroup.FilterLauncher(launcher, group, text),
            Launcher = launcher,
            Group = group,
            GroupVisible = HomeCreateGroup.ResolveGroupVisible(group, text)
        };
        _appsBindings.Refresh();
    }

    private bool TryResolveAppRoute(string? key, out AppContentAddress address)
    {
        address = AppContentAddress.Parse("9to1://home/apps");
        if (string.IsNullOrWhiteSpace(key)) return false;
        var app = _appsCatalogue.FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (app is null) return false;
        // Real Chat is owned by the canonical Spaces Chat space; canonicalize before strict native
        // admission so the registered Spaces owner receives the resource. Other apps keep their plain route.
        address = HomeLaunchRouteResolver.Resolve(app.Key);
        return true;
    }
    private void OnDashboardApp(object? sender, ModeDefinition mode)
    {
        var path = mode.Key.Trim().Equals("assistants", StringComparison.OrdinalIgnoreCase) ? "spaces/assistants" : mode.Key;
        StartNavigation(AppContentAddress.Parse("9to1://" + path));
    }
    private void OnConversation(object? sender, Conversation conversation) =>
        StartNavigation(AppContentAddress.Parse($"9to1://spaces/chat/{conversation.Id:D}"));
    private void OnManageProviders(object? sender, EventArgs args) => StartNavigation(AppContentAddress.Parse("9to1://home/settings"));
    private void OnManageApps(object? sender, EventArgs args) =>
        StartNavigation(AppContentAddress.Parse("browse://home"));
    private void OnDiscover(object? sender, EventArgs args) =>
        StartNavigation(AppContentAddress.Parse("browse://home/models"));
    private void StartNavigation(AppContentAddress address)
    {
        if (IsAlive) _ = _work.RunAsync(original => { Queue(address, original); return Task.CompletedTask; });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        RefreshNewTabClock();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        _newTabClock?.Stop();
        base.OnDetachedFromVisualTree(args);
    }

    private void OnNewTabClockTick(object? sender, EventArgs args) => RefreshNewTabClock();

    private void OnNewTabShortcutsSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        var width = args.NewSize.Width;
        if (!args.WidthChanged || width <= 0 || _newTabShortcuts?.Layout is not WrapPanel shortcuts) return;

        // Keep three cards on a desktop row and leave room for each icon and label as it narrows.
        var columns = width >= 660 ? 3 : width >= 312 ? 2 : 1;
        shortcuts.ItemWidth = Math.Floor((width - (columns - 1) * shortcuts.ItemSpacing) / columns);
        shortcuts.ItemHeight = columns == 3 ? 94 : 80;
        Set(_newTabTime, "FontSize",
            EffectiveNewTabLayout.Preset == NewTabLayoutPreset.MoreWidgets ? 42d : width >= 520 ? 58d : 46d);
    }

    private void RefreshNewTabClock()
    {
        if (_newTabClock is null) return;
        if (!IsAlive || _section != "newtab" || !this.IsAttachedToVisualTree())
        {
            _newTabClock.Stop();
            return;
        }

        var now = DateTimeOffset.Now;
        var culture = CultureInfo.CurrentCulture;
        Set(_newTabTime, "Text", now.ToString("t", culture));
        Set(_newTabDate, "Text", now.ToString("D", culture));
        // Avalonia schedules in whole milliseconds. Round past the minute boundary.
        var ticksUntilNextMinute = TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute;
        _newTabClock.Interval = TimeSpan.FromTicks(ticksUntilNextMinute + TimeSpan.TicksPerMillisecond);
        _newTabClock.Start();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs args) => RefreshNewTabSearchActions();

    private void RefreshNewTabSearchActions()
    {
        var hasQuery = !string.IsNullOrWhiteSpace(_search.Text);
        Set(_newTabGo, "IsEnabled", hasQuery);
        Set(_newTabAskAi, "IsEnabled", hasQuery && _aiAvailable && !_preparingAiDraft);
    }

    private void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || !IsAlive || _section != "newtab") return;
        args.Handled = true;
        _ = _work.RunAsync(original =>
        {
            SubmitSearch(original, original.Token);
            return Task.CompletedTask;
        });
    }

    private void SubmitSearch(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var text = _search.Text?.Trim();
        if (_section != "newtab" || string.IsNullOrWhiteSpace(text)) return;
        AppContentAddress address;
        if (AppContentAddress.IsReserved(text))
        {
            if (!AppContentAddress.TryParse(text, out var parsed) || parsed!.Namespace == AppContentNamespace.Cui)
            {
                Set(_status, "Text", "This app address is invalid or unavailable in this host.");
                return; // A rejected local route never becomes an external search.
            }
            address = AppContentRouteMigration.Canonicalize(parsed);
        }
        else
        {
            try { address = AppContentAddress.Parse(BrowserSessionService.NormaliseAddress(text).AbsoluteUri); }
            catch (Exception error) when (error is UriFormatException or ArgumentException)
            {
                Set(_status, "Text", "Enter a valid address or a search phrase.");
                return;
            }
        }
        Queue(address, original);
    }

    private async Task AskAiAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var query = _search.Text;
        if (_section != "newtab" || _preparingAiDraft || string.IsNullOrWhiteSpace(query)) return;

        var generation = _selection;
        original.BindPublicationGuard(() => IsAlive && _selection == generation &&
            string.Equals(_search.Text, query, StringComparison.Ordinal));
        _preparingAiDraft = true;
        RefreshNewTabSearchActions();
        try
        {
            if (_preparedAiDraft is not { } draft || draft.Query != query)
            {
                // The Spaces owner prepares an unsent draft and returns its own exact route.
                var address = await original.AwaitAsync(_prepareAiDraft(query, token));
                if (address.Namespace != AppContentNamespace.NativeApp || address.Authority != "spaces" || !HasConversationIdentity(address))
                    throw new InvalidOperationException("The canonical AI draft must identify its conversation in Spaces.");
                draft = (query, address);
                _preparedAiDraft = draft;
            }
            // Browse admission precedes mounting, which can fail while this source tab is retained.
            // Retrying the same query must open the already prepared draft.
            token.ThrowIfCancellationRequested();
            if (original.IsAcceptedPublicationCurrent) Queue(draft.Address, original);
        }
        finally
        {
            _preparingAiDraft = false;
            if (IsAlive) RefreshNewTabSearchActions();
        }
    }

    private static bool HasConversationIdentity(AppContentAddress address)
    {
        return address.ResourceSegments switch
        {
            ["chat", var conversation] => IsNonemptyId(conversation),
            ["selected", var space, "chat", var conversation] => IsNonemptyId(space) && IsNonemptyId(conversation),
            _ => false
        };

        static bool IsNonemptyId(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty;
    }

    private bool Queue(AppContentAddress address, DesktopOriginalWorkLifetime.Original original, bool? newTab = null,
        Task? additionalSettlement = null)
    {
        original.Token.ThrowIfCancellationRequested();
        // App tiles belong to the nested Apps loader, while rail/search actions
        // belong to the outer loader. Retire neither until the actual sender has settled.
        var settlement = Task.WhenAll(original.Task, WhenActionsIdleAsync(), additionalSettlement ?? Task.CompletedTask);
        var accepted = false;
        original.RunAcceptedPublicationCallback(() => accepted = _queue(
            new(_request.TabId, address, newTab ?? _request.Address.Authority == "home"), settlement));
        if (!accepted && original.IsAcceptedPublicationCurrent)
        {
            // A false receipt acquired no navigation work (for example a second
            // click while Browse is busy). Keep this page usable for retry; real
            // callback exceptions still propagate through the retained work owner.
            const string retry = "The browser cannot open this destination right now. Finish the current browser action, then try again.";
            Set(_status, "Text", retry);
            if (_section == "apps") UpdateAppsState(retry);
        }
        return accepted;
    }

    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _work.DemandExternalClose();
        if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _preparation = _work.RunAsync(async original =>
        {
            await start.Task;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, original.Token);
            if (_dashboard is not null && !await original.AwaitAsync(_dashboard.TryPrepareToCloseAsync(linked.Token))) return false;
            if (_discover is not null && !await original.AwaitAsync(_discover.TryPrepareToCloseAsync(linked.Token))) return false;
            if (_events is not null && !await original.AwaitAsync(_events.TryPrepareToCloseAsync(linked.Token))) return false;
            if (_settings is not null && !await original.AwaitAsync(_settings.TryPrepareToCloseAsync(linked.Token))) return false;
            return _library is null || await original.AwaitAsync(_library.TryPrepareToCloseAsync(linked.Token));
        });
        start.SetResult(); return _preparation;
    }
    internal Task CloseAndDrainAsync() => _work.CloseAndDrainAsync();

    private async Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); _stopping = true; ++_selection;
        if (_newTabClock is not null)
        {
            _newTabClock.Stop();
            _newTabClock.Tick -= OnNewTabClockTick;
            if (_newTabShortcuts is not null) _newTabShortcuts.SizeChanged -= OnNewTabShortcutsSizeChanged;
        }
        List<Exception> failures = [];
        void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
        void Acquire(Func<Task> action) => Attempt(() => _stopSources.Add(action()));
        // Retire the keyboard-focus tooltip host and the scoped-appearance observation FIRST, before
        // any loader teardown, so no tooltip subscription, popup or appearance callback outlives the
        // children they describe. Both are retired through the same retained-failure collection as the
        // loaders, so a later close failure cannot resurrect them.
        Attempt(_keyboardTooltips.Dispose);
        Attempt(_appsBindings.RetireAppearanceObservation);
        Attempt(StopNewTabCustomisation);
        Attempt(StopNewTabWidgets);
        Attempt(_loader.Dispose); Attempt(_appsLoader.Dispose);
        // Start child retirement before joining admitted page actions.
        if (_dashboard is not null) Acquire(_dashboard.CloseAndDrainAsync);
        if (_discover is not null) Acquire(_discover.CloseAndDrainAsync);
        if (_events is not null) Acquire(_events.CloseAndDrainAsync);
        if (_settings is not null)
        {
            _settings.CategoryChanged -= OnSettingsCategoryChanged;
            Acquire(_settings.CloseAndDrainAsync);
        }
        if (_library is not null) Acquire(_library.CloseAndDrainAsync);
        Acquire(_loader.WhenActionsIdleAsync); Acquire(_appsLoader.WhenActionsIdleAsync);
        Acquire(WhenNewTabCustomisationActionsIdleAsync); Acquire(WhenWidgetActionsIdleAsync);
        foreach (var actual in _stopSources)
            try { await actual; }
            catch (Exception error)
            {
                if (actual.Exception is { } aggregate) failures.AddRange(aggregate.InnerExceptions);
                else failures.Add(error);
            }
        if (failures.Count > 0) throw new AggregateException("Home content did not close cleanly.", failures);
        _stopped = true;
    }
    private Task CleanupAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_stopped) throw new InvalidOperationException("The original Home children must finish closing before detachment.");
        // The scoped-appearance observation was retired in StopAsync before the loaders tore down.
        _search.KeyDown -= OnSearchKey;
        _search.TextChanged -= OnSearchTextChanged;
        CleanUpNewTabCustomisation();
        if (_dashboard is not null)
        {
            _dashboard.ModeRequested -= OnDashboardApp; _dashboard.ManageAppsRequested -= OnManageApps;
            _dashboard.DiscoverRequested -= OnDiscover; _dashboard.ConversationRequested -= OnConversation;
        }
        if (_discover is not null) _discover.ManageProvidersRequested -= OnManageProviders;
        _viewport.Child = null; Content = null;
        return Task.CompletedTask;
    }
    private static void Set(Control control, string property, object? value) =>
        CuiPropertyPresentation.Set(control, property, value, CuiPropertyLevel.Instance, "Shared Home state", order: 2);
}
