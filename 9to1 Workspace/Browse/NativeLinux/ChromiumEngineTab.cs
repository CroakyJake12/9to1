using System.Text.Json;
using Avalonia.Controls;
using Haven.Browser;

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
internal sealed class ChromiumEngineTab : IBrowseNativeEngineTab, IBrowseOriginalPageCommitSource, IBrowseEngineRetirementGuard
{
    private readonly Uri _initialAddress;
    private readonly ChromiumLease _lease;
    private readonly IChromiumEndpoint _connection;
    private readonly IChromiumEngineView _view;
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

    public ChromiumEngineTab(BrowseEngineTabRequest request, ChromiumLease lease, IChromiumEngineView? engineView = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        _initialAddress = request.InitialAddress;
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _connection = _lease.Connection;
        _view = engineView ?? new ChromiumEngineSurface();
        _state = new BrowserSnapshot(request.InitialAddress, request.InitialAddress.Host, false, false, false, "Chromium is connecting.");
        if (_view is ChromiumEngineSurface surface)
        {
            surface.MouseGesture += OnMouse;
            surface.KeyGesture += OnKey;
            surface.TextGesture += OnText;
        }
    }

    public Control OriginalNativeView => _view.View;
    public bool CanRetireOriginalView => true;
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

        _targetId = await _connection.CreateTargetAsync("about:blank", cancellationToken).ConfigureAwait(false);
        _sessionId = await _connection.AttachAsync(_targetId, cancellationToken).ConfigureAwait(false);
        _connection.Recv += OnCdpEvent;

        try
        {
            await SendVoidAsync("Page.enable", null, cancellationToken).ConfigureAwait(false);
            await SendVoidAsync("Page.setLifecycleEventsEnabled", new Dictionary<string, object?> { ["enabled"] = true }, cancellationToken).ConfigureAwait(false);
            await SendVoidAsync("Runtime.enable", null, cancellationToken).ConfigureAwait(false);
            // Intercept requests BEFORE any forbidden document loads so the HTTP(S)-only
            // top-level document/redirect policy is enforced up front, not after a bad
            // frame has already navigated. Subresources are continued; only Document
            // requests are assessed.
            await SendVoidAsync("Fetch.enable", new Dictionary<string, object?>
            {
                ["patterns"] = new[] { new { urlPattern = "*", requestStage = "Request" } },
                ["handleAuthRequests"] = false
            }, cancellationToken).ConfigureAwait(false);
            // Deny downloads by default; nothing is ever auto-downloaded or auto-granted.
            await SendVoidAsync("Browser.setDownloadBehavior", new Dictionary<string, object?> { ["behavior"] = "deny" }, cancellationToken).ConfigureAwait(false);
            // Browser-level target discovery (a browser command, not a page-session
            // command). Genuine new pages are attributed by opener id in HandleTargetCreated.
            await _connection.SendAsync(null, "Target.setDiscoverTargets", new Dictionary<string, object?> { ["discover"] = true }, cancellationToken).ConfigureAwait(false);

            Publish(_state with { Status = "Chromium page is ready." });
            await NavigateAsync(_initialAddress, cancellationToken).ConfigureAwait(false);

            _ = QueueInputAsync(() => SendVoidAsync("Page.startScreencast", new Dictionary<string, object?>
            {
                ["format"] = "jpeg",
                ["quality"] = 80,
                ["maxWidth"] = 1280,
                ["maxHeight"] = 800,
                ["everyNthFrame"] = 1
            }, CancellationToken.None));
        }
        catch
        {
            // A failed tab must not leave its event subscription behind.
            _connection.Recv -= OnCdpEvent;
            throw;
        }
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
    public Task StopAsync(CancellationToken cancellationToken) => SendVoidAsync("Page.stopLoading", null, cancellationToken);

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
        _ = QueueInputAsync(() => _connection.SendAsync(null, "Target.closeTarget",
            new Dictionary<string, object?> { ["targetId"] = targetId }, CancellationToken.None));

    private void HandleScreencastFrame(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String) return;
        if (!parameters.TryGetProperty("sessionId", out var session) || session.ValueKind != JsonValueKind.Number) return;
        try { _view.PresentFrame(Convert.FromBase64String(data.GetString()!)); }
        catch (FormatException) { return; }
        _ = QueueInputAsync(() => SendVoidAsync("Page.screencastFrameAck", new Dictionary<string, object?> { ["sessionId"] = session.GetInt32() }, CancellationToken.None));
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

    private void OnKey(object? sender, ChromiumKeyArgs args)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = args.Type,
            ["key"] = args.Key,
            ["code"] = args.Code,
            ["autoRepeat"] = args.AutoRepeat,
            ["windowsVirtualKeyCode"] = args.VirtualKeyCode
        };
        _ = QueueInputAsync(() => SendVoidAsync("Input.dispatchKeyEvent", parameters, CancellationToken.None));
    }

    private void OnText(object? sender, ChromiumTextArgs args)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "char",
            ["text"] = args.Text
        };
        _ = QueueInputAsync(() => SendVoidAsync("Input.dispatchKeyEvent", parameters, CancellationToken.None));
    }

    // ---- Command plumbing ----

    private Task<JsonElement> SendCommandAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        _connection.SendAsync(_sessionId, method, parameters, cancellationToken);

    private async Task SendVoidAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        _ = await _connection.SendAsync(_sessionId, method, parameters, cancellationToken).ConfigureAwait(false);
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
        StateChanged?.Invoke(this, snapshot);
    }

    private void Crash(string reason)
    {
        if (_disposed || _retiring) return;
        Publish(State with { IsLoading = false, Status = reason });
        Crashed?.Invoke(this, new BrowseEngineCrash(reason));
    }

    public void DemandExternalJoin() { }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closeTask is not null) return new(_closeTask);
            _retiring = true;
            _closeTask = CloseCoreAsync();
        }
        return new(_closeTask);
    }

    private async Task CloseCoreAsync()
    {
        var failures = new List<Exception>();
        async Task Join(Task? task) { if (task is null) return; try { await task.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); } }

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
                await _connection.SendAsync(null, "Target.closeTarget",
                    new Dictionary<string, object?> { ["targetId"] = _targetId }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        _connection.Recv -= OnCdpEvent;
        if (_view is ChromiumEngineSurface surface)
        {
            surface.MouseGesture -= OnMouse;
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
