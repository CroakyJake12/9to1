using System.Reflection;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

// Full genuine Application cohort and maintained caller fixtures are required
// linked inputs. This proves managed process custody, not installed native exit.
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Desktop_process_adapter_returns_the_same_canonical_close_and_retains_raw_business_faults()
    {
        await using var rig = new Rig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new CanonicalProcessFixture(rig, new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return raw.Task; }));
        var adapter = new CanonicalProcessOriginalRetirementAdapter(fixture.Owner);
        var actualRun = fixture.Agents.RunAsync(fixture.Definition.Id, "No provider entry", TestContext.Current.CancellationToken);
        Task? close = null;
        var first = new OperationCanceledException("Faulted original discovery, not actual cancellation");
        var second = new IOException("Independent raw discovery sibling");
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            adapter.DemandExternalOriginalRetirementJoin();
            Assert.False(rig.Authority.IsOriginalAdmissionSealed);
            adapter.RequestRetirement();
            Assert.True(rig.Authority.IsOriginalAdmissionSealed);
            close = adapter.CloseAndDrainAsync();
            Assert.Same(close, fixture.Owner.CloseAndSuspendOriginalProducersAsync());
            Assert.Same(close, adapter.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(actualRun.IsCompleted);
            raw.SetException([first, second]);
            await Assert.ThrowsAnyAsync<Exception>(() => actualRun);
            var fault = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.True(raw.Task.IsFaulted);
            Assert.True(actualRun.IsFaulted);
            Assert.True(close.IsFaulted);
            Assert.Same(first, raw.Task.Exception!.InnerExceptions[0]);
            Assert.Same(second, raw.Task.Exception.InnerExceptions[1]);
            Assert.Contains(first, fault.Flatten().InnerExceptions);
            Assert.Contains(second, fault.Flatten().InnerExceptions);
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.Equal(0, rig.Provider.Starts);
        }
        finally
        {
            raw.TrySetException([first, second]);
            try { await actualRun; } catch (Exception) { }
            if (close is not null) try { await close; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task App_retains_returned_process_owner_before_later_resolution_failure_and_joins_its_held_original()
    {
        await using var rig = new Rig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new CanonicalProcessFixture(rig, new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return raw.Task; }));
        var app = new App();
        var acquisitionFault = new IOException("Later configured singleton resolution failed");
        var actualRun = fixture.Agents.RunAsync(fixture.Definition.Id, "Held real process source", TestContext.Current.CancellationToken);
        Task? appClose = null;
        var first = new OperationCanceledException("Raw source was faulted, not canceled");
        var second = new IOException("Same raw source's second direct failure");
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var provider = new CanonicalAppProvider(type => type == typeof(TaskRunCanonicalProcessRetirementOwner)
                ? fixture.Owner : throw acquisitionFault);
            Assert.Same(acquisitionFault, Assert.Throws<IOException>(() => CaptureCanonicalAppOwner(app, provider)));
            Assert.IsType<CanonicalProcessOriginalRetirementAdapter>(GetCanonicalAppField(app, "_actualCanonicalProcessRetirement"));
            Assert.False(rig.Authority.IsOriginalAdmissionSealed);
            appClose = CloseCanonicalApp(app);
            Assert.Same(appClose, CloseCanonicalApp(app));
            Assert.True(rig.Authority.IsOriginalAdmissionSealed);
            Assert.False(appClose.IsCompleted);
            Assert.False(actualRun.IsCompleted);
            Assert.False(raw.Task.IsCompleted);
            var actualCleanup = Assert.IsAssignableFrom<Task>(GetCanonicalAppField(app, "_actualUntransferredCanonicalProcessDrain"));
            Assert.False(actualCleanup.IsCompleted);
            raw.SetException([first, second]);
            await Assert.ThrowsAnyAsync<Exception>(() => actualRun);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => appClose);
            Assert.True(raw.Task.IsFaulted);
            Assert.True(actualCleanup.IsFaulted);
            Assert.True(appClose.IsFaulted);
            Assert.Contains(acquisitionFault, failure.Flatten().InnerExceptions);
            Assert.Contains(first, failure.Flatten().InnerExceptions);
            Assert.Contains(second, failure.Flatten().InnerExceptions);
            var enrolled = Assert.IsAssignableFrom<IEnumerable<Task>>(GetCanonicalAppField(app, "_actualCanonicalProcessCloses"));
            Assert.Same(fixture.Owner.CloseAndSuspendOriginalProducersAsync(), Assert.Single(enrolled));
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.Equal(0, rig.Provider.Starts);
        }
        finally
        {
            raw.TrySetException([first, second]);
            try { await actualRun; } catch (Exception) { }
            try { await (appClose ?? CloseCanonicalApp(app)); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task App_actual_configured_factory_refuses_its_own_close_with_restored_pre_app_context()
    {
        await using var rig = new Rig();
        await using var fixture = new CanonicalProcessFixture(rig);
        var app = new App();
        var restored = ExecutionContext.Capture()!;
        Exception? refusal = null;
        var factories = 0;
        var provider = new CanonicalAppProvider(type =>
        {
            if (type == typeof(TaskRunCanonicalProcessRetirementOwner))
            {
                factories++;
                ExecutionContext.Run(restored, state =>
                {
                    try { CloseCanonicalApp(app).GetAwaiter().GetResult(); }
                    catch (Exception cause) { refusal = cause; }
                }, null);
                return fixture.Owner;
            }
            return ResolveCanonicalAppFixture(type, rig, fixture);
        });
        try
        {
            CaptureCanonicalAppOwner(app, provider);
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.False(rig.Authority.IsOriginalAdmissionSealed);
            Assert.Equal(1, factories);
            Assert.Null(GetCanonicalAppField(app, "_actualUntransferredCanonicalProcessDrain"));
            var close = CloseCanonicalApp(app);
            Assert.Same(close, CloseCanonicalApp(app));
            await close;
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(rig.Authority.IsOriginalAdmissionSealed);
            Assert.True(fixture.Owner.CloseAndSuspendOriginalProducersAsync().IsCompletedSuccessfully);
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.Equal(0, rig.Provider.Starts);
        }
        finally { try { await CloseCanonicalApp(app); } catch (Exception) { } }
    }

    [Fact]
    public async Task App_mixed_configured_alias_is_refused_after_actual_owner_capture_without_losing_its_process_drain()
    {
        await using var rig = new Rig();
        await using var fixture = new CanonicalProcessFixture(rig);
        var app = new App();
        var provider = new CanonicalAppProvider(type => type == typeof(TaskExecutionCoordinator)
            ? rig.Tasks : ResolveCanonicalAppFixture(type, rig, fixture));
        try
        {
            var mismatch = Assert.Throws<InvalidOperationException>(() => CaptureCanonicalAppOwner(app, provider));
            Assert.Contains("SAME configured", mismatch.Message);
            Assert.IsType<CanonicalProcessOriginalRetirementAdapter>(GetCanonicalAppField(app, "_actualCanonicalProcessRetirement"));
            Assert.False(rig.Authority.IsOriginalAdmissionSealed);
            var close = CloseCanonicalApp(app);
            var fault = await Record.ExceptionAsync(() => close);
            Assert.NotNull(fault);
            Assert.Contains(mismatch, AppCanonicalDirectCauses(fault!));
            Assert.True(close.IsFaulted);
            Assert.True(rig.Authority.IsOriginalAdmissionSealed);
            Assert.True(fixture.Owner.CloseAndSuspendOriginalProducersAsync().IsCompletedSuccessfully);
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.Equal(0, rig.Provider.Starts);
        }
        finally { try { await CloseCanonicalApp(app); } catch (Exception) { } }
    }

    private static object ResolveCanonicalAppFixture(Type type, Rig rig, CanonicalProcessFixture fixture) =>
        type == typeof(TaskRunCanonicalProcessRetirementOwner) ? fixture.Owner :
        type == typeof(TaskExecutionCoordinator) ? fixture.Tasks :
        type == typeof(AgentTaskRuntimeService) ? fixture.Agents :
        type == typeof(TaskRunPermissionAuthority) ? rig.Authority :
        type == typeof(TaskRunOriginalFrameOwner) ? fixture.Frames :
        throw new InvalidOperationException("No actual configured fixture singleton exists for " + type.FullName);

    private sealed class CanonicalAppProvider(Func<Type, object?> source) : IServiceProvider
    { public object? GetService(Type serviceType) => source(serviceType); }

    private static object? InvokeCanonicalApp(App app, string method, params object?[] arguments)
    {
        try { return typeof(App).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, arguments); }
        catch (TargetInvocationException wrapper) when (wrapper.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
    private static void CaptureCanonicalAppOwner(App app, IServiceProvider provider) =>
        InvokeCanonicalApp(app, "CaptureOriginalCanonicalProcessOwner", provider);
    private static Task CloseCanonicalApp(App app) => (Task)InvokeCanonicalApp(app, "JoinOriginalAppProducersAsync")!;
    private static object? GetCanonicalAppField(App app, string field) =>
        typeof(App).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app);
    private static IEnumerable<Exception> AppCanonicalDirectCauses(Exception actual) =>
        actual is AggregateException group ? group.Flatten().InnerExceptions : [actual];
}
