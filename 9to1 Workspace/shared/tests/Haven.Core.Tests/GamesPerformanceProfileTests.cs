using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>ED-GAMES-004: low-power/low-memory profiles reduce preview resolution, detail/work scheduling and cache residency while preserving
/// authored assets and full project semantics; unavailable local capabilities remain visible rather than silently approximated.
/// Proves profile validation, capability visibility, selection/override transactions and stale-edit no-mutation.</summary>
public sealed class GamesPerformanceProfileTests
{
    private static GamesPerformanceDocument MakeDoc() =>
        GamesPerformanceDocument.Create([GamesPerformanceProfile.LowPower(), GamesPerformanceProfile.FullQuality()], "low-power");

    [Fact]
    public void LowPowerProfileReducesResolutionDetailAndCacheWithinBounds()
    {
        var low = GamesPerformanceProfile.LowPower();
        Assert.True(low.PreviewResolutionScale > 0 && low.PreviewResolutionScale <= 1);
        Assert.False(low.AllowBackgroundRendering);
        Assert.Equal(GamesRemoteExecutorMode.RemoteIfAvailable, low.RemoteExecutorMode);

        var full = GamesPerformanceProfile.FullQuality();
        Assert.True(low.DetailBudget < full.DetailBudget);
        Assert.True(low.CacheResidencyMB < full.CacheResidencyMB);
    }

    [Fact]
    public void OutOfRangeValuesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new GamesPerformanceProfile("bad", "Bad", 1.5, 32, 2, 128, false, GamesRemoteExecutorMode.LocalOnly).Capture());
        Assert.Throws<InvalidDataException>(() => new GamesPerformanceProfile("bad", "Bad", 0, 32, 2, 128, false, GamesRemoteExecutorMode.LocalOnly).Capture());
        Assert.Throws<InvalidDataException>(() => new GamesPerformanceProfile("bad", "Bad", 0.5, 0, 2, 128, false, GamesRemoteExecutorMode.LocalOnly).Capture());
        Assert.Throws<InvalidDataException>(() => new GamesPerformanceProfile("bad", "Bad", 0.5, 32, 2, -1, false, GamesRemoteExecutorMode.LocalOnly).Capture());
    }

    [Fact]
    public void UnavailableCapabilityMustStateReason()
    {
        // An unavailable local capability is visible only when it reports why.
        Assert.Throws<InvalidDataException>(() => new GamesCapability("raytracing", AvailableLocally: false, null).Capture());
        var visible = new GamesCapability("raytracing", AvailableLocally: false, "No hardware support on this device").Capture();
        Assert.False(visible.AvailableLocally);
        Assert.Equal("No hardware support on this device", visible.UnavailableReason);
    }

    [Fact]
    public void SelectProfileIsRevisionAwareAndNeverMutatesOriginal()
    {
        var doc = MakeDoc();
        var selected = doc.SelectProfile("full-quality", 1);
        Assert.Equal(2, selected.Revision);
        Assert.Equal("full-quality", selected.SelectedProfileID);
        // Original unchanged.
        Assert.Equal("low-power", doc.SelectedProfileID);
        Assert.Equal(1, doc.Revision);
    }

    [Fact]
    public void SelectUnknownProfileIsRejected()
    {
        var doc = MakeDoc();
        Assert.Throws<InvalidOperationException>(() => doc.SelectProfile("missing", 1));
    }

    [Fact]
    public void StaleEditNeverMutatesDocument()
    {
        var doc = MakeDoc();
        Assert.Throws<InvalidOperationException>(() => doc.AddProfile(GamesPerformanceProfile.LowPower("extra"), 99));
        Assert.Equal(1, doc.Revision);
        Assert.Equal(2, doc.Profiles.Count);
    }

    [Fact]
    public void AddProfileRejectsDuplicateIdentity()
    {
        var doc = MakeDoc();
        Assert.Throws<InvalidOperationException>(() => doc.AddProfile(GamesPerformanceProfile.LowPower(), 1));
    }

    [Fact]
    public void DeclareExecutorAndSetCapabilityRoundTrip()
    {
        var doc = MakeDoc();
        var withExecutor = doc.DeclareExecutor(new GamesRemoteExecutor("render-farm-1", "linux", Compatible: true), 1);
        Assert.Single(withExecutor.RemoteExecutors);

        var withCapability = withExecutor.SetCapability(new GamesCapability("path-tracing", AvailableLocally: false, "Local GPU insufficient"), 2);
        Assert.Single(withCapability.Capabilities);
        // Replacing the same capability identity updates rather than duplicates.
        var replaced = withCapability.SetCapability(new GamesCapability("path-tracing", AvailableLocally: true, null), 3);
        Assert.Single(replaced.Capabilities);
        Assert.True(replaced.Capabilities.Single().AvailableLocally);
    }

    [Fact]
    public void ResolveSelectedReturnsCurrentProfile()
    {
        var doc = MakeDoc();
        Assert.Equal("low-power", doc.ResolveSelected().ProfileID);
        Assert.Equal("full-quality", doc.SelectProfile("full-quality", 1).ResolveSelected().ProfileID);
    }
}
