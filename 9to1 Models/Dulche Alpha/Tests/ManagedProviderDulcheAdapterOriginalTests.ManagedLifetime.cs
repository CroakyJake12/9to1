using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>The real production lifetime service/runtime/adapter/coordinator/frame owner with the
/// existing explicitly synthetic raw provider/repository/authority. No network/Windows readiness.</summary>
public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
    [Fact]
    public Task Service_publishes_factory_and_startup_before_callbacks_and_coalesces_original_start()
        => ServiceControl(async (h, service, configurations, registry) =>
        {
            var held = new TaskCompletionSource<ProviderConfiguration?>(TaskCreationOptions.RunContinuationsAsynchronously);
            configurations.Read = (_, _) => held.Task;
            configurations.BeforeReturn = () =>
            {
                Assert.NotNull(OwnedTask(service, "_originalInitialization"));
                Assert.NotNull(OwnedTask(service, "_originalFactory"));
                Assert.False(OwnedTask(service, "_originalFactory")!.IsCompleted);
            };
            h.Provider.HealthFactory = _ =>
            {
                var startup = Assert.IsType<DulcheEndpointStartupOriginal>(Owned(service, "_startup"));
                Assert.True(startup.WasAcquired);
                Assert.False(startup.OriginalStartup.IsCompleted);
                Assert.NotNull(OwnedTask(service, "_originalInitialization"));
                return Task.FromResult(h.Provider.Healthy());
            };
            var actual = service.StartConfiguredProviderAsync(h.Provider.Id);
            try
            {
                await configurations.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Same(actual, service.StartConfiguredProviderAsync(h.Provider.Id));
                Assert.Same(held.Task, configurations.OriginalRead);
                using var caller = new CancellationTokenSource();
                var withdrawnWait = service.StartConfiguredProviderAsync(h.Provider.Id, caller.Token);
                caller.Cancel();
                Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => withdrawnWait));
                Assert.False(actual.IsCompleted);
                Assert.Equal(0, registry.Disposes);
                held.TrySetResult(configurations.Configuration);
                Assert.True((await actual).Succeeded);
                Assert.Equal(1, configurations.Reads);
                Assert.Equal(0, h.Provider.CompleteCalls);
            }
            finally { held.TrySetResult(configurations.Configuration); await actual; }
        });

    [Fact]
    public Task Service_uses_exact_issued_action_and_real_request_handle_and_refuses_copied_binding()
        => ServiceControl(async (h, service, _, _) =>
        {
            var started = await service.StartConfiguredProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var original = started.Value!;
            var request = h.Request() with { SessionId = null };
            var admitted = await service.SubmitWithContextAsync(original, request, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var handle = admitted.Value!;
            Assert.Equal(RequestState.Completed, (await handle.AwaitResult(default)).Status);
            Assert.Equal(1, h.Provider.CompleteCalls);
            Assert.True(service.TryObserveOriginalRequestWork(original, handle, out var processing, out _, out _));
            Assert.NotNull(processing); await processing!;
            Assert.Same(h.Admission, await h.Coordinator.GetIssuedAttemptAsync(h.Snapshot.TaskId,
                h.Snapshot.ExecutionId, h.Admission.AttemptId, default));
            var clone = Assert.IsType<ManagedDulcheProviderBinding>(typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(original, null));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = service.SubmitWithContextAsync(clone,
                request, h.Admission, Guid.NewGuid()); });
            Assert.Equal(1, h.Provider.CompleteCalls);
        });

    [Fact]
    public Task Held_startup_keeps_same_service_close_pending_and_borrowed_configuration_and_registry_open()
        => ServiceControl(async (h, service, configurations, registry) =>
        {
            var health = new TaskCompletionSource<ProviderHealthStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = Signal(); h.Provider.HealthFactory = _ => { entered.TrySetResult(); return health.Task; };
            var start = service.StartConfiguredProviderAsync(h.Provider.Id); Task? close = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                close = service.CloseAndDrainAsync();
                Assert.Same(close, service.CloseAndDrainAsync());
                Assert.False(close.IsCompleted); Assert.False(start.IsCompleted);
                Assert.Equal(0, registry.Disposes); Assert.Equal(0, configurations.Disposes);
                health.TrySetResult(h.Provider.Healthy());
                var startError = await Record.ExceptionAsync(() => start);
                Assert.NotNull(startError); h.Expect(startError!);
                var closeError = await Record.ExceptionAsync(() => close);
                Assert.NotNull(closeError);
                Assert.Contains(Leaves(closeError!), e => Leaves(startError!).Any(original => ReferenceEquals(original, e)));
                h.Expect(closeError!);
                Assert.True(OwnedTask(service, "_originalAdapterClose")!.IsCompleted);
            }
            finally
            {
                health.TrySetResult(h.Provider.Healthy());
                var startError = await Record.ExceptionAsync(() => start); if (startError is not null) h.Expect(startError);
                close ??= service.CloseAndDrainAsync();
                var error = await Record.ExceptionAsync(() => close); if (error is not null) h.Expect(error);
            }
        });

    [Fact]
    public Task Factory_direct_compound_causes_survive_actual_start_and_same_service_close()
        => ServiceControl(async (h, service, configurations, _) =>
        {
            var first = new IOException("first original configuration failure");
            var second = new InvalidOperationException("second original configuration failure");
            var actual = new TaskCompletionSource<ProviderConfiguration?>(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.SetException([first, second]); configurations.Read = (_, _) => actual.Task;
            var start = service.StartConfiguredProviderAsync(h.Provider.Id);
            var error = await Record.ExceptionAsync(() => start);
            Assert.NotNull(error); Assert.Same(actual.Task, configurations.OriginalRead);
            Assert.Contains(Leaves(error!), e => ReferenceEquals(first, e));
            Assert.Contains(Leaves(error!), e => ReferenceEquals(second, e));
            h.Expect(first); h.Expect(second);
            var close = service.CloseAndDrainAsync();
            Assert.Same(close, service.CloseAndDrainAsync());
            var closeError = await Record.ExceptionAsync(() => close);
            Assert.Contains(Leaves(closeError!), e => ReferenceEquals(first, e));
            Assert.Contains(Leaves(closeError!), e => ReferenceEquals(second, e));
            Assert.Equal(0, h.Provider.CompleteCalls);
        });

    [Fact]
    public Task Null_execution_context_health_cancel_callback_cannot_join_its_encompassing_service_close()
        => ServiceControl(async (h, service, _, _) =>
        {
            var health = new TaskCompletionSource<ProviderHealthStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = Signal(); Exception? refusal = null; CancellationTokenRegistration registration = default;
            h.Provider.HealthFactory = token =>
            {
                using (ExecutionContext.SuppressFlow())
                    registration = token.Register(() =>
                    {
                        try { service.CloseAndDrainAsync().GetAwaiter().GetResult(); }
                        catch (Exception error) { refusal = error; }
                        finally { health.TrySetCanceled(token); }
                    });
                entered.TrySetResult(); return health.Task;
            };
            var start = service.StartConfiguredProviderAsync(h.Provider.Id); Task? close = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                close = service.CloseAndDrainAsync();
                var error = await Record.ExceptionAsync(() => close);
                Assert.IsType<InvalidOperationException>(refusal);
                Assert.Contains("own close", refusal!.Message);
                Assert.True(health.Task.IsCanceled); Assert.True(close.IsCompleted);
                Assert.NotNull(error); h.Expect(error!);
                var startError = await Record.ExceptionAsync(() => start); Assert.NotNull(startError); h.Expect(startError!);
            }
            finally
            {
                health.TrySetCanceled(); close ??= service.CloseAndDrainAsync();
                var error = await Record.ExceptionAsync(() => close); if (error is not null) h.Expect(error);
                var startError = await Record.ExceptionAsync(() => start); if (startError is not null) h.Expect(startError);
                registration.Dispose();
            }
        });

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task Missing_actual_tool_or_remote_context_port_refuses_before_any_raw_generation(bool remote, bool tools)
        => ServiceControl(async (h, service, _, _) =>
        {
            var startup = await service.StartConfiguredProviderAsync(h.Provider.Id); Assert.True(startup.Succeeded);
            var action = await h.RegisterActionAsync();
            if (tools)
            {
                var error = await Record.ExceptionAsync(() => service.SubmitWithContextAsync(startup.Value!,
                    h.Request(tools: true) with { SessionId = null }, h.Admission, action));
                Assert.NotNull(error); Assert.Contains(Leaves(error!), e => e is UnauthorizedAccessException);
                h.Expect(error!);
            }
            else
            {
                var submitted = await service.SubmitWithContextAsync(startup.Value!, h.Request() with { SessionId = null }, h.Admission, action);
                Assert.True(submitted.Succeeded);
                Assert.Equal(RequestState.Failed, (await submitted.Value!.AwaitResult(default)).Status);
                var unavailable = await Record.ExceptionAsync(() => service.CloseAndDrainAsync());
                Assert.NotNull(unavailable); Assert.Contains(Leaves(unavailable!), e => e is UnauthorizedAccessException);
                h.Expect(unavailable!);
            }
            Assert.Equal(0, h.Provider.CompleteCalls); Assert.Equal(0, h.Provider.StreamCalls); Assert.Equal(0, h.Provider.ToolCalls);
        }, remote, tools ? [ToolCapability.Text, ToolCapability.Tools] : [ToolCapability.Text]);

    [Fact]
    public Task Unhealthy_real_provider_observation_returns_unavailable_without_issuing_binding_or_loading_model()
        => ServiceControl(async (h, service, _, _) =>
        {
            h.Provider.HealthFactory = _ => Task.FromResult(h.Provider.Healthy() with { IsHealthy = false, Message = "synthetic actual unhealthy" });
            var result = await service.StartConfiguredProviderAsync(h.Provider.Id);
            Assert.False(result.Succeeded); Assert.Null(result.Value); Assert.NotNull(result.Error);
            Assert.Equal(DulcheErrorCode.ProviderUnavailable, result.Error!.Code);
            Assert.Null(Owned(service, "_binding"));
            Assert.Equal(0, h.Provider.CompleteCalls); Assert.Equal(0, h.Provider.CatalogueCalls);
        });

    [Fact]
    public Task Actual_request_faults_remain_in_owned_runtime_and_adapter_close_after_response_completion()
        => ServiceControl(async (h, service, _, _) =>
        {
            var first = new IOException("first actual generation error"); var second = new ApplicationException("second actual generation error");
            var raw = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            raw.SetException([first, second]); h.Provider.CompleteFactory = (_, _) => raw.Task;
            var start = await service.StartConfiguredProviderAsync(h.Provider.Id); Assert.True(start.Succeeded);
            var admitted = await service.SubmitWithContextAsync(start.Value!, h.Request() with { SessionId = null },
                h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var response = await admitted.Value!.AwaitResult(default);
            Assert.Equal(RequestState.Failed, response.Status);
            Assert.Same(raw.Task, h.Provider.OriginalComplete);
            h.Expect(first); h.Expect(second);
            var close = service.CloseAndDrainAsync(); var failure = await Record.ExceptionAsync(() => close);
            Assert.Contains(Leaves(failure!), e => ReferenceEquals(first, e));
            Assert.Contains(Leaves(failure!), e => ReferenceEquals(second, e));
            Assert.True(OwnedTask(service, "_originalRuntimeStop")!.IsCompleted);
            Assert.True(OwnedTask(service, "_originalAdapterClose")!.IsCompleted);
        });

    [Fact]
    public Task Restored_pre_service_execution_context_configuration_callback_cannot_join_service_factory()
        => ServiceControl(async (h, service, configurations, _) =>
        {
            var originalContext = ExecutionContext.Capture() ?? throw new InvalidOperationException("Expected actual fixture context.");
            Exception? refusal = null;
            configurations.BeforeReturn = () => ExecutionContext.Run(originalContext, _ =>
            {
                try { service.CloseAndDrainAsync().GetAwaiter().GetResult(); }
                catch (Exception error) { refusal = error; }
            }, null);
            var started = await service.StartConfiguredProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded);
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains("own close", refusal!.Message);
            Assert.Null(OwnedTask(service, "_close"));
            Assert.Equal(0, h.Provider.CompleteCalls);
        });

    [Fact]
    public Task Original_endpoint_retirement_releases_unstarted_receipt_and_joins_actual_registration_cleanup()
        => Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]); var healthCalls = 0;
            h.Provider.HealthFactory = _ => { Interlocked.Increment(ref healthCalls); return Task.FromResult(h.Provider.Healthy()); };
            var original = runtime.PrepareManagedProviderOriginal(h.Provider.Id);
            Assert.True(original.WasAcquired); Assert.False(original.OriginalStartup.IsCompleted);
            // Deliberately do not call StartOriginal: retirement must withdraw the exact gated original.
            var stop = runtime.JoinEndpointStopAsync(original.OriginalAcquisition.EndpointId);
            try
            {
                var stoppedError = await Record.ExceptionAsync(() => stop.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.True(stop.IsCompleted); Assert.True(original.OriginalStartup.IsCompleted);
                var startupError = await Record.ExceptionAsync(() => original.OriginalStartup);
                Assert.NotNull(startupError); Assert.Contains(Leaves(startupError!), e => e is ObjectDisposedException);
                if (stoppedError is not null) h.Expect(stoppedError);
                h.Expect(startupError!);
                Assert.Equal(0, healthCalls);
                Assert.True(Assert.IsAssignableFrom<Task>(typeof(DulcheEndpointStartupOriginal).GetField("OriginalRetirementRelease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)).IsCompleted);
                Assert.True(Assert.IsAssignableFrom<Task>(typeof(DulcheEndpointStartupOriginal).GetField("OriginalCallerRelease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)).IsCompleted);
            }
            finally
            {
                original.StartOriginal();
                var startupError = await Record.ExceptionAsync(() => original.OriginalStartup); if (startupError is not null) h.Expect(startupError);
                var stopError = await Record.ExceptionAsync(() => stop); if (stopError is not null) h.Expect(stopError);
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Service_preserves_faulted_configuration_and_synchronous_registry_OCE_as_original_faults(bool registryFault)
        => ServiceControl(async (h, service, configurations, registry) =>
        {
            var original = new OperationCanceledException("actual synthetic callback fault");
            var configurationTask = new TaskCompletionSource<ProviderConfiguration?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (registryFault) registry.BeforeGet = () => throw original;
            else
            {
                configurationTask.SetException(original);
                configurations.Read = (_, _) => configurationTask.Task;
            }
            var started = service.StartConfiguredProviderAsync(h.Provider.Id);
            var startError = await Record.ExceptionAsync(() => started);
            Assert.NotNull(startError); Assert.True(started.IsFaulted); Assert.False(started.IsCanceled);
            Assert.Contains(Leaves(startError!), e => ReferenceEquals(e, original)); h.Expect(startError!);
            Assert.True(OwnedTask(service, "_originalFactory")!.IsFaulted);
            if (!registryFault)
            {
                Assert.Same(configurationTask.Task, configurations.OriginalRead);
                Assert.True(configurationTask.Task.IsFaulted);
                Assert.Same(original, Assert.Single(configurationTask.Task.Exception!.InnerExceptions));
            }
            var close = service.CloseAndDrainAsync(); var closeError = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeError); Assert.True(close.IsFaulted);
            Assert.Contains(Leaves(closeError!), e => ReferenceEquals(e, original)); h.Expect(closeError!);
            Assert.Null(Owned(service, "_adapter")); Assert.Equal(0, h.Provider.CompleteCalls);
        });

    [Fact]
    public Task Service_keeps_actual_canceled_configuration_distinct_from_a_faulted_OCE()
        => ServiceControl(async (h, service, configurations, _) =>
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var actualConfiguration = Task.FromCanceled<ProviderConfiguration?>(canceled.Token);
            configurations.Read = (_, _) => actualConfiguration;
            var started = service.StartConfiguredProviderAsync(h.Provider.Id);
            var startError = await Record.ExceptionAsync(() => started);
            Assert.IsAssignableFrom<OperationCanceledException>(startError); Assert.True(started.IsCanceled);
            Assert.Same(actualConfiguration, configurations.OriginalRead); Assert.True(actualConfiguration.IsCanceled);
            Assert.Null(actualConfiguration.Exception); Assert.True(OwnedTask(service, "_originalFactory")!.IsCanceled);
            h.Expect(startError!);
            var close = service.CloseAndDrainAsync(); var closeError = await Record.ExceptionAsync(() => close);
            Assert.IsAssignableFrom<OperationCanceledException>(closeError); Assert.True(close.IsCompleted);
            h.Expect(closeError!); Assert.Null(Owned(service, "_adapter")); Assert.Equal(0, h.Provider.CompleteCalls);
        });

    [Fact]
    public Task Service_refuses_restored_context_close_join_from_actual_start_health_callback()
        => ServiceControl(async (h, service, _, _) =>
        {
            var outsideOriginal = ExecutionContext.Capture();
            Assert.NotNull(outsideOriginal);
            h.Provider.HealthFactory = _ =>
            {
                ExecutionContext.Run(outsideOriginal!, _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => { service.CloseAndDrainAsync().GetAwaiter().GetResult(); });
                    Assert.Null(OwnedTask(service, "_close"));
                }, null);
                return Task.FromResult(h.Provider.Healthy());
            };
            var started = service.StartConfiguredProviderAsync(h.Provider.Id);
            Assert.True((await started).Succeeded); Assert.Null(OwnedTask(service, "_close"));
            Assert.True(OwnedTask(service, "_originalFactory")!.IsCompletedSuccessfully);
        });

    private static Task ServiceControl(Func<Harness, ManagedDulcheRuntimeService, ObservedConfigurations, BorrowedRegistry, Task> body,
        bool remote = false, ToolCapability[]? required = null)
        => Control(async h =>
        {
            await h.Frames.RegisterOriginalAttemptAsync(h.Admission, default);
            var registry = new BorrowedRegistry(h.Provider); var configurations = new ObservedConfigurations(h.Provider);
            var service = new ManagedDulcheRuntimeService(registry, configurations, h.Coordinator, h.ObservedFrames);
            var errors = new List<Exception>(); Task? actualBody = null, actualClose = null;
            try { actualBody = body(h, service, configurations, registry); await actualBody; }
            catch (Exception error) { if (actualBody is null) errors.Add(error); else { errors.Add(error); if (actualBody.Exception is { } group) foreach (var cause in group.InnerExceptions) if (!errors.Contains(cause)) errors.Add(cause); } }
            finally
            {
                try { actualClose = service.CloseAndDrainAsync(); } catch (Exception error) { AddUnexpected(h, errors, error); }
                if (actualClose is not null) await JoinUnexpected(h, errors, actualClose);
                try { Assert.Equal(0, registry.Disposes); } catch (Exception error) { AddUnexpected(h, errors, error); }
                try { Assert.Equal(0, configurations.Disposes); } catch (Exception error) { AddUnexpected(h, errors, error); }
            }
            if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count != 0) throw new AggregateException(errors);
        }, remote, required);

    private static object? Owned(object owner, string field) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static Task? OwnedTask(object owner, string field) => Owned(owner, field) as Task;
    private sealed class ObservedConfigurations(SyntheticProvider provider) : IProviderConfigurationStore, IAsyncDisposable
    {
        public ProviderConfiguration Configuration { get; } = new(provider.Id, provider.Kind, provider.DisplayName,
            provider.IsLocal ? "http://127.0.0.1:9477" : "https://synthetic.invalid", true, provider.IsLocal, false,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        public int Reads, Disposes;
        public Func<string, CancellationToken, Task<ProviderConfiguration?>>? Read = null;
        public Action? BeforeReturn = null;
        public Task<ProviderConfiguration?>? OriginalRead;
        public TaskCompletionSource Entered { get; } = Signal();
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token)
        {
            Reads++; var actual = Read?.Invoke(id, token) ?? Task.FromResult<ProviderConfiguration?>(Configuration);
            OriginalRead = actual; BeforeReturn?.Invoke(); Entered.TrySetResult(); return actual;
        }
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }
    private sealed class BorrowedRegistry(SyntheticProvider provider) : IModelProviderRegistry, IAsyncDisposable
    {
        public int Disposes;
        public Action? BeforeGet = null;
        public IReadOnlyList<IModelProvider> Providers => [provider];
        public IModelProvider? Find(string id) => id == provider.Id ? provider : null;
        public IModelProvider GetRequired(string id) { BeforeGet?.Invoke(); return Find(id) ?? throw new InvalidOperationException(); }
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => provider.GetModelsAsync(token);
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }
}
