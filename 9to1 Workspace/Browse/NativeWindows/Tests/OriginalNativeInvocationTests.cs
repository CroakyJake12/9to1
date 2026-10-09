using Xunit;
namespace HavenOS.Apps.Browse.Tests;

// These pure owner-scope controls do not certify the Windows native backend.
public sealed class OriginalNativeInvocationTests
{
    [Fact]
    public async Task Actual_logical_driver_refuses_its_own_join_across_await_and_releases_after_settlement()
    {
        var originalOwner = new object();
        using (OriginalNativeInvocation.EnterDriver(originalOwner))
        {
            await Task.Yield();
            Assert.Throws<InvalidOperationException>(() => OriginalNativeInvocation.DemandExternalJoin(originalOwner));
        }
        OriginalNativeInvocation.DemandExternalJoin(originalOwner);
    }
    [Fact]
    public void Physical_external_invocation_refuses_join_under_restored_ExecutionContext()
    {
        var originalOwner = new object(); var prior = ExecutionContext.Capture(); Assert.NotNull(prior);
        using (OriginalNativeInvocation.EnterExternal(originalOwner))
        {
            ExecutionContext.Run(prior!, _ => Assert.Throws<InvalidOperationException>(() => OriginalNativeInvocation.DemandExternalJoin(originalOwner)), null);
        }
        OriginalNativeInvocation.DemandExternalJoin(originalOwner);
    }
    [Fact]
    public void Parent_and_child_scopes_retain_exact_owner_identity()
    {
        var parent = new object(); var child = new object(); var foreign = new object();
        using (OriginalNativeInvocation.EnterExternal(parent))
        using (OriginalNativeInvocation.EnterExternal(child))
        {
            Assert.Throws<InvalidOperationException>(() => OriginalNativeInvocation.DemandExternalJoin(parent));
            Assert.Throws<InvalidOperationException>(() => OriginalNativeInvocation.DemandExternalJoin(child));
            OriginalNativeInvocation.DemandExternalJoin(foreign);
        }
        OriginalNativeInvocation.DemandExternalJoin(parent); OriginalNativeInvocation.DemandExternalJoin(child);
    }
    [Fact]
    public void Expired_callback_marker_does_not_block_later_gated_owner_retirement()
    {
        var originalOwner = new object(); ExecutionContext? captured;
        using (OriginalNativeInvocation.EnterExternal(originalOwner)) captured = ExecutionContext.Capture();
        Assert.NotNull(captured);
        ExecutionContext.Run(captured!, _ => OriginalNativeInvocation.DemandExternalJoin(originalOwner), null);
    }
}
