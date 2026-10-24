using Haven.Core;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    private static readonly AppContentAddress HomeAddress = AppContentAddress.Parse("browse://home");
    private static readonly AppContentAddress NewTabAddress = AppContentAddress.Parse("browse://newtab");
    private readonly IBrowseAppContentFactory? _appContentFactory;
    private readonly IBrowseInternalContentFactory? _internalContentFactory;
    private readonly string _homeProfileId;

    private sealed class AppContentOwner(IBrowseAppContentSession session)
    {
        public IBrowseAppContentSession Session { get; } = session;
        public Task? Initialization, Close;
        public Task? ReportedInitialization, ReportedClose;
        public Exception? CloseContractFailure;
        public EventHandler? Changed;
        public bool Published;
        public ContentCloseAttempt? Preparation;
    }

    private static bool IsHomeAddress(AppContentAddress address) => address.Namespace == AppContentNamespace.Browse
        && address.Authority == "home" && address.ResourceSegments.Count == 0;
    private static bool IsOwnedCanvas(AppContentAddress address) => address.Namespace == AppContentNamespace.Browse
        && ((address.Authority is "home" or "newtab" && address.ResourceSegments.Count == 0)
            || (address.Authority == "home" && address.ResourceSegments.Count == 1
                && address.ResourceSegments[0] is "models" or "events" or "settings"));
    private static bool TryStoredAddress(string value, out AppContentAddress? address)
    {
        if (string.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase))
        { address = NewTabAddress; return true; }
        if (!AppContentAddress.TryParse(value, out address)) return false;
        address = AppContentRouteMigration.Canonicalize(address!); return true;
    }

    public IBrowseAppContentSession? ObserveOriginalAppContent(Guid tabId)
    { ThrowIfDisposed(); return RequireTab(tabId).AppOwner?.Session; }
    public bool HasAppContent => _tabs.Any(tab => tab.AppOwner is not null);

    private readonly HashSet<Task> _contractSources = [];
    private void PinContractSource(Task? source)
    { if (source is not null) lock (_nativeSourceGate) { _contractSources.Add(source); _originalNativeSources.Add(source); } }
    private void RetainAppSource(Task source, ContentTransition? fence = null)
    { lock (_nativeSourceGate) { (fence ?? _executingContentTransition.Value)?.Retained.Add(source); _originalNativeSources.RemoveAll(task => task.IsCompletedSuccessfully && !_contractSources.Contains(task)); PruneAdmissionRefusalsLocked(_originalNativeSources); _originalNativeSources.Add(source); } }

    private Task<BrowseChromeSnapshot> RetainNativeAdmission(Func<Task<BrowseChromeSnapshot>> operation, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = RunNativeAdmissionAsync(start.Task, operation);
        RetainAppSource(original); start.SetResult(); return original;
    }
    private async Task<BrowseChromeSnapshot> RunNativeAdmissionAsync(Task start, Func<Task<BrowseChromeSnapshot>> operation)
    {
        await start.ConfigureAwait(false); ThrowIfCannotAccept();
        return await operation().ConfigureAwait(false);
    }

    private async Task<AppContentOwner?> InitializeAppOwnerAsync(TabRuntime tab, AppContentAddress address, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ThrowIfCannotAccept();
        if (_tabs.Sum(item => item.AppOwners.Count) >= 128)
            throw new InvalidOperationException("Browse retains unresolved original app owners. Retire this host before opening another app.");
        var request = new BrowseAppContentRequest(tab.Id, address, tab.ProfileId, tab.Privacy);
        IBrowseAppContentSession? session;
        try
        {
            session = address.Namespace switch
            {
                AppContentNamespace.NativeApp => _appContentFactory?.CreateSession(request),
                AppContentNamespace.Browse when IsOwnedCanvas(address) => _internalContentFactory?.CreateSession(request),
                _ => throw new NotSupportedException("The content route is not registered in this host.")
            };
        }
        catch (Exception refusal)
        {
            // Witnessed by this chrome: this exact CreateSession call for this exact
            // request returned no session, so a trusted missing-binding proof acquired
            // nothing to retire. Every other route rejection and factory fault keeps its
            // ordinary conservative handling.
            TryWitnessAdmissionRefusal(refusal, request);
            throw;
        }
        if (session is null) return null;
        if (_tabs.SelectMany(item => item.AppOwners).Any(owner => ReferenceEquals(owner.Session, session)))
            throw new InvalidOperationException("The app factory reused an existing tab's content owner.");
        var owner = new AppContentOwner(session);
        tab.AppOwners.Add(owner); // retain before inspecting or starting the acquired owner
        if (session.Address != address) throw new UnauthorizedAccessException("The app changed the admitted content address.");
        session.StateChanged += owner.Changed = (_, _) => ObserveAppContentState(tab, owner);
        owner.Initialization = session.InitializeAsync(token) ?? throw new InvalidOperationException("The app supplied no original initialization task.");
        RetainAppSource(owner.Initialization);
        owner.ReportedInitialization = session.OriginalInitialization;
        if (owner.ReportedInitialization is not null) RetainAppSource(owner.ReportedInitialization);
        if (!ReferenceEquals(owner.Initialization, owner.ReportedInitialization))
        {
            PinContractSource(owner.Initialization); PinContractSource(owner.ReportedInitialization);
            throw new InvalidOperationException("The app did not retain its original initialization task.");
        }
        await owner.Initialization.ConfigureAwait(false);
        DemandContentAdmission(token);
        if (_nativeRetiring || _disposed || !_tabs.Contains(tab) || !session.IsPublicationCurrent)
            throw new UnauthorizedAccessException("The original app admission changed before publication.");
        if (session.Address.Namespace != address.Namespace || session.Address.Authority != address.Authority)
            throw new UnauthorizedAccessException("The app changed its owning content namespace.");
        return owner;
    }

    private async Task AttachAppContentAsync(TabRuntime tab, AppContentAddress address, CancellationToken token)
    {
        var owner = await InitializeAppOwnerAsync(tab, address, token).ConfigureAwait(false);
        ApplyAppContent(tab, address, owner);
    }

    private static void ApplyAppContent(TabRuntime tab, AppContentAddress address, AppContentOwner? owner)
    {
        tab.AppOwner = owner; tab.Address = owner?.Session.Address ?? address;
        tab.NativePage = Haven.Browser.BrowserNativePage.None;
        tab.Title = owner?.Session.Title ?? (IsHomeAddress(address) ? "Home" : address == NewTabAddress ? "New tab" : address.Authority);
        tab.IconKey = owner?.Session.IconKey ?? (IsHomeAddress(address) ? "home" : "apps");
        tab.EngineState = owner is null ? BrowseEngineState.Unsupported : BrowseEngineState.NativeApp;
        tab.CanGoBack = false; tab.CanGoForward = false; tab.IsLoading = false;
        tab.FindQuery = string.Empty; tab.ZoomPercent = 100; tab.HasOriginalCommittedPage = false;
        tab.Status = owner is null ? "This native content is not registered in the current host. Its route is retained." : tab.Title;
        if (owner is not null) owner.Published = true;
    }

    private void ObserveAppContentState(TabRuntime tab, AppContentOwner owner)
    {
        if (_nativeRetiring || _disposed || !owner.Published || !ReferenceEquals(tab.AppOwner, owner) || !_tabs.Contains(tab)) return;
        var session = owner.Session;
        if (!session.IsPublicationCurrent)
        { tab.EngineState = BrowseEngineState.Unsupported; tab.Status = "The original native app is no longer available. Its owner is retained."; }
        else
        {
            var address = session.Address;
            if (address.Namespace != tab.Address.Namespace || address.Authority != tab.Address.Authority) return;
            tab.Address = address; tab.Title = session.Title; tab.IconKey = session.IconKey;
            tab.EngineState = BrowseEngineState.NativeApp; tab.Status = session.Title;
        }
        var originalSave = SaveTabsAsync(CancellationToken.None);
        RetainAppSource(originalSave);
        Publish();
    }

    /// <summary>A bool-only observer cannot own a reversible lease. Acquire and
    /// abort it before returning, keeping the legacy caller safely retryable.</summary>
    public async Task<bool> TryPrepareToCloseAsync(CancellationToken token = default)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var entries = new List<ContentCloseAttempt>();
        var failures = new List<Exception>();
        var accepted = true;
        try
        {
            foreach (var tab in _tabs.ToArray())
            {
                var entry = await AcquireContentCloseAsync(tab, token).ConfigureAwait(false);
                if (entry is null) { accepted = false; break; }
                entries.Add(entry);
            }
        }
        catch (Exception failure) { failures.Add(failure); }
        foreach (var entry in entries) await AbortContentCloseAsync(entry, failures).ConfigureAwait(false);
        ThrowCloseCauses(failures);
        return accepted;
    }

    private async Task ReleaseContentAsync(TabRuntime tab, AppContentOwner? retainedOwner = null, bool preservePendingHosts = false)
    {
        var failures = new List<Exception>();
        var pendingJoins = new List<Task>();
        try { pendingJoins.Add(ReleaseHostAsync(tab)); } catch (Exception failure) { failures.Add(failure); }
        if (!preservePendingHosts)
            foreach (var pending in tab.PendingHosts.ToArray())
            {
                try
                {
                    if (!tab.PendingHostCloses.TryGetValue(pending, out var close))
                    { close = pending.DisposeAsync().AsTask(); tab.PendingHostCloses.Add(pending, close); RetainAppSource(close); }
                    pendingJoins.Add(close);
                }
                catch (Exception failure) { failures.Add(failure); }
            }
        var owners = tab.AppOwners.Where(owner => !ReferenceEquals(owner, retainedOwner)).ToArray();
        // Start every actual close before awaiting any initialization or close:
        // an owner may release its pending initialization only when close begins.
        foreach (var owner in owners)
        {
            try
            {
                if (owner.Close is null)
                {
                    owner.Published = false;
                    if (owner.Changed is not null) owner.Session.StateChanged -= owner.Changed;
                    owner.Close = owner.Session.CloseAndDrainAsync() ?? throw new InvalidOperationException("The app supplied no original close task.");
                    RetainAppSource(owner.Close);
                    try
                    {
                        owner.ReportedClose = owner.Session.OriginalClose;
                        if (owner.ReportedClose is not null) RetainAppSource(owner.ReportedClose);
                        if (!ReferenceEquals(owner.Close, owner.ReportedClose))
                            owner.CloseContractFailure = new InvalidOperationException("The app did not retain its original close task.");
                    }
                    catch (Exception failure) { owner.CloseContractFailure = failure; }
                }
                pendingJoins.Add(owner.Close);
                if (owner.ReportedClose is not null) pendingJoins.Add(owner.ReportedClose);
                if (owner.CloseContractFailure is not null) failures.Add(owner.CloseContractFailure);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        foreach (var source in pendingJoins.Distinct())
            try { await source.ConfigureAwait(false); } catch (Exception failure) { AddCloseCauses(failures, source, failure); }
        foreach (var owner in owners)
            if (owner.Close is { IsCompletedSuccessfully: true } && owner.CloseContractFailure is null)
            {
                if (!owner.Session.CanRetireOriginalView) failures.Add(new InvalidOperationException("The original app still owns its native view after close."));
                else tab.AppOwners.Remove(owner);
            }
        if (!preservePendingHosts)
            foreach (var pending in tab.PendingHostCloses.Where(pair => pair.Value.IsCompletedSuccessfully).Select(pair => pair.Key).ToArray())
            { tab.PendingHosts.Remove(pending); tab.PendingHostCloses.Remove(pending); }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Browse retained original content retirement failures.", failures);
        tab.AppOwner = null;
    }
}
