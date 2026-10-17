using Haven.Application;
using Haven.Browser;
using HavenOS.Apps.Browse;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseAppRouteTests
{
    [Theory]
    [InlineData("browse")]
    [InlineData("browser")]
    [InlineData("web")]
    [InlineData(" WEB ")]
    public void ExistingBrowseAliasesAreAccepted(string routeKey)
    {
        Assert.True(BrowseAppRoute.Matches(routeKey));
    }

    [Fact]
    public async Task UnknownRouteIsRejectedBeforeBrowserNavigation()
    {
        using var paths = new TestPaths();
        using var session = new BrowserSessionService(paths);
        var host = new FakeEmbeddedBrowserHost();
        session.Attach(host);
        var route = new BrowseAppRoute(session);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            route.NavigateAsync("spaces", "example.com"));

        Assert.Null(host.LastNavigated);
    }

    [Fact]
    public async Task BareDomainUsesExistingBrowserSessionHttpsNormalization()
    {
        using var paths = new TestPaths();
        using var session = new BrowserSessionService(paths);
        var host = new FakeEmbeddedBrowserHost();
        session.Attach(host);
        var route = new BrowseAppRoute(session);

        var result = await route.NavigateAsync("browse", " example.com ");

        var expected = new Uri("https://example.com/", UriKind.Absolute);
        Assert.Equal(expected, host.LastNavigated);
        Assert.Equal(expected, result.Snapshot.Address);
        Assert.True(result.IsInteractiveAvailable);
        Assert.Equal($"Navigating to {expected}.", result.Status);
    }

    [Fact]
    public async Task CancellationFlowsIntoExistingBrowserHostCapability()
    {
        using var paths = new TestPaths();
        using var session = new BrowserSessionService(paths);
        var host = new FakeEmbeddedBrowserHost();
        session.Attach(host);
        var route = new BrowseAppRoute(session);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            route.NavigateAsync("web", "example.com", cancellation.Token));

        Assert.Null(host.LastNavigated);
    }

    [Theory]
    [InlineData("9to1://Write/private-document")]
    [InlineData("browse://unregistered")]
    [InlineData("  CUI://private/custom-page")]
    public async Task Reserved_native_routes_never_become_external_search_queries(string address)
    {
        using var paths = new TestPaths();
        using var session = new BrowserSessionService(paths);
        var host = new FakeEmbeddedBrowserHost(); session.Attach(host);
        var result = await new BrowseAppRoute(session).NavigateAsync("browse", address);
        Assert.Null(host.LastNavigated);
        Assert.Contains("not registered", result.Status);
        await using var chrome = await BrowseChrome.CreateAsync(paths);
        var original = chrome.State.SelectedTab;
        var state = await chrome.NavigateAsync(address);
        if (Haven.Core.AppContentAddress.TryParse(address, out var parsed) && parsed!.Namespace == Haven.Core.AppContentNamespace.NativeApp)
        {
            Assert.Equal(original.Id, state.SelectedTabId); Assert.Equal(parsed, state.SelectedTab.Address);
            Assert.Null(state.SelectedTab.Address.WebUri); Assert.Equal(BrowseEngineState.Unsupported, state.SelectedTab.EngineState);
        }
        else Assert.Equal(original, state.SelectedTab);
        Assert.Empty(state.History);
        Assert.Contains("not registered", state.Status);
    }

    private sealed class FakeEmbeddedBrowserHost : IEmbeddedBrowserHost
    {
        public event EventHandler<BrowserSnapshot>? StateChanged;

        public BrowserSnapshot State { get; private set; } =
            new(null, "Browser", false, false, false, "Idle");

        public Uri? LastNavigated { get; private set; }

        public Task NavigateAsync(Uri address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastNavigated = address;
            State = new BrowserSnapshot(address, address.Host, false, false, false, "Ready");
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task GoBackAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task GoForwardAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public Task OpenDeveloperToolsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "havenos-browse-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
