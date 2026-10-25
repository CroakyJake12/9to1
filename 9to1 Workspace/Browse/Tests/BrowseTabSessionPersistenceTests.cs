using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Fact]
    public async Task Canonical_session_projection_replaces_tab_storage_and_preserves_original_browser_data()
    {
        var paths = new PreservedPaths(); var legacyId = Guid.NewGuid(); var webId = Guid.NewGuid(); var appId = Guid.NewGuid();
        using (var data = new BrowserDataService(paths))
        {
            await data.SaveTabsAsync([new(legacyId, "Legacy", "https://legacy.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)], default);
            await data.AddBookmarkAsync("Original bookmark", "https://bookmark.example/", "", default);
            await data.RecordVisitAsync("Original history", "https://history.example/", false, default);
        }
        var session = new TabSessionFixture(new([
            new(webId, "Web", "https://projected.example/", BrowserTabPrivacy.Standard, "original-group", DateTimeOffset.UtcNow) { ProfileId = "profile" },
            new(appId, "Assistant", "9to1://assistants/library/id", BrowserTabPrivacy.Standard, "original-group", DateTimeOffset.UtcNow) { ProfileId = "profile" },
            new(Guid.NewGuid(), "Private", "https://private.example/", BrowserTabPrivacy.Private, "", DateTimeOffset.UtcNow)
        ], appId));
        var apps = new AppFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: apps, homeProfileId: "profile", tabSessionPersistence: session);
        Assert.Equal(appId, chrome.State.SelectedTabId); Assert.Equal(2, chrome.State.Tabs.Count);
        Assert.Equal("9to1://spaces/assistants/library/id", Assert.Single(apps.Requests).Address.Value);
        Assert.Single(chrome.State.Bookmarks); Assert.Single(chrome.State.History);
        Assert.Equal(appId, session.Saves[^1].SelectedTabId);
        await chrome.SelectTabAsync(webId);
        Assert.Equal(webId, session.Saves[^1].SelectedTabId);
        Assert.All(session.Saves[^1].Tabs, tab => Assert.Equal("original-group", tab.Group));
        await chrome.NewTabAsync(true);
        Assert.Null(session.Saves[^1].SelectedTabId);
        Assert.DoesNotContain(session.Saves[^1].Tabs, tab => tab.Privacy == BrowserTabPrivacy.Private);
        using var originalStore = new BrowserDataService(paths);
        Assert.Equal(legacyId, Assert.Single(originalStore.Tabs).Id);
        Assert.Single(originalStore.Bookmarks); Assert.Single(originalStore.History);
    }

    [Fact]
    public async Task Canonical_session_selection_save_retains_the_actual_pending_owner_task()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], first));
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        var actualSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); session.NextSave = actualSave.Task;
        var selection = chrome.SelectTabAsync(second);
        Assert.False(selection.IsCompleted); Assert.Contains(actualSave.Task, chrome.OriginalNativeSources);
        Assert.Equal(second, session.Saves[^1].SelectedTabId);
        actualSave.SetResult(); await selection;
        Assert.Equal(second, chrome.State.SelectedTabId);
    }

    [Fact]
    public async Task Canonical_session_save_failure_remains_in_original_retirement_evidence()
    {
        var session = new TabSessionFixture(null);
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        var failure = new IOException("Canonical session write failed"); var actual = Task.FromException(failure); session.NextSave = actual;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => chrome.SelectTabAsync(chrome.State.SelectedTabId)));
        Assert.Contains(actual, chrome.OriginalNativeSources);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions);
    }

    [Fact]
    public async Task Standard_engine_storage_isolated_by_profile_and_stable_across_restart()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        const string currentProfile = "profile-current";
        const string otherProfile = "profile-other";
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Chromium,
            new Dictionary<string, BrowseEngineKind>()));
        var currentFirst = Guid.NewGuid(); var other = Guid.NewGuid(); var currentSecond = Guid.NewGuid();
        var projection = new BrowseTabSessionProjection([
            new(currentFirst, "Current profile", "https://one.example/", BrowserTabPrivacy.Standard,
                string.Empty, DateTimeOffset.UtcNow) { ProfileId = currentProfile },
            new(other, "Other profile", "https://two.example/", BrowserTabPrivacy.Standard,
                string.Empty, DateTimeOffset.UtcNow) { ProfileId = otherProfile },
            new(currentSecond, "Current profile again", "https://three.example/", BrowserTabPrivacy.Standard,
                string.Empty, DateTimeOffset.UtcNow) { ProfileId = currentProfile }
        ], currentFirst);
        var currentDirectory = Path.Combine(paths.BrowserProfileDirectory, "engines", "chromium");
        Directory.CreateDirectory(paths.BrowserProfileDirectory);
        var legacyData = Path.Combine(paths.BrowserProfileDirectory, "legacy-profile-data.fixture");
        await File.WriteAllTextAsync(legacyData, "keep existing default-profile data");
        string otherDirectory;

        await using (var firstRun = await BrowseChrome.CreateAsync(paths, factory,
            homeProfileId: currentProfile, tabSessionPersistence: new TabSessionFixture(projection)))
        {
            Assert.Equal(currentDirectory, factory.Requests[0].ProfileDirectory);
            otherDirectory = factory.Requests[1].ProfileDirectory;
            Assert.Equal(currentDirectory, factory.Requests[2].ProfileDirectory);
            Assert.NotEqual(currentDirectory, otherDirectory);
            Assert.True(otherDirectory.StartsWith(
                Path.Combine(paths.BrowserProfileDirectory, "profiles") + Path.DirectorySeparatorChar,
                StringComparison.Ordinal));
            Assert.Equal("keep existing default-profile data", await File.ReadAllTextAsync(legacyData));
        }

        await using var restarted = await BrowseChrome.CreateAsync(paths, factory,
            homeProfileId: currentProfile, tabSessionPersistence: new TabSessionFixture(projection));
        Assert.Equal(currentDirectory, factory.Requests[3].ProfileDirectory);
        Assert.Equal(otherDirectory, factory.Requests[4].ProfileDirectory);
        Assert.Equal(currentDirectory, factory.Requests[5].ProfileDirectory);
        Assert.Equal("keep existing default-profile data", await File.ReadAllTextAsync(legacyData));
    }

    [Fact]
    public async Task Site_permissions_are_profile_scoped_and_legacy_decisions_stay_with_current_profile()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        const string currentProfile = "permission-profile-current";
        const string otherProfile = "permission-profile-other";
        var legacyOrigin = new Uri("https://legacy.example/");
        var currentOrigin = new Uri("https://one.example/");
        var otherOrigin = new Uri("https://one.example/");
        using (var legacy = new BrowserSitePermissionStore(paths))
            await legacy.SetDecisionAsync(legacyOrigin, BrowserSitePermissionKind.Camera,
                BrowserSitePermissionDecision.Deny, default);
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Chromium,
            new Dictionary<string, BrowseEngineKind>()));
        var currentId = Guid.NewGuid(); var otherId = Guid.NewGuid();
        var projection = new BrowseTabSessionProjection([
            new(currentId, "Current profile", currentOrigin.AbsoluteUri, BrowserTabPrivacy.Standard,
                string.Empty, DateTimeOffset.UtcNow) { ProfileId = currentProfile },
            new(otherId, "Other profile", otherOrigin.AbsoluteUri, BrowserTabPrivacy.Standard,
                string.Empty, DateTimeOffset.UtcNow) { ProfileId = otherProfile }
        ], currentId);

        await using (var chrome = await BrowseChrome.CreateAsync(paths, factory,
            homeProfileId: currentProfile, tabSessionPersistence: new TabSessionFixture(projection)))
        {
            await chrome.SetPermissionAsync(BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Allow);
            Assert.Equal(BrowserSitePermissionDecision.Allow, chrome.ObserveOriginPermission(currentId,
                chrome.ObserveOriginalEngine(currentId)!, currentOrigin, BrowserSitePermissionKind.Camera));
            Assert.Contains(chrome.State.Permissions, permission => permission.Origin == "https://legacy.example");

            await chrome.SelectTabAsync(otherId);
            Assert.Equal(BrowserSitePermissionDecision.Ask, chrome.ObserveOriginPermission(otherId,
                chrome.ObserveOriginalEngine(otherId)!, otherOrigin, BrowserSitePermissionKind.Camera));
            Assert.Empty(chrome.State.Permissions);
            await chrome.SetPermissionAsync(BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Deny);
            await chrome.SelectTabAsync(currentId);
            Assert.Equal(BrowserSitePermissionDecision.Allow, chrome.ObserveOriginPermission(currentId,
                chrome.ObserveOriginalEngine(currentId)!, currentOrigin, BrowserSitePermissionKind.Camera));
            Assert.Contains(chrome.State.Permissions, permission => permission.Origin == "https://legacy.example" &&
                permission.Decision == BrowserSitePermissionDecision.Deny);
        }

        await using var restarted = await BrowseChrome.CreateAsync(paths, factory,
            homeProfileId: currentProfile, tabSessionPersistence: new TabSessionFixture(projection));
        Assert.Equal(BrowserSitePermissionDecision.Allow, restarted.ObserveOriginPermission(currentId,
            restarted.ObserveOriginalEngine(currentId)!, currentOrigin, BrowserSitePermissionKind.Camera));
        Assert.Contains(restarted.State.Permissions, permission => permission.Origin == "https://legacy.example" &&
            permission.Decision == BrowserSitePermissionDecision.Deny);
        await restarted.SelectTabAsync(otherId);
        Assert.Equal(BrowserSitePermissionDecision.Deny, restarted.ObserveOriginPermission(otherId,
            restarted.ObserveOriginalEngine(otherId)!, otherOrigin, BrowserSitePermissionKind.Camera));
        Assert.DoesNotContain(restarted.State.Permissions, permission => permission.Origin == "https://legacy.example");
    }

    private sealed class TabSessionFixture(BrowseTabSessionProjection? projection) : IBrowseTabSessionPersistence
    {
        public List<BrowseTabSessionProjection> Saves { get; } = [];
        public Task? NextSave;
        public Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken token) => Task.FromResult(projection);
        public Task SaveAsync(BrowseTabSessionProjection value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Saves.Add(value); var original = NextSave ?? Task.CompletedTask; NextSave = null; return original; }
    }
}
