using System.Reflection;
using Haven.Application;

namespace Haven.Infrastructure.Tests;

public sealed partial class ResilientProviderRoutingRecoveryTests
{
    [Fact]
    public async Task Provider_name_and_faulted_tool_task_cannot_issue_inspected_method_dispatch_absence()
    {
        var originalCause = Quota();
        var raw = Task.FromException<OllamaToolResponse>(originalCause);
        // This arbitrary implementation deliberately has a recognized provider's name.
        // A name, a successful failure CAS or a faulted Task cannot prove its method body.
        var provider = new Provider("openai", isLocal: true, tools: _ => raw);
        var fixture = await CanonicalFixture.CreateAsync([provider]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(provider) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var actual = router.ChatWithToolsAsync(request, CancellationToken.None);
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => actual);
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(
                router.TryGetOriginalFinalRequestFailure(request, outward));
            Assert.Null(router.TryGetOriginalToolResponseDispatchWitness(binding));
            var settlement = fixture.Frames.AwaitSettlementAsync(fixture.Task.TaskId,
                fixture.Task.ExecutionId, binding.OriginalAdmission.AttemptId, CancellationToken.None);
            await settlement;
            var settled = Assert.IsAssignableFrom<TaskRunOriginalRequestFailure>(
                router.TryGetOriginalRequestFailure(request, binding.OriginalAdmission, outward));
            Assert.Null(router.TryGetOriginalToolResponseDispatchWitness(binding));
            Assert.Equal(TaskRunOriginalFailureEffectKnowledge.Unknown, settled.OriginalFailedAttempt.ProviderNativeEffects);
            Assert.True(raw.IsFaulted);
            Assert.Same(originalCause, Assert.Single(raw.Exception!.InnerExceptions));
            Assert.True(actual.IsFaulted);
            Assert.Same(request, binding.OriginalCallerRequest);
            Assert.Same(outward, binding.OriginalOutwardFailure);
            var actualWire = provider.LastToolRequest;
            Assert.NotNull(actualWire);
            Assert.Same(request.Messages, actualWire!.Messages);
            Assert.Same(request.Tools, actualWire.Tools);
            Assert.Equal(1, provider.ToolCalls);
        }
        catch (Exception cause) { failures.Add(cause); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Copied_final_call_binding_cannot_query_a_native_dispatch_witness_or_start_more_work()
    {
        var cause = Quota();
        var raw = Task.FromException<OllamaToolResponse>(cause);
        var provider = new Provider("only", isLocal: true, tools: _ => raw);
        var fixture = await CanonicalFixture.CreateAsync([provider]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(provider) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var actual = router.ChatWithToolsAsync(request, CancellationToken.None);
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => actual);
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(
                router.TryGetOriginalFinalRequestFailure(request, outward));
            var copy = (TaskRunOriginalFinalRequestFailure)typeof(object)
                .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(binding, null)!;
            Assert.False(router.IsIssuedOriginalFinalRequestFailure(copy, request, outward));
            Assert.Null(router.TryGetOriginalToolResponseDispatchWitness(copy));
            Assert.Equal(1, provider.ToolCalls);
            Assert.True(raw.IsFaulted);
            Assert.Same(cause, Assert.Single(raw.Exception!.InnerExceptions));
            Assert.True(actual.IsFaulted);
            await fixture.Frames.AwaitSettlementAsync(fixture.Task.TaskId, fixture.Task.ExecutionId,
                binding.OriginalAdmission.AttemptId, CancellationToken.None);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }
    [Fact]
    public async Task Every_exhausted_raw_invocation_remains_retained_and_an_earlier_unknown_route_denies_the_whole_call()
    {
        var firstCause = Quota();
        var lastCause = Quota();
        var firstRaw = Task.FromException<OllamaToolResponse>(firstCause);
        var lastRaw = Task.FromException<OllamaToolResponse>(lastCause);
        var first = new Provider("first-unknown", isLocal: true, tools: _ => firstRaw);
        // A recognized name cannot convert either arbitrary implementation into an inspected method.
        var last = new Provider("openai", isLocal: true, tools: _ => lastRaw);
        var fixture = await CanonicalFixture.CreateAsync([first, last]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(first) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var actual = router.ChatWithToolsAsync(request, CancellationToken.None);
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => actual);
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(
                router.TryGetOriginalFinalRequestFailure(request, outward));
            var retained = OriginalToolDispatchControlCohort(router, request);
            Assert.Equal(2, retained.Length);
            Assert.Same(first, OriginalToolDispatchControlField(retained[0], "Provider"));
            Assert.Same(last, OriginalToolDispatchControlField(retained[1], "Provider"));
            Assert.Same(firstRaw, OriginalToolDispatchControlField(retained[0], "Task"));
            Assert.Same(lastRaw, OriginalToolDispatchControlField(retained[1], "Task"));
            Assert.Same(first.LastToolRequest, OriginalToolDispatchControlField(retained[0], "Request"));
            Assert.Same(last.LastToolRequest, OriginalToolDispatchControlField(retained[1], "Request"));
            var firstObservation = Assert.IsType<TaskRunOriginalFailureObservation>(
                OriginalToolDispatchControlField(retained[0], "Observation"));
            var firstAdmission = Assert.IsType<TaskRunAttemptAdmission>(
                OriginalToolDispatchControlField(retained[0], "Admission"));
            Assert.Same(firstObservation.OriginalFrame, OriginalToolDispatchControlField(retained[0], "Frame"));
            Assert.Same(binding.OriginalObservation, OriginalToolDispatchControlField(retained[1], "Observation"));
            Assert.Same(binding.OriginalAdmission, OriginalToolDispatchControlField(retained[1], "Admission"));
            Assert.NotSame(firstAdmission, binding.OriginalAdmission);
            Assert.Same(firstCause, outward.InnerException);
            Assert.Null(router.TryGetOriginalToolResponseDispatchWitness(binding));
            await fixture.Frames.AwaitSettlementAsync(fixture.Task.TaskId, fixture.Task.ExecutionId,
                binding.OriginalAdmission.AttemptId, CancellationToken.None);
            var source = Assert.IsAssignableFrom<ITaskRunOriginalFailedAttemptSettlementSource>(fixture.Frames);
            var firstSettled = Assert.IsType<TaskRunOriginalFailedAttemptSettlement>(
                source.TryGetOriginalFailedAttemptSettlement(firstObservation, firstAdmission));
            var lastSettled = Assert.IsType<TaskRunOriginalFailedAttemptSettlement>(
                source.TryGetOriginalFailedAttemptSettlement(binding.OriginalObservation, binding.OriginalAdmission));
            Assert.True(source.IsIssuedOriginalFailedAttemptSettlement(firstSettled, firstObservation, firstAdmission));
            Assert.True(source.IsIssuedOriginalFailedAttemptSettlement(lastSettled, binding.OriginalObservation, binding.OriginalAdmission));
            Assert.True(firstSettled.OriginalSettlement.IsCompletedSuccessfully);
            Assert.True(lastSettled.OriginalLeaseClose.IsCompletedSuccessfully);
            Assert.Null(router.TryGetOriginalToolResponseDispatchWitness(binding));
            Assert.True(firstRaw.IsFaulted);
            Assert.True(lastRaw.IsFaulted);
            Assert.Same(firstCause, Assert.Single(firstRaw.Exception!.InnerExceptions));
            Assert.Same(lastCause, Assert.Single(lastRaw.Exception!.InnerExceptions));
            Assert.True(actual.IsFaulted);
            Assert.Equal(1, first.ToolCalls);
            Assert.Equal(1, last.ToolCalls);
        }
        catch (Exception cause) { failures.Add(cause); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    private static object[] OriginalToolDispatchControlCohort(ResilientProviderRoutingModelClient router,
        OllamaToolRequest request)
    {
        var slots = typeof(ResilientProviderRoutingModelClient)
            .GetField("_originalRequestFailures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(router)!;
        object?[] arguments = [request, null];
        Assert.True((bool)slots.GetType().GetMethod("TryGetValue")!.Invoke(slots, arguments)!);
        var body = OriginalToolDispatchControlField(arguments[1]!, "Current")!;
        var cohort = (System.Collections.IEnumerable)OriginalToolDispatchControlField(body, "ToolDispatchInvocations")!;
        return cohort.Cast<object>().ToArray();
    }

    private static object? OriginalToolDispatchControlField(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(owner);
}
