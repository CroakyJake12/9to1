using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Core.Tests;

// Actual existing authority/issued lease and raw controlled catalogue; no Strata installation,
// CUDA/model startup, domain or artifact revision approval is supplied by these controls.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Scoped_inference_raw_catalogue_fault_preserves_OCE_and_direct_sibling()
    {
        var provider = new PolicyProvider("scoped", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider);
        var admission = await InferenceAdmission(authority, provider.Model); var originals = new List<Task>();
        var oce = new OperationCanceledException("raw inference catalogue fault"); var sibling = new IOException("raw inference sibling");
        var raw = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([oce, sibling]); provider.Read = () => raw.Task;
        Exception? primary = null;
        try
        {
            var driver = authority.ValidateOriginalInferenceAdmissionAsync(admission, action => action(), originals.Add, default);
            var error = await Assert.ThrowsAsync<AggregateException>(() => driver);
            Assert.True(raw.Task.IsFaulted); Assert.False(raw.Task.IsCanceled);
            Assert.Contains(originals, task => ReferenceEquals(task, raw.Task));
            var causes = error.Flatten().InnerExceptions; Assert.Contains(causes, cause => ReferenceEquals(cause, oce));
            Assert.Contains(causes, cause => ReferenceEquals(cause, sibling));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, [oce, sibling]); }
    }
    [Fact]
    public async Task Scoped_inference_delayed_catalogue_callback_is_revoked_before_late_raw_factory()
    {
        var provider = new PolicyProvider("scoped", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); var admission = await InferenceAdmission(authority, provider.Model);
        var calls = 0; provider.Read = () => { calls++; return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]); };
        var callbacks = 0; Action? delayed = null; var originals = new List<Task>();
        Exception? primary = null; Exception[] knownOwnerCauses = [];
        try
        {
            var driver = authority.ValidateOriginalInferenceAdmissionAsync(admission,
                action => { if (++callbacks == 7) delayed = action; else action(); }, originals.Add, default);
            var error = await Assert.ThrowsAsync<AggregateException>(() => driver);
            Assert.Contains(error.Flatten().InnerExceptions, cause => cause is InvalidOperationException);
            knownOwnerCauses = error.Flatten().InnerExceptions.ToArray();
            Assert.NotNull(delayed); Assert.Equal(0, calls);
            Assert.Throws<InvalidOperationException>(() => delayed!()); Assert.Equal(0, calls);
            Assert.All(originals, task => Assert.True(task.IsCompleted));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, knownOwnerCauses); }
    }
    [Fact]
    public async Task Scoped_inference_postawait_provider_restored_context_cannot_join_authority_driver()
    {
        var provider = new PolicyProvider("scoped", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); var admission = await InferenceAdmission(authority, provider.Model);
        var originalContext = ExecutionContext.Capture()!; var refused = false; var originals = new List<Task>();
        provider.Read = () =>
        {
            ExecutionContext.Run(originalContext, state => { Assert.Throws<InvalidOperationException>((Action)(() =>
                { _ = authority.CloseAndDrainOwnerReauthenticationAsync(); })); refused = true; }, null);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        };
        Exception? primary = null;
        try
        {
            await authority.ValidateOriginalInferenceAdmissionAsync(admission, action => action(), originals.Add, default);
            Assert.True(refused); Assert.NotEmpty(originals);
            Assert.All(originals, task => Assert.True(task.IsCompletedSuccessfully));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, []); }
    }
    [Fact]
    public async Task Scoped_inference_genuine_raw_canceled_catalogue_keeps_canceled_status_and_exact_original_task()
    {
        var provider = new PolicyProvider("scoped", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); var admission = await InferenceAdmission(authority, provider.Model);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var raw = Task.FromCanceled<IReadOnlyList<ProviderModelDescriptor>>(cancellation.Token); provider.Read = () => raw;
        var originals = new List<Task>(); Exception? primary = null; Exception[] knownOwnerCauses = [];
        try
        {
            var driver = authority.ValidateOriginalInferenceAdmissionAsync(admission, action => action(), originals.Add, default);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver);
            Assert.True(raw.IsCanceled); Assert.False(raw.IsFaulted);
            Assert.True(driver.IsCanceled); Assert.False(driver.IsFaulted);
            Assert.Contains(originals, task => ReferenceEquals(task, raw));
            Assert.All(originals, task => Assert.True(task.IsCompleted));
            // Actual terminal authority records supply exact cleanup-cause identities;
            // cancellation category alone never accepts an unrelated close failure.
            var work = (System.Collections.IEnumerable)typeof(TaskRunPermissionAuthority)
                .GetField("_renewalWork", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(authority)!;
            knownOwnerCauses = work.Cast<object>().SelectMany(actual =>
                ((IEnumerable<Exception>)actual.GetType().GetField("Errors")!.GetValue(actual)!)).ToArray();
            Assert.NotEmpty(knownOwnerCauses); Assert.All(knownOwnerCauses, cause => Assert.IsAssignableFrom<OperationCanceledException>(cause));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, knownOwnerCauses); }
    }
    [Fact]
    public async Task Scoped_inference_entire_caller_boundary_refuses_restored_context_authority_self_join()
    {
        var provider = new PolicyProvider("scoped", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); var admission = await InferenceAdmission(authority, provider.Model);
        var context = ExecutionContext.Capture()!; var originals = new List<Task>(); var before = 0; var after = 0; Exception? primary = null;
        try
        {
            await authority.ValidateOriginalInferenceAdmissionAsync(admission, body =>
            {
                Refuse(); before++; body(); Refuse(); after++;
            }, originals.Add, default);
            Assert.True(before >= 4); Assert.Equal(before, after);
            Assert.NotEmpty(originals); Assert.All(originals, actual => Assert.True(actual.IsCompletedSuccessfully));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, []); }
        void Refuse() => ExecutionContext.Run(context, state =>
            Assert.Throws<InvalidOperationException>((Action)(() => { _ = authority.CloseAndDrainOwnerReauthenticationAsync(); })), null);
    }
    private static async Task CompleteInferenceOriginalCleanupAsync(TaskRunPermissionAuthority authority,
        TaskRunAttemptAdmission admission, Exception? primary, IReadOnlyList<Exception> expectedOwnerCauses)
    {
        var unexpected = new List<Exception>(); Task? leaseClose = null; Task? ownerClose = null;
        // Acquire BOTH originals before either join. A sibling close cannot skip the other.
        try { leaseClose = admission.Lease.DisposeAsync().AsTask(); } catch (Exception cause) { Capture(null, cause); }
        try { ownerClose = authority.CloseAndDrainOwnerReauthenticationAsync(); } catch (Exception cause) { Capture(null, cause); }
        if (leaseClose is not null) try { await leaseClose; } catch (Exception cause) { Capture(leaseClose, cause); }
        if (ownerClose is not null)
        {
            try
            {
                if (expectedOwnerCauses.Count == 0) await ownerClose;
                else
                {
                    var known = await Assert.ThrowsAsync<AggregateException>(() => ownerClose);
                    Assert.NotEmpty(known.Flatten().InnerExceptions);
                    Assert.All(known.Flatten().InnerExceptions, cause =>
                        Assert.Contains(expectedOwnerCauses, original => ReferenceEquals(original, cause)));
                    Assert.All(expectedOwnerCauses, original =>
                        Assert.Contains(known.Flatten().InnerExceptions, cause => ReferenceEquals(original, cause)));
                }
            }
            catch (Exception cause) { Capture(ownerClose, cause); }
        }
        if (unexpected.Count != 0)
        {
            if (primary is not null && !unexpected.Any(cause => ReferenceEquals(cause, primary))) unexpected.Insert(0, primary);
            throw new AggregateException("Actual inference fixture body and independently joined original cleanup failed.", unexpected);
        }
        void Capture(Task? actual, Exception cause)
        {
            Add(cause);
            if (actual?.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); }
        }
        void Add(Exception cause) { if (!unexpected.Any(original => ReferenceEquals(original, cause))) unexpected.Add(cause); }
    }
    private static async Task<TaskRunAttemptAdmission> InferenceAdmission(TaskRunPermissionAuthority authority, ProviderModelDescriptor model)
    {
        var proposed = Proposed(); var snapshot = proposed with { OwnerBinding = await authority.AuthorizeStartAsync(proposed, default) };
        var candidate = await authority.CaptureSelectedRouteAsync(snapshot, model, [ToolCapability.Text], []);
        var attempt = Guid.NewGuid(); return new(snapshot, attempt, await authority.AuthorizeAttemptAsync(snapshot, attempt, candidate, null, default));
    }
}
