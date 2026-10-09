using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse.Runtime;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

/// <summary>These tests launch the pinned donor binary. They never substitute a fake browser host.</summary>
[Trait("Category", "NativeChromium")]
public sealed class ChromiumNativeRuntimeTests
{
    private static ChromiumRuntimeOptions Options() => new(
        Environment.GetEnvironmentVariable("BROWSE_CHROMIUM_EXE") ?? throw new InvalidOperationException("Native Chromium test executable was not provided."),
        Environment.GetEnvironmentVariable("BROWSE_CHROMIUM_SOURCE_SHA") ?? throw new InvalidOperationException("Native source pin was not provided."),
        Environment.GetEnvironmentVariable("BROWSE_CHROMIUM_EXE_SHA256") ?? throw new InvalidOperationException("Native binary hash was not provided."), Headless: true);

    [Fact]
    public async Task RealDonorNavigationHistoryScriptAndCancellation()
    {
        using var paths = new TestPaths();
        await using var server = new PageServer();
        await using var factory = new ChromiumBrowseEngineFactory(Options());
        var id = Guid.NewGuid();
        await using var tab = (ChromiumEngineTab)await factory.CreateAsync(new(id, BrowserTabPrivacy.Standard, paths.BrowserProfileDirectory,
            server.Address("one"), BrowseEngineKind.Chromium), CancellationToken.None);
        Assert.Equal(id, tab.TabId);
        Assert.Equal(Options().SourceCommit, tab.RuntimeIdentity.SourceCommit);
        Assert.Equal(server.Address("one"), tab.State.Address);
        Assert.Equal("\"one\"", await tab.ExecuteScriptAsync("document.querySelector('h1').textContent", CancellationToken.None));
        await tab.NavigateAsync(server.Address("two"), CancellationToken.None);
        Assert.True(tab.State.CanGoBack);
        await tab.GoBackAsync(CancellationToken.None);
        Assert.Equal(server.Address("one"), tab.State.Address);
        await tab.GoForwardAsync(CancellationToken.None);
        Assert.Equal(server.Address("two"), tab.State.Address);
        await tab.ReloadAsync(CancellationToken.None);
        Assert.Equal("\"two\"", await tab.ExecuteScriptAsync("document.title", CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tab.NavigateAsync(server.Address("cancelled"), cancellation.Token));
        Assert.Equal(server.Address("two"), tab.State.Address);
        await Assert.ThrowsAsync<ArgumentException>(() => tab.NavigateAsync(new Uri("file:///etc/passwd"), CancellationToken.None));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => tab.OpenDeveloperToolsAsync(CancellationToken.None));
        SaveEvidence("native-navigation", tab.RuntimeIdentity, "Real HTTP documents, DOM evaluation, donor history, reload, cancellation and privileged-URL rejection passed; headless only.");
    }

    [Fact]
    public async Task BrowseUsesRealFactoryPreservesIdentityAndDoesNotSilentlyReplaceFirefox()
    {
        using var paths = new TestPaths();
        await using var server = new PageServer();
        using (var data = new BrowserDataService(paths))
            await data.SaveSettingsAsync(data.Settings with { HomePage = server.Address("home").AbsoluteUri }, CancellationToken.None);
        await using var factory = new ChromiumBrowseEngineFactory(Options());
        Guid tabId;
        await using (var chrome = await BrowseChrome.CreateAsync(paths, factory))
        {
            tabId = chrome.State.SelectedTabId;
            Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
            Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
            await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
            Assert.Equal(tabId, chrome.State.SelectedTabId);
            Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
            await chrome.NavigateAsync(server.Address("real-browse").AbsoluteUri);
            Assert.Equal(server.Address("real-browse"), chrome.State.SelectedTab.Address);
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Gecko));
            Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
            Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
            await chrome.NavigateAsync(server.Address("still-working").AbsoluteUri);
        }
        await using var restored = await BrowseChrome.CreateAsync(paths, factory);
        Assert.Equal(tabId, restored.State.SelectedTabId);
        Assert.Equal(BrowseEngineKind.Chromium, restored.State.SelectedTab.Engine);
        Assert.Equal(server.Address("still-working"), restored.State.SelectedTab.Address);
        SaveEvidence("browse-composition", null, "Actual BrowseChrome and BrowserSessionService drove Chromium; stable tab identity, explicit selection, failed-switch preservation, saved session reopen passed. Native CUI embedding was not tested.");
    }

    [Fact]
    public async Task SeparateProfilesDoNotShareDonorStorageAndLastTabReleasesProcess()
    {
        using var paths = new TestPaths();
        await using var server = new PageServer();
        await using var factory = new ChromiumBrowseEngineFactory(Options());
        var firstProfile = Path.Combine(paths.BrowserProfileDirectory, "standard");
        var privateProfile = Path.Combine(paths.BrowserProfileDirectory, "private");
        var first = (ChromiumEngineTab)await factory.CreateAsync(new(Guid.NewGuid(), BrowserTabPrivacy.Standard, firstProfile, server.Address("one"), BrowseEngineKind.Chromium), CancellationToken.None);
        try
        {
            await first.ExecuteScriptAsync("localStorage.setItem('proof', 'standard'); true", CancellationToken.None);
            await using (var same = (ChromiumEngineTab)await factory.CreateAsync(new(Guid.NewGuid(), BrowserTabPrivacy.Standard, firstProfile, server.Address("two"), BrowseEngineKind.Chromium), CancellationToken.None))
            {
                Assert.Equal(first.RuntimeIdentity.ProcessId, same.RuntimeIdentity.ProcessId);
                Assert.Equal("\"standard\"", await same.ExecuteScriptAsync("localStorage.getItem('proof')", CancellationToken.None));
            }
            await using (var isolated = (ChromiumEngineTab)await factory.CreateAsync(new(Guid.NewGuid(), BrowserTabPrivacy.Private, privateProfile, server.Address("private"), BrowseEngineKind.Chromium), CancellationToken.None))
            {
                Assert.NotEqual(first.RuntimeIdentity.ProcessId, isolated.RuntimeIdentity.ProcessId);
                Assert.Equal("null", await isolated.ExecuteScriptAsync("localStorage.getItem('proof')", CancellationToken.None));
            }
            Directory.Delete(privateProfile, recursive: true);
            Assert.False(Directory.Exists(privateProfile));
        }
        finally { await first.DisposeAsync(); }
        using var lease = new FileStream(Path.Combine(firstProfile, ".browse-runtime.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        SaveEvidence("profile-isolation", null, "Same-profile tabs share a donor process/storage; isolated profile uses separate process/storage; last-tab closure releases owned profile lease. Private-profile deletion after closure passed.");
    }

    [Fact]
    public async Task WrongBinaryHashIsRejectedBeforeStartingTheDonor()
    {
        using var paths = new TestPaths();
        await using var factory = new ChromiumBrowseEngineFactory(Options() with { ExecutableSha256 = new string('0', 64) });
        await Assert.ThrowsAsync<InvalidDataException>(() => factory.CreateAsync(new(Guid.NewGuid(), BrowserTabPrivacy.Standard,
            paths.BrowserProfileDirectory, new Uri("about:blank"), BrowseEngineKind.Chromium), CancellationToken.None));
    }

    private static void SaveEvidence(string name, ChromiumRuntimeIdentity? identity, string scope)
    {
        var folder = Environment.GetEnvironmentVariable("BROWSE_RUNTIME_EVIDENCE");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(new
        {
            checkedAtUtc = DateTimeOffset.UtcNow, identity, expectedSource = Options().SourceCommit,
            testOutcome = "passed", scope, nativeCuiEmbedding = "not tested", fullBrowseParity = "not tested", localDonorCompilation = "not run"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "browse-native-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public TestPaths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }

    private sealed class PageServer : IAsyncDisposable
    {
        private readonly TcpListener _server = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly ConcurrentBag<Task> _requests = [];
        public PageServer() { _server.Start(); _loop = ServeAsync(); }
        public Uri Address(string name) => new($"http://127.0.0.1:{((IPEndPoint)_server.LocalEndpoint).Port}/{name}");
        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _server.AcceptTcpClientAsync(_stop.Token);
                    _requests.Add(RespondAsync(client));
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task RespondAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token) ?? "GET / HTTP/1.1";
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                    var title = WebUtility.HtmlEncode(request.Split(' ')[1].Trim('/'));
                    var html = Encoding.UTF8.GetBytes($"<!doctype html><html><head><title>{title}</title></head><body><h1>{title}</h1><button onclick=\"this.textContent='Clicked'\">Click</button></body></html>");
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {html.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _stop.Token); await stream.WriteAsync(html, _stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (IOException) { }
            }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _server.Stop(); await _loop; await Task.WhenAll(_requests); _stop.Dispose(); }
    }
}
