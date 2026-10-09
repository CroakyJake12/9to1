using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Actual_preview_observes_same_Home_request_decline_and_later_explicit_save() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync();
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        await using var foreignPresentation = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var observer = (IAssistantMemoryWriteObservationSource)management;
        var view = await management.ReadAsync(binding, Token); Assert.True(view.IsAvailable);
        var preview = await management.PrepareAsync(view, "Preferred examples", "Use concrete local examples", Guid.NewGuid(), Token);
        var prepared = observer.ObserveOriginalWrite(preview);
        Assert.Equal(preview.OperationId, prepared.OperationId); Assert.Equal(AssistantMemoryWriteState.Prepared, prepared.State);
        Assert.Null(prepared.HomeApprovalRequestId);
        Assert.Throws<UnauthorizedAccessException>(() => foreignPresentation.ObserveOriginalWrite(preview));

        var declined = management.CommitAsync(preview, Token); string? actualDeclinedRequest = null;
        await DecideMemoryWrite(rig, declined, HomeApprovalChoice.Decline, requestId =>
        {
            actualDeclinedRequest = requestId;
            var pending = observer.ObserveOriginalWrite(preview);
            Assert.Equal(AssistantMemoryWriteState.HomeReview, pending.State);
            Assert.Equal(requestId, pending.HomeApprovalRequestId); Assert.Equal(preview.OperationId, pending.OperationId);
        });
        Assert.False((await declined).Saved);
        var refused = observer.ObserveOriginalWrite(preview);
        Assert.Equal(AssistantMemoryWriteState.Declined, refused.State); Assert.Equal(actualDeclinedRequest, refused.HomeApprovalRequestId);
        Assert.False((await management.CommitAsync(preview, Token)).Saved); // SAME operation never replays.
        Assert.Empty((await management.ReadAsync(binding, Token)).Records);

        var acceptedPreview = await management.PrepareAsync(view, "Preferred examples", "Use short concrete local examples", Guid.NewGuid(), Token);
        var accepted = management.CommitAsync(acceptedPreview, Token);
        await DecideMemoryWrite(rig, accepted, HomeApprovalChoice.Accept, requestId =>
        {
            Assert.NotEqual(actualDeclinedRequest, requestId);
            Assert.Equal(requestId, observer.ObserveOriginalWrite(acceptedPreview).HomeApprovalRequestId);
        });
        var result = await accepted; Assert.True(result.Saved); Assert.NotNull(result.Record);
        Assert.Equal(AssistantMemoryWriteState.Saved, observer.ObserveOriginalWrite(acceptedPreview).State);
        var persisted = Assert.Single((await management.ReadAsync(binding, Token)).Records);
        Assert.Equal(result.Record.Id, persisted.Id); Assert.Equal(acceptedPreview.Candidate.Summary, persisted.Summary);
        Assert.Equal(AssistantMemoryWriteState.Declined, observer.ObserveOriginalWrite(preview).State);
    });
}
