using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

// These tests verify engine identity and persisted tab custody, not rendering.
public sealed class LinuxWebKitEngineAvailabilityTests
{
    [Theory]
    [InlineData(BrowseEngineKind.Gecko, "Firefox")]
    [InlineData(BrowseEngineKind.Chromium, "Chromium")]
    public async Task WebKit_cannot_be_acquired_as_a_different_engine(BrowseEngineKind engine, string name)
    {
        var factory = new LinuxWebKitEngineFactory();
        Assert.False(factory.IsSupported);
        Assert.False(((IBrowseEngineAvailability)factory).IsEngineSupported(engine));
        var request = new BrowseEngineTabRequest(Guid.NewGuid(), BrowserTabPrivacy.Standard,
            Path.GetFullPath("unused-engine-profile"), new Uri("https://example.com/"), engine);
        var failure = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => factory.CreateAsync(request, TestContext.Current.CancellationToken));
        Assert.Contains(name, failure.Message);
        Assert.Contains("WebKit cannot substitute", failure.Message);
    }

    [Theory]
    [InlineData(BrowseEngineKind.Gecko)]
    [InlineData(BrowseEngineKind.Chromium)]
    public async Task Unavailable_engine_retains_tab_address_profile_and_preference_after_reopen(BrowseEngineKind engine)
    {
        var paths = new FixturePaths();
        var savedId = Guid.NewGuid();
        using (var data = new BrowserDataService(paths))
            await data.SaveTabsAsync([new(savedId, "Stored tab", "https://example.com/original",
                BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow) { ProfileId = "original-profile" }],
                TestContext.Current.CancellationToken);
        var factory = new LinuxWebKitEngineFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, factory, cancellationToken: TestContext.Current.CancellationToken);
        Guid id;
        string profile;
        try
        {
            await chrome.SetDefaultEngineAsync(engine, TestContext.Current.CancellationToken);
            await chrome.SetSelectedTabEngineAsync(engine, TestContext.Current.CancellationToken);
            var state = await chrome.NavigateAsync("https://example.com/kept", TestContext.Current.CancellationToken);
            var tab = state.SelectedTab;
            id = tab.Id; profile = tab.ProfileId;
            Assert.Equal(savedId, id);
            Assert.Equal("original-profile", profile);
            Assert.Equal(engine, tab.Engine);
            Assert.Equal(BrowseEngineState.Unsupported, tab.EngineState);
            Assert.Null(chrome.ObserveOriginalEngine(id));
            Assert.False(tab.IsLoading);
            Assert.Empty(state.History);
        }
        finally { await chrome.DisposeAsync(); }

        await using var reopened = await BrowseChrome.CreateAsync(paths, factory, cancellationToken: TestContext.Current.CancellationToken);
        var restored = Assert.Single(reopened.State.Tabs);
        Assert.Equal(id, restored.Id);
        Assert.Equal(new Uri("https://example.com/kept"), restored.Address.WebUri);
        Assert.Equal(profile, restored.ProfileId);
        Assert.Equal(engine, restored.Engine);
        Assert.Equal(BrowseEngineState.Unsupported, restored.EngineState);
        Assert.Null(reopened.ObserveOriginalEngine(id));
        Assert.Empty(reopened.State.History);
        var next = (await reopened.NewTabAsync(false, TestContext.Current.CancellationToken)).SelectedTab;
        Assert.Equal(engine, next.Engine);
        Assert.Equal(BrowseEngineState.Unsupported, next.EngineState);
    }

    private sealed class FixturePaths : IAppPaths
    {
        public FixturePaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "linux-engine-availability-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "fixture.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
    }
}
