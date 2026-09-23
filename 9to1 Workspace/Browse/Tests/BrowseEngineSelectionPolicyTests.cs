using HavenOS.Apps.Browse;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseEngineSelectionPolicyTests
{
    [Fact]
    public void DefaultEngineIsGeckoUntilExplicitlyChanged()
    {
        var policy = new BrowseEngineSelectionPolicy();

        Assert.Equal(BrowseEngineKind.Gecko, policy.DefaultEngine);
        Assert.Equal(BrowseEngineKind.Gecko, policy.Resolve(new Uri("about:blank"), Guid.NewGuid()));

        policy.SetDefault(BrowseEngineKind.Chromium);

        Assert.Equal(BrowseEngineKind.Chromium, policy.Resolve(new Uri("https://example.test/"), Guid.NewGuid()));
    }

    [Fact]
    public void SiteOverrideUsesNormalizedHostAndFallsBackAfterRemoval()
    {
        var policy = new BrowseEngineSelectionPolicy();
        policy.SetSiteOverride(new Uri("https://Example.TEST./"), BrowseEngineKind.Chromium);

        Assert.Equal(BrowseEngineKind.Chromium, policy.Resolve(new Uri("https://example.test/page"), Guid.NewGuid()));
        Assert.Equal(BrowseEngineKind.Gecko, policy.Resolve(new Uri("https://other.test/"), Guid.NewGuid()));

        policy.SetSiteOverride(new Uri("https://example.test/"), null);

        Assert.Equal(BrowseEngineKind.Gecko, policy.Resolve(new Uri("https://example.test/"), Guid.NewGuid()));
    }

    [Fact]
    public void TabOverrideTakesPrecedenceAndDoesNotChangeSiblingTab()
    {
        var policy = new BrowseEngineSelectionPolicy();
        var address = new Uri("https://example.test/");
        var firstTab = Guid.NewGuid();
        var siblingTab = Guid.NewGuid();
        policy.SetSiteOverride(address, BrowseEngineKind.Chromium);
        policy.SetTabOverride(firstTab, BrowseEngineKind.Gecko);

        Assert.Equal(BrowseEngineKind.Gecko, policy.Resolve(address, firstTab));
        Assert.Equal(BrowseEngineKind.Chromium, policy.Resolve(address, siblingTab));

        policy.SetTabOverride(firstTab, null);

        Assert.Equal(BrowseEngineKind.Chromium, policy.Resolve(address, firstTab));
        Assert.Equal(BrowseEngineKind.Chromium, policy.Resolve(address, siblingTab));
    }

    [Fact]
    public void SiteOverrideRejectsNonWebAddresses()
    {
        var policy = new BrowseEngineSelectionPolicy();

        Assert.Throws<ArgumentException>(() =>
            policy.SetSiteOverride(new Uri("about:blank"), BrowseEngineKind.Chromium));
    }
}
