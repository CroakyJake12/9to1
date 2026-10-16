using NineToOne.Dulche.Context;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Theory]
    [InlineData("Hi", false, true)]
    [InlineData("Please tell me about heliotrope", true, true)]
    [InlineData("Hi", false, false)]
    public async Task Ucml_conversation_preserves_permitted_identity_and_selects_topic_without_substring_noise(
        string query, bool expectTopic, bool identityAllowed)
    {
        var local = new UcmlDispatchProvider("llama-cpp", true); var rig = DispatchRig(local);
        try
        {
            await rig.InitializeAsync(true);
            var binding = await rig.CreateAsync(DispatchConfiguration(identityAllowed ? ["identity", "communication"] : ["communication"]));
            var identity = UcmlFixture("Conversational identity", "My fictional name is Rowan-Cedar.");
            identity = identity with { Metadata = identity.Metadata with { Category = "identity" } };
            await ImportForDispatchAsync(rig, binding, identity);
            await ImportForDispatchAsync(rig, binding, UcmlFixture("Heliotrope project", "The fictional heliotrope marker is amber-petal."));
            await ImportForDispatchAsync(rig, binding, UcmlFixture("Unrelated historical detail", "This is unrelated history with the marker violet-thistle."));
            var choice = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
            var turn = await rig.Bridge.SendConversationOriginalAsync(binding,
                new(query, choice.Model, ProviderId: choice.ProviderId), Token);
            await turn.WaitAsync(Token); await turn.CloseAndDrainAsync();
            var sent = Assert.Single(local.Requests);
            Assert.Equal(identityAllowed, (sent.SystemPrompt ?? "").Contains("Rowan-Cedar", StringComparison.Ordinal));
            Assert.Equal(expectTopic, (sent.SystemPrompt ?? "").Contains("amber-petal", StringComparison.Ordinal));
            Assert.DoesNotContain("violet-thistle", sent.SystemPrompt);
        }
        finally { await CloseDispatchRigAsync(rig); }
    }
}
