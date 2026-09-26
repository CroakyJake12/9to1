/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ModelRouterTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ModelRouterTests, StubRegistry. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents model router tests and keeps its related state and behavior together.
/// </summary>
public sealed class ModelRouterTests
{
    /// <summary>
    /// Performs the automatic routing prefers compatible local model step owned by this component.
    /// </summary>
    [Fact]
    public async Task AutomaticRoutingPrefersCompatibleLocalModel()
    {
        var local = Descriptor("ollama", true, "qwen", ToolCapability.Text, ToolCapability.Tools);
        var cloud = Descriptor("openai", false, "cloud", ToolCapability.Text, ToolCapability.Tools, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([cloud, local]));

        var result = await router.RouteAsync(new ModelRoutingRequest(null, new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools },
            new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, true, [])), CancellationToken.None);

        Assert.Equal(local.Key, result.Model.Key);
        Assert.Contains("local", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the manual routing uses first compatible fallback step owned by this component.
    /// </summary>
    [Fact]
    public async Task ManualRoutingUsesFirstCompatibleFallback()
    {
        var textOnly = Descriptor("ollama", true, "text", ToolCapability.Text);
        var vision = Descriptor("openai", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([textOnly, vision]));

        var result = await router.RouteAsync(new ModelRoutingRequest(textOnly, new HashSet<ToolCapability> { ToolCapability.Vision },
            new ModelRoutingPolicy(ModelRoutingMode.ManualFallback, true, true, [textOnly.Key, vision.Key])), CancellationToken.None);

        Assert.Equal(vision.Key, result.Model.Key);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task AutomaticRoutingHonorsPreferredOrderAfterEligibilityFiltering()
    {
        var first = Descriptor("ollama", true, "text", ToolCapability.Text);
        var preferredEligible = Descriptor("openai", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var otherwiseHigherRanked = Descriptor("anthropic", false, "rich", ToolCapability.Text, ToolCapability.Vision, ToolCapability.Tools);
        var router = new ModelRouter(new StubRegistry([otherwiseHigherRanked, preferredEligible, first]));

        var result = await router.RouteAsync(new ModelRoutingRequest(null, new HashSet<ToolCapability> { ToolCapability.Vision },
            new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, true, [first.Key, preferredEligible.Key])), CancellationToken.None);

        Assert.Equal(preferredEligible.Key, result.Model.Key);
        Assert.Contains("eligible", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitLocalSelectionDoesNotSilentlyFallThroughToCloud()
    {
        var local = Descriptor("ollama", true, "private", ToolCapability.Text);
        var cloud = Descriptor("openai", false, "cloud", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([local, cloud]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(new ModelRoutingRequest(local,
            new HashSet<ToolCapability> { ToolCapability.Vision }, new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, true, [cloud.Key], false)), CancellationToken.None));
    }

    [Fact]
    public async Task AutomaticExplicitSelectionUsesEligibleConfiguredFallbackInOrder()
    {
        var selected = Descriptor("ollama", true, "pinned", ToolCapability.Text);
        var first = Descriptor("openai", false, "no-vision", ToolCapability.Text);
        var next = Descriptor("anthropic", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var later = Descriptor("gemini", false, "also-vision", ToolCapability.Text, ToolCapability.Vision, ToolCapability.Tools);
        var router = new ModelRouter(new StubRegistry([selected, later, next, first]));

        var result = await router.RouteAsync(new ModelRoutingRequest(selected, new HashSet<ToolCapability> { ToolCapability.Vision },
            new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, true, [first.Key, next.Key, later.Key])), CancellationToken.None);

        Assert.Equal(next.Key, result.Model.Key);
        Assert.True(result.UsedFallback);
        Assert.Contains("configured fallback chain", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfiguredRouteDoesNotEscapeToUnlistedProviderWhenAllRouteCandidatesAreIneligible()
    {
        var selected = Descriptor("ollama", true, "pinned", ToolCapability.Text);
        var configured = Descriptor("openai", false, "configured", ToolCapability.Text);
        var unlisted = Descriptor("anthropic", false, "unlisted", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([selected, configured, unlisted]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(new ModelRoutingRequest(selected,
            new HashSet<ToolCapability> { ToolCapability.Vision }, new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, true, [configured.Key])), CancellationToken.None));
    }

    [Fact]
    public async Task LocalOnlyExplicitFallbackCannotCrossToCloud()
    {
        var selected = Descriptor("ollama", true, "pinned", ToolCapability.Text);
        var cloud = Descriptor("openai", false, "cloud", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([selected, cloud]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(new ModelRoutingRequest(selected,
            new HashSet<ToolCapability> { ToolCapability.Vision }, new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, false, [cloud.Key])), CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitSelectionFallsBackOnlyThroughConfiguredEligibleRoute()
    {
        var selected = Descriptor("ollama", true, "pinned", ToolCapability.Text);
        var ineligible = Descriptor("openai", false, "no-vision", ToolCapability.Text);
        var eligible = Descriptor("openai", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([selected, ineligible, eligible]));

        var result = await router.RouteAsync(new ModelRoutingRequest(selected, new HashSet<ToolCapability> { ToolCapability.Vision },
            new ModelRoutingPolicy(ModelRoutingMode.ManualFallback, true, true, [ineligible.Key, eligible.Key])), CancellationToken.None);

        Assert.Equal(eligible.Key, result.Model.Key);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task ExplicitSelectionWithNoConfiguredFallbackFailsInsteadOfChoosingUnrelatedModel()
    {
        var selected = Descriptor("ollama", true, "pinned", ToolCapability.Text);
        var unrelated = Descriptor("openai", false, "unrelated", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([selected, unrelated]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(new ModelRoutingRequest(selected,
            new HashSet<ToolCapability> { ToolCapability.Vision }, new ModelRoutingPolicy(ModelRoutingMode.ManualFallback, true, true, [])), CancellationToken.None));
    }

    [Fact]
    public async Task CloudCandidatesAreFilteredBeforeOrderedRouteSelectionWhenCloudIsDisallowed()
    {
        var cloudFirst = Descriptor("openai", false, "cloud", ToolCapability.Text, ToolCapability.Vision);
        var localNext = Descriptor("ollama", true, "local", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([cloudFirst, localNext]));

        var result = await router.RouteAsync(new ModelRoutingRequest(null, new HashSet<ToolCapability> { ToolCapability.Vision },
            new ModelRoutingPolicy(ModelRoutingMode.Automatic, false, false, [cloudFirst.Key, localNext.Key])), CancellationToken.None);

        Assert.Equal(localNext.Key, result.Model.Key);
    }

    /// <summary>
    /// Performs the local only policy rejects cloud only capability step owned by this component.
    /// </summary>
    [Fact]
    public async Task LocalOnlyPolicyRejectsCloudOnlyCapability()
    {
        var cloud = Descriptor("openai", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([cloud]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(new ModelRoutingRequest(null,
            new HashSet<ToolCapability> { ToolCapability.Vision }, new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, false, [])), CancellationToken.None));
    }

    /// <summary>
    /// Performs the descriptor step owned by this component.
    /// </summary>
    private static ProviderModelDescriptor Descriptor(string provider, bool local, string name, params ToolCapability[] capabilities) =>
        new(provider, local, new ModelDescriptor(name, 0, provider, string.Empty, string.Empty, capabilities.ToHashSet(), DateTimeOffset.UtcNow));

    /// <summary>
    /// Represents stub registry and keeps its related state and behavior together.
    /// </summary>
    private sealed class StubRegistry(IReadOnlyList<ProviderModelDescriptor> models) : IModelProviderRegistry
    {
        /// <summary>
        /// Gets or updates providers, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<IModelProvider> Providers => [];
        /// <summary>
        /// Performs the find step owned by this component.
        /// </summary>
        public IModelProvider? Find(string providerId) => null;
        /// <summary>
        /// Retrieves required for the current operation.
        /// </summary>
        public IModelProvider GetRequired(string providerId) => throw new NotSupportedException();
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult(models);
    }
}
