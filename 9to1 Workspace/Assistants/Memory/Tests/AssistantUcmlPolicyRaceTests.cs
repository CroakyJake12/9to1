using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Theory]
    [InlineData("category")]
    [InlineData("ask-block")]
    [InlineData("allow-block")]
    public async Task Ucml_current_category_revocation_or_Home_block_prevents_dispatch_after_selection(string revocation)
    {
        var ask = revocation == "ask-block"; var block = revocation != "category";
        var provider = new UcmlDispatchProvider("llama-cpp", true);
        var dispatching = false; var revoked = false;
        var rig = DispatchRigWithSafety(new UcmlDispatchSafety(() => dispatching = true), provider);
        var expected = block ? "Home no longer approves" : "configured identity or membership changed";
        try
        {
            await rig.InitializeAsync(true);
            var configuration = DispatchConfiguration(ask ? [] : ["communication"]);
            if (ask) configuration = configuration with
                { Memory = configuration.Memory with { UcmlAskCategories = ["communication"] } };
            var binding = await rig.CreateAsync(configuration);
            await ImportForDispatchAsync(rig, binding, UcmlFixture(body: "Explain examples with the revocable marker amber-reed."));
            var actor = (await rig.Profiles.GetCurrentAsync(Token))!;
            provider.BeforeCatalogue = async () =>
            {
                if (!dispatching || revoked) return;
                revoked = true;
                if (block)
                    Assert.True((await rig.Permissions.BlockCallerAsync("assistants.ucml:" + actor.ProfileId, Token)).Succeeded);
                else
                    await rig.Bridge.UpdateAsync(binding.Definition.Identity, binding.Definition.Revision,
                        configuration with { Memory = configuration.Memory with { UcmlCategories = [] } }, Guid.NewGuid(), Token);
            };
            var choice = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
            var observation = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Explain examples", choice.Model, ProviderId: choice.ProviderId), Token);
            var finished = observation.WaitAsync(Token);
            if (ask) await DecideUcmlAskAsync(rig, finished, HomeApprovalChoice.Accept,
                _ => Assert.Empty(provider.Requests));
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => finished);
            Assert.True(revoked); Assert.Empty(provider.Requests);
            if (block) Assert.Contains((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).RecentAuditEvents,
                value => value.ResultCode == "HOME_UCML_ACCESS_DENIED");
            Assert.Contains(DispatchLeaves(failure), leaf => leaf.Message.Contains(expected, StringComparison.Ordinal));
            await Assert.ThrowsAnyAsync<Exception>(() => observation.CloseAndDrainAsync());
        }
        finally
        {
            try { await rig.CloseAsync(); }
            catch (Exception failure)
            {
                var leaves = DispatchLeaves(failure);
                Assert.NotEmpty(leaves);
                Assert.All(leaves, leaf =>
                {
                    Assert.True(leaf is UnauthorizedAccessException or AssistantCommandRefusedException,
                        leaf.GetType().Name + ": " + leaf.Message);
                    Assert.Contains(expected, leaf.Message, StringComparison.Ordinal);
                });
            }
        }
    }

    [Fact]
    public Task Ucml_Ask_refuses_foreign_actor_and_cannot_rebind_an_accepted_request_to_another_original_input() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync();
        var prepared = await rig.PrepareAsync(binding);
        var otherPrepared = await rig.PrepareAsync(binding);
        var actor = (await rig.Profiles.GetCurrentAsync(Token))!;
        var owner = new HomeUcmlContextApprovalOwner(rig.OriginalStateStore, rig.Profiles, rig.Permissions, rig.Memory);
        var id = Guid.NewGuid(); var request = Guid.NewGuid();
        Task<bool> Ask(IChatOriginalPersistentMemoryInput input, AuthenticatedResourceActor identity) =>
            owner.AuthorizeOriginalAsync(input, request, identity, binding.Definition.Identity.DefinitionId,
                binding.Conversation.Id, new string('D', 64), [id], ["communication"], null, Scope, rig.Retain, Token);
        Assert.False(await Ask(prepared.Input!, actor with { ActorId = "foreign-actor" }));
        Assert.DoesNotContain((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
            value => value.Scope.ActionName == HomeUcmlContextApprovalOwner.Action);
        var accepted = Ask(prepared.Input!, actor);
        var pending = await ObserveUcmlApproval(rig, accepted);
        Assert.True((await rig.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
        Assert.True(await accepted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Ask(otherPrepared.Input!, actor));
        Assert.True((await rig.Permissions.BlockCallerAsync(pending.Caller.CallerId, Token)).Succeeded);
        Assert.False(await Ask(prepared.Input!, actor));
    });
}
