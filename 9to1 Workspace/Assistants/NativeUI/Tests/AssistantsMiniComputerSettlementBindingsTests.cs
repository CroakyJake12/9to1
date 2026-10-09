using Haven.Application;
using HavenOS.Apps.Assistants.MiniComputer;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsMiniComputerSettlementBindingsTests
{
    [Fact]
    public async Task Same_held_original_settlement_survives_revocation_without_control_publication()
    {
        var (bindings, preview) = Create(); var original = new TaskCompletionSource<AssistantMiniComputerOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bindings.BeginOperation(); bindings.RetainOriginalOperation(preview, original.Task);
        var publications = 0; bindings.PropertyChanged += (_, _) => publications++;
        bindings.Revoke(); Assert.True(bindings.HasUnconfirmedChanges);
        Assert.False(bindings.ObserveOriginalSettlement(preview, original.Task));
        var result = new AssistantMiniComputerOperationResult("NotObserved", null, false, false, "Observed source result fixture; no Home or VM effect.");
        Assert.False(bindings.ObserveOriginalSettlement(preview, Task.FromResult(result))); // Equal result, foreign task.
        original.SetResult(result); await original.Task;
        Assert.True(bindings.ObserveOriginalSettlement(preview, original.Task));
        Assert.False(bindings.HasUnconfirmedChanges); Assert.Equal(0, publications);
        Assert.False(bindings.IsActionAvailable("assistants.mini.confirm"));
    }
    [Fact]
    public async Task Actual_failed_source_and_unknown_publication_remain_retirement_barriers()
    {
        var (bindings, preview) = Create(); var original = new TaskCompletionSource<AssistantMiniComputerOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bindings.BeginOperation(); bindings.RetainOriginalOperation(preview, original.Task); bindings.Revoke();
        var cause = new IOException("Actual display-source fixture failure"); original.SetException(cause);
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => original.Task));
        Assert.False(bindings.ObserveOriginalSettlement(preview, original.Task)); Assert.True(bindings.HasUnconfirmedChanges);
        var (unknown, second) = Create(); var healthy = Task.FromResult(new AssistantMiniComputerOperationResult("NotObserved", null, false, false, "Source settled"));
        unknown.BeginOperation(); unknown.RetainOriginalOperation(second, healthy); unknown.MarkUnconfirmed("Actual independent publication failure"); unknown.Revoke();
        Assert.True(unknown.ObserveOriginalSettlement(second, healthy)); Assert.True(unknown.HasUnconfirmedChanges);
    }
    private static (AssistantsMiniComputerCuiBindings, AssistantMiniComputerOperationPreview) Create()
    {
        var bindings = new AssistantsMiniComputerCuiBindings(true, "", (_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        var preview = new AssistantMiniComputerOperationPreview(new object(), new DisplayIntent());
        bindings.SetPreview(new(preview, "Exact source observation fixture only")); return (bindings, preview);
    }
    // No production issuer/authority is fabricated. This supplies display fields only.
    private sealed class DisplayIntent : ICanonicalMiniComputerOperationIntent
    {
        public Guid OperationId { get; } = Guid.NewGuid();
        public CanonicalMiniComputerAction Action => CanonicalMiniComputerAction.Inspect;
        public CanonicalMiniComputerTarget Target { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "fixture", "Fictional VM", 1, 1, "fixture-observation");
        public AuthenticatedResourceActor Actor => throw new NotSupportedException();
        public ResourceStoreIdentity OriginalStoreIdentity => throw new NotSupportedException();
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => throw new NotSupportedException();
        public VerifiedResourceStoreOwnership OriginalDenOwnership => throw new NotSupportedException();
        public string DenId => throw new NotSupportedException(); public string NamespaceId => throw new NotSupportedException();
        public string DefinitionId => throw new NotSupportedException(); public long DefinitionRevision => throw new NotSupportedException();
        public Guid ConversationId => throw new NotSupportedException(); public Guid? TaskId => throw new NotSupportedException();
    }
}
