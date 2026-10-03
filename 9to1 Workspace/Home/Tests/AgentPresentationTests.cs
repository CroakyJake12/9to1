using System.Text.Json;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;
public sealed class AgentPresentationTests
{
    private static AgentDefinitionRecord Agent() => new()
    {
        Id = "canonical-agent", NamespaceId = "personal", DisplayName = "Coder", Version = "1",
        Presentation = new(1, AgentIconPresentation.Animated, "asset:static", "Coder agent", "idle",
            [new("idle", "Ready", "asset:idle", true), new("coding", "Coding", "asset:coding", true), new("laugh", "Laughing", "asset:laugh", false)],
            [new("idle", "coding", "activity.coding"), new("coding", "idle", "activity.completed")],
            [new("conversation.joke", "laugh")])
    };
    [Fact] public async Task Save_cannot_persist_assets_injected_while_authorization_is_waiting()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-agent-presentation-" + Guid.NewGuid().ToString("N"));
        await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
        var den = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
        var original = await den.SaveAsync(Agent(), 0, "create-agent");
        var states = original.Presentation!.States.ToList();
        var access = new BlockingAssets();
        var service = new AgentPresentationService(den, access);
        var pending = service.SetAsync("personal", original.Id, original.Revision, original.Presentation with { States = states }, "save-presentation");
        await access.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        states[0] = new("idle", "Injected", "asset:unchecked", true);
        access.Release.SetResult();
        var saved = await pending;
        Assert.Equal("asset:idle", saved.Presentation!.States[0].AssetReference);
        Assert.DoesNotContain("asset:unchecked", access.Observed);
        var observed = await den.GetAsync<AgentDefinitionRecord>("personal", original.Id);
        Assert.Equal("asset:idle", observed!.Presentation!.States[0].AssetReference);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_preview_is_detached_read_only_and_rejects_concurrent_Agent_change(bool changeAgent)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-agent-preview-" + Guid.NewGuid().ToString("N"));
        await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
        var den = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
        var original = await den.SaveAsync(Agent(), 0, "create-agent");
        var states = original.Presentation!.States.ToList();
        var assets = new BlockingAssets();
        var service = new AgentPresentationService(den, assets);
        var pending = service.PreviewDraftAsync("personal", original.Id, original.Revision,
            original.Presentation with { States = states }, "idle", "activity.coding", "Preview coding", false);
        await assets.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        states[1] = new("coding", "Injected", "asset:unchecked", true);
        if (changeAgent) await den.SaveAsync(original with { DisplayName = "Changed" }, original.Revision, "change-agent");
        assets.Release.SetResult();
        if (changeAgent) await Assert.ThrowsAsync<DenException>(() => pending);
        else
        {
            var frame = await pending;
            Assert.Equal("asset:coding", frame.AssetReference);
            Assert.Equal(original.Revision, (await service.GetAsync("personal", original.Id)).Revision);
        }
        Assert.DoesNotContain("asset:unchecked", assets.Observed);
    }

    private sealed class BlockingAssets : IAgentPresentationAssetAccess
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Observed { get; } = [];
        public async ValueTask<bool> CanReadAsync(string principalId, string namespaceId, string assetReference, CancellationToken cancellationToken)
        {
            Observed.Add(assetReference); Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return assetReference != "asset:unchecked";
        }
    }
    [Fact] public void Snapshot_detaches_caller_collections_and_rejects_corrupt_shape()
    {
        var states = Agent().Presentation!.States.ToList();
        var definition = Agent().Presentation! with { States = states };
        var captured = AgentAvatarPresentation.Snapshot(definition);
        states[0] = new("idle", "Untrusted replacement", "asset:unchecked", true);
        states.Add(new("injected", "Injected", "asset:unchecked", true));
        Assert.Equal("asset:idle", captured.States[0].AssetReference); Assert.Equal(3, captured.States.Count);
        Assert.Throws<DenException>(() => AgentAvatarPresentation.Validate(null));
        Assert.Throws<DenException>(() => AgentAvatarPresentation.Validate(definition with { States = null! }));
        Assert.Throws<DenException>(() => AgentAvatarPresentation.Validate(definition with { States = [null!] }));
        Assert.Throws<DenException>(() => AgentAvatarPresentation.Validate(definition with { Reactions = [null!] }));
        Assert.Throws<DenException>(() => AgentAvatarPresentation.Validate(definition with { Transitions = [null!] }));
    }
    [Fact] public void SameCanonicalAgentSurvivesSerializationAndReactsToObservedPresentationEvents()
    {
        var agent = JsonSerializer.Deserialize<AgentDefinitionRecord>(JsonSerializer.Serialize(Agent(), DenJson.Options), DenJson.Options)!;
        var coding = AgentAvatarPresentation.Present(agent, "idle", "activity.coding", "Coding", false);
        Assert.Equal("canonical-agent", coding.AgentId); Assert.Equal("coding", coding.StateId);
        Assert.Equal("asset:coding", coding.AssetReference); Assert.True(coding.Animated);
        var laugh = AgentAvatarPresentation.Present(agent, "coding", "conversation.joke", "Responding", false);
        Assert.Equal("laugh", laugh.StateId);
        Assert.Equal(agent.AllowedPermissions, Agent().AllowedPermissions);
    }
    [Fact] public void ReducedMotionAndMissingAnimationAssetsRetainAccessibleStaticIdentityAndActivity()
    {
        foreach (var frame in new[] {
            AgentAvatarPresentation.Present(Agent(), "idle", "activity.coding", "Coding", true),
            AgentAvatarPresentation.Present(Agent(), "idle", "activity.coding", "Coding", false, false) })
        {
            Assert.False(frame.Animated); Assert.Equal("asset:static", frame.AssetReference);
            Assert.Equal("Coder agent", frame.AccessibleName); Assert.Equal("Coding", frame.ReadableActivity);
        }
    }
    [Fact] public void UnknownSchemaAndAmbiguousTransitionsAreRejected()
    {
        var presentation = Agent().Presentation!;
        Assert.Throws<DenException>(() => AgentAvatarPresentation.Validate(presentation with { SchemaVersion = 2 }));
        Assert.Throws<DenException>(() => AgentAvatarPresentation.Validate(presentation with { Transitions = [new("idle", "coding", "same"), new("idle", "laugh", "same")] }));
    }
}
