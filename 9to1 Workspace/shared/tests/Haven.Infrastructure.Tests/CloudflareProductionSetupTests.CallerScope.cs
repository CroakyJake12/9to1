using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class CloudflareProductionSetupTests
{
    [Fact] public Task Actual_Home_connection_read_after_held_capture_uses_same_parent_physical_scope() => Run(async rig =>
    {
        var review = await rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default));
        await rig.Own(review.SubmitOriginalAsync(default));
        Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HavenOS.Home.PermissionsTrustNotifications.HomeApprovalChoice.Accept))).Succeeded);
        await rig.Own(review.CommitOriginalAsync(default));
        var earlier = ExecutionContext.Capture()!; var parent = new object(); int callbacks = 0;
        Action<Action> caller = finite => CloudflareOriginalExecutionGuard.InvokeOriginal(parent, () => { finite(); return true; });
        var held = new TaskCompletionSource<ExternalConnection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Connections.ReadOriginal = held.Task;
        rig.Connections.BeforeRead = () => callbacks++;
        var composite = new CanonicalWorkspaceCloudflareToolActionOwner(null!, null!, null!, rig.Owner);
        var active = new[] { new ActiveCapability(ExternalConnectionNaming.CapabilityKey(rig.Connections.Value.Id), "configured", "tool", "", "mcp", "connections") };
        var actual = rig.Own(composite.GetCloudflareDefinitionsAsync(active, caller, default));
        try
        {
            Assert.False(actual.IsCompleted);
            rig.Connections.BeforeRead = () =>
            {
                callbacks++;
                ExecutionContext.Run(earlier, _ => Assert.Throws<InvalidOperationException>(() => CloudflareOriginalExecutionGuard.DemandExternalJoin(parent)), null);
            };
        }
        finally { held.TrySetResult(rig.Connections.Value); }
        var definitions = await actual;
        Assert.True(callbacks >= 2); Assert.Contains(definitions, definition => definition.Name == "cloudflare_kv_list");
        Assert.Equal(0, rig.Mcp.Calls);
        rig.Connections.BeforeRead = null; rig.Connections.ReadOriginal = null;
    });

    [Fact] public Task Different_parent_scope_cannot_rebind_a_privately_issued_Home_service() => Run(async rig =>
    {
        var review = await rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default)); await rig.Own(review.SubmitOriginalAsync(default));
        Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HavenOS.Home.PermissionsTrustNotifications.HomeApprovalChoice.Accept))).Succeeded);
        await rig.Own(review.CommitOriginalAsync(default));
        Action<Action> first = body => body(); Action<Action> foreign = body => { body(); GC.KeepAlive(rig); };
        var service = await rig.Own(rig.Owner.AcquireCallerScopedOriginalAsync(first, default));
        var actual = rig.Own(rig.Owner.RevalidateCallerScopedOriginalAsync(service, foreign, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actual); rig.Expect(actual);
        Assert.Equal(0, rig.Mcp.Calls);
    });
}
