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
