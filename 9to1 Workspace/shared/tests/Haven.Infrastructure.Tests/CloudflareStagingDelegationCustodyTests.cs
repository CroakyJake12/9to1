using System.Reflection;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class CloudflareStagingDelegationTests
{
    [Fact] public Task Actual_completion_lease_survives_post_factory_scope_fault_and_is_released_before_failure_settles() => Run(async rig =>
    {
        await rig.Configure(); var review = await rig.Own(rig.Owner.PrepareStagingDelegationAsync(rig.Selection, 0, default)); rig.Reviews.Add(review);
        await rig.Own(review.SubmitOriginalAsync(default)); Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept))).Succeeded);
        var parent = Field<CloudflareOriginalTaskLedger>(review, "_stages");
        CloudflareOriginalTaskLedger? worker = null; ICloudflareOriginalWorkerReadAuthority? admission = null;
        var cause = new IOException("actual parent finite scope fault after original completion acquisition"); var fired = false;
        rig.Client.BeforeAdmission = actual => { admission = actual; worker = Field<CloudflareOriginalTaskLedger>(actual, "_workerStages"); };
        parent.BindOriginalCallerCallback(action =>
        {
            action();
            if (!fired && worker?.OriginalTasks.Any(task => task is Task<IAsyncDisposable?> { IsCompletedSuccessfully: true }) == true)
            { fired = true; throw cause; }
        });
        var commit = rig.Own(review.CommitOriginalAsync(default));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => commit); rig.Expected.Add(commit); rig.ExpectedReviews.Add(review);
        Assert.True(fired); Assert.True(commit.IsFaulted); Assert.Contains(cause, Leaves(failure)); Assert.NotNull(worker); Assert.NotNull(admission);
        var originalAcquisition = Assert.Single(worker!.OriginalTasks.OfType<Task<IAsyncDisposable?>>());
        Assert.True(originalAcquisition.IsCompletedSuccessfully); Assert.NotNull(await originalAcquisition);
        Assert.Contains(parent.OriginalTasks, task => ReferenceEquals(task, originalAcquisition));
        Assert.Equal(0, rig.Client.Reads); Assert.DoesNotContain((await rig.State()).Records, record => record.RecordType == "home.cloudflare.staging");
        // The real private completion semaphore is free again. This probes lifetime only;
        // it performs no retry, remote read, permission response or local configuration save.
        var capability = admission!.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(HomeResourceExecutionCapability)).Select(field => (HomeResourceExecutionCapability)field.GetValue(admission)!).Single();
        var broker = Field<HomeResourceOperationBroker>(rig.Owner, "_broker");
        var method = typeof(HomeResourceExecutionCapability).GetMethod("AcquireCommitCompletionLeaseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var actualProbe = rig.Own(((ValueTask<IAsyncDisposable?>)method.Invoke(capability, [broker, CancellationToken.None])!).AsTask());
        IAsyncDisposable? probe = null;
        try { probe = await actualProbe.WaitAsync(TimeSpan.FromSeconds(10)); Assert.NotNull(probe); }
        finally { if (probe is not null) await rig.Own(probe.DisposeAsync().AsTask()); }
    });
    [Fact] public void Invalid_extra_settings_projection_is_refused_and_never_serialized_as_public_binding_observation()
    {
        var selection = new CloudflareStagingBindingSelection("staging-worker", "MARKERS");
        var raw = JsonSerializer.SerializeToElement(new { ok = true, worker = selection.WorkerName, binding = selection.BindingName, namespace_id = new string('b', 32), secret = "source-held-invalid-field" });
        var failure = Assert.Throws<CloudflareSetupRequiredException>(() => CloudflareStagingBindingContract.DetachValidatedOriginalProjection(raw, selection));
        var original = Task.FromResult(raw); var observation = new CloudflareWorkerBindingObservation("", JsonSerializer.SerializeToElement(new { }), [original], [failure]);
        var wire = JsonSerializer.Serialize(observation);
        Assert.DoesNotContain("source-held-invalid-field", wire); Assert.DoesNotContain("OriginalTasks", wire); Assert.DoesNotContain("OriginalErrors", wire);
        Assert.Same(original, Assert.Single(observation.OriginalTasks)); Assert.Same(failure, Assert.Single(observation.OriginalErrors));
    }
    [Fact] public void Validated_detached_projection_exposes_only_the_exact_nonsecret_selected_binding()
    {
        var selection = new CloudflareStagingBindingSelection("staging-worker", "MARKERS");
        var raw = JsonSerializer.SerializeToElement(new { namespace_id = new string('b', 32), binding = selection.BindingName, worker = selection.WorkerName, ok = true });
        var detached = CloudflareStagingBindingContract.DetachValidatedOriginalProjection(raw, selection);
        Assert.Equal(4, detached.EnumerateObject().Count()); Assert.Equal(new string('b', 32), CloudflareStagingBindingContract.DemandNamespace(detached, selection));
        Assert.Equal(selection.WorkerName, detached.GetProperty("worker").GetString()); Assert.Equal(selection.BindingName, detached.GetProperty("binding").GetString());
    }
    private static T Field<T>(object original, string name) => (T)original.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)!;
}
