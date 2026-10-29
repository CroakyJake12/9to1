using System.Text.Json;
using Avalonia.Controls;
using Haven.Browser;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>
/// One canonical Browse tab backed by an installed headless Chromium renderer.
/// The tab owns a distinct CDP page target inside the shared per-profile process
/// and drives it exclusively through the DevTools protocol. After attaching and
/// enabling the blank target it genuinely navigates to the requested address.
/// Real browser/page events drive state, titles, history, back/forward and the
/// canonical PageCommitted signal (only emitted at genuine load completion when
/// the final address is safe). Popups are attributed by opener target id and
/// routed through canonical Browse boundaries or denied — never auto-granted.
/// </summary>
internal sealed class ChromiumEngineTab : IBrowseNativeEngineTab, IBrowseOriginalPageCommitSource, IBrowseEngineRetirementGuard, IBrowseOriginalDownloadContentSource, IBrowseOriginalPendingDownloadSource
{
    private readonly Uri _initialAddress;
    private readonly ChromiumLease _lease;
    private readonly IChromiumEndpoint _connection;
    private readonly IChromiumEngineView _view;
    private readonly BrowserDownloadTransport? _downloadTransport;
    private readonly IBrowserNativeDownloadService? _nativeDownloads;
    private readonly bool _private;
    private ChromiumResponseDownloadBridge? _responseDownloads;
    private readonly object _gate = new();
    private readonly object _sourceGate = new();
    private readonly List<Task> _sourceTasks = [];
    private readonly List<Exception> _sourceFailures = [];
    private string? _targetId, _sessionId;
    private Uri? _committedAddress;
    private bool _navigationBlocked;
    private BrowserSnapshot _state;
    private bool _retiring, _disposed;
    private Task? _closeTask;
    private string _stage = "idle";
    private bool _instrumentInit;
    private readonly Dictionary<string, int> _lifecycleCounts = new(StringComparer.Ordinal);
    private long _framesAccepted;
    private bool _firstFrameReported;
    private int _publishDiagCount;
    private string? _lastKeyText;
    private ChromiumViewport _viewport = ChromiumViewport.Default;
    private ChromiumViewport _frameViewport = ChromiumViewport.Default;
    private ChromiumViewport? _pendingViewport;
    private Task? _viewportUpdate;
    private bool _viewportReady;

    // Bounded set of known lifecycle events we care about for stall diagnosis. Only
    // these are recorded/emitted, so the diagnostic dictionary never grows with
    // arbitrary/unrelated method keys.
    private static readonly HashSet<string> _trackedLifecycleEvents = new(StringComparer.Ordinal)
    {
        "Page.frameNavigated",
        "Page.loadEventFired",
        "Page.lifecycleEvent",
        "Page.screencastFrame",
        "Fetch.requestPaused",
        "Inspector.targetCrashed"
    };

    /// <summary>Optional per-instance diagnostic sink used in place of Console.Error
    /// so tests can capture the bounded safe context without globally mutating the
    /// process console. Production leaves this null (direct Console.Error).</summary>
    internal Action<string>? DiagnosticSink;

    private void EmitDiagnostic(string line)
    {
        try { if (DiagnosticSink is not null) DiagnosticSink(line); else Console.Error.WriteLine(line); }
        catch { /* diagnostics are best-effort and never fatal */ }
    }

    public ChromiumEngineTab(BrowseEngineTabRequest request, ChromiumLease lease, IChromiumEngineView? engineView = null,
        BrowserDownloadTransport? downloadTransport = null, IBrowserNativeDownloadService? nativeDownloads = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        _initialAddress = request.InitialAddress;
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _connection = _lease.Connection;
        _view = engineView ?? new ChromiumEngineSurface();
        if ((downloadTransport is null) != (nativeDownloads is null)) throw new ArgumentException("The same transport and approval owner are required together.");
        _downloadTransport = downloadTransport; _nativeDownloads = nativeDownloads;
        _private = request.Privacy == BrowserTabPrivacy.Private;
        _state = new BrowserSnapshot(request.InitialAddress, request.InitialAddress.Host, false, false, false, "Chromium is connecting.");
        if (_view is ChromiumEngineSurface surface)
        {
            surface.MouseGesture += OnMouse;
            surface.ViewportChanged += OnViewportChanged;
            surface.KeyGesture += OnKey;
            surface.TextGesture += OnText;
        }
    }

    public Control OriginalNativeView => _view.View;
    public bool CanRetireOriginalView => _responseDownloads?.CanRetireOriginal ?? true;
    public IBrowserOriginalDownloadContent? ObserveOriginalDownloadContent(BrowserDownloadRecord currentCanonicalRow) =>
        _responseDownloads?.ObserveOriginalDownloadContent(currentCanonicalRow);
    public bool IsOriginalPendingDownload(BrowserPendingAction samePendingAction) =>
        !_retiring && !_disposed && _responseDownloads?.IsOriginalPendingDownload(samePendingAction) == true;
    public BrowserSnapshot State { get { lock (_gate) return _state; } }

    public event EventHandler<BrowserSnapshot>? StateChanged;
    public event EventHandler<BrowsePopupRequest>? PopupRequested;
    public event EventHandler<BrowseEngineCrash>? Crashed;
    public event EventHandler<BrowserSnapshot>? PageCommitted;

    internal ChromiumLease Lease => _lease;
    internal IChromiumEndpoint Connection => _connection;

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var initialAssessment = ChromiumNavigationPolicy.Assess(_initialAddress);
        if (!initialAssessment.IsAllowed) throw new ArgumentException(initialAssessment.Reason, nameof(_initialAddress));

        _stage = "attach";
        _targetId = await _connection.CreateTargetAsync("about:blank", cancellationToken).ConfigureAwait(false);
        _sessionId = await _connection.AttachAsync(_targetId, cancellationToken).ConfigureAwait(false);
        if (_downloadTransport is not null && _nativeDownloads is not null)
            _responseDownloads = new(_connection, _sessionId, () => _committedAddress ?? State.Address,
                _private, _downloadTransport, _nativeDownloads, message =>
                {
                    if (!_retiring && !_disposed)
                        Publish(State with { Address = _committedAddress ?? State.Address, IsLoading = false, Status = message });
                });
        _connection.Recv += OnCdpEvent;

        try
        {
            // Instrument only the bounded initialization command batch so diagnostics
            // are low-volume (one begin/ok per init command) and never flood later
            // input/navigation traffic.
            _instrumentInit = true;
            _stage = "enable";
            await SendVoidAsync("Page.enable", null, cancellationToken).ConfigureAwait(false);
            await SendVoidAsync("Page.setLifecycleEventsEnabled", new Dictionary<string, object?> { ["enabled"] = true }, cancellationToken).ConfigureAwait(false);
            await SendVoidAsync("Runtime.enable", null, cancellationToken).ConfigureAwait(false);
            // Intercept requests BEFORE any forbidden document loads so the HTTP(S)-only
            // top-level document/redirect policy is enforced up front, not after a bad
            // frame has already navigated. Subresources are continued; only Document
            // requests are assessed.
            await SendVoidAsync("Fetch.enable", new Dictionary<string, object?>
            {
                ["patterns"] = new object[] {
                    new { urlPattern = "*", requestStage = "Request" },
                    new { urlPattern = "*", resourceType = "Document", requestStage = "Response" }
                },
                ["handleAuthRequests"] = false
            }, cancellationToken).ConfigureAwait(false);
            // Deny downloads by default; nothing is ever auto-downloaded or auto-granted.
            await SendVoidAsync("Browser.setDownloadBehavior", new Dictionary<string, object?> { ["behavior"] = "deny" }, cancellationToken).ConfigureAwait(false);
            // Browser-level target discovery (a browser command, not a page-session
            // command). Genuine new pages are attributed by opener id in HandleTargetCreated.
            await SendBrowserAsync("Target.setDiscoverTargets", new Dictionary<string, object?> { ["discover"] = true }, cancellationToken).ConfigureAwait(false);

            // The emulated CSS viewport follows the actual control and monitor DPI.
            _stage = "viewport";
            _viewport = (_view as ChromiumEngineSurface)?.RequestedViewport ?? ChromiumViewport.Default;
            await SetViewportMetricsAsync(_viewport, cancellationToken).ConfigureAwait(false);

            // Activate the stable blank target: it is unambiguously active here,
            // before any navigation. Page.bringToFront then succeeds.
            _stage = "activate";
            await SendVoidAsync("Page.bringToFront", null, cancellationToken).ConfigureAwait(false);

            // Establish capture on THIS stable active owned page, before navigation.
            // The observed native failure (CDP-32000 "Not attached to an active
            // page" at stage=capture) happens when startScreencast is requested
            // right after navigate, while the new document is not yet committed/
            // active. Requesting capture on the already-active stable page avoids
            // that mid-transition window; screencast then carries across navigation
            // and delivers frames for the committed document. Kept as a real
            // awaited step so a capture failure still surfaces truthfully.
            _stage = "capture";
            await StartCaptureAsync(_viewport, cancellationToken).ConfigureAwait(false);
            lock (_gate) _viewportReady = true;
            if (_view is ChromiumEngineSurface actualSurface) OnViewportChanged(actualSurface, actualSurface.RequestedViewport);

            Publish(_state with { Status = "Chromium page is ready." });
            _stage = "navigate";
            await NavigateAsync(_initialAddress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed tab must not leave its event subscription behind.
            _connection.Recv -= OnCdpEvent;
            _stage = "failed";
            throw;
        }
        finally
        {
            _instrumentInit = false;
        }
    }

    private async Task SetViewportMetricsAsync(ChromiumViewport viewport, CancellationToken token)
    {
        await SendVoidAsync("Emulation.setDeviceMetricsOverride", new Dictionary<string, object?>
        {
            ["width"] = viewport.Width, ["height"] = viewport.Height,
            ["deviceScaleFactor"] = viewport.Scale, ["mobile"] = false
        }, token).ConfigureAwait(false);
        lock (_gate) _frameViewport = viewport;
        if (_view is ChromiumEngineSurface surface)
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => surface.ApplyViewport(viewport)).GetTask().ConfigureAwait(false);
    }

    private async Task StartCaptureAsync(ChromiumViewport viewport, CancellationToken token)
    {
        await SendVoidAsync("Page.startScreencast", new Dictionary<string, object?>
        {
            ["format"] = "jpeg", ["quality"] = 95, ["maxWidth"] = viewport.ScaledCaptureWidth,
            ["maxHeight"] = viewport.ScaledCaptureHeight, ["everyNthFrame"] = 1
        }, token).ConfigureAwait(false);
        lock (_gate) _captureActive = true;
    }

    private bool _pageVisible = true;
    private bool _captureActive = true;
    private bool _visibilityPending;

    /// <summary>Suspends frame streaming while this tab is not the visible page, and restores it when
    /// it becomes visible. Without this, every hidden web tab in a multi-tab window kept streaming
    /// full-window JPEG frames that the owning surface turned into images on the UI thread forever;
    /// the owner's 29 October report - "when I do get in, the whole UI becomes extremely laggy and
    /// unresponsive" - is that, with the live window measured at 2850x1820 and 17 tabs restored.
    /// Toggles are queued through the same single-flight input channel the viewport uses, so a toggle
    /// can never race a viewport change or a teardown.</summary>
    public void SetBrowsePageVisible(bool visible)
    {
        lock (_gate)
        {
            if (_retiring || _disposed || _pageVisible == visible) return;
            _pageVisible = visible;
            _visibilityPending = true;
            if (!_viewportReady || _viewportUpdate is { IsCompleted: false }) return;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try { _viewportUpdate = QueueInputAsync(() => DrainVisibilityAsync(start.Task)); }
            catch { start.TrySetCanceled(); throw; }
            start.SetResult();
        }
    }

    private async Task DrainVisibilityAsync(Task start)
    {
        await start.ConfigureAwait(false);
        try
        {
            bool visible; ChromiumViewport viewport;
            lock (_gate)
            {
                if (_retiring || _disposed) { _viewportUpdate = null; return; }
                _visibilityPending = false;
                visible = _pageVisible; viewport = _viewport;
            }
            if (visible)
            {
                await SetViewportMetricsAsync(viewport, CancellationToken.None).ConfigureAwait(false);
                await StartCaptureAsync(viewport, CancellationToken.None).ConfigureAwait(false);
            }
            else if (_captureActive)
            {
                await SendVoidAsync("Page.stopScreencast", null, CancellationToken.None).ConfigureAwait(false);
                lock (_gate) _captureActive = false;
            }
        }
        catch
        {
            // A failed toggle must not fault the input queue; the next visibility change retries it and
            // the page stays correct either way. The failure is recorded rather than swallowed silently.
            EmitDiagnostic("capture visibility toggle failed; capture state will be retried on the " +
                "next visibility or viewport change");
        }
        finally { lock (_gate) if (!_visibilityPending) _viewportUpdate = null; }
    }

    private void OnViewportChanged(object? sender, ChromiumViewport viewport)
    {
        lock (_gate)
        {
            if (_retiring || _disposed || viewport == _viewport && _pendingViewport is null) return;
            _pendingViewport = viewport;
            _lastKeyText = null;
            if (!_viewportReady || _viewportUpdate is { IsCompleted: false }) return;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try { _viewportUpdate = QueueInputAsync(() => DrainViewportsAsync(start.Task)); }
            catch { start.TrySetCanceled(); throw; }
            start.SetResult();
        }
    }

    private async Task DrainViewportsAsync(Task start)
    {
        await start.ConfigureAwait(false);
        try
        {
            while (true)
            {
                ChromiumViewport next;
                lock (_gate)
                {
                    if (_retiring || _disposed || _pendingViewport is not { } pending)
                    { _viewportUpdate = null; return; }
                    next = pending; _pendingViewport = null;
                }
                if (next == _viewport) continue;
                await SendVoidAsync("Page.stopScreencast", null, CancellationToken.None).ConfigureAwait(false);
                await SetViewportMetricsAsync(next, CancellationToken.None).ConfigureAwait(false);
                // A hidden tab has no visible frame to produce; resizing while hidden must not restart
                // streaming. The visibility toggle restarts it when the tab is shown again.
                bool visibleNow; lock (_gate) { visibleNow = _pageVisible; _captureActive = false; }
                if (visibleNow) await StartCaptureAsync(next, CancellationToken.None).ConfigureAwait(false);
                lock (_gate) _viewport = next;
            }
        }
        catch { lock (_gate) _viewportUpdate = null; throw; }
    }

    // ---- IEmbeddedBrowserHost ----

    public async Task NavigateAsync(Uri address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var assessment = ChromiumNavigationPolicy.Assess(address);
        if (!assessment.IsAllowed) throw new ArgumentException(assessment.Reason, nameof(address));
        Publish(State with { Address = address, IsLoading = true, Status = "Loading…" });
        var result = await SendCommandAsync("Page.navigate", new Dictionary<string, object?> { ["url"] = address.AbsoluteUri }, cancellationToken).ConfigureAwait(false);
        if (result.TryGetProperty("errorText", out var error) && error.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(error.GetString()))
            Publish(State with { IsLoading = false, Status = "Chromium could not load this page." });
    }

    public Task GoBackAsync(CancellationToken cancellationToken) => HistoryStepAsync(-1, cancellationToken);
    public Task GoForwardAsync(CancellationToken cancellationToken) => HistoryStepAsync(+1, cancellationToken);

    private async Task HistoryStepAsync(int delta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var result = await SendCommandAsync("Page.getNavigationHistory", null, cancellationToken).ConfigureAwait(false);
        if (!result.TryGetProperty("entries", out var entries) || !result.TryGetProperty("currentIndex", out var indexEl))
            return;
        var current = indexEl.GetInt32();
        var target = current + delta;
        if (target < 0 || target >= entries.GetArrayLength())
        {
            Publish(State with { Status = delta < 0 ? "No previous page." : "No forward page." });
            return;
        }
        var entryId = entries[target].GetProperty("id").GetInt32();
        _ = await SendCommandAsync("Page.navigateToHistoryEntry", new Dictionary<string, object?> { ["entryId"] = entryId }, cancellationToken).ConfigureAwait(false);
        Publish(State with { CanGoBack = target > 0, CanGoForward = target < entries.GetArrayLength() - 1 });
    }

    public Task ReloadAsync(CancellationToken cancellationToken) => SendVoidAsync("Page.reload", null, cancellationToken);
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_responseDownloads is not null) await _responseDownloads.WithdrawPendingAsync(cancellationToken).ConfigureAwait(false);
        await SendVoidAsync("Page.stopLoading", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(script);
        var result = await SendCommandAsync("Runtime.evaluate", new Dictionary<string, object?>
        {
            ["expression"] = script,
            ["returnByValue"] = true
        }, cancellationToken).ConfigureAwait(false);
        if (result.TryGetProperty("exceptionDetails", out _)) return null;
        if (result.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var primitive) &&
            primitive.ValueKind != JsonValueKind.Null && primitive.ValueKind != JsonValueKind.Undefined)
            return primitive.ToString();
        return null;
    }

    public Task OpenDeveloperToolsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException("This Chromium engine does not expose an in-process DevTools surface.");

    // ---- CDP event handling ----

    private void OnCdpEvent(object? sender, CdpEvent e)
    {
        if (_disposed) return;
        if (e.SessionId is null)
        {
            // Browser-level events are not attached to a page session.
            if (e.Method == "Target.targetCreated") HandleTargetCreated(e.Params);
            return;
        }
        if (e.SessionId != _sessionId) return;
        // Record the first occurrence of each known owned lifecycle event (name +
        // count only; never URL/params/body) so a white-viewport stall can be pinned
        // to whether navigation/load/commit events ever arrive on our session. The
        // set is bounded to known event names.
        if (_trackedLifecycleEvents.Contains(e.Method))
        {
            _lifecycleCounts.TryGetValue(e.Method, out var prior);
            var count = prior + 1;
            _lifecycleCounts[e.Method] = count;
            if (prior == 0 && !_disposed)
                EmitDiagnostic($"lifecycle event={e.Method} count={count}");
        }
        switch (e.Method)
        {
            case "Page.frameNavigated":
                HandleFrameNavigated(e.Params);
                break;
            case "Page.loadEventFired":
                HandleLoadEventFired();
                break;
            case "Page.screencastFrame":
                HandleScreencastFrame(e.Params);
                break;
            case "Fetch.requestPaused":
                HandleFetchRequestPaused(e.Params);
                break;
            case "Page.javascriptDialogOpening":
                // No automatic dialog grants; the dialog is dismissed by the page
                // or left pending. We never auto-accept side effects.
                _ = QueueInputAsync(() => SendVoidAsync("Page.handleJavaScriptDialog", new Dictionary<string, object?> { ["accept"] = false }, CancellationToken.None));
                break;
            case "Inspector.targetCrashed":
                Crash("Chromium renderer crashed.");
                break;
        }
    }

    private void HandleFrameNavigated(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("frame", out var frame) || frame.TryGetProperty("parentId", out _)) return;
        if (!frame.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String) return;
        if (!Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var uri)) return;

        var assessment = ChromiumNavigationPolicy.Assess(uri);
        if (!assessment.IsAllowed)
        {
            // A redirect/new-target reached an unsafe (internal/file) document; refuse
            // to surface it as a commit.
            _navigationBlocked = true;
            Publish(State with { IsLoading = false, Status = assessment.Reason });
            return;
        }

        _navigationBlocked = false;
        _committedAddress = uri;
        Publish(State with
        {
            Address = uri,
            Title = string.IsNullOrWhiteSpace(State.Title) ? uri.Host : State.Title,
            IsLoading = true,
            Status = "Loading…"
        });
    }

    private void HandleLoadEventFired()
    {
        if (_navigationBlocked)
        {
            _navigationBlocked = false;
            Publish(State with { IsLoading = false, Status = "The requested page was blocked." });
            return;
        }

        var address = _committedAddress ?? State.Address;
        var snapshot = State with
        {
            Address = address,
            IsLoading = false,
            Status = "Chromium page loaded."
        };
        Publish(snapshot);
        if (address is { Scheme: "http" or "https" }) PageCommitted?.Invoke(this, snapshot);
        _ = QueueInputAsync(RefreshHistoryStateAsync);
    }

    private async Task RefreshHistoryStateAsync()
    {
        try
        {
            var result = await SendCommandAsync("Page.getNavigationHistory", null, CancellationToken.None).ConfigureAwait(false);
            if (result.TryGetProperty("entries", out var entries) && result.TryGetProperty("currentIndex", out var indexEl))
            {
                var current = indexEl.GetInt32();
                Publish(State with { CanGoBack = current > 0, CanGoForward = current < entries.GetArrayLength() - 1 });
            }
        }
        catch { /* history refresh is best-effort and never fatal */ }
    }

    private void HandleTargetCreated(JsonElement parameters)
    {
        if (_targetId is null || _disposed || !parameters.TryGetProperty("targetInfo", out var info)) return;
        if (info.TryGetProperty("type", out var type) && type.GetString() != "page") return;
        if (!info.TryGetProperty("targetId", out var targetIdEl) || targetIdEl.ValueKind != JsonValueKind.String) return;
        var targetId = targetIdEl.GetString()!;
        // Only popups opened by THIS page (opener id matches) belong to this tab; a
        // managed sibling tab in the same profile is never surfaced or closed.
        if (!info.TryGetProperty("openerId", out var opener) || opener.GetString() != _targetId) return;
        if (!info.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri))
        {
            // Unroutable popup: close it rather than leaving an uncontrolled window.
            CloseUnmanagedTarget(targetId);
            return;
        }
        if (!ChromiumNavigationPolicy.Assess(uri).IsAllowed)
        {
            // A denied popup is closed, never auto-opened or auto-granted.
            CloseUnmanagedTarget(targetId);
            return;
        }
        PopupRequested?.Invoke(this, new BrowsePopupRequest(uri));
    }

    private void HandleFetchRequestPaused(JsonElement parameters)
    {
        if (_responseDownloads?.TryHandleResponse(parameters) == true) return;
        if (!parameters.TryGetProperty("requestId", out var requestIdEl) || requestIdEl.ValueKind != JsonValueKind.String) return;
        var requestId = requestIdEl.GetString()!;
        // Only top-level Document requests carry the navigation policy; subresources
        // of an already-allowed document are always continued.
        if (!parameters.TryGetProperty("resourceType", out var typeEl) || typeEl.ValueKind != JsonValueKind.String ||
            typeEl.GetString() != "Document")
        {
            ContinueFetch(requestId);
            return;
        }
        if (!parameters.TryGetProperty("request", out var request) || !request.TryGetProperty("url", out var urlEl) ||
            urlEl.ValueKind != JsonValueKind.String)
        {
            ContinueFetch(requestId);
            return;
        }
        if (!Uri.TryCreate(urlEl.GetString(), UriKind.Absolute, out var uri))
        {
            ContinueFetch(requestId);
            return;
        }
        var assessment = ChromiumNavigationPolicy.Assess(uri);
        if (!assessment.IsAllowed)
        {
            FailFetch(requestId, "AccessDenied");
            return;
        }
        ContinueFetch(requestId);
    }

    private void ContinueFetch(string requestId) =>
        _ = QueueInputAsync(() => SendVoidAsync("Fetch.continueRequest",
            new Dictionary<string, object?> { ["requestId"] = requestId }, CancellationToken.None));

    private void FailFetch(string requestId, string reason) =>
        _ = QueueInputAsync(() => SendVoidAsync("Fetch.failRequest",
            new Dictionary<string, object?> { ["requestId"] = requestId, ["errorReason"] = reason }, CancellationToken.None));

    private void CloseUnmanagedTarget(string targetId) =>
        _ = QueueInputAsync(() => SendBrowserAsync("Target.closeTarget",
            new Dictionary<string, object?> { ["targetId"] = targetId }, CancellationToken.None));

    private void HandleScreencastFrame(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String) return;
        if (!parameters.TryGetProperty("sessionId", out var session) || session.ValueKind != JsonValueKind.Number) return;
        // Report the first owned screencast frame bytes/count (never base64 or image
        // content) so the stall can be pinned to no-frame vs decode vs render.
        var frames = Interlocked.Increment(ref _framesAccepted);
        if (!_firstFrameReported)
        {
            _firstFrameReported = true;
            EmitDiagnostic($"first frame accepted bytes={data.GetString()!.Length} frames={frames}");
        }
        try
        {
            var jpeg = Convert.FromBase64String(data.GetString()!);
            if (_view is ChromiumEngineSurface surface)
            {
                ChromiumViewport expected; lock (_gate) expected = _frameViewport;
                surface.PresentFrame(jpeg, MatchScreencastViewport(parameters, expected));
            }
            else _view.PresentFrame(jpeg);
        }
        catch (FormatException) { return; }
        _ = QueueInputAsync(() => SendVoidAsync("Page.screencastFrameAck", new Dictionary<string, object?> { ["sessionId"] = session.GetInt32() }, CancellationToken.None));
    }

    // CDP supplies device dimensions in DIP with each frame. Matching those dimensions
    // prevents an in-flight old JPEG from enabling input against newly emulated metrics.
    internal static ChromiumViewport? MatchScreencastViewport(JsonElement frame, ChromiumViewport expected)
    {
        if (!frame.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("deviceWidth", out var width) || width.ValueKind != JsonValueKind.Number ||
            !metadata.TryGetProperty("deviceHeight", out var height) || height.ValueKind != JsonValueKind.Number ||
            !width.TryGetDouble(out var w) || !height.TryGetDouble(out var h) ||
            !double.IsFinite(w) || !double.IsFinite(h) ||
            Math.Abs(w - expected.Width) > .5 || Math.Abs(h - expected.Height) > .5) return null;
        return expected;
    }

    // ---- Input routing ----

    private void OnMouse(object? sender, ChromiumMouseArgs args)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = args.Type,
            ["x"] = args.X,
            ["y"] = args.Y,
            ["buttons"] = args.Buttons
        };
        if (args.Button is not null) parameters["button"] = args.Button;
        if (args.ClickCount > 0) parameters["clickCount"] = args.ClickCount;
        if (args.Type == "mouseWheel") { parameters["deltaX"] = args.DeltaX; parameters["deltaY"] = args.DeltaY; }
        _ = QueueInputAsync(() => SendVoidAsync("Input.dispatchMouseEvent", parameters, CancellationToken.None));
    }

    internal void OnKey(object? sender, ChromiumKeyArgs args)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = args.Type,
            ["key"] = args.Key,
            ["code"] = args.Code,
            ["autoRepeat"] = args.AutoRepeat,
            ["windowsVirtualKeyCode"] = args.VirtualKeyCode
        };
        if (args.Modifiers != 0) parameters["modifiers"] = args.Modifiers;
        if (args.Text is not null)
        {
            // The genuine platform symbol (layout/shift-aware) travels on the keyDown
            // so the character is inserted by the renderer. Remember it so a matching
            // plain-character TextInput is not inserted a second time. It is cleared
            // once consumed (or replaced by the next printable keyDown).
            parameters["text"] = args.Text;
            parameters["unmodifiedText"] = args.Text;
            if (args.Type == "keyDown") _lastKeyText = args.Text;
        }
        _ = QueueInputAsync(() => SendVoidAsync("Input.dispatchKeyEvent", parameters, CancellationToken.None));
    }

    internal void OnText(object? sender, ChromiumTextArgs args)
    {
        // Genuine composed text (IME/dead-key composition, or a platform that only
        // delivers text via TextInput). If it equals the character we already sent on
        // the immediately preceding keyDown, it is the duplicate of that plain key and
        // must not be inserted a second time.
        if (args.Text == _lastKeyText)
        {
            _lastKeyText = null;
            return;
        }
        _lastKeyText = null;
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "char",
            ["text"] = args.Text
        };
        _ = QueueInputAsync(() => SendVoidAsync("Input.dispatchKeyEvent", parameters, CancellationToken.None));
    }

    // ---- Command plumbing ----

    private async Task<JsonElement> SendCommandAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        // Central init-command begin/ok instrumentation covers EVERY owned CDP command,
        // including Page.navigate (which calls SendCommandAsync directly), so the next
        // native run sees begin/ok for the exact pending candidate. Diagnostics are
        // bounded (init-only) and payload-free (method/session/target/stage only).
        EmitInitCommand("begin", method, _sessionId);
        try
        {
            var result = await _connection.SendAsync(_sessionId, method, parameters, cancellationToken).ConfigureAwait(false);
            EmitInitCommand("ok", method, _sessionId);
            return result;
        }
        catch (ChromiumCdpException original) { throw Enrich(original, method, _sessionId!); }
    }

    private async Task SendVoidAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        _ = await SendCommandAsync(method, parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendBrowserAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        // Browser-level commands have no page session; they still carry target and
        // stage provenance so a failure is observable in context. Covered by the
        // same bounded begin/ok instrumentation.
        EmitInitCommand("begin", method, null);
        try
        {
            _ = await _connection.SendAsync(null, method, parameters, cancellationToken).ConfigureAwait(false);
            EmitInitCommand("ok", method, null);
        }
        catch (ChromiumCdpException original) { throw Enrich(original, method, sessionId: null); }
    }

    private ChromiumCdpException Enrich(ChromiumCdpException original, string method, string? sessionId)
    {
        // Preserve the original fault as the inner exception while adding the
        // typed method/session/target/lifetime-stage provenance. The bumped
        // exception rethrows with the same visible fault, never swallowing it.
        var enriched = new ChromiumCdpException(original)
        {
            Method = method,
            SessionId = sessionId,
            TargetId = _targetId,
            Stage = _stage
        };
        // Guaranteed bounded payload-free diagnostic: the failing CDP method plus
        // owned session/target/lifetime-stage IDs, written directly to Console.Error
        // (Trace may have no listeners in ordinary Release Linux). No raw original
        // message, URL, request params, cookie/profile/user text, or personal data.
        // A logging failure must never replace the original CDP failure, so it is
        // caught and ignored while the original fault is always rethrown.
        SafeWriteDiagnostic(method, sessionId, _targetId, _stage, _retiring);
        return enriched;
    }

    private void SafeWriteDiagnostic(string method, string? sessionId, string? targetId, string stage, bool retiring)
    {
        EmitDiagnostic(string.Format(
            "Chromium CDP command failed: method={0} session={1} target={2} stage={3} retiring={4}",
            method, sessionId ?? "-", targetId ?? "-", stage, retiring));
    }

    private void EmitInitCommand(string kind, string method, string? sessionId)
    {
        if (!_instrumentInit) return;
        EmitDiagnostic(string.Format(
            "init cmd {0} method={1} session={2} target={3} stage={4}",
            kind, method, sessionId ?? "-", _targetId ?? "-", _stage));
    }

    private Task QueueInputAsync(Func<Task> action)
    {
        if (_retiring || _disposed) return Task.CompletedTask;
        var task = action();
        lock (_sourceGate)
        {
            for (var i = _sourceTasks.Count - 1; i >= 0; i--)
            {
                var source = _sourceTasks[i];
                if (source.IsCompleted)
                {
                    if (source.IsFaulted) _sourceFailures.Add(source.Exception!);
                    _sourceTasks.RemoveAt(i);
                }
            }
            if (_sourceTasks.Count >= 256) throw new InvalidOperationException("Chromium retains too many engine input sources.");
            _sourceTasks.Add(task);
        }
        return task;
    }

    private void Publish(BrowserSnapshot snapshot)
    {
        lock (_gate) _state = snapshot;
        if (_disposed) return;
        // Bounded (first 6 only) callback begin/end metadata so the next native run
        // can prove whether a StateChanged callback blocks/faults. No state content,
        // URLs, or payloads are emitted.
        var diag = Interlocked.Increment(ref _publishDiagCount);
        var emit = diag <= 6;
        var subscriber = StateChanged is not null;
        if (emit)
            EmitDiagnostic($"publish begin count={diag} stage={_stage} subscriber={subscriber} tid={Environment.CurrentManagedThreadId}");
        try
        {
            StateChanged?.Invoke(this, snapshot);
        }
        finally
        {
            if (emit)
                EmitDiagnostic($"publish end count={diag} stage={_stage} tid={Environment.CurrentManagedThreadId}");
        }
    }

    private void Crash(string reason)
    {
        if (_disposed || _retiring) return;
        Publish(State with { IsLoading = false, Status = reason });
        Crashed?.Invoke(this, new BrowseEngineCrash(reason));
    }

    public void DemandExternalJoin() => _responseDownloads?.DemandExternalJoin();

    public ValueTask DisposeAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_closeTask is not null) return new(_closeTask);
            if (!CanRetireOriginalView)
                throw new InvalidOperationException("Complete or cancel the pending native download through Home approval before closing its Chromium tab.");
            _retiring = true;
            _closeTask = CloseCoreAsync();
        }
        return new(_closeTask);
    }

    private async Task CloseCoreAsync()
    {
        var failures = new List<Exception>();
        async Task Join(Task? task) { if (task is null) return; try { await task.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); } }

        // A failed original transfer retirement keeps its real endpoint and view
        // attached; do not destroy its source while merely retaining an exception.
        if (_responseDownloads is not null) await _responseDownloads.DisposeAsync().ConfigureAwait(false);

        Task[] sources;
        lock (_sourceGate)
        {
            sources = [.. _sourceTasks];
            _sourceTasks.Clear();
            failures.AddRange(_sourceFailures);
            _sourceFailures.Clear();
        }
        foreach (var source in sources) await Join(source);

        if (_sessionId is not null)
        {
            try { await SendVoidAsync("Page.stopScreencast", null, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (_targetId is not null)
        {
            // Close our owned page target even while other same-profile tabs remain.
            try
            {
                await SendBrowserAsync("Target.closeTarget",
                    new Dictionary<string, object?> { ["targetId"] = _targetId }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        _connection.Recv -= OnCdpEvent;
        if (_view is ChromiumEngineSurface surface)
        {
            surface.MouseGesture -= OnMouse;
            surface.ViewportChanged -= OnViewportChanged;
            surface.KeyGesture -= OnKey;
            surface.TextGesture -= OnText;
        }
        await _lease.DisposeAsync().ConfigureAwait(false);
        lock (_gate) _disposed = true;
        if (failures.Count != 0) throw new AggregateException("Chromium retained its engine source failures.", failures);
    }

    private void ThrowIfDisposed()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed || _retiring, this);
    }
}
