using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Actual adapter original ledger and guard over an explicitly synthetic model-observation
/// source. These controls issue no native worker, artifact receipt, model-use lease or permission.</summary>
public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
    [Fact]
    public async Task Generic_raw_provider_keeps_unsupported_initialized_model_observation()
    {
        await Control(async h =>
        {
            var model = new ModelIdentity(h.Provider.Id, "model");
            var result = await h.Adapter.LoadModelAsync(h.Endpoint with { Model = model }, model, default);
            Assert.False(result.Succeeded);
            Assert.Equal(DulcheErrorCode.UnsupportedCapability, result.Error!.Code);
            Assert.Equal(0, h.Provider.CatalogueCalls);
            Assert.Equal(0, h.Provider.CompleteCalls);
        });
    }

    [Fact]
    public async Task Initialized_model_source_refuses_mismatched_model_before_its_callback()
    {
        await Control(h => WithInitializedModelAdapter(h, async (source, adapter, endpoint) =>
        {
            var different = new ModelIdentity(source.Id, "different-model");
            var result = await adapter.LoadModelAsync(endpoint, different, default);
            Assert.False(result.Succeeded);
            Assert.Equal(DulcheErrorCode.InvalidArgument, result.Error!.Code);
            Assert.Equal(0, source.Observations);
            Assert.Null(source.OriginalObservation);
        }));
    }

    [Fact]
    public async Task Held_initialized_model_observation_has_physical_guard_and_is_joined_by_close()
    {
        await Control(h => WithInitializedModelAdapter(h, async (source, adapter, endpoint) =>
        {
            var preInvocation = ExecutionContext.Capture()!;
            var entered = Signal();
            var actual = new TaskCompletionSource<OperationResult<Unit>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errors = new List<Exception>(); Task<OperationResult<Unit>>? load = null; Task? close = null;
            source.Factory = (model, token) =>
            {
                Assert.Equal(endpoint.Model, model);
                Assert.Equal(default, token);
                ExecutionContext.Run(preInvocation, ignoredState =>
                {
                    Assert.Throws<InvalidOperationException>(adapter.RequireIndependentOriginalProviderJoin);
                    Assert.Throws<InvalidOperationException>(() => { _ = adapter.DisposeAsync(); });
                }, null);
                return actual.Task;
            };
            source.ObservationCaptured = () => entered.SetResult();
            try
            {
                load = adapter.LoadModelAsync(endpoint, endpoint.Model!, default).AsTask();
                await entered.Task;
                Assert.Same(actual.Task, source.OriginalObservation);
                Assert.False(load.IsCompleted);
                close = adapter.DisposeAsync().AsTask();
                Assert.False(close.IsCompleted);
                actual.SetResult(OperationResult<Unit>.Success(Unit.Value));
                Assert.True((await load).Succeeded);
                await close;
                Assert.True(actual.Task.IsCompletedSuccessfully);
                Assert.True(load.IsCompletedSuccessfully);
                Assert.True(close.IsCompletedSuccessfully);
                Assert.Equal(1, source.Observations);
            }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                actual.TrySetResult(OperationResult<Unit>.Success(Unit.Value));
                if (load is not null) await JoinUnexpected(h, errors, load);
                if (close is not null) await JoinUnexpected(h, errors, close);
            }
            ThrowInitializedModelControlErrors(errors);
        }));
    }

    [Fact]
    public async Task Synchronous_initialized_model_source_OCE_remains_a_fault_and_exact_close_cause()
    {
        await Control(h => WithInitializedModelAdapter(h, async (source, adapter, endpoint) =>
        {
            var original = new OperationCanceledException("synthetic synchronous model-observation callback fault");
            source.Factory = (_, _) => throw original;
            var load = adapter.LoadModelAsync(endpoint, endpoint.Model!, default).AsTask();
            var error = await Record.ExceptionAsync(() => load);
            Assert.IsType<AggregateException>(error);
            Assert.Contains(original, Leaves(error!));
            Assert.True(load.IsFaulted);
            Assert.False(load.IsCanceled);
            Assert.Null(source.OriginalObservation);
            h.Expect(error!);
            var close = adapter.DisposeAsync().AsTask();
            var closeError = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeError);
            Assert.Contains(original, Leaves(closeError!));
            Assert.True(close.IsFaulted);
            h.Expect(closeError!);
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Faulted_initialized_model_task_preserves_OCE_status_and_all_direct_siblings(bool withSibling)
    {
        await Control(h => WithInitializedModelAdapter(h, async (source, adapter, endpoint) =>
        {
            var one = new OperationCanceledException("synthetic faulted model-observation OCE");
            var two = new IOException("synthetic model-observation sibling");
            var actual = new TaskCompletionSource<OperationResult<Unit>>(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.SetException(withSibling ? new Exception[] { one, two } : new Exception[] { one });
            source.Factory = (_, _) => actual.Task;
            var load = adapter.LoadModelAsync(endpoint, endpoint.Model!, default).AsTask();
            var error = await Record.ExceptionAsync(() => load);
            Assert.IsType<AggregateException>(error);
            Assert.Same(actual.Task, source.OriginalObservation);
            Assert.True(actual.Task.IsFaulted);
            Assert.False(actual.Task.IsCanceled);
            Assert.Equal(withSibling ? 2 : 1, actual.Task.Exception!.InnerExceptions.Count);
            Assert.Contains(one, Leaves(error!));
            if (withSibling) Assert.Contains(two, Leaves(error!));
            Assert.True(load.IsFaulted);
            Assert.False(load.IsCanceled);
            h.Expect(error!);
            var close = adapter.DisposeAsync().AsTask();
            var closeError = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeError);
            Assert.Contains(one, Leaves(closeError!));
            if (withSibling) Assert.Contains(two, Leaves(closeError!));
            Assert.True(close.IsFaulted);
            h.Expect(closeError!);
        }));
    }

    [Fact]
    public async Task Genuinely_canceled_initialized_model_task_keeps_actual_canceled_status()
    {
        await Control(h => WithInitializedModelAdapter(h, async (source, adapter, endpoint) =>
        {
            using var actualCancellation = new CancellationTokenSource();
            actualCancellation.Cancel();
            var actual = Task.FromCanceled<OperationResult<Unit>>(actualCancellation.Token);
            source.Factory = (_, _) => actual;
            var load = adapter.LoadModelAsync(endpoint, endpoint.Model!, default).AsTask();
            var error = await Record.ExceptionAsync(() => load);
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Same(actual, source.OriginalObservation);
            Assert.True(actual.IsCanceled);
            Assert.False(actual.IsFaulted);
            Assert.True(load.IsCanceled);
            Assert.False(load.IsFaulted);
            h.ExpectedCanceledOriginals.Add(actual);
            h.ExpectedCanceledOriginals.Add(load);
            h.Expect(error!);
            var close = adapter.DisposeAsync().AsTask();
            var closeError = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeError);
            Assert.All(Leaves(closeError!), cause => Assert.IsAssignableFrom<OperationCanceledException>(cause));
            h.Expect(closeError!);
        }));
    }

    private static async Task WithInitializedModelAdapter(Harness h,
        Func<InitializedModelProvider, ManagedProviderDulcheAdapter, DulcheEndpoint, Task> body)
    {
        var source = new InitializedModelProvider(h.Provider);
        var adapter = await ManagedProviderDulcheAdapter.CreateAsync(source.Id, new SyntheticRegistry(source),
            new SyntheticConfigurations(h.Provider), h.Coordinator, h.ObservedFrames);
        var errors = new List<Exception>();
        try
        {
            var endpoint = h.Endpoint with { Model = new ModelIdentity(source.Id, "model") };
            Assert.True((await adapter.StartAsync(endpoint, default)).Succeeded);
            await body(source, adapter, endpoint);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            Task? close = null;
            try { close = adapter.DisposeAsync().AsTask(); } catch (Exception error) { AddUnexpected(h, errors, error); }
            if (close is not null) await JoinUnexpected(h, errors, close);
        }
        ThrowInitializedModelControlErrors(errors);
    }

    private static void ThrowInitializedModelControlErrors(List<Exception> errors)
    {
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }

    private sealed class InitializedModelProvider(SyntheticProvider actualRaw) : IModelProvider, IOriginalInferenceEngineModelSource
    {
        public int Observations;
        public Task<OperationResult<Unit>>? OriginalObservation;
        public Action? ObservationCaptured;
        public Func<ModelIdentity, CancellationToken, Task<OperationResult<Unit>>> Factory = (_, _) => Task.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public Task<OperationResult<Unit>> ObserveOriginalInitializedModelAsync(ModelIdentity model, CancellationToken token)
        { Observations++; var original = Factory(model, token); OriginalObservation = original; ObservationCaptured?.Invoke(); return original; }
        public string Id => actualRaw.Id;
        public string DisplayName => actualRaw.DisplayName;
        public ModelProviderKind Kind => actualRaw.Kind;
        public bool IsLocal => actualRaw.IsLocal;
        public bool CanManageModels => false;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => actualRaw.CheckHealthAsync(token);
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => actualRaw.GetModelsAsync(token);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => actualRaw.StreamChatAsync(request, token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => actualRaw.CompleteAsync(request, token);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => actualRaw.ChatWithToolsAsync(request, token);
    }
}
