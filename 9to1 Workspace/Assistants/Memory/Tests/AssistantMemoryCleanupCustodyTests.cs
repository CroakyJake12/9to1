using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed class AssistantMemoryCleanupCustodyTests
{
    [Fact]
    public async Task Close_joins_late_original_cleanup_descendant_and_retains_its_failure()
    {
        var owner = new AssistantMemoryOriginals();
        var parentRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = owner.Admit(async () => { await parentRelease.Task; return true; });
        owner.DemandExternalOriginalRetirementJoin(); Assert.Null(owner.OriginalClose);
        var close = owner.CloseAndDrainAsync();
        var cleanup = owner.AdmitOriginalCleanup(parent, async () =>
        { cleanupEntered.SetResult(); await cleanupRelease.Task; });
        await cleanupEntered.Task;
        parentRelease.SetResult(); await parent;
        Assert.False(close.IsCompleted); Assert.Same(close, owner.OriginalClose);
        var originalFailure = new InvalidOperationException("Original late cleanup failed");
        cleanupRelease.SetException(originalFailure);
        Assert.Same(originalFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup));
        Assert.Same(originalFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => close));
        Assert.Same(close, owner.CloseAndDrainAsync());
        var invoked = false;
        Assert.Throws<InvalidOperationException>(() => { _ = owner.AdmitOriginalCleanup(parent, () => { invoked = true; return Task.CompletedTask; }); });
        Assert.False(invoked);
    }

    [Fact]
    public async Task First_cleanup_requires_actual_live_parent_and_pure_preflight_refuses_self_join()
    {
        var owner = new AssistantMemoryOriginals(); var invoked = false;
        Assert.Throws<InvalidOperationException>(() => { _ = owner.AdmitOriginalCleanup(Task.CompletedTask,
            () => { invoked = true; return Task.CompletedTask; }); });
        Assert.False(invoked); Assert.Null(owner.OriginalClose);
        var parent = owner.Admit(() =>
        {
            Assert.Throws<InvalidOperationException>(owner.DemandExternalOriginalRetirementJoin);
            Assert.Null(owner.OriginalClose); return Task.FromResult(true);
        });
        await parent;
        Assert.Throws<InvalidOperationException>(() => { _ = owner.AdmitOriginalCleanup(parent,
            () => { invoked = true; return Task.CompletedTask; }); });
        Assert.False(invoked); await owner.CloseAndDrainAsync();
    }
}
