using System.Reflection;
using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Genuine_inference_lease_returns_the_same_authority_driver_and_retains_actual_catalogue()
    {
        var provider = new PolicyProvider("lease-source", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); TaskRunAttemptAdmission? admission = null;
        Task<TaskRunAttemptAdmission>? mint = null;
        var raw = Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]); provider.Read = () => raw;
        var originals = new List<Task>(); Exception? primary = null;
        try
        {
            mint = InferenceAdmission(authority, provider.Model); admission = await mint;
            var source = Assert.IsAssignableFrom<ITaskRunOriginalInferenceLeaseSource>(admission.Lease);
            var currentness = Assert.IsAssignableFrom<ITaskRunOriginalInferenceLeaseCurrentnessSource>(admission.Lease);
            currentness.DemandOriginalInferenceWithinSource(admission);
            var actual = source.RevalidateOriginalInferenceWithinSourceAsync(admission, callback => callback(), originals.Add, default);
            await actual;
            Assert.True(actual.IsCompletedSuccessfully); Assert.Contains(originals, item => ReferenceEquals(item, raw));
            Assert.All(originals, item => Assert.True(item.IsCompletedSuccessfully));
            var work = (System.Collections.IEnumerable)typeof(TaskRunPermissionAuthority)
                .GetField("_renewalWork", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(authority)!;
            Assert.Contains(work.Cast<object>(), item => ReferenceEquals(item.GetType().GetField("Driver")!.GetValue(item), actual));
        }
        catch (Exception error) { primary = error; throw; }
        finally { await CloseGenuineLeaseSourceFixtureAsync(authority, admission, mint, primary); }
    }
    [Fact]
    public async Task Genuine_inference_lease_refuses_swapped_or_closed_pairing_before_raw_read()
    {
        var provider = new PolicyProvider("lease-source", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); TaskRunAttemptAdmission? admission = null;
        Task<TaskRunAttemptAdmission>? mint = null;
        var originals = new List<Task>(); var reads = 0; Exception? primary = null;
        try
        {
            mint = InferenceAdmission(authority, provider.Model); admission = await mint;
            provider.Read = () => { reads++; return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]); };
            var source = Assert.IsAssignableFrom<ITaskRunOriginalInferenceLeaseSource>(admission.Lease);
            Assert.Throws<UnauthorizedAccessException>((Action)(() =>
            { _ = source.RevalidateOriginalInferenceWithinSourceAsync(admission with { Lease = null! }, callback => callback(), originals.Add, default); }));
            var currentness = Assert.IsAssignableFrom<ITaskRunOriginalInferenceLeaseCurrentnessSource>(admission.Lease);
            Assert.Throws<UnauthorizedAccessException>((Action)(() =>
            { currentness.DemandOriginalInferenceWithinSource(admission with { Lease = null! }); }));
            var close = admission.Lease.DisposeAsync().AsTask(); await close;
            Assert.Throws<UnauthorizedAccessException>((Action)(() =>
            { currentness.DemandOriginalInferenceWithinSource(admission); }));
            Assert.Throws<UnauthorizedAccessException>((Action)(() =>
            { _ = source.RevalidateOriginalInferenceWithinSourceAsync(admission, callback => callback(), originals.Add, default); }));
            Assert.Equal(0, reads); Assert.Empty(originals); Assert.True(close.IsCompletedSuccessfully);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await CloseGenuineLeaseSourceFixtureAsync(authority, admission, mint, primary); }
    }
    private static async Task CloseGenuineLeaseSourceFixtureAsync(TaskRunPermissionAuthority authority,
        TaskRunAttemptAdmission? admission, Task<TaskRunAttemptAdmission>? mint, Exception? primary)
    {
        if (admission is not null)
        {
            await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, []);
            return;
        }
        var failures = new List<Exception>(); Task? close = null;
        if (mint?.Exception is { } mintFault)
        {
            if (primary is not null) Add(primary);
            Add(mintFault); foreach (var cause in mintFault.InnerExceptions) Add(cause);
        }
        try { close = authority.CloseAndDrainOwnerReauthenticationAsync(); }
        catch (Exception error) { Add(error); }
        if (close is not null)
        {
            try { await close; }
            catch (Exception error)
            {
                Add(error);
                if (close.Exception is { } group)
                { Add(group); foreach (var cause in group.InnerExceptions) Add(cause); }
            }
        }
        if (failures.Count != 0)
        {
            if (primary is not null && !failures.Any(value => ReferenceEquals(value, primary))) failures.Insert(0, primary);
            throw new AggregateException("Actual issuance/body and independently joined authority cleanup failed.", failures);
        }
        void Add(Exception actual)
        { if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual); }
    }
}
