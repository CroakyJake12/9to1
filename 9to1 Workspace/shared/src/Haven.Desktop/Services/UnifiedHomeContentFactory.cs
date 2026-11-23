using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>Composition for Browse-owned Home/New Tab only. The supplied admission
/// reads the original host's authenticated profile; request metadata is not authority.</summary>
internal sealed class UnifiedHomeContentFactory : IBrowseInternalContentFactory
{
    private readonly IModeRegistry _modes;
    private readonly Func<NewDashboardPage> _dashboard;
    private readonly Func<HomeDiscoverPage> _discover;
    private readonly Func<BrowseNavigationIntent, Task, bool> _queue;
    private readonly Func<BrowseAppContentRequest, string, CancellationToken, Task<AppContentAddress>> _prepareAiDraft;
    private readonly Func<BrowseAppContentRequest, CancellationToken, Task> _admit;
    private readonly Func<bool> _hostIsCurrent;
    private readonly CancellationToken _hostLifetime;
    private readonly bool _aiAvailable;
    private readonly bool _assistantsAvailable;
    private readonly Func<ModeDefinition, bool>? _shortcutAppAvailable;
    private readonly Func<BrowseAppContentRequest, HomeEventsDestinationPage>? _events;
    private readonly Func<BrowseAppContentRequest, HomeSettingsDestinationPage>? _settings;
    private readonly Func<BrowseAppContentRequest, HomeLibraryDestinationPage>? _library;
    private readonly Func<BrowseAppContentRequest, NewTabLayoutPreferences>? _newTabLayout;
    private readonly Func<BrowseAppContentRequest, NewTabWidgetSources>? _newTabWidgets;

    internal UnifiedHomeContentFactory(IModeRegistry modes, Func<NewDashboardPage> dashboard,
        Func<HomeDiscoverPage> discover, Func<BrowseNavigationIntent, Task, bool> queue,
        Func<BrowseAppContentRequest, string, CancellationToken, Task<AppContentAddress>> prepareAiDraft,
        Func<BrowseAppContentRequest, CancellationToken, Task> admit,
        Func<bool> hostIsCurrent, CancellationToken hostLifetime, bool aiAvailable = true,
        Func<BrowseAppContentRequest, HomeEventsDestinationPage>? events = null,
        Func<BrowseAppContentRequest, HomeSettingsDestinationPage>? settings = null,
        Func<BrowseAppContentRequest, HomeLibraryDestinationPage>? library = null,
        Func<BrowseAppContentRequest, NewTabLayoutPreferences>? newTabLayout = null,
        Func<BrowseAppContentRequest, NewTabWidgetSources>? newTabWidgets = null,
        bool assistantsAvailable = false, Func<ModeDefinition, bool>? shortcutAppAvailable = null)
    {
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _prepareAiDraft = prepareAiDraft ?? throw new ArgumentNullException(nameof(prepareAiDraft));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _hostIsCurrent = hostIsCurrent ?? throw new ArgumentNullException(nameof(hostIsCurrent));
        if (!hostLifetime.CanBeCanceled) throw new ArgumentException("Retain the original host lifetime.", nameof(hostLifetime));
        _hostLifetime = hostLifetime; _aiAvailable = aiAvailable; _events = events; _settings = settings; _library = library;
        _newTabLayout = newTabLayout;
        _newTabWidgets = newTabWidgets;
        _assistantsAvailable = assistantsAvailable;
        _shortcutAppAvailable = shortcutAppAvailable;
    }

    public bool RecognizesNestedCanvas(AppContentAddress address) =>
        _settings is not null && HomeSettingsCatalog.IsSettingsAddress(address) ||
        _newTabLayout is not null && HomeSettingsCatalog.IsNewTabCustomisationAddress(address);

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); _hostLifetime.ThrowIfCancellationRequested();
        if (!_hostIsCurrent()) throw new InvalidOperationException("The original Home host is no longer current.");
        if (request.Address.Namespace != AppContentNamespace.Browse)
            throw new NotSupportedException("This owner admits only registered Home destinations and New Tab.");
        if (request.Address.Authority is not ("home" or "newtab"))
            throw new NotSupportedException("This owner admits only registered Home destinations and New Tab.");
        var segments = request.Address.ResourceSegments;
        // Feature routes belong to Home only, and each requires its genuine composed owner.
        var isHome = request.Address.Authority == "home";
        var admissibleSegments = segments.Count == 0 ||
            (isHome && _library is not null && BrowseOmniboxSearchRoute.TryParse(request.Address, out _, out _)) ||
            (isHome && _library is not null && segments.SequenceEqual(new[] { "library", "boards" }, StringComparer.Ordinal)) ||
            RecognizesNestedCanvas(request.Address) ||
            (isHome && segments.Count == 1 && (segments[0] is "dashboard" or "models" ||
                _events is not null && segments[0].Equals("events", StringComparison.Ordinal) ||
                _library is not null && segments[0].Equals("library", StringComparison.Ordinal)));
        if (!admissibleSegments)
            throw new NotSupportedException("This Home destination is unavailable or belongs to another authority.");
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original Home profile and tab identity.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("The personal Home canvas is unavailable in a private tab.");
        return new Session(this, request);
    }

    private HomeSettingsDestinationPage CreateSettings(BrowseAppContentRequest request, BrowsePageCommandSlot commands)
    {
        var page = _settings!(request);
        page.AttachPageCommands(commands);
        page.NewTabCustomisationAvailable = _newTabLayout is not null;
        return page;
    }

    private sealed class Session : IBrowseNativePageCommandSource, IOriginalTabTransientPresentationOwner
    {
        public BrowsePageCommandSlot PageCommandSlot { get; } = new();
        private OriginalTabTransientPresentation? _transientPresentation;
        public OriginalTabTransientPresentation TransientPresentation => _transientPresentation
            ?? throw new InvalidOperationException("The original tab presentation has not initialized.");
        private readonly UnifiedHomeContentFactory _owner;
        private readonly BrowseAppContentRequest _request;
        private readonly DesktopOriginalWorkLifetime _work;
        private readonly object _gate = new();
        private UnifiedHomePage? _page;
        private Task? _initialization;
        private Task<bool>? _preparation;
        private volatile bool _published;
        private volatile string? _settingsTitle;

        internal Session(UnifiedHomeContentFactory owner, BrowseAppContentRequest request)
        {
            _owner = owner; _request = request;
            _work = new(StopAsync, CleanupAsync);
        }
        public AppContentAddress Address => _request.Address;
        public string Title => _settingsTitle ?? (Address.Authority switch
        {
            "home" when Address.ResourceSegments is ["dashboard"] => "Dashboard",
            "home" => "Home",
            _ => "New Tab"
        });
        public string IconKey => "home";
        public event EventHandler? StateChanged;
        public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
        public Task? OriginalClose => _work.OriginalClose;
        public bool IsPublicationCurrent => _published && !_work.IsRetiring &&
            OriginalInitialization is { IsCompletedSuccessfully: true } &&
            !_owner._hostLifetime.IsCancellationRequested && _owner._hostIsCurrent() && _page is { IsAlive: true };
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Control OriginalNativeView
        {
            get
            {
                Dispatcher.UIThread.VerifyAccess();
                if (!IsPublicationCurrent) throw new InvalidOperationException("The original Home view is not admitted.");
                return TransientPresentation;
            }
        }
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_initialization is not null) return _initialization;
                return _work.RunAsync(async original =>
                {
                    using var sourcePhase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken, _owner._hostLifetime);
                    await original.AwaitAsync(CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                        () => _owner._admit(_request, linked.Token))).ConfigureAwait(false);
                    await original.AwaitAsync(OnUiAsync(async () =>
                    {
                        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                        DemandCurrent(); linked.Token.ThrowIfCancellationRequested();
                        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                            _page = new UnifiedHomePage(
                                (_request.Address.ResourceSegments.SequenceEqual(new[] { "library", "boards" }, StringComparer.Ordinal) ||
                                    BrowseOmniboxSearchRoute.TryParse(_request.Address, out _, out _))
                                    ? _request with { Address = AppContentAddress.Parse("browse://home/library") } : _request, _owner._modes, _owner._dashboard,
                                _owner._discover, _owner._queue, (text, token) => _owner._prepareAiDraft(_request, text, token), _owner._aiAvailable,
                                _owner._events is null ? null : () => _owner._events(_request),
                                _owner._settings is null ? null : () => _owner.CreateSettings(_request, PageCommandSlot),
                                _owner._library is null ? null : () => _owner._library(_request),
                                newTabLayout: _request.Address.Authority == "newtab" ? _owner._newTabLayout?.Invoke(_request) : null,
                                newTabWidgets: _request.Address.Authority == "newtab" ? _owner._newTabWidgets?.Invoke(_request) : null,
                                assistantsAvailable: _owner._assistantsAvailable, shortcutAppAvailable: _owner._shortcutAppAvailable));
                        DemandCurrent();
                        var page = _page ?? throw new InvalidOperationException("The original Home page was not acquired.");
                        await original.AwaitAsync(page.InitializeAsync(linked.Token));
                        _transientPresentation = new OriginalTabTransientPresentation();
                        _transientPresentation.BindOriginal(page);
                        await original.AwaitAsync(CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                            () => _owner._admit(_request, linked.Token)));
                        DemandCurrent(); linked.Token.ThrowIfCancellationRequested();
                        _settingsTitle = page.SettingsTitle;
                        page.SettingsTitleChanged += OnSettingsTitleChanged;
                        _published = true;
                        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; }); }
                        catch
                        {
                            _published = false;
                            page.SettingsTitleChanged -= OnSettingsTitleChanged;
                            throw;
                        }
                    })).ConfigureAwait(false);
                }, task => _initialization = task);
            }
        }
        private void OnSettingsTitleChanged(object? sender, EventArgs args)
        {
            if (sender is not UnifiedHomePage page || !IsPublicationCurrent) return;
            var title = page.SettingsTitle;
            if (_settingsTitle == title) return;
            _settingsTitle = title;
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
        }

        public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
        {
            DemandExternalJoin();
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
                if (!IsPublicationCurrent) return Task.FromResult(false);
                var reservation = TransientPresentation.TryReserveClose();
                if (reservation is null) return Task.FromResult(false);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                try { _preparation = _work.RunAsync(original => PrepareAsync(original, start.Task, cancellationToken, reservation)); }
                catch { reservation.Dispose(); throw; }
                start.SetResult(); return _preparation;
            }
        }
        private async Task<bool> PrepareAsync(DesktopOriginalWorkLifetime.Original original, Task start, CancellationToken caller, IDisposable reservation)
        {
            using var closeReservation = reservation;
            await start.ConfigureAwait(false);
                using var sourcePhase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller, _owner._hostLifetime);
                var allowed = false;
                await original.AwaitAsync(OnUiAsync(async () =>
                {
                    using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                    if (IsPublicationCurrent && _page is { } page)
                        allowed = await original.AwaitAsync(CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                            () => page.TryPrepareToCloseAsync(linked.Token)));
                })).ConfigureAwait(false);
                return allowed && IsPublicationCurrent;
        }
        public Task CloseAndDrainAsync()
        { DemandExternalJoin(); _transientPresentation?.CancelPending(); _published = false; return _work.CloseAndDrainAsync(); }
        private Task StopAsync() => OnUiAsync(async () =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            _published = false;
            if (_page is { } page)
            {
                page.SettingsTitleChanged -= OnSettingsTitleChanged;
                await CloudflareOriginalExecutionGuard.InvokeOriginal(this, page.CloseAndDrainAsync);
            }
        });
        private Task CleanupAsync() => OnUiAsync(async () =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            // Recheck after initialization settles, including a partial page acquired
            // while cancellation was requested. A failed close remains inspectable.
            if (_page is { } page) await CloudflareOriginalExecutionGuard.InvokeOriginal(this, page.CloseAndDrainAsync);
        });
        private void DemandCurrent()
        {
            _work.DemandAdmission(); _owner._hostLifetime.ThrowIfCancellationRequested();
            if (!_owner._hostIsCurrent()) throw new UnauthorizedAccessException("The original Home host is no longer current.");
        }
        private void DemandExternalJoin()
        { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); }
        private static async Task OnUiAsync(Func<Task> source)
        {
            var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(source).GetTask();
            var original = await dispatch.ConfigureAwait(false);
            await original.ConfigureAwait(false);
        }
    }
}
