using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Dulche.Runtime;
using Haven.Application.Haven;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

/// <summary>
/// Verifies the truthful, separated model/usage contract: the percentage beside the model name is
/// only the reasoning-effort tier; usage/context are separate labelled metrics; absent measurement
/// is unavailable (never an invented 0%/100%); and local models never show a usage arc or an "M"
/// merely because they are local.
/// </summary>
public sealed class HavenModelInfoTruthfulnessTests
{
    [Theory]
    [InlineData(false, true, true, false)]   // remote + provider usage -> usage arc visible
    [InlineData(false, false, false, true)]  // remote without provider usage -> hidden arc + M marker
    [InlineData(true, false, false, false)]  // local -> hidden arc, no M just because local
    [InlineData(false, false, false, false, false)] // unknown locality -> no M (never guessed remote)
    public void Usage_and_M_rules_are_truthful(bool isLocal, bool remoteUsage, bool usageVisible, bool meterMarker,
        bool? localityKnown = null)
    {
        var info = new HavenTopBarModelInfo(
            Name: "Qwen 3.6",
            EffortConfigurable: true,
            EffortTierCount: 3,
            EffortLabel: "70%",
            IsLocal: isLocal,
            RemoteUsageAvailable: remoteUsage,
            UsageSummary: remoteUsage ? "128 MB / 1 GB · provider" : null,
            UsageScope: remoteUsage ? "provider:acme/model:qwen" : null,
            ContextMeasured: false,
            ContextSummary: null,
            EffortLevelOrdinal: 1,
            LocalityKnown: localityKnown);
        Assert.Equal(usageVisible, info.UsageVisible);
        Assert.Equal(meterMarker, info.MeterMarker);
    }

    [Fact]
    public void Unknown_locality_never_shows_a_misleading_metering_marker()
    {
        var info = new HavenTopBarModelInfo("m", false, 0, string.Empty, false, false, null, null, false, null,
            LocalityKnown: false);
        Assert.False(info.MeterMarker);
        Assert.False(info.UsageVisible);
        Assert.Equal(string.Empty, info.EffortPercent);
    }

    [Fact]
    public void Neither_metric_measured_yields_no_fabricated_ring()
    {
        var info = new HavenTopBarModelInfo("m", true, 5, "60%", false, false, null, null, false, null);
        Assert.False(info.UsageVisible);
        Assert.True(info.MeterMarker); // remote without provider usage keeps the honest M note
        Assert.False(info.ContextVisible);
    }

    [Fact]
    public async Task Null_ModelInfo_publishes_unavailable_bindings_never_a_guessed_percentage()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: new NullModelInfoContext());
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "model-truth", "Ready"));
        try
        {
            Assert.True(workspace.TryGetValue("Haven.ModelName", out var name));
            Assert.Equal(string.Empty, name);
            Assert.True(workspace.TryGetValue("Haven.EffortLabel", out var effort));
            Assert.Equal(string.Empty, effort);
            Assert.True(workspace.TryGetValue("Haven.UsageVisible", out var usage));
            Assert.False((bool)usage!);
            Assert.True(workspace.TryGetValue("Haven.MeterMarker", out var meter));
            Assert.False((bool)meter!);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    [Fact]
    public void Snapshot_unknown_scale_yields_no_percentage_and_known_local_locality()
    {
        var snapshot = HavenModelProjectionSnapshot.CreateValidated(
            null, null, "User", "p1", "chat::p1", 3, 7,
            "test-local", "qwen", isLocal: true, localityKnown: true,
            Array.Empty<string>(), null);
        Assert.Null(snapshot.SelectedReasoningOrdinal);
        var info = snapshot.ToPresentation();
        Assert.Equal("qwen", info.Name);
        Assert.True(info.IsLocal);
        Assert.Equal(string.Empty, info.EffortPercent);
        Assert.False(info.MeterMarker);
        Assert.False(info.UsageVisible);
    }

    [Theory]
    [InlineData(new[] { "low", "medium", "high" }, "medium", 1, "70%")]
    [InlineData(new[] { "a", "b", "c", "d" }, "d", 3, "100%")]
    [InlineData(new[] { "a", "b", "c", "d", "e" }, "a", 0, "20%")]
    public void Snapshot_selected_advertised_id_maps_to_true_tier_percentage(
        string[] ids, string selected, int expectedOrdinal, string expectedPercent)
    {
        var snapshot = HavenModelProjectionSnapshot.CreateValidated(
            null, null, "User", "p1", "chat::p1", 2, 9,
            "provider", "model", isLocal: false, localityKnown: true, ids, selected);
        Assert.Equal(expectedOrdinal, snapshot.SelectedReasoningOrdinal);
        Assert.Equal(expectedPercent, snapshot.ToPresentation().EffortPercent);
    }

    [Fact]
    public void Snapshot_general_scale_produces_distinct_evenly_distributed_percentages()
    {
        var snapshot = HavenModelProjectionSnapshot.CreateValidated(
            null, null, "User", "p1", "chat::p1", 4, 11,
            "provider", "model", isLocal: false, localityKnown: true,
            new[] { "1", "2", "3", "4", "5", "6" }, "5");
        // General N=6, selected ordinal 4 -> round(100*5/6)=83%.
        Assert.Equal("83%", snapshot.ToPresentation().EffortPercent);
    }

    [Theory]
    [InlineData(new[] { "low", "low" })]         // duplicate ids -> not a stable bijection
    [InlineData(new[] { "", "high" })]           // blank id -> invalid metadata
    public void Snapshot_invalid_advertised_ids_stay_truthfully_unknown(string[] ids)
    {
        var snapshot = HavenModelProjectionSnapshot.CreateValidated(
            null, null, "User", "p1", "chat::p1", 1, 13,
            "provider", "model", isLocal: false, localityKnown: true, ids, "low");
        Assert.Empty(snapshot.AdvertisedReasoningIds);
        Assert.Null(snapshot.SelectedReasoningId);
        Assert.Null(snapshot.SelectedReasoningOrdinal);
        Assert.Equal(string.Empty, snapshot.ToPresentation().EffortPercent);
    }

    private sealed class NullModelInfoContext : IHavenHostContext
    {
        public bool IsEmbedded => true;
        public bool ModelSupported => true;
        public bool NotificationsSupported => true;
        public bool ProfileSupported => true;
        public IHavenSharedControlGateway? SharedControls => null;
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-model-truth-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
