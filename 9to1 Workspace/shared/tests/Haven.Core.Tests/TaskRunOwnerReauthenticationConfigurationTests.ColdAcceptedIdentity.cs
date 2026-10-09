using System.Reflection;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

// Actual cold policy dispatch controls only. Public capsule material below never enters
// Prepare/Activate and issues no private journal claim, owner activation or permission.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Accepted_identity_policy_observes_current_policy_without_requiring_unavailable_original_catalogue()
    {
        var reads = 0; var provider = new PolicyProvider("failed-original", () => { reads++; throw new IOException("must not select original failed provider"); });
        var policy = new PolicyStore(); var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority);
        await RunAcceptedPolicyControl(authority, sources, [], async () =>
        {
            await ReadActualColdActivationPolicy(authority, sources, AcceptedPolicyMaterial(provider.Model));
            Assert.Equal(0, reads); Assert.Equal(1, policy.Reads); Assert.Empty(sources.OriginalErrors);
            Assert.False(authority.HasOriginalColdRecoveryComposition(null!, null!));
        });
    }
    [Fact]
    public async Task Schema_one_still_requires_original_model_and_retains_its_actual_catalogue_failure()
    {
        var cause = new IOException("original schema one catalogue unavailable");
        var original = Task.FromException<IReadOnlyList<ProviderModelDescriptor>>(cause);
        var provider = new PolicyProvider("failed-original", () => original); var policy = new PolicyStore();
        var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority);
        await RunAcceptedPolicyControl(authority, sources, [cause], async () =>
        {
            var material = AcceptedPolicyMaterial(provider.Model) with { SchemaVersion = 1, Boundary = TaskRunColdBoundaryKind.NeverStartedAcceptedInput };
            var read = ReadActualColdActivationPolicy(authority, sources, material);
            var error = await Assert.ThrowsAsync<AggregateException>(() => read);
            Assert.True(read.IsFaulted); Assert.Contains(AcceptedPolicyLeaves(error), value => ReferenceEquals(value, cause));
            Assert.Contains(sources.OriginalTasks, value => ReferenceEquals(value, original)); Assert.Equal(0, policy.Reads);
        });
    }
    [Fact]
    public async Task Accepted_identity_policy_refuses_unowned_resource_context_before_policy_read()
    {
        var provider = new PolicyProvider("selected", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        var policy = new PolicyStore(); var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority);
        var known = new List<Exception>();
        await RunAcceptedPolicyControl(authority, sources, known, async () =>
        {
            var material = AcceptedPolicyMaterial(provider.Model);
            var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadActualColdActivationPolicy(authority, sources,
                material with { OriginalInput = material.OriginalInput with { WorkspaceRoot = "/unowned/public-path" } })); known.Add(error);
            Assert.Equal(0, policy.Reads); Assert.Contains(sources.OriginalErrors, value => ReferenceEquals(value, error));
            Assert.False(authority.HasOriginalColdRecoveryComposition(null!, null!));
        });
    }
    [Fact]
    public async Task Accepted_identity_policy_keeps_actual_faulted_policy_OCE_and_all_direct_causes()
    {
        var cancellation = new OperationCanceledException("actual central policy fault"); var sibling = new IOException("actual central policy sibling");
        var original = new TaskCompletionSource<ModelPermissionPolicy>(TaskCreationOptions.RunContinuationsAsynchronously); original.SetException([cancellation, sibling]);
        var policy = new PolicyStore { Read = () => original.Task };
        var provider = new PolicyProvider("unused-original", () => throw new InvalidOperationException("No model selection in identity policy"));
        var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority);
        await RunAcceptedPolicyControl(authority, sources, [cancellation, sibling], async () =>
        {
            var read = ReadActualColdActivationPolicy(authority, sources, AcceptedPolicyMaterial(provider.Model));
            var error = await Assert.ThrowsAsync<AggregateException>(() => read);
            Assert.True(read.IsFaulted); Assert.False(read.IsCanceled); Assert.True(original.Task.IsFaulted);
            Assert.Contains(sources.OriginalTasks, value => ReferenceEquals(value, original.Task));
            Assert.Contains(AcceptedPolicyLeaves(error), value => ReferenceEquals(value, cancellation));
            Assert.Contains(AcceptedPolicyLeaves(error), value => ReferenceEquals(value, sibling));
        });
    }
    private static TaskRunColdCapsule AcceptedPolicyMaterial(ProviderModelDescriptor model) => new(2, Guid.NewGuid(),
        TaskRunColdBoundaryKind.SettledUnfinishedToolResponse, Proposed(), null!, null!, PolicyInput(model), DateTimeOffset.UnixEpoch);
    private static Task ReadActualColdActivationPolicy(TaskRunPermissionAuthority authority, CloudflareOriginalTaskLedger sources, TaskRunColdCapsule capsule) =>
        typeof(TaskRunPermissionAuthority).GetMethod("ReadColdActivationPolicyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<CloudflareOriginalTaskLedger, TaskRunColdCapsule, CancellationToken, Task>>(authority)(sources, capsule, default);
    private static IEnumerable<Exception> AcceptedPolicyLeaves(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(AcceptedPolicyLeaves) : [error];
    private static async Task RunAcceptedPolicyControl(TaskRunPermissionAuthority authority, CloudflareOriginalTaskLedger sources,
        IReadOnlyCollection<Exception> known, Func<Task> body)
    {
        Exception? primary = null; var errors = new List<Exception>(); Task? close = null;
        try { await body(); } catch (Exception error) { primary = error; }
        try { close = authority.CloseAndDrainOwnerReauthenticationAsync(); } catch (Exception error) { errors.Add(error); }
        foreach (var original in sources.OriginalTasks)
            try { await original; } catch (Exception error) { errors.Add(original.Exception ?? error); }
        try { await sources.ObserveAllOriginalTasksAsync(); } catch (Exception error) { errors.Add(error); }
        errors.AddRange(sources.OriginalErrors);
        if (close is not null) try { await close; } catch (Exception error) { errors.Add(close.Exception ?? error); }
        var unexpected = errors.SelectMany(AcceptedPolicyLeaves).Distinct<Exception>(ReferenceEqualityComparer.Instance)
            .Where(error => !known.Any(value => ReferenceEquals(value, error))).ToArray();
        if (unexpected.Length != 0) throw new AggregateException("Actual accepted policy/source cleanup failed.", primary is null ? unexpected : new[] { primary }.Concat(unexpected));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
}
