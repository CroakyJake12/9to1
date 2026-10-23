using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
