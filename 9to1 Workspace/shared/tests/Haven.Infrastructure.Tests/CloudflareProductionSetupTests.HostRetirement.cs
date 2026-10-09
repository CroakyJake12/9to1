using Haven.Core;
using HavenOS.Home.Core;
using Haven.Application;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class CloudflareProductionSetupTests
{
    [Fact] public Task Host_retirement_joins_actual_held_source_driver_and_every_direct_cause() => Run(async rig =>
    {
        var first = new OperationCanceledException("actual faulted read, not cancelled task");
        var second = new IOException("independent original read cause");
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<ExternalConnection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Connections.ReadOriginal = raw.Task; rig.Connections.BeforeRead = () => reached.TrySetResult();
        var actual = rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default));
        Task? close = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            rig.Owner.RequestOriginalHostRetirement(); close = rig.Own(rig.Owner.CloseAndDrainOriginalHostAsync());
            Assert.Same(close, rig.Owner.CloseAndDrainOriginalHostAsync()); Assert.False(close.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => { _ = rig.Owner.GetSetupAsync(default); });
        }
        finally { raw.TrySetException([first, second]); }
        var bodyError = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expect(actual);
        Assert.True(actual.IsFaulted); Assert.Contains(first, bodyError.InnerExceptions); Assert.Contains(second, bodyError.InnerExceptions);
        if (close is not null)
        {
            var closeError = await Assert.ThrowsAsync<AggregateException>(() => close); rig.Expect(close);
            Assert.True(close.IsFaulted); Assert.Contains(first, closeError.InnerExceptions); Assert.Contains(second, closeError.InnerExceptions);
        }
        Assert.Same(raw.Task, rig.Connections.LastRead); Assert.Equal(0, rig.Mcp.Calls);
    });

    [Fact] public Task Restored_context_raw_connection_factory_cannot_join_same_host_close() => Run(async rig =>
    {
        var earlier = ExecutionContext.Capture()!; int callbacks = 0;
        rig.Connections.BeforeRead = () =>
        {
            callbacks++;
            ExecutionContext.Run(earlier, _ => Assert.Throws<InvalidOperationException>(() => { _ = rig.Owner.CloseAndDrainOriginalHostAsync(); }), null);
        };
        var review = await rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default));
        await rig.Own(review.SubmitOriginalAsync(default));
        Assert.True(callbacks > 0); Assert.Equal(0, rig.Mcp.Calls);
        rig.Connections.BeforeRead = null;
        rig.Owner.RequestOriginalHostRetirement(); var close = rig.Own(rig.Owner.CloseAndDrainOriginalHostAsync()); await close;
        Assert.Same(close, rig.Owner.CloseAndDrainOriginalHostAsync());
    });

    [Fact] public Task Startup_prerequisite_refuses_before_any_actual_connection_factory() => Run(async rig =>
    {
        // Synthetic negative prerequisite only; this is not a native Home bootstrap proof.
        rig.Owner.BindOriginalHostStartup(() => false); int reads = 0; rig.Connections.BeforeRead = () => reads++;
        var failure = Assert.Throws<CloudflareSetupRequiredException>(() => { _ = rig.Owner.GetSetupAsync(default); });
        Assert.Equal("CF_ORIGINAL_HOME_STARTUP_REQUIRED", failure.Code); Assert.Equal(0, reads); Assert.Equal(0, rig.Mcp.Calls);
        await rig.Own(rig.Owner.CloseAndDrainOriginalHostAsync());
    });
}
