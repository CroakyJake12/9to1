using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed class StrataRequestObservationBindingTests
{
    [Fact]
    public async Task Unbound_global_observer_refuses_without_artifact_acquisition()
    {
        var artifacts = new StrataNativeArtifactSource(null!, null!);
        var source = new StrataRuntimeObservationSource(artifacts); var scope = new Scope();
        Exception? primary = null;
        try
        {
            var actual = source.ObserveOriginalAsync(new("strata", "model"), scope, default);
            var error = await Assert.ThrowsAsync<InferenceEngineException>(() => actual);
            Assert.Equal("STRATA_ORIGINAL_REQUEST_MODEL_USE_ADMISSION_REQUIRED", error.Error.Message);
            Assert.Empty(scope.Raw); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await CloseActualAsync(artifacts, primary); }
    }
    [Fact]
    public async Task Independent_bound_observers_keep_exact_request_reference_without_global_slot()
    {
        var artifacts = new StrataNativeArtifactSource(null!, null!);
        var descriptor = new StrataRuntimeObservationSource(artifacts);
        var first = Admission(); var second = Admission();
        var observer1 = descriptor.BindOriginalRequest(first); var observer2 = descriptor.BindOriginalRequest(second);
        var fault = new IOException("finite observation stopped before artifact acquisition");
        var scope1 = new Scope(fault); var scope2 = new Scope(fault); Exception? primary = null;
        try
        {
            Assert.NotSame(observer1, observer2);
            var actual1 = observer1.ObserveOriginalAsync(new("strata", "model"), scope1, default);
            var actual2 = observer2.ObserveOriginalAsync(new("strata", "model"), scope2, default);
            Assert.Same(fault, await Assert.ThrowsAsync<IOException>(() => actual1));
            Assert.Same(fault, await Assert.ThrowsAsync<IOException>(() => actual2));
            Assert.Same(first, scope1.ObservedAdmission); Assert.Same(second, scope2.ObservedAdmission);
            Assert.Empty(scope1.Raw); Assert.Empty(scope2.Raw); Assert.True(actual1.IsFaulted); Assert.True(actual2.IsFaulted);
            Assert.Throws<ArgumentNullException>(() => descriptor.BindOriginalRequest(null!));
        }
        catch (Exception error) { primary = error; throw; }
        finally { await CloseActualAsync(artifacts, primary); }
    }
    private static TaskRunAttemptAdmission Admission() => new(
        null!, Guid.NewGuid(), null!);
    private static async Task CloseActualAsync(StrataNativeArtifactSource source, Exception? primary)
    {
        Task? actual = null;
        try { actual = source.DisposeAsync().AsTask(); await actual.ConfigureAwait(false); }
        catch (Exception error)
        {
            var causes = new List<Exception>(); if (primary is not null) causes.Add(primary); causes.Add(error);
            if (actual?.Exception is { } group) { causes.Add(group); causes.AddRange(group.InnerExceptions); }
            throw new AggregateException("Actual observer fixture body and artifact source close failed.", causes);
        }
    }
    private sealed class Scope(Exception? after = null) : IInferenceEngineOriginalSourceScope
    {
        public readonly List<Task> Raw = [];
        public TaskRunAttemptAdmission? ObservedAdmission;
        public T InvokeOriginalFactory<T>(Func<T> callback)
        {
            var result = callback(); if (result is TaskRunAttemptAdmission admission) ObservedAdmission = admission;
            if (after is not null) throw after; return result;
        }
        public T InvokeOriginalCleanup<T>(Func<T> callback) => callback();
        public void RetainOriginalTask(Task actual) => Raw.Add(actual);
    }
}
