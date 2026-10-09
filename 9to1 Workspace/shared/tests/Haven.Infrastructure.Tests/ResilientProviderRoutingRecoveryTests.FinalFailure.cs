using System.Reflection;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed partial class ResilientProviderRoutingRecoveryTests
{
    [Fact]
    public async Task Final_tool_failure_binds_same_request_before_real_settlement_and_retains_failed_originals()
    {
        var rawCause = Quota();
        var raw = Task.FromException<OllamaToolResponse>(rawCause);
        var provider = new Provider("only", isLocal: true, tools: _ => raw);
        var fixture = await CanonicalFixture.CreateAsync([provider]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(provider) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var publicCall = router.ChatWithToolsAsync(request, CancellationToken.None);
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => publicCall);
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(router.TryGetOriginalFinalRequestFailure(request, outward));
            Assert.True(router.IsIssuedOriginalFinalRequestFailure(binding, request, outward));
            Assert.Same(request, binding.OriginalCallerRequest);
            Assert.Same(outward, binding.OriginalOutwardFailure);
            Assert.Same(fixture.Admission, binding.OriginalAdmission);
            Assert.Same(rawCause, binding.OriginalObservation.OriginalCause);
            Assert.True(publicCall.IsFaulted);
            Assert.True(raw.IsFaulted);
            Assert.Same(rawCause, Assert.Single(raw.Exception!.InnerExceptions));
            Assert.Null(router.TryGetOriginalRequestFailure(request, binding.OriginalAdmission, outward));
            var settlement = fixture.Frames.AwaitSettlementAsync(fixture.Task.TaskId,
                fixture.Task.ExecutionId, binding.OriginalAdmission.AttemptId, CancellationToken.None);
            await settlement;
            var receipt = Assert.IsAssignableFrom<TaskRunOriginalRequestFailure>(
                router.TryGetOriginalRequestFailure(request, binding.OriginalAdmission, outward));
            Assert.True(router.IsIssuedOriginalRequestFailure(receipt, request, binding.OriginalAdmission, outward));
            Assert.Same(binding.OriginalObservation, receipt.OriginalFailedAttempt.OriginalObservation);
            Assert.Same(settlement, receipt.OriginalFailedAttempt.OriginalSettlement);
            Assert.Equal(TaskRunOriginalFailureEffectKnowledge.Unknown, receipt.OriginalFailedAttempt.ProviderNativeEffects);
            Assert.Null(receipt.OriginalFailedAttempt.OriginalRetirementAcknowledgment);
            Assert.True(publicCall.IsFaulted);
            Assert.True(binding.OriginalObservation.OriginalFrame.IsFaulted);
            Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
            Assert.Null(router.TryGetOriginalFinalRequestFailure(request with { }, outward));
            Assert.Null(router.TryGetOriginalFinalRequestFailure(request, new AggregateException(outward)));
            var clone = (TaskRunOriginalFinalRequestFailure)typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(binding, null)!;
            Assert.False(router.IsIssuedOriginalFinalRequestFailure(clone, request, outward));
            Assert.False(router.IsIssuedOriginalFinalRequestFailure(binding, request with { }, outward));
            Assert.False(router.IsIssuedOriginalRequestFailure(receipt, request, binding.OriginalAdmission with { }, outward));
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Successful_in_call_tool_fallback_never_exposes_the_earlier_failure_as_final()
    {
        var firstCause = Quota();
        var first = new Provider("first", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(firstCause));
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(first) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var actual = router.ChatWithToolsAsync(request, CancellationToken.None);
            var response = await actual;
            Assert.True(actual.IsCompletedSuccessfully);
            Assert.Same(second.Descriptor, response.EffectiveModel);
            Assert.Equal(1, first.ToolCalls);
            Assert.Equal(1, second.ToolCalls);
            Assert.Null(router.TryGetOriginalFinalRequestFailure(request, firstCause));
            Assert.Null(router.TryGetOriginalRequestFailure(request, fixture.Admission, firstCause));
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Exhausted_two_attempt_tool_call_binds_only_its_actual_final_admission_and_frame()
    {
        var firstCause = Quota();
        var finalCause = Quota();
        var firstRaw = Task.FromException<OllamaToolResponse>(firstCause);
        var finalRaw = Task.FromException<OllamaToolResponse>(finalCause);
        var first = new Provider("first", isLocal: true, tools: _ => firstRaw);
        var last = new Provider("last", isLocal: true, tools: _ => finalRaw);
        var fixture = await CanonicalFixture.CreateAsync([first, last]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(first) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var original = router.ChatWithToolsAsync(request, CancellationToken.None);
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => original);
            Assert.Same(firstCause, outward.InnerException); // Established outward behavior remains unchanged.
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(router.TryGetOriginalFinalRequestFailure(request, outward));
            Assert.NotSame(fixture.Admission, binding.OriginalAdmission);
            Assert.Same(finalCause, binding.OriginalObservation.OriginalCause);
            Assert.Null(router.TryGetOriginalRequestFailure(request, fixture.Admission, outward));
            await fixture.Frames.AwaitSettlementAsync(fixture.Task.TaskId, fixture.Task.ExecutionId,
                binding.OriginalAdmission.AttemptId, CancellationToken.None);
            var receipt = Assert.IsAssignableFrom<TaskRunOriginalRequestFailure>(router.TryGetOriginalRequestFailure(request, binding.OriginalAdmission, outward));
            Assert.Same(binding.OriginalObservation, receipt.OriginalFailedAttempt.OriginalObservation);
            Assert.True(original.IsFaulted);
            Assert.Same(firstCause, Assert.Single(firstRaw.Exception!.InnerExceptions));
            Assert.Same(finalCause, Assert.Single(finalRaw.Exception!.InnerExceptions));
            Assert.Equal(2, fixture.Authority.Leases.Count);
            Assert.All(fixture.Authority.Leases, lease => Assert.Equal(1, lease.Disposals));
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Reusing_same_caller_request_invalidates_earlier_final_failure_binding_even_if_new_capture_refuses()
    {
        var cause = Quota();
        var first = new Provider("first", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(cause));
        var fixture = await CanonicalFixture.CreateAsync([first]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(first) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => router.ChatWithToolsAsync(request, CancellationToken.None));
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(router.TryGetOriginalFinalRequestFailure(request, outward));
            Assert.True(router.IsIssuedOriginalFinalRequestFailure(binding, request, outward));
            Assert.NotNull(await Record.ExceptionAsync(() => router.ChatWithToolsAsync(request, CancellationToken.None)));
            Assert.False(router.IsIssuedOriginalFinalRequestFailure(binding, request, outward));
            Assert.Null(router.TryGetOriginalFinalRequestFailure(request, outward));
            Assert.Equal(1, first.ToolCalls);
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Active_raw_tool_original_cannot_issue_final_failure_evidence_before_its_actual_terminal()
    {
        var cause = Quota();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Provider("first", isLocal: true, tools: _ => { entered.TrySetResult(); return raw.Task; });
        var fixture = await CanonicalFixture.CreateAsync([first]);
        var router = Assert.IsType<ResilientProviderRoutingModelClient>(fixture.Client);
        var request = Tools(first) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>(); Task<OllamaToolResponse>? original = null;
        try
        {
            original = router.ChatWithToolsAsync(request, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(original.IsCompleted);
            Assert.Null(router.TryGetOriginalFinalRequestFailure(request, cause));
            raw.SetException(cause);
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => original);
            Assert.NotNull(router.TryGetOriginalFinalRequestFailure(request, outward));
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            raw.TrySetException(cause);
            if (original is not null) _ = await Record.ExceptionAsync(() => original);
            await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures);
        }
        ThrowFinalFailureControlErrors(failures);
    }

    private static async Task CollectFinalFailureOriginalAsync(Task actual, List<Exception> failures)
    {
        var error = await Record.ExceptionAsync(() => actual);
        if (error is not null) failures.Add(error);
    }
    private static void ThrowFinalFailureControlErrors(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
}
