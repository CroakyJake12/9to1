using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Migration.Tests;

public sealed partial class LegacySavedAgentMigrationTests
{
    [Fact]
    public Task First_use_import_survives_view_reopen_then_explicit_classification_is_the_same_persisted_assistant() => RunImportAsync(async rig =>
    {
        var before = await rig.CaptureLegacyAsync();
        var inspected = await rig.Controller.InspectImportAsync(Token);
        Assert.Equal(LegacyAgentImportState.RequiresReview, inspected.State); Assert.False(inspected.CanBrowse);
        Assert.Equal((await rig.Source.GetStoreIdentityAsync(Token)).StoreId, inspected.StoreId);
        Assert.Empty((await rig.Bridge.ListAsync(Token)).Definitions);
        var pending = await rig.Controller.RequestImportAsync(inspected, Token);
        Assert.Equal(LegacyAgentImportState.PendingApproval, pending.State); Assert.NotNull(pending.RequestId);
        Assert.False(pending.CanComplete); Assert.Equal(before, await rig.CaptureLegacyAsync());
        await rig.Controller.CloseAndDrainAsync();
        var reopenedView = rig.NewController();
        var sameRequest = await reopenedView.InspectImportAsync(Token);
        Assert.Equal(pending.RequestId, sameRequest.RequestId); Assert.Equal(pending.State, sameRequest.State);
        Assert.True((await rig.Permissions.DecideAsync(sameRequest.RequestId!, HomeApprovalChoice.Accept,
            cancellationToken: Token)).Succeeded);
        var approved = await reopenedView.RefreshImportAsync(sameRequest, Token);
        Assert.Equal(LegacyAgentImportState.Approved, approved.State); Assert.True(approved.CanComplete);
        var imported = await reopenedView.CompleteImportAsync(approved, Token);
        Assert.True(imported.CanBrowse); Assert.Equal(LegacyAgentImportState.Imported, imported.State);
        Assert.Equal(2, (await reopenedView.ListPageAsync(token: Token)).Items.Count);
        var preview = await reopenedView.PreviewAsync(rig.First, Token);
        Assert.Equal(LegacyAgentMigrationState.Unselected, preview.State); Assert.Null(preview.PendingClassification);
        var saved = await reopenedView.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant,
            preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        Assert.Equal(rig.First.ToString("D"), saved.Definition.Identity.DefinitionId);
        Assert.Equal(saved.Definition.Identity, Assert.Single((await rig.Bridge.ListAsync(Token)).Definitions).Identity);
        Assert.Equal(before, await rig.CaptureLegacyAsync());
        var conversationId = Guid.NewGuid();
        var conversation = await rig.Bridge.CreateConversationAsync(saved.Definition.Identity, saved.Definition.Revision,
            conversationId, "First conversation after explicit import", Guid.NewGuid(), Token);
        Assert.Equal(saved.Definition.Identity, conversation.Definition.Identity);
        Assert.Equal(conversationId, conversation.Conversation.Id);
        Assert.NotEqual(rig.ConversationId, conversationId);
        Assert.Equal("Existing original conversation", (await new ConversationRepository(rig.Database).GetAsync(rig.ConversationId, Token))!.Title);
        await rig.ReopenAsync();
        Assert.True((await rig.Controller.InspectImportAsync(Token)).CanBrowse);
        var current = await rig.Controller.PreviewAsync(rig.First, Token);
        Assert.Equal(saved.Definition.Identity, current.ClassifiedDefinition!.Identity);
        Assert.Equal(conversationId, (await rig.Bridge.OpenConversationAsync(saved.Definition.Identity, conversationId, Token)).Conversation.Id);
        Assert.Equal(LegacyAgentMigrationState.Unselected, (await rig.Controller.PreviewAsync(rig.Second, Token)).State);
    });

    [Fact]
    public Task Manual_decline_preserves_every_original_and_new_review_requires_a_new_explicit_request() => RunImportAsync(async rig =>
    {
        var before = await rig.CaptureLegacyAsync();
        var review = await rig.Controller.InspectImportAsync(Token);
        var pending = await rig.Controller.RequestImportAsync(review, Token);
        var stillPending = await rig.Controller.CompleteImportAsync(pending, Token);
        Assert.Equal(LegacyAgentImportState.PendingApproval, stillPending.State);
        Assert.Equal(pending.RequestId, stillPending.RequestId); Assert.False(stillPending.CanBrowse);
        Assert.True((await rig.Permissions.DecideAsync(pending.RequestId!, HomeApprovalChoice.Decline,
            cancellationToken: Token)).Succeeded);
        var declined = await rig.Controller.RefreshImportAsync(stillPending, Token);
        Assert.Equal(LegacyAgentImportState.Declined, declined.State); Assert.True(declined.CanRequest);
        Assert.False(declined.CanComplete); Assert.False(declined.CanBrowse);
        Assert.Empty((await rig.Bridge.ListAsync(Token)).Definitions); Assert.Equal(before, await rig.CaptureLegacyAsync());
        var fresh = await rig.Controller.RequestImportAsync(declined, Token);
        Assert.Equal(LegacyAgentImportState.PendingApproval, fresh.State); Assert.NotEqual(pending.RequestId, fresh.RequestId);
        Assert.Equal(before, await rig.CaptureLegacyAsync());
    });

    [Fact]
    public Task Foreign_and_superseded_import_previews_are_acknowledged_pre_effect_refusals() => RunImportAsync(async rig =>
    {
        var before = await rig.CaptureLegacyAsync();
        var review = await rig.Controller.InspectImportAsync(Token);
        var other = rig.NewController();
        var foreign = other.RequestImportAsync(review, Token);
        Assert.Equal("ForeignImportPreview", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => foreign)).Code);
        Assert.True(other.IsAcknowledgedOriginalCommandRefusal(foreign));
        var pending = await rig.Controller.RequestImportAsync(review, Token);
        var stale = rig.Controller.RequestImportAsync(review, Token);
        Assert.Equal("ForeignImportPreview", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => stale)).Code);
        Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(stale));
        var current = await other.InspectImportAsync(Token);
        Assert.Equal(pending.RequestId, current.RequestId);
        Assert.Equal(before, await rig.CaptureLegacyAsync());
    });

}
