using Haven.Browser;

namespace HavenOS.Apps.Browse;

/// <summary>Optional installed-factory observation: one available backend must
/// not falsely certify a different engine. Existing factories remain compatible.</summary>
public interface IBrowseEngineAvailability
{
    bool IsEngineSupported(BrowseEngineKind engine);
    string UnsupportedReasonFor(BrowseEngineKind engine);
}

/// <summary>Actual native page completion, using the existing BrowserSnapshot
/// contract. A requested URL alone is not a committed visit.</summary>
public interface IBrowseEngineRetirementGuard
{
    void DemandExternalJoin();
}

public interface IBrowseOriginalPageCommitSource
{
    event EventHandler<BrowserSnapshot>? PageCommitted;
}

public sealed partial class BrowseChrome
{
    /// <summary>Observes the SAME source-owned engine object for a current tab.
    /// This is an identity observation, not permission to create another renderer
    /// or access another profile. Browse retains engine lifetime ownership.</summary>
    private static readonly AsyncLocal<BrowseChrome?> LogicalNativeSource = new();
    private readonly object _nativeSourceGate = new();
    private readonly List<Task> _originalNativeSources = [];
    private bool _nativeRetiring;
    private Task? _originalChromeClose, _lastOriginalPageCommit;
    public Task? OriginalClose { get { lock (_nativeSourceGate) return _originalChromeClose; } }
    public IReadOnlyList<Task> OriginalNativeSources { get { lock (_nativeSourceGate) return _originalNativeSources.ToArray(); } }
    private void ObserveOriginalNativeState(TabRuntime tab, IBrowseEngineTab original, BrowserSnapshot snapshot)
    {
        if (_nativeRetiring || _disposed || !ReferenceEquals(tab.Host, original) || !_tabs.Contains(tab)) return;
        ApplyHostState(tab, snapshot);
        if (snapshot.IsLoading) tab.HasOriginalCommittedPage = false;
        _status = tab.Status; Publish();
    }
    private void CaptureOriginalNativeCallback(TabRuntime tab, IBrowseEngineTab original, Func<Task> callback)
    {
        lock (_nativeSourceGate)
        {
            if (_nativeRetiring || _disposed || !ReferenceEquals(tab.Host, original) || !_tabs.Contains(tab)) return;
            _originalNativeSources.RemoveAll(source => source.IsCompletedSuccessfully && !_contractSources.Contains(source));
            PruneAdmissionRefusalsLocked(_originalNativeSources);
            if (_originalNativeSources.Count >= 128) throw new InvalidOperationException("Browse retains unresolved original native callbacks.");
            ContentTransition admission;
            try { admission = AdmitContentTransition(); }
            catch (Exception refusal)
            {
                // This actual callback was refused by the reversible barrier. Keep
                // its explicit admission fault; never silently drop acquired work.
                _originalNativeSources.Add(Task.FromException(refusal));
                return;
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var driver = RunOriginalNativeCallbackAsync(start.Task, callback, admission);
            _originalNativeSources.Add(driver); start.SetResult();
        }
    }
    private async Task RunOriginalNativeCallbackAsync(Task start, Func<Task> callback, ContentTransition admission)
    {
        var prior = LogicalNativeSource.Value; LogicalNativeSource.Value = this;
        try
        {
            await start.ConfigureAwait(false);
            var original = callback() ?? throw new InvalidOperationException("The native callback did not issue its original task.");
            lock (_nativeSourceGate) _originalNativeSources.Add(original);
            await original.ConfigureAwait(false);
        }
        finally { LogicalNativeSource.Value = prior; admission.Dispose(); }
    }
    private void CaptureOriginalPageCommit(TabRuntime tab, IBrowseEngineTab original, BrowserSnapshot snapshot)
    {
        if (_nativeRetiring || _disposed || !ReferenceEquals(tab.Host, original) || !_tabs.Contains(tab) ||
            snapshot.IsLoading || snapshot.Address is not { Scheme: "http" or "https" }) return;
        var originalTabState = CaptureStandardTabs();
        lock (_nativeSourceGate)
        {
            if (_nativeRetiring || _disposed || !ReferenceEquals(tab.Host, original) || !_tabs.Contains(tab)) return;
            _originalNativeSources.RemoveAll(source => source.IsCompletedSuccessfully && !_contractSources.Contains(source));
            PruneAdmissionRefusalsLocked(_originalNativeSources);
            if (_originalNativeSources.Count >= 128) throw new InvalidOperationException("Browse retains unresolved native history sources.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tab.HasOriginalCommittedPage = true;
            tab.ZoomPercent = 100;
            var driver = PersistOriginalCommitAsync(start.Task, _lastOriginalPageCommit, snapshot, tab.Privacy, originalTabState, PersistedSelectedTabId());
            _lastOriginalPageCommit = driver;
            _originalNativeSources.Add(driver);
            try { Publish(); }
            catch (Exception failure) { start.SetException(failure); throw; }
            finally { start.TrySetResult(); }
        }
    }
    private async Task PersistOriginalCommitAsync(Task start, Task? previousOriginalCommit, BrowserSnapshot snapshot, Haven.Core.BrowserTabPrivacy privacy,
        BrowserTabState[] originalTabState, Guid? selectedTabId)
    {
        await start.ConfigureAwait(false);
        if (previousOriginalCommit is not null) await previousOriginalCommit.ConfigureAwait(false);
        var originalVisit = _data.RecordVisitAsync(snapshot.Title, snapshot.Address!.ToString(), privacy == Haven.Core.BrowserTabPrivacy.Private, CancellationToken.None);
        lock (_nativeSourceGate) _originalNativeSources.Add(originalVisit);
        await originalVisit.ConfigureAwait(false);
        var originalSession = SaveTabProjectionAsync(originalTabState, selectedTabId, CancellationToken.None);
        lock (_nativeSourceGate) _originalNativeSources.Add(originalSession);
        await originalSession.ConfigureAwait(false);
        if (!_nativeRetiring && !_disposed) Publish();
    }
    private bool IsEngineAvailable(BrowseEngineKind engine) => EngineAvailable &&
        (_engineFactory is not IBrowseEngineAvailability availability || availability.IsEngineSupported(engine));
    private string EngineUnavailableReason(BrowseEngineKind engine) =>
        _engineFactory is IBrowseEngineAvailability availability ? availability.UnsupportedReasonFor(engine) : EngineUnsupportedReason;

    /// <summary>Read-only per-engine capability probe: true only when the
    /// installed factory exposes typed <see cref="IBrowseEngineAvailability"/> and
    /// reports that EXACT engine supported. Fail-closed — a factory without a
    /// per-engine capability signal is treated as unknown, never as a basis for a
    /// fallback offer. Legacy private admission semantics are untouched.</summary>
    public bool QueryEngineCapability(BrowseEngineKind engine) =>
        _engineFactory is IBrowseEngineAvailability availability && availability.IsEngineSupported(engine);

    public BrowserSitePermissionDecision ObserveOriginPermission(Guid tabId, IBrowseEngineTab originalEngine,
        Uri origin, BrowserSitePermissionKind kind)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(origin);
        if (_nativeRetiring) return BrowserSitePermissionDecision.Deny;
        var tab = RequireTab(tabId);
        if (!ReferenceEquals(tab.Host, originalEngine) || tab.EngineState != BrowseEngineState.Ready ||
            origin.Scheme is not ("http" or "https") || !tab.Address.IsWeb ||
            !string.Equals(origin.GetLeftPart(UriPartial.Authority), tab.Address.WebUri!.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            return BrowserSitePermissionDecision.Deny;
        return GetPermission(tab, kind);
    }

    public IBrowseEngineTab? ObserveOriginalEngine(Guid tabId)
    {
        ThrowIfDisposed();
        var tab = RequireTab(tabId);
        return tab.Host;
    }
}
