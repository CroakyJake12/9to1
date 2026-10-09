using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

// Actual Application owners with explicitly controlled model discovery. No installed
// account, native provider, process final marker, or application shutdown acceptance.
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Canonical_process_cohort_constructor_requires_the_same_configured_authority_frame_and_agent_chat()
    {
        await using var rig = new Rig();
        await using var fixture = new CanonicalProcessFixture(rig);
        Assert.True(fixture.Owner.HasOriginalComposition(fixture.Tasks, fixture.Agents, rig.Authority, fixture.Frames));
        Assert.False(fixture.Owner.HasOriginalComposition(rig.Tasks, fixture.Agents, rig.Authority, fixture.Frames));
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new TaskRunCanonicalProcessRetirementOwner(rig.Tasks, fixture.Agents, rig.Authority, fixture.Frames);
        });
        var (foreignAgents, _) = CreateAgentCaller(rig, new AgentRows());
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new TaskRunCanonicalProcessRetirementOwner(fixture.Tasks, foreignAgents, rig.Authority, fixture.Frames);
        });
        Assert.False(rig.Authority.IsOriginalAdmissionSealed);
    }

    [Fact]
    public async Task Canonical_process_request_seals_both_real_business_admissions_before_external_close()
    {
        await using var rig = new Rig();
        await using var fixture = new CanonicalProcessFixture(rig);
        fixture.Owner.DemandExternalOriginalProcessJoin();
        Assert.False(rig.Authority.IsOriginalAdmissionSealed);
        fixture.Owner.RequestOriginalProcessRetirement();
        Assert.True(rig.Authority.IsOriginalAdmissionSealed);
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = fixture.Agents.RunAsync(fixture.Definition.Id, "No new discovery after process seal", TestContext.Current.CancellationToken);
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = fixture.Chat.SendAsync(rig.Conversation, "No new canonical Chat after process seal",
                rig.Provider.Model.Model, EffortLevel.Medium, [], "controlled", "", DuoMode.Solo,
                null, null, null, null, TestContext.Current.CancellationToken,
                taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask);
        });
        var actual = fixture.Owner.CloseAndSuspendOriginalProducersAsync();
        Assert.Same(actual, fixture.Owner.CloseAndSuspendOriginalProducersAsync());
        await actual;
        Assert.True(actual.IsCompletedSuccessfully);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Provider.Starts);
    }

    [Fact]
    public async Task Canonical_process_close_retains_held_precontext_agent_driver_and_all_raw_fault_siblings()
    {
        await using var rig = new Rig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new CanonicalProcessFixture(rig, new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return source.Task; }));
        var actualRun = fixture.Agents.RunAsync(fixture.Definition.Id, "Controlled discovery only", TestContext.Current.CancellationToken);
        Task? close = null;
        var first = new OperationCanceledException("Faulted actual model source, not owner cancellation");
        var second = new IOException("The same raw model task's independent sibling");
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            fixture.Owner.RequestOriginalProcessRetirement();
            close = fixture.Owner.CloseAndSuspendOriginalProducersAsync();
            Assert.False(close.IsCompleted);
            Assert.False(actualRun.IsCompleted);
            Assert.False(source.Task.IsCompleted);
            source.SetException([first, second]);
            await Assert.ThrowsAnyAsync<Exception>(() => actualRun);
            var fault = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.True(source.Task.IsFaulted);
            Assert.True(actualRun.IsFaulted);
            Assert.True(close.IsFaulted);
            Assert.Same(first, source.Task.Exception!.InnerExceptions[0]);
            Assert.Same(second, source.Task.Exception.InnerExceptions[1]);
            Assert.Contains(first, fault.Flatten().InnerExceptions);
            Assert.Contains(second, fault.Flatten().InnerExceptions);
            Assert.Same(close, fixture.Owner.CloseAndSuspendOriginalProducersAsync());
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.Equal(0, rig.Provider.Starts);
        }
        finally
        {
            source.TrySetException([first, second]);
            try { await actualRun; } catch (Exception) { }
            if (close is not null) try { await close; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Canonical_process_whole_join_refuses_restored_context_inside_the_actual_agent_source_factory()
    {
        await using var rig = new Rig();
        var restored = ExecutionContext.Capture()!;
        Exception? refusal = null;
        TaskRunCanonicalProcessRetirementOwner? owner = null;
        var bodyFault = new IOException("Controlled discovery terminal");
        await using var fixture = new CanonicalProcessFixture(rig, new ControlledAgentDiscovery(() =>
        {
            ExecutionContext.Run(restored, _ =>
            {
                try { _ = owner!.CloseAndSuspendOriginalProducersAsync(); }
                catch (Exception cause) { refusal = cause; }
            }, null);
            return Task.FromException<IReadOnlyList<ModelDescriptor>>(bodyFault);
        }));
        owner = fixture.Owner;
        var actual = fixture.Agents.RunAsync(fixture.Definition.Id, "No provider body", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.IsType<InvalidOperationException>(refusal);
        Assert.False(rig.Authority.IsOriginalAdmissionSealed); // Pure preflight preceded any stop.
        var close = owner.CloseAndSuspendOriginalProducersAsync();
        var fault = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(bodyFault, fault.Flatten().InnerExceptions);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Provider.Starts);
    }

    private sealed class CanonicalProcessFixture : IAsyncDisposable
    {
        internal TaskExecutionCoordinator Tasks { get; }
        internal TaskRunOriginalFrameOwner Frames { get; }
        internal ChatSessionService Chat { get; }
        internal AgentTaskRuntimeService Agents { get; }
        internal AgentDefinition Definition { get; }
        internal TaskRunCanonicalProcessRetirementOwner Owner { get; }

        internal CanonicalProcessFixture(Rig rig, IOllamaClient? actualDiscovery = null)
        {
            TaskExecutionCoordinator? tasks = null;
            Frames = new((task, run, attempt, token) => tasks!.TryGetIssuedAttemptAsync(task, run, attempt, token));
            Tasks = tasks = new(rig.TaskRows, rig.Events, admissionAuthority: rig.Authority, runtimeSettlement: Frames);
            Chat = new(rig.Conversations, rig.Client, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
                taskCoordinator: Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture);
            Definition = new(Guid.NewGuid(), "Controlled process Agent", "No provider effects", "Observe discovery",
                "agent", rig.Provider.Model.Key, null, "[]", "{}", false, true, DateTimeOffset.UnixEpoch);
            Agents = new(new AgentCatalog(Definition), new AgentRows(),
                actualDiscovery ?? new AgentModelDiscovery(rig.Provider.Model.Model),
                new CapabilityRegistryService(new EmptyAgentCapabilities()), Chat, rig.Policy);
            Owner = new(Tasks, Agents, rig.Authority, Frames);
        }

        // Frame cleanup is attempted independently even when a test's actual business source faults.
        public ValueTask DisposeAsync() => new(Frames.CloseAndDrainAsync());
    }
}
