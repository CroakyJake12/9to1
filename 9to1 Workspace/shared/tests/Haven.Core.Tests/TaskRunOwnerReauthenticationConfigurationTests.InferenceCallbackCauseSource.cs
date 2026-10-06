using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Local_inference_swallowed_repeated_callback_faults_with_exact_refusal_and_retained_raw_task()
    {
        var provider = new PolicyProvider("local-callback", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); TaskRunAttemptAdmission? admission = null;
        Task<TaskRunAttemptAdmission>? mint = null; var originals = new List<Task>();
        Exception? primary = null; Exception[] knownOwnerCauses = []; InvalidOperationException? refusal = null;
        var callbacks = 0; var catalogueReads = 0;
        try
        {
            mint = InferenceAdmission(authority, provider.Model); admission = await mint;
            provider.Read = () => { catalogueReads++; return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]); };
            var actual = authority.ValidateOriginalInferenceAdmissionAsync(admission, callback =>
            {
                callback();
                if (++callbacks == 1)
                    try { callback(); } catch (InvalidOperationException error) { refusal = error; }
            }, originals.Add, default);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
            knownOwnerCauses = refusal is null ? [] : [refusal];
            Assert.NotNull(refusal); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, refusal));
            Assert.All(failure.Flatten().InnerExceptions, error => Assert.Same(refusal, error));
            Assert.Equal(1, callbacks); Assert.Equal(0, catalogueReads);
            Assert.Single(originals); Assert.All(originals, task => Assert.True(task.IsCompletedSuccessfully));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (admission is not null) await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, knownOwnerCauses);
            else await CloseGenuineLeaseSourceFixtureAsync(authority, admission, mint, primary);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_inference_swallowed_or_replaced_direct_factory_fault_conserves_exact_causes(bool synchronousCancellation)
    {
        var provider = new PolicyProvider("local-body", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(new PolicyStore(), provider); TaskRunAttemptAdmission? admission = null;
        Task<TaskRunAttemptAdmission>? mint = null; var originals = new List<Task>();
        Exception original = synchronousCancellation ? new OperationCanceledException("actual synchronous model catalogue fault")
            : new IOException("actual synchronous model catalogue fault");
        var replacement = new IOException("actual caller replacement after catching the catalogue fault");
        Exception? primary = null; Exception[] knownOwnerCauses = []; var caught = false; var catalogueReads = 0;
        try
        {
            mint = InferenceAdmission(authority, provider.Model); admission = await mint;
            provider.Read = () => { catalogueReads++; throw original; };
            var actual = authority.ValidateOriginalInferenceAdmissionAsync(admission, callback =>
            {
                try { callback(); }
                catch (Exception error)
                {
                    if (!ReferenceEquals(error, original)) throw;
                    caught = true;
                    if (synchronousCancellation) throw replacement;
                }
            }, originals.Add, default);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
            knownOwnerCauses = synchronousCancellation ? [original, replacement] : [original];
            Assert.True(caught); Assert.Equal(1, catalogueReads);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, original));
            if (synchronousCancellation) Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, replacement));
            Assert.All(failure.Flatten().InnerExceptions, error => Assert.Contains(knownOwnerCauses, known => ReferenceEquals(known, error)));
            Assert.NotEmpty(originals); Assert.All(originals, task => Assert.True(task.IsCompletedSuccessfully));
            Assert.DoesNotContain(originals, task => task is Task<IReadOnlyList<ProviderModelDescriptor>>);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (admission is not null) await CompleteInferenceOriginalCleanupAsync(authority, admission, primary, knownOwnerCauses);
            else await CloseGenuineLeaseSourceFixtureAsync(authority, admission, mint, primary);
        }
    }
}
