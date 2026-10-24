using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

// These are protocol/approval/transport tests, not a claim that installed Linux
// Chromium has completed a transfer. The endpoint scripts the ORIGINAL paused
// response; the production bridge, approval decorator, store and transport run.
public sealed class ChromiumDownloadsTests
{
    [Fact]
    public async Task Attachment_body_waits_for_real_Home_approval_then_uses_same_response_and_canonical_transport()
    {
        await using var fixture = new Fixture();
        var body = Encoding.UTF8.GetBytes("original attachment bytes\n");
        fixture.Endpoint.Body = body;
        var action = await fixture.PauseAsync(length: body.Length);

        Assert.True(fixture.Bridge.IsOriginalPendingDownload(action));
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action with { Summary = "foreign descriptor" }));
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action with { Id = Guid.NewGuid() }));
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action with { ExpiresAt = action.ExpiresAt.AddSeconds(1) }));
        Assert.False(fixture.Bridge.CanRetireOriginal);
        Assert.Empty(fixture.Endpoint.Commands);
        Assert.Empty(Directory.EnumerateFiles(fixture.DownloadDirectory));

        var result = await fixture.Service.ApproveAsync(action.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Executed, result.State);
        var record = Assert.IsType<BrowserDownloadRecord>(result.Download);
        Assert.Equal(body, await File.ReadAllBytesAsync(record.StoredPath));
        Assert.Equal(body.Length, record.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), record.Sha256);
        Assert.Equal("report.txt", record.FileName);
        Assert.Same(record, Assert.Single(await fixture.Service.GetDownloadsAsync(20, CancellationToken.None)));
        Assert.Equal(new[] { "Fetch.takeResponseBodyAsStream", "IO.read", "IO.close", "Fetch.fulfillRequest" },
            fixture.Endpoint.Commands.Select(command => command.Method));
        Assert.All(fixture.Endpoint.Commands, command => Assert.Equal("original-session", command.Session));
        Assert.All(fixture.Endpoint.Commands.Where(command => command.Method.StartsWith("Fetch.", StringComparison.Ordinal)),
            command => Assert.Equal("original-request", command.Parameters.GetProperty("requestId").GetString()));
        Assert.Equal(204, fixture.Endpoint.Commands.Last().Parameters.GetProperty("responseCode").GetInt32());
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action));
        Assert.True(fixture.Bridge.CanRetireOriginal);
        // The currently configured legacy transport has no original Files owner.
        // A ledger row is deliberately not fabricated into original Files custody.
        Assert.Null(fixture.Bridge.ObserveOriginalDownloadContent(record));
    }

    [Fact]
    public async Task Original_stream_is_read_sequentially_in_bounded_chunks_until_real_EOF()
    {
        await using var fixture = new Fixture();
        var body = Enumerable.Range(0, 70 * 1024).Select(value => (byte)(value % 251)).ToArray();
        fixture.Endpoint.Chunks.Enqueue(body[..(64 * 1024)]);
        fixture.Endpoint.Chunks.Enqueue(body[(64 * 1024)..]);
        var action = await fixture.PauseAsync(length: body.Length);
        Assert.True(fixture.Bridge.TryHandleResponse(Paused(length: body.Length)));
        Assert.Single(await fixture.Service.GetPendingAsync(CancellationToken.None));
        var result = await fixture.Service.ApproveAsync(action.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Executed, result.State);
        Assert.Equal(body, await File.ReadAllBytesAsync(result.Download!.StoredPath));
        var reads = fixture.Endpoint.Commands.Where(command => command.Method == "IO.read").ToArray();
        Assert.Equal(2, reads.Length);
        Assert.All(reads, command =>
        {
            Assert.Equal("original-stream", command.Parameters.GetProperty("handle").GetString());
            Assert.Equal(64 * 1024, command.Parameters.GetProperty("size").GetInt32());
        });
    }

    [Fact]
    public async Task Reject_withdraws_exact_paused_response_without_reading_body_or_creating_a_completed_file()
    {
        await using var fixture = new Fixture();
        var action = await fixture.PauseAsync();
        var result = await fixture.Service.RejectAsync(action.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Rejected, result.State);
        var command = Assert.Single(fixture.Endpoint.Commands);
        Assert.Equal("Fetch.failRequest", command.Method);
        Assert.Equal("original-request", command.Parameters.GetProperty("requestId").GetString());
        Assert.Equal("Aborted", command.Parameters.GetProperty("errorReason").GetString());
        Assert.Empty(await fixture.Service.GetPendingAsync(CancellationToken.None));
        Assert.Empty(await fixture.Service.GetDownloadsAsync(20, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(fixture.DownloadDirectory));
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action));
        Assert.True(fixture.Bridge.CanRetireOriginal);
    }

    [Fact]
    public async Task Stop_uses_canonical_rejection_before_losing_the_original_response()
    {
        await using var fixture = new Fixture();
        var action = await fixture.PauseAsync();
        await fixture.Bridge.WithdrawPendingAsync(CancellationToken.None);
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action));
        Assert.Equal(BrowserActionState.Rejected, (await fixture.Store.GetActionAsync(action.Id, CancellationToken.None))!.State);
        Assert.Equal("Fetch.failRequest", Assert.Single(fixture.Endpoint.Commands).Method);
        Assert.True(fixture.Bridge.CanRetireOriginal);
    }

    [Theory]
    [InlineData("https://example.test/report", "inline", "Document", 200)]
    [InlineData("blob:https://example.test/report", "attachment", "Document", 200)]
    [InlineData("file:///tmp/report", "attachment", "Document", 200)]
    [InlineData("https://example.test/report", "attachment", "XHR", 200)]
    [InlineData("https://example.test/report", "attachment", "Document", 302)]
    public async Task Unsupported_download_forms_remain_outside_the_bridge_and_receive_no_approval_or_body_read(
        string url, string disposition, string resourceType, int status)
    {
        await using var fixture = new Fixture();
        Assert.False(fixture.Bridge.TryHandleResponse(Paused(url, disposition, resourceType, status)));
        Assert.Empty(await fixture.Service.GetPendingAsync(CancellationToken.None));
        Assert.Empty(fixture.Endpoint.Commands);
        Assert.True(fixture.Bridge.CanRetireOriginal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Truncated_or_oversized_original_chunks_fail_without_a_completed_ledger_record(bool oversized)
    {
        await using var fixture = new Fixture();
        fixture.Endpoint.Body = oversized ? new byte[64 * 1024 + 1] : Encoding.UTF8.GetBytes("short");
        var action = await fixture.PauseAsync(length: oversized ? null : 100);
        var result = await fixture.Service.ApproveAsync(action.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Failed, result.State);
        Assert.Null(result.Download);
        Assert.Empty(await fixture.Service.GetDownloadsAsync(20, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(fixture.DownloadDirectory));
        Assert.Contains(fixture.Endpoint.Commands, command => command.Method == "Fetch.failRequest");
        Assert.Contains(fixture.Endpoint.Commands, command => command.Method == "IO.close");
        Assert.DoesNotContain(fixture.Endpoint.Commands, command => command.Method == "Fetch.fulfillRequest");
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action));
        // The SAME failed stream remains an original source, even though the
        // canonical service returned a truthful Failed result.
        await fixture.ExpectRetainedFailuresAsync();
    }

    [Fact]
    public async Task Private_original_action_does_not_create_persisted_download_or_audit_metadata()
    {
        await using var fixture = new Fixture(isPrivate: true);
        fixture.Endpoint.Body = Encoding.UTF8.GetBytes("private original");
        var action = await fixture.PauseAsync(length: fixture.Endpoint.Body.Length);
        Assert.True(fixture.Bridge.IsOriginalPendingDownload(action));
        Assert.Null(await fixture.Store.GetActionAsync(action.Id, CancellationToken.None));
        var result = await fixture.Service.ApproveAsync(action.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Executed, result.State);
        Assert.NotNull(result.Download);
        Assert.Empty(await fixture.Store.GetDownloadsAsync(20, CancellationToken.None));
        Assert.Empty(await fixture.Store.GetAuditAsync(20, CancellationToken.None));
        Assert.Empty(await fixture.Service.GetPendingAsync(CancellationToken.None));
        Assert.False(fixture.Bridge.IsOriginalPendingDownload(action));
    }

    private static JsonElement Paused(string url = "https://example.test/report", string disposition = "attachment; filename=report.txt",
        string resourceType = "Document", int status = 200, long? length = null)
    {
        var headers = new List<object> { new { name = "Content-Disposition", value = disposition }, new { name = "Content-Type", value = "text/plain" } };
        if (length is { } expected) headers.Add(new { name = "Content-Length", value = expected.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return JsonSerializer.SerializeToElement(new { requestId = "original-request", request = new { url }, resourceType, responseStatusCode = status, responseHeaders = headers });
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-linux-download-test-" + Guid.NewGuid().ToString("N"));
        private bool _closed;
        internal ScriptedEndpoint Endpoint { get; } = new();
        internal BrowserAutomationStore Store { get; }
        internal BrowserNativeDownloadAutomationService Service { get; }
        internal ChromiumResponseDownloadBridge Bridge { get; }
        internal string DownloadDirectory => Path.Combine(_root, "downloads");
        internal Fixture(bool isPrivate = false)
        {
            Directory.CreateDirectory(_root);
            Store = new(new Paths(_root));
            var policy = new AllowFixturePolicy();
            Service = new(new StoreObservations(Store), policy, Store);
            Bridge = new(Endpoint, "original-session", () => new Uri("https://example.test/page"), isPrivate,
                new BrowserDownloadTransport(policy, DownloadDirectory), Service);
        }
        internal async Task<BrowserPendingAction> PauseAsync(long? length = null)
        {
            Assert.True(Bridge.TryHandleResponse(Paused(length: length)));
            await Task.WhenAll(Bridge.OriginalSources);
            return Assert.Single(await Service.GetPendingAsync(CancellationToken.None));
        }
        internal async Task ExpectRetainedFailuresAsync()
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Bridge.DisposeAsync().AsTask());
            await Assert.ThrowsAnyAsync<Exception>(() => Service.DisposeAsync().AsTask());
            _closed = true;
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_closed) { await Bridge.DisposeAsync(); await Service.DisposeAsync(); }
            }
            finally { Store.Dispose(); Directory.Delete(_root, recursive: true); }
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "profiles");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    private sealed class AllowFixturePolicy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new BrowserNavigationAssessment(address, true, "scripted fixture", [])); }
    }
    private sealed class StoreObservations(IBrowserAutomationStore store) : IBrowserAutomationService
    {
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken token) => store.GetPendingAsync(token);
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken token) => store.GetDownloadsAsync(limit, token);
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken token) => store.GetAuditAsync(limit, token);
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> NavigateAsync(string address, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickReferenceAsync(string reference, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FillReferenceAsync(string reference, string value, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? name, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> RejectAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed record Command(string? Session, string Method, JsonElement Parameters);
    private sealed class ScriptedEndpoint : IChromiumEndpoint
    {
        internal byte[] Body { get; set; } = [];
        internal Queue<byte[]> Chunks { get; } = new();
        internal List<Command> Commands { get; } = [];
        public event EventHandler<CdpEvent>? Recv { add { } remove { } }
        public Task<JsonElement> SendAsync(string? sessionId, string method, object? parameters, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commands.Add(new(sessionId, method, JsonSerializer.SerializeToElement(parameters)));
            object result = method switch
            {
                "Fetch.takeResponseBodyAsStream" => new { stream = "original-stream" },
                "IO.read" => ReadOriginalChunk(),
                "IO.close" or "Fetch.failRequest" or "Fetch.fulfillRequest" => new { },
                _ => throw new InvalidOperationException("Unexpected command; the transfer must not replay a URL or change global download policy: " + method)
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        private object ReadOriginalChunk()
        {
            var bytes = Chunks.Count == 0 ? Body : Chunks.Dequeue();
            return new { data = Convert.ToBase64String(bytes), base64Encoded = true, eof = Chunks.Count == 0 };
        }
        public Task<string> CreateTargetAsync(string url, CancellationToken token) => throw new NotSupportedException();
        public Task<string> AttachAsync(string targetId, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// This deterministic endpoint verifies ChromiumEngineTab's CDP contract only. It
// is not installed-Chromium execution or native browser acceptance evidence.
public sealed class ChromiumHistoryNavigationProtocolTests
{
    [Theory]
    [InlineData("fragment")]
    [InlineData("historyApi")]
    public async Task Same_document_top_level_navigation_updates_url_and_refreshes_history_without_leaking_subframes(string navigationType)
    {
        var endpoint = new HistoryEndpoint();
        var pool = new ChromiumProfilePool((_, _) => Task.FromResult<IChromiumEndpoint>(endpoint));
        var profile = Path.Combine(Path.GetTempPath(), "chromium-history-same-document-fixture");
        var lease = await pool.AcquireAsync(profile, CancellationToken.None);
        var initial = new Uri("https://history.example/current");
        var request = new BrowseEngineTabRequest(Guid.NewGuid(), BrowserTabPrivacy.Standard, profile, initial, BrowseEngineKind.Chromium);
        await using var tab = new ChromiumEngineTab(request, lease, new HistoryView());
        await tab.InitializeAsync(CancellationToken.None);

        using (var mainFrame = JsonDocument.Parse(JsonSerializer.Serialize(new { frame = new { id = "main-frame", url = initial.AbsoluteUri } })))
            endpoint.Emit(new("Page.frameNavigated", mainFrame.RootElement.Clone(), "history-session"));
        endpoint.Commands.Clear();

        using (var subframeNavigation = JsonDocument.Parse(JsonSerializer.Serialize(new
            { frameId = "child-frame", url = "https://history.example/child#section", navigationType = "fragment" })))
            endpoint.Emit(new("Page.navigatedWithinDocument", subframeNavigation.RootElement.Clone(), "history-session"));
        Assert.Equal(initial, tab.State.Address);
        Assert.Empty(endpoint.Commands);

        var fragment = new Uri("https://history.example/current#section");
        var refreshed = new TaskCompletionSource<BrowserSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canonicalStateChange = new TaskCompletionSource<BrowserSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        tab.StateChanged += (_, snapshot) =>
        {
            if (snapshot.Address == fragment && snapshot.CanGoBack) refreshed.TrySetResult(snapshot);
        };
        tab.NavigationStateChanged += (_, snapshot) =>
        {
            if (snapshot.Address == fragment) canonicalStateChange.TrySetResult(snapshot);
        };
        using (var mainDocumentNavigation = JsonDocument.Parse(JsonSerializer.Serialize(new
            { frameId = "main-frame", url = fragment.AbsoluteUri, navigationType })))
            endpoint.Emit(new("Page.navigatedWithinDocument", mainDocumentNavigation.RootElement.Clone(), "history-session"));

        var state = await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(fragment, (await canonicalStateChange.Task.WaitAsync(TimeSpan.FromSeconds(5))).Address);
        Assert.Equal(fragment, state.Address);
        Assert.True(state.CanGoBack);
        Assert.False(state.CanGoForward);
        Assert.Equal("Page.getNavigationHistory", Assert.Single(endpoint.Commands).Method);

        using var deniedRoute = JsonDocument.Parse(JsonSerializer.Serialize(new
            { frameId = "main-frame", url = "file:///private/document", navigationType = "historyApi" }));
        endpoint.Commands.Clear();
        endpoint.Emit(new("Page.navigatedWithinDocument", deniedRoute.RootElement.Clone(), "history-session"));
        Assert.Equal(fragment, tab.State.Address);
        Assert.Equal("Internal or privileged routes cannot be opened through the Chromium web backend.", tab.State.Status);
        Assert.Empty(endpoint.Commands);

        using var loadParameters = JsonDocument.Parse("{}");
        endpoint.Emit(new("Page.loadEventFired", loadParameters.RootElement.Clone(), "history-session"));
        Assert.Equal("Chromium page loaded.", tab.State.Status);
    }

    [Fact]
    public async Task Completed_page_refreshes_back_forward_capabilities_from_CDPhistory()
    {
        var endpoint = new HistoryEndpoint();
        var pool = new ChromiumProfilePool((_, _) => Task.FromResult<IChromiumEndpoint>(endpoint));
        var profile = Path.Combine(Path.GetTempPath(), "chromium-history-refresh-fixture");
        var lease = await pool.AcquireAsync(profile, CancellationToken.None);
        var request = new BrowseEngineTabRequest(Guid.NewGuid(), BrowserTabPrivacy.Standard, profile,
            new Uri("https://history.example/current"), BrowseEngineKind.Chromium);
        await using var tab = new ChromiumEngineTab(request, lease, new HistoryView());
        await tab.InitializeAsync(CancellationToken.None);
        endpoint.Commands.Clear();
        var committed = new TaskCompletionSource<BrowserSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource<BrowserSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        tab.PageCommitted += (_, snapshot) => committed.TrySetResult(snapshot);
        tab.StateChanged += (_, snapshot) =>
        {
            if (snapshot.CanGoBack || snapshot.CanGoForward) refreshed.TrySetResult(snapshot);
        };

        using var emptyParameters = JsonDocument.Parse("{}");
        endpoint.Emit(new("Page.loadEventFired", emptyParameters.RootElement.Clone(), "history-session"));

        var page = await committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var historyState = await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new Uri("https://history.example/current"), page.Address);
        Assert.True(historyState.CanGoBack);
        Assert.False(historyState.CanGoForward);
        var refresh = Assert.Single(endpoint.Commands);
        Assert.Equal("Page.getNavigationHistory", refresh.Method);
        Assert.Equal("history-session", refresh.SessionId);
    }

    [Fact]
    public async Task Back_forward_and_boundary_use_exact_CDP_history_entry_ids_without_URL_replay()
    {
        var endpoint = new HistoryEndpoint();
        var pool = new ChromiumProfilePool((_, _) => Task.FromResult<IChromiumEndpoint>(endpoint));
        var lease = await pool.AcquireAsync(Path.Combine(Path.GetTempPath(), "chromium-history-fixture"), CancellationToken.None);
        var request = new BrowseEngineTabRequest(Guid.NewGuid(), BrowserTabPrivacy.Standard,
            Path.Combine(Path.GetTempPath(), "chromium-history-fixture"), new Uri("https://history.example/current"), BrowseEngineKind.Chromium);
        await using var tab = new ChromiumEngineTab(request, lease, new HistoryView());
        await tab.InitializeAsync(CancellationToken.None);
        endpoint.Commands.Clear();

        await tab.GoBackAsync(CancellationToken.None);

        Assert.Equal(new[] { "Page.getNavigationHistory", "Page.navigateToHistoryEntry" },
            endpoint.Commands.Select(command => command.Method));
        var backRead = endpoint.Commands[0];
        Assert.Equal("history-session", backRead.SessionId);
        Assert.Equal(JsonValueKind.Null, backRead.Parameters.ValueKind);
        var back = endpoint.Commands[1];
        Assert.Equal("history-session", back.SessionId);
        Assert.Equal(101, back.Parameters.GetProperty("entryId").GetInt32());
        Assert.DoesNotContain("Page.navigate", endpoint.Commands.Select(command => command.Method));
        Assert.False(tab.State.CanGoBack);
        Assert.True(tab.State.CanGoForward);

        endpoint.Commands.Clear();
        await tab.GoForwardAsync(CancellationToken.None);
        Assert.Equal(new[] { "Page.getNavigationHistory", "Page.navigateToHistoryEntry" },
            endpoint.Commands.Select(command => command.Method));
        Assert.Equal(102, endpoint.Commands[1].Parameters.GetProperty("entryId").GetInt32());
        Assert.True(tab.State.CanGoBack);
        Assert.False(tab.State.CanGoForward);

        endpoint.CurrentIndex = endpoint.Entries.Length - 1;
        endpoint.Commands.Clear();
        await tab.GoForwardAsync(CancellationToken.None);
        Assert.Equal("Page.getNavigationHistory", Assert.Single(endpoint.Commands).Method);
        Assert.Equal("No forward page.", tab.State.Status);
    }

    [Fact]
    public async Task Pre_cancelled_history_step_dispatches_no_CDP_command_or_state_change()
    {
        var endpoint = new HistoryEndpoint();
        var pool = new ChromiumProfilePool((_, _) => Task.FromResult<IChromiumEndpoint>(endpoint));
        var profile = Path.Combine(Path.GetTempPath(), "chromium-history-cancel-fixture");
        var lease = await pool.AcquireAsync(profile, CancellationToken.None);
        var request = new BrowseEngineTabRequest(Guid.NewGuid(), BrowserTabPrivacy.Standard, profile,
            new Uri("https://history.example/current"), BrowseEngineKind.Chromium);
        await using var tab = new ChromiumEngineTab(request, lease, new HistoryView());
        await tab.InitializeAsync(CancellationToken.None);
        endpoint.Commands.Clear();
        var before = tab.State;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tab.GoBackAsync(cancellation.Token));

        Assert.Empty(endpoint.Commands);
        Assert.Equal(before, tab.State);
    }

    [Fact]
    public async Task Failed_CDP_history_navigation_does_not_publish_a_false_back_forward_state()
    {
        var endpoint = new HistoryEndpoint();
        var pool = new ChromiumProfilePool((_, _) => Task.FromResult<IChromiumEndpoint>(endpoint));
        var profile = Path.Combine(Path.GetTempPath(), "chromium-history-failure-fixture");
        var lease = await pool.AcquireAsync(profile, CancellationToken.None);
        var request = new BrowseEngineTabRequest(Guid.NewGuid(), BrowserTabPrivacy.Standard, profile,
            new Uri("https://history.example/current"), BrowseEngineKind.Chromium);
        await using var tab = new ChromiumEngineTab(request, lease, new HistoryView());
        await tab.InitializeAsync(CancellationToken.None);
        await tab.GoBackAsync(CancellationToken.None);
        Assert.False(tab.State.CanGoBack);
        Assert.True(tab.State.CanGoForward);
        var before = tab.State;
        endpoint.FailNextHistoryNavigation = true;
        endpoint.Commands.Clear();

        await Assert.ThrowsAsync<ChromiumCdpException>(() => tab.GoForwardAsync(CancellationToken.None));

        Assert.Equal(new[] { "Page.getNavigationHistory", "Page.navigateToHistoryEntry" },
            endpoint.Commands.Select(command => command.Method));
        Assert.Equal(before, tab.State);
        Assert.Equal(0, endpoint.CurrentIndex);
    }

    private sealed record CdpCommand(string? SessionId, string Method, JsonElement Parameters);

    private sealed class HistoryEndpoint : IChromiumEndpoint
    {
        public event EventHandler<CdpEvent>? Recv;
        public List<CdpCommand> Commands { get; } = [];
        public (int Id, string Url)[] Entries { get; } =
        [
            (101, "https://history.example/first"),
            (102, "https://history.example/current")
        ];
        public int CurrentIndex { get; set; } = 1;
        public bool FailNextHistoryNavigation { get; set; }
        public void Emit(CdpEvent message) => Recv?.Invoke(this, message);

        public Task<JsonElement> SendAsync(string? sessionId, string method, object? parameters, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var captured = JsonSerializer.SerializeToElement(parameters);
            Commands.Add(new(sessionId, method, captured));
            if (method == "Page.navigateToHistoryEntry" && FailNextHistoryNavigation)
            {
                FailNextHistoryNavigation = false;
                throw new ChromiumCdpException("Scripted history-entry navigation failure.");
            }
            object result = method switch
            {
                "Page.getNavigationHistory" => new
                {
                    currentIndex = CurrentIndex,
                    entries = Entries.Select(entry => new { id = entry.Id, url = entry.Url, title = entry.Url, transitionType = "typed" }).ToArray()
                },
                "Page.navigateToHistoryEntry" => NavigateTo(parameters),
                _ => new { }
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }

        private object NavigateTo(object? parameters)
        {
            var entryId = JsonSerializer.SerializeToElement(parameters).GetProperty("entryId").GetInt32();
            CurrentIndex = Array.FindIndex(Entries, entry => entry.Id == entryId);
            if (CurrentIndex < 0) throw new InvalidOperationException("The test requested an unknown history entry ID.");
            return new { };
        }

        public Task<string> CreateTargetAsync(string url, CancellationToken token) => Task.FromResult("history-target");
        public Task<string> AttachAsync(string targetId, CancellationToken token) => Task.FromResult("history-session");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HistoryView : IChromiumEngineView
    {
        public Control View { get; } = new Border();
        public void PresentFrame(byte[] jpeg) { }
    }
}
