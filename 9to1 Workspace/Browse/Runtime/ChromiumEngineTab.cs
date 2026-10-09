using System.Text.Json;
using Haven.Browser;

namespace HavenOS.Apps.Browse.Runtime;

/// <summary>A real native Chromium target, attached to Browse's existing tab/session contract.</summary>
public sealed class ChromiumEngineTab : IBrowseEngineTab
{
    private readonly ChromiumProcess _runtime;
    private readonly Func<ValueTask> _release;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly string _sessionId;
    private int _disposed;
    private BrowserSnapshot _state = new(new Uri("about:blank"), "New tab", false, false, false, "Chromium is starting.");
    public Guid TabId { get; }
    public string TargetId { get; }
    public ChromiumRuntimeIdentity RuntimeIdentity => _runtime.Identity;
    public BrowserSnapshot State => Volatile.Read(ref _state);
    public event EventHandler<BrowserSnapshot>? StateChanged;
    public event EventHandler<BrowsePopupRequest>? PopupRequested;
    public event EventHandler<BrowseEngineCrash>? Crashed;

    private ChromiumEngineTab(ChromiumProcess runtime, Guid tabId, string targetId, string sessionId, Func<ValueTask> release)
    {
        _runtime = runtime; TabId = tabId; TargetId = targetId; _sessionId = sessionId; _release = release;
        runtime.Connection.EventReceived += OnEventAsync;
        runtime.Connection.Disconnected += OnDisconnected;
    }

    internal static async Task<ChromiumEngineTab> CreateAsync(ChromiumProcess runtime, BrowseEngineTabRequest request,
        Func<ValueTask> release, CancellationToken cancellationToken)
    {
        var target = await runtime.Connection.CallAsync("Target.createTarget", new { url = "about:blank", newWindow = !runtime.Identity.Headless }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var targetId = target.GetProperty("targetId").GetString()!;
        ChromiumEngineTab? tab = null;
        try
        {
            var session = await runtime.Connection.CallAsync("Target.attachToTarget", new { targetId, flatten = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
            tab = new(runtime, request.TabId, targetId, session.GetProperty("sessionId").GetString()!, release);
            await tab.CallAsync("Page.enable", cancellationToken: cancellationToken).ConfigureAwait(false);
            await tab.CallAsync("Runtime.enable", cancellationToken: cancellationToken).ConfigureAwait(false);
            await tab.CallAsync("Page.setInterceptFileChooserDialog", new { enabled = true }, cancellationToken).ConfigureAwait(false);
            await tab.NavigateAsync(request.InitialAddress, cancellationToken).ConfigureAwait(false);
            return tab;
        }
        catch
        {
            if (tab is not null) tab.Unsubscribe();
            try { await runtime.Connection.CallAsync("Target.closeTarget", new { targetId }, cancellationToken: CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            // The factory releases the profile reservation on failed creation.
            throw;
        }
    }

    internal static void ValidateAddress(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || (address.Scheme is not ("http" or "https") && address.AbsoluteUri != "about:blank"))
            throw new ArgumentException("This web-engine adapter accepts HTTP(S) and about:blank only. Privileged CUI/9to1 routes require the native app router.", nameof(address));
        if (!string.IsNullOrEmpty(address.UserInfo)) throw new ArgumentException("URLs containing credentials are not accepted.", nameof(address));
    }

    public Task NavigateAsync(Uri address, CancellationToken cancellationToken)
    {
        ValidateAddress(address);
        return OperateAsync(async token =>
        {
            Update(State with { IsLoading = true, Status = "Loading in Chromium." });
            var response = await CallAsync("Page.navigate", new { url = address.AbsoluteUri }, token).ConfigureAwait(false);
            if (response.TryGetProperty("errorText", out var error)) throw new IOException(error.GetString());
            await WaitForDocumentAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task GoBackAsync(CancellationToken cancellationToken) => MoveHistoryAsync(-1, cancellationToken);
    public Task GoForwardAsync(CancellationToken cancellationToken) => MoveHistoryAsync(1, cancellationToken);
    private Task MoveHistoryAsync(int offset, CancellationToken cancellationToken) => OperateAsync(async token =>
    {
        var history = await CallAsync("Page.getNavigationHistory", cancellationToken: token).ConfigureAwait(false);
        var index = history.GetProperty("currentIndex").GetInt32() + offset;
        var entries = history.GetProperty("entries");
        if (index < 0 || index >= entries.GetArrayLength()) return;
        await CallAsync("Page.navigateToHistoryEntry", new { entryId = entries[index].GetProperty("id").GetInt32() }, token).ConfigureAwait(false);
        await WaitForDocumentAsync(token).ConfigureAwait(false);
    }, cancellationToken);
    public Task ReloadAsync(CancellationToken cancellationToken) => OperateAsync(async token =>
    {
        await CallAsync("Page.reload", cancellationToken: token).ConfigureAwait(false);
        await WaitForDocumentAsync(token).ConfigureAwait(false);
    }, cancellationToken);
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await CallAsync("Page.stopLoading", cancellationToken: cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        Update(State with { IsLoading = false, Status = "Stopped." });
    }
    public async Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var response = await CallAsync("Runtime.evaluate", new { expression = script, returnByValue = true, awaitPromise = true }, cancellationToken).ConfigureAwait(false);
        if (response.TryGetProperty("exceptionDetails", out var exception))
            throw new InvalidOperationException("Page script failed: " + exception.GetProperty("text").GetString());
        return response.GetProperty("result").TryGetProperty("value", out var value) ? value.GetRawText() : null;
    }
    public async Task OpenDeveloperToolsAsync(CancellationToken cancellationToken)
    {
        if (RuntimeIdentity.Headless) throw new PlatformNotSupportedException("Headless validation cannot display developer tools.");
        await _runtime.Connection.CallAsync("Target.openDevTools", new { targetId = TargetId }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task OperateAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(45));
        await _operations.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await operation(linked.Token).ConfigureAwait(false); await RefreshAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                try { await CallAsync("Page.stopLoading", cancellationToken: _lifetime.Token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { }
                Update(State with { IsLoading = false, Status = "Navigation cancelled." });
            }
            throw;
        }
        finally { _operations.Release(); }
    }

    private async Task WaitForDocumentAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ready = await ExecuteScriptAsync("document.readyState", cancellationToken).ConfigureAwait(false);
                if (ready == "\"complete\"") return;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("context", StringComparison.OrdinalIgnoreCase)) { }
            await Task.Delay(30, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var history = await CallAsync("Page.getNavigationHistory", cancellationToken: cancellationToken).ConfigureAwait(false);
        var entries = history.GetProperty("entries");
        var index = history.GetProperty("currentIndex").GetInt32();
        if (index < 0 || index >= entries.GetArrayLength()) return;
        var entry = entries[index];
        var address = new Uri(entry.GetProperty("url").GetString()!);
        var title = entry.GetProperty("title").GetString() ?? address.Host;
        Update(new BrowserSnapshot(address, title, index > 0, index + 1 < entries.GetArrayLength(), false, "Ready in Chromium."));
    }

    private Task<JsonElement> CallAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _runtime.Connection.CallAsync(method, parameters, _sessionId, cancellationToken);
    }
    private async Task OnEventAsync(JsonElement message)
    {
        if (_lifetime.IsCancellationRequested || !message.TryGetProperty("method", out var method)) return;
        var name = method.GetString();
        if (name == "Target.targetCrashed" && message.GetProperty("params").GetProperty("targetId").GetString() == TargetId)
        { Crashed?.Invoke(this, new("Chromium's native renderer process crashed.")); return; }
        if (name == "Target.targetCreated")
        {
            var info = message.GetProperty("params").GetProperty("targetInfo");
            if (info.TryGetProperty("openerId", out var opener) && opener.GetString() == TargetId)
            {
                // The canonical Browse popup policy owns any resulting tab, not a second donor-managed tab list.
                await _runtime.Connection.CallAsync("Target.closeTarget", new { targetId = info.GetProperty("targetId").GetString() }, cancellationToken: _lifetime.Token).ConfigureAwait(false);
                if (Uri.TryCreate(info.GetProperty("url").GetString(), UriKind.Absolute, out var address) && address.Scheme is "http" or "https")
                    PopupRequested?.Invoke(this, new(address));
            }
            return;
        }
        if (!message.TryGetProperty("sessionId", out var session) || session.GetString() != _sessionId) return;
        if (name == "Page.javascriptDialogOpening")
            await CallAsync("Page.handleJavaScriptDialog", new { accept = false }, _lifetime.Token).ConfigureAwait(false);
        if (name is "Page.loadEventFired" or "Page.navigatedWithinDocument")
        {
            try { await RefreshAsync(_lifetime.Token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException) { }
        }
    }
    private void OnDisconnected(Exception exception)
    {
        if (!_lifetime.IsCancellationRequested) Crashed?.Invoke(this, new("Chromium's native runtime disconnected: " + exception.Message));
    }
    private void Update(BrowserSnapshot state)
    {
        if (_lifetime.IsCancellationRequested) return;
        Volatile.Write(ref _state, state);
        StateChanged?.Invoke(this, state);
    }
    private void Unsubscribe()
    {
        _lifetime.Cancel();
        _runtime.Connection.EventReceived -= OnEventAsync;
        _runtime.Connection.Disconnected -= OnDisconnected;
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Unsubscribe();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _runtime.Connection.CallAsync("Target.closeTarget", new { targetId = TargetId }, cancellationToken: timeout.Token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        finally { await _release().ConfigureAwait(false); }
    }
}
