using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
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
    public void Usage_and_M_rules_are_truthful(bool isLocal, bool remoteUsage, bool usageVisible, bool meterMarker)
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
            ContextSummary: null);
        Assert.Equal(usageVisible, info.UsageVisible);
        Assert.Equal(meterMarker, info.MeterMarker);
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
