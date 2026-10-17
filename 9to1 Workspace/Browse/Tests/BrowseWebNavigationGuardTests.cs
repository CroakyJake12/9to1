using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Theory]
    [InlineData("9to1://write/document-42", true)]
    [InlineData("9to1://unavailable/document-42", false)]
    [InlineData("browse://home", true)]
    [InlineData("browse://newtab", true)]
    [InlineData("browse://history", true)]
    [InlineData("browse://bookmarks", true)]
    [InlineData("browse://downloads", true)]
    [InlineData("browse://settings", true)]
    public async Task Web_commands_refuse_nonweb_tabs_before_retaining_work_or_changing_original_content(
        string route, bool registerNativeApps)
    {
        var engines = new ChromiumFixtureFactory(includeGecko: true);
        var apps = new AppFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), engines,
            appContentFactory: registerNativeApps ? apps : null, internalContentFactory: apps, homeProfileId: "profile");
        await chrome.OpenContentAsync(AppContentAddress.Parse("https://retained.example/"));
        var webTab = chrome.State.SelectedTab;
        var renderer = Assert.Single(engines.Hosts);
        await chrome.OpenContentAsync(AppContentAddress.Parse(route));
        var before = chrome.State.SelectedTab;
        var status = chrome.State.Status;
        var originalSources = chrome.OriginalNativeSources.ToArray();
        var observedOwner = chrome.ObserveOriginalAppContent(before.Id);
        var owner = observedOwner is null ? null : Assert.IsType<AppFixtureSession>(observedOwner);
        if (route is "9to1://write/document-42" or "browse://home" or "browse://newtab")
            Assert.NotNull(owner);
        if (owner is not null) owner.Unsaved = true;
        var publications = 0;
        chrome.StateChanged += (_, _) => publications++;
        Func<Task<BrowseChromeSnapshot>>[] commands =
        [
            () => chrome.BackAsync(), () => chrome.ForwardAsync(),
            () => chrome.ReloadAsync(), () => chrome.StopAsync()
        ];

        foreach (var command in commands)
        {
            // Preserve the public asynchronous refusal contract as well as the
            // original content: none of these calls may throw synchronously.
            var refused = command();
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => refused);
            Assert.Equal("This action requires an HTTP or HTTPS page.", failure.Message);
            Assert.Equal(before, chrome.State.SelectedTab);
            Assert.Equal(status, chrome.State.Status);
            Assert.Equal(originalSources, chrome.OriginalNativeSources.ToArray());
            Assert.Same(owner, chrome.ObserveOriginalAppContent(before.Id));
            Assert.Null(chrome.ObserveOriginalEngine(before.Id));
        }

        Assert.Equal(0, publications);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(webTab.Id));
        Assert.Empty(renderer.WebNavigationCalls);
        Assert.Equal(0, renderer.CloseCalls);
        Assert.Empty(chrome.State.History);
        Assert.Null(chrome.OriginalClose);
        if (owner is not null)
        {
            Assert.Equal(0, owner.CloseCalls);
            Assert.True(owner.OriginalInitialization!.IsCompletedSuccessfully);
            Assert.False(await chrome.TryPrepareToCloseAsync());
            owner.ChangeResource(route, "Original content still usable");
            Assert.Equal("Original content still usable", chrome.State.SelectedTab.Title);
            owner.Unsaved = false;
        }
        // Await-using must close successfully: command refusal is not retained
        // as a failed original operation that poisons later retirement.
    }

    [Theory]
    [InlineData("http://retained.example/")]
    [InlineData("https://retained.example/")]
    public async Task Web_commands_still_dispatch_to_the_same_original_http_or_https_renderer(string address)
    {
        var engines = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), engines);
        await chrome.NavigateAsync(address);
        var tab = chrome.State.SelectedTab;
        var renderer = Assert.Single(engines.Hosts);

        await chrome.BackAsync();
        await chrome.ForwardAsync();
        await chrome.ReloadAsync();
        await chrome.StopAsync();

        Assert.Equal(new[] { "back", "forward", "reload", "stop" }, renderer.WebNavigationCalls);
        Assert.Equal(tab.Id, chrome.State.SelectedTabId);
        Assert.Equal(tab.Address, chrome.State.SelectedTab.Address);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(tab.Id));
        Assert.Equal(0, renderer.CloseCalls);
    }
}
