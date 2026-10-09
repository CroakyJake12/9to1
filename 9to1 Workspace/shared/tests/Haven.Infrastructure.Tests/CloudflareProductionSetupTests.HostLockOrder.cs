using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class CloudflareProductionSetupTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Host_admission_and_independent_review_shutdown_keep_one_metadata_lock_order(int operation) => Run(async rig =>
    {
        // Genuine local store/profile/broker; the SDK stub refuses every network operation.
        var mcp = new LockOrderNoNetworkClient(); var profiles = new HomeLocalProfileIdentity(rig.Store, new Principal());
        HomeCloudflareServiceOwner? current = null;
        var resolver = new HomeCloudflareResourceResolver(() => current ?? throw new InvalidOperationException());
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(profiles, [resolver]), rig.Permissions);
        var owner = current = new HomeCloudflareServiceOwner(rig.Store, profiles, broker, rig.Permissions, rig.Connections, new Actions(), mcp);
        using var release = new ManualResetEventSlim();
        var admissionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int predicates = 0; Task<CloudflareSetupObservation>? actualAdmission = null; Task? actualContender = null; Thread? contenderThread = null;
        owner.BindOriginalHostStartup(() =>
        {
            if (Interlocked.Increment(ref predicates) == 1)
            {
                admissionEntered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Actual finite host admission was not released.");
            }
            return true;
        });
        var admission = rig.Own(Task.Run(async () =>
        { actualAdmission = owner.GetSetupAsync(default); return await actualAdmission; }));
        Task? contender = null; Task? probe = null;
        try
        {
            await admissionEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            contender = rig.Own(Task.Factory.StartNew(async () =>
            {
                contenderThread = Thread.CurrentThread;
                contenderEntered.TrySetResult();
                try
                {
                    actualContender = operation switch
                    {
                        0 => owner.CloseAndDrainOriginalStagingReviewsAsync(),
                        1 => owner.CloseAndDrainOriginalRecoveriesAsync(),
                        _ => owner.PrepareStagingDelegationAsync(new("staging-worker", "TASK_MARKERS"), 0, default)
                    };
                    await actualContender;
                }
                catch (ObjectDisposedException) when (operation == 2) { }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap());
            await contenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await rig.Own(Task.Run(() => SpinWait.SpinUntil(() =>
                contenderThread is { } thread && (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(2)))));
            // This actual owner method takes only the review metadata gate. It must remain
            // callable while the competing original waits for the held host admission gate.
            probe = rig.Own(Task.Run(owner.RequestOriginalStagingRetirement));
            await probe.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(contender.IsCompleted);
            Assert.Equal(0, mcp.Calls);
        }
        finally { release.Set(); }
        await admission;
        if (contender is not null) await contender;
        if (probe is not null) await probe;
        Assert.NotNull(actualAdmission); _ = rig.Own(actualAdmission!); Assert.True(actualAdmission!.IsCompletedSuccessfully);
        if (actualContender is not null)
        {
            _ = rig.Own(actualContender);
            Assert.True(actualContender.IsCompletedSuccessfully);
            Assert.Same(actualContender, operation == 0 ? owner.CloseAndDrainOriginalStagingReviewsAsync() : owner.CloseAndDrainOriginalRecoveriesAsync());
        }
        owner.RequestOriginalHostRetirement(); await rig.Own(owner.CloseAndDrainOriginalHostAsync());
        Assert.Equal(0, mcp.Calls);
    });
    private sealed class LockOrderNoNetworkClient : ICloudflareMcpInvocationClient, ICloudflareWorkerBindingClient
    {
        internal int Calls;
        public Task<CloudflareMcpDispatchResult> InvokeOriginalAsync(CloudflareOriginalTaskBinding binding,
            ICloudflareSavedServiceSource services, ITaskRunOriginalActionAdmissionSource actions,
            ICloudflareOriginalPermission permission, CancellationToken token)
        { Interlocked.Increment(ref Calls); throw new NotSupportedException("No network in metadata lock-order controls."); }
        public bool IsIssuedOriginalResult(CloudflareCompiledInvocation invocation, CloudflareMcpDispatchResult result) => false;
        public Task<CloudflareWorkerBindingObservation> ReadOriginalWorkerBindingAsync(CloudflareSavedService service,
            CloudflareStagingBindingSelection selection, ICloudflareOriginalWorkerReadAuthority authority,
            Action<Action>? caller, CancellationToken token)
        { Interlocked.Increment(ref Calls); throw new NotSupportedException("No network in metadata lock-order controls."); }
        public bool IsIssuedOriginalWorkerBinding(CloudflareSavedService service,
            CloudflareStagingBindingSelection selection, CloudflareWorkerBindingObservation result) => false;
    }
}
