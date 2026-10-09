using System.Runtime.CompilerServices;
using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

// The maintained real Chat/coordinator/frame fixture is reused. Its actors, transport and
// stores remain controlled component sources, never installed/cloud/model authority.
public sealed partial class ChatCanonicalContextProducerTests
{
    [Fact]
    public async Task Actual_stream_response_stays_running_until_original_dispose_then_completes_same_read()
    {
        var h = Harness.Create(temporary: true); h.Provider.BindResponse = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.OnStreamFinally = () => { entered.TrySetResult(); return release.Task; };
        await WithResponseOwnersAsync(h, [], async () =>
        {
            var run = h.RunAsync("original response");
            var primaryAndRun = new List<Exception>();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                var ack = h.Provider.ResponseAck!;
                var before = (await h.Coordinator.GetAsync(ack.AcknowledgedSnapshot.TaskId, TestContext.Current.CancellationToken))!;
                Assert.Equal(TaskPlanNodeState.Running, Assert.Single(before.Plan, value => value.ActionId == ack.ActualActionId).State);
                Assert.False(run.IsCompleted);
                Assert.True(ack.OriginalRegistration.IsCompletedSuccessfully);
                Assert.All(ack.OriginalSources, actual => Assert.True(actual.IsCompletedSuccessfully));
            }
            catch (Exception primary) { primaryAndRun.Add(primary); }
            finally
            {
                release.TrySetResult();
                try { await JoinIndependentFixtureCleanupAsync(run, TimeSpan.FromSeconds(5)); }
                catch (Exception cleanup)
                {
                    if (cleanup is TimeoutException) primaryAndRun.Add(cleanup);
                    else
                    {
                        IEnumerable<Exception> causes = run.Exception is { } fault ? fault.InnerExceptions : [cleanup];
                        primaryAndRun.AddRange(causes);
                    }
                }
            }
            if (primaryAndRun.Count != 0) throw new AggregateException("Primary response assertions and real released run failures remain retained.", primaryAndRun);
            var after = h.Service.CurrentCanonicalTask!;
            Assert.Equal(TaskExecutionLifecycle.Completed, after.State);
            Assert.Equal(TaskPlanNodeState.Completed, Assert.Single(after.Plan, value => value.ActionId == h.Provider.ResponseAck!.ActualActionId).State);
            Assert.Equal(1, h.Provider.Frames);
            Assert.Equal(0, h.Workspace.Effects);
        });
    }

    [Fact]
    public async Task Actual_finite_tool_response_ack_is_read_completion_without_tool_acceptance_or_replay()
    {
        var h = Harness.Create(temporary: true, tools: true); h.Provider.BindResponse = true;
        h.Provider.ControlledResponse = Task.FromResult(new OllamaToolResponse("actual controlled result", []));
        await WithResponseOwnersAsync(h, [], async () =>
        {
            await h.RunAsync("original response");
            var ack = h.Provider.ResponseAck!; var current = h.Service.CurrentCanonicalTask!;
            Assert.Equal(ack.ActualActionId, h.Provider.ToolRequest!.ExecutionContext!.ActionId);
            Assert.Equal(ack.AcknowledgedSnapshot.TaskId, current.TaskId);
            Assert.Equal(ack.AcknowledgedSnapshot.ExecutionId, current.ExecutionId);
            var node = Assert.Single(current.Plan, value => value.ActionId == ack.ActualActionId);
            Assert.Equal(TaskPlanNodeState.Completed, node.State);
            Assert.Equal(TaskActionInterruptionPolicy.ReadOnlyCancellable, node.InterruptionPolicy);
            Assert.Null(node.Acceptance);
            Assert.Equal(0, h.ToolOwner.Preparations);
            Assert.Equal(0, h.Workspace.Effects);
            Assert.Equal(1, h.Provider.Frames);
        });
    }

    [Fact]
    public async Task Copied_request_admission_and_duplicate_bind_cannot_authorize_or_replay_response()
    {
        var h = Harness.Create(temporary: true); h.Provider.BindResponse = true;
        h.Provider.AfterResponseBind = (request, admission, source, ack) =>
        {
            var chat = (OllamaChatRequest)request;
            Assert.Same(source, h.Coordinator.TryGetOriginalResponseActionSource(chat));
            Assert.Null(h.Coordinator.TryGetOriginalResponseActionSource(chat with { }));
            Assert.True(source.IsIssuedOriginalResponseActionAcknowledgment(ack, chat, admission));
            Assert.False(source.IsIssuedOriginalResponseActionAcknowledgment(ack, chat, admission with { }));
            Assert.False(source.IsIssuedOriginalResponseActionAcknowledgment(ack, chat with { }, admission));
            Assert.Throws<InvalidOperationException>(() => { _ = source.BindOriginalResponseAsync(chat, admission,
                ack.AcknowledgedSnapshot, callback => callback(), _ => { }, TestContext.Current.CancellationToken); });
            return Task.CompletedTask;
        };
        await WithResponseOwnersAsync(h, [], async () =>
        {
            await h.RunAsync("original response");
            Assert.Equal(1, h.Provider.Frames);
            var source = h.Coordinator.TryGetOriginalResponseActionSource(h.Provider.ChatRequest!)!;
            Assert.Throws<InvalidOperationException>(() => { _ = source.BindOriginalResponseAsync(h.Provider.ChatRequest!,
                h.Provider.ResponseAdmission!, h.Provider.ResponseAck!.AcknowledgedSnapshot,
                callback => callback(), _ => { }, TestContext.Current.CancellationToken); });
        });
    }

    [Fact]
    public async Task Restored_execution_context_source_callback_refuses_own_process_join_before_raw_model()
    {
        var clean = ExecutionContext.Capture()!;
        var h = Harness.Create(temporary: true); h.Provider.BindResponse = true; Exception? refusal = null;
        h.Authority.ResponseCallback = () => ExecutionContext.Run(clean, _ =>
        {
            refusal = Record.Exception(() => h.Coordinator.DemandExternalOriginalProcessJoin());
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Equal(0, h.Provider.Frames);
        }, null);
        await WithResponseOwnersAsync(h, [], async () =>
        {
            await h.RunAsync("original response");
            Assert.NotNull(refusal);
            Assert.Equal(1, h.Provider.Frames);
            Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask!.State);
        });
    }

    [Fact]
    public async Task Faulted_original_registration_read_keeps_full_OCE_IO_group_and_never_dispatches()
    {
        var h = Harness.Create(temporary: true); h.Provider.BindResponse = true;
        var first = new OperationCanceledException("actual faulted repository OCE");
        var second = new IOException("actual sibling repository fault");
        var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([first, second]);
        h.Provider.BeforeResponseBind = () => { h.TaskRepository.ResponseRead = raw.Task; return Task.CompletedTask; };
        await WithResponseOwnersAsync(h, [first, second], async () =>
        {
            var observed = await Record.ExceptionAsync(() => h.RunAsync("original response"));
            h.TaskRepository.ResponseRead = null;
            Assert.NotNull(observed);
            Assert.True(raw.Task.IsFaulted);
            Assert.True(h.Provider.ActualResponseBind!.IsFaulted);
            Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, first));
            Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, second));
            Assert.Equal(0, h.Provider.Frames);
            Assert.Null(h.Provider.ResponseAck);
        });
    }

    [Fact]
    public async Task Genuinely_canceled_original_registration_read_remains_distinct_and_never_dispatches()
    {
        var h = Harness.Create(temporary: true); h.Provider.BindResponse = true;
        using var originalLifetime = new CancellationTokenSource(); originalLifetime.Cancel();
        var raw = Task.FromCanceled<TaskExecutionSnapshot?>(originalLifetime.Token);
        h.Provider.BeforeResponseBind = () => { h.TaskRepository.ResponseRead = raw; return Task.CompletedTask; };
        var expected = new List<Exception>();
        await WithResponseOwnersAsync(h, expected, async () =>
        {
            var observed = await Record.ExceptionAsync(() => h.RunAsync("original response"));
            h.TaskRepository.ResponseRead = null;
            Assert.NotNull(observed);
            Assert.True(raw.IsCanceled);
            Assert.True(h.Provider.ActualResponseBind!.IsCanceled);
            Assert.Equal(0, h.Provider.Frames);
            // These are only the SAME original cancellation references, never unrelated cleanup.
            foreach (var cause in OriginalCauses(observed!).Where(value => value is OperationCanceledException)) expected.Add(cause);
        });
    }

    [Fact]
    public async Task Actual_failed_response_keeps_running_action_and_same_run_without_fabricated_checkpoint_grant()
    {
        var h = Harness.Create(temporary: true, tools: true); h.Provider.BindResponse = true;
        var first = new OperationCanceledException("faulted original provider OCE");
        var second = new IOException("faulted original provider sibling");
        var raw = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([first, second]); h.Provider.ControlledResponse = raw.Task;
        await WithResponseOwnersAsync(h, [first, second], async () =>
        {
            var failure = await Record.ExceptionAsync(() => h.RunAsync("original response"));
            Assert.NotNull(failure);
            Assert.True(raw.Task.IsFaulted);
            Assert.Contains(OriginalCauses(failure!), cause => ReferenceEquals(cause, first));
            Assert.Contains(OriginalCauses(failure!), cause => ReferenceEquals(cause, second));
            var ack = h.Provider.ResponseAck!;
            var row = (await h.Coordinator.GetAsync(ack.AcknowledgedSnapshot.TaskId, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskPlanNodeState.Running, Assert.Single(row.Plan, value => value.ActionId == ack.ActualActionId).State);
            Assert.Equal(ack.AcknowledgedSnapshot.ExecutionId, row.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, row.State);
            var refused = await Record.ExceptionAsync(() => h.Coordinator.StartObservedOriginalRunResumeAsync(row.TaskId,
                row.ExecutionId, TestContext.Current.CancellationToken));
            Assert.NotNull(refused);
            Assert.Equal(1, h.Provider.Frames);
            Assert.Equal(1, h.TaskRepository.RowCount);
            Assert.Equal(0, h.Workspace.Effects);
            Assert.True(raw.Task.IsFaulted);
            var exact = Assert.Single(OriginalCauses(refused!).OfType<InvalidOperationException>()
                .Distinct<InvalidOperationException>(ReferenceEqualityComparer.Instance),
                cause => cause.Message == "The actual unfinished tool step has no complete original cleanup/accepted-work boundary.");
            expectedResponseRefusals.Add(exact);
            // Start the genuine whole process close, then inspect only the actual
            // privately bound stage's existing close. Reflection creates no witness.
            var wholeClose = h.Coordinator.CloseAndSuspendOriginalProducersAsync();
            var wholeFailure = await Record.ExceptionAsync(() => JoinIndependentFixtureCleanupAsync(wholeClose, TimeSpan.FromSeconds(5)));
            Assert.NotNull(wholeFailure);
            Assert.True(wholeClose.IsFaulted);
            var source = h.Coordinator.TryGetOriginalResponseActionSource(h.Provider.ToolRequest!)!;
            var actualStageClose = ReadActualResponseStageClose(source, ack);
            Assert.True(actualStageClose.IsFaulted);
            var missingResult = Assert.Single(OriginalCauses(actualStageClose.Exception!).OfType<InvalidOperationException>()
                .Distinct<InvalidOperationException>(ReferenceEqualityComparer.Instance),
                cause => cause.Message == "The actual coordinator preparation has no fully closed original result.");
            Assert.Contains(OriginalCauses(wholeFailure!), cause => ReferenceEquals(cause, missingResult));
            expectedResponseRefusals.Add(missingResult);
        }, expectedResponseRefusals);
    }

    [Fact]
    public async Task Actual_response_CAS_survives_post_publication_scope_fault_without_model_dispatch()
    {
        var h = Harness.Create(temporary: true); h.Provider.BindResponse = true;
        var exact = new IOException("actual response scope failed after its durable node ACK");
        var published = false;
        h.Coordinator.SnapshotChanged += (_, row) =>
        {
            if (h.Provider.ChatRequest?.ExecutionContext?.ActionId is { } action
                && row.Plan.Any(node => node.ActionId == action && node.State == TaskPlanNodeState.Running)) published = true;
        };
        h.Provider.ResponseScope = callback => { callback(); if (published) throw exact; };
        await WithResponseOwnersAsync(h, [exact], async () =>
        {
            var failure = await Record.ExceptionAsync(() => h.RunAsync("original response"));
            Assert.NotNull(failure);
            Assert.Contains(OriginalCauses(failure!), cause => ReferenceEquals(cause, exact));
            Assert.True(h.Provider.ActualResponseBind!.IsFaulted);
            Assert.Null(h.Provider.ResponseAck);
            var request = h.Provider.ChatRequest!;
            var row = (await h.Coordinator.GetAsync(request.ExecutionContext!.TaskId, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskPlanNodeState.Running, Assert.Single(row.Plan,
                value => value.ActionId == request.ExecutionContext.ActionId).State);
            Assert.Equal(request.ExecutionContext.ExecutionId, row.ExecutionId);
            Assert.Equal(0, h.Provider.Frames);
            Assert.Equal(0, h.Workspace.Effects);
        });
    }

    private readonly List<Exception> expectedResponseRefusals = [];

    private static Task ReadActualResponseStageClose(ITaskRunOriginalResponseActionSource source,
        TaskRunOriginalResponseActionAcknowledgment acknowledged)
    {
        const BindingFlags actualPrivate = BindingFlags.NonPublic | BindingFlags.Instance;
        var bindings = (System.Collections.IEnumerable)source.GetType().GetField("Bindings", actualPrivate)!.GetValue(source)!;
        var sameBinding = Assert.Single(bindings.Cast<object>(), value => ReferenceEquals(
            value.GetType().GetField("Registration", actualPrivate)!.GetValue(value), acknowledged.OriginalRegistration));
        var stage = sameBinding.GetType().GetField("Stage", actualPrivate)!.GetValue(sameBinding)!;
        Assert.Same(acknowledged.OriginalRegistration, stage.GetType().GetField("ActualDriver", actualPrivate)!.GetValue(stage));
        return (Task)stage.GetType().GetField("_close", actualPrivate)!.GetValue(stage)!;
    }

    private static async Task WithResponseOwnersAsync(Harness h, IReadOnlyList<Exception> known,
        Func<Task> body, IReadOnlyList<Exception>? commandRefusals = null)
    {
        var failures = new List<Exception>();
        bool Expected(Exception cause) => known.Any(value => ReferenceEquals(value, cause))
            || commandRefusals?.Any(value => ReferenceEquals(value, cause)) == true
            || cause is TaskCanceledException canceled && canceled.Task is { IsCanceled: true } original
                && h.Provider.ResponseSources.Any(value => ReferenceEquals(value, original))
            || cause is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(Expected);
        try { await body(); } catch (Exception cause) { failures.Add(cause); }
        finally
        {
            h.TaskRepository.ResponseRead = null;
            Task? coordinatorClose = null; Task? frameClose = null;
            try { coordinatorClose = h.Coordinator.CloseAndSuspendOriginalProducersAsync(); }
            catch (Exception cause) { if (!Expected(cause)) failures.Add(cause); }
            try { frameClose = h.Runtime.CloseAndDrainAsync(); }
            catch (Exception cause) { if (!Expected(cause)) failures.Add(cause); }
            foreach (var actual in new[] { coordinatorClose, frameClose }.OfType<Task>())
            {
                try { await JoinIndependentFixtureCleanupAsync(actual, TimeSpan.FromSeconds(5)); }
                catch (Exception cause)
                {
                    if (cause is TimeoutException) failures.Add(cause);
                    else
                    {
                        IEnumerable<Exception> direct = actual.Exception is { } fault ? fault.InnerExceptions : [cause];
                        foreach (var original in direct) if (!Expected(original)) failures.Add(original);
                    }
                }
            }
        }
        if (failures.Count != 0) throw new AggregateException("Response assertions and unknown independent owner cleanup remain failed.", failures);
    }

    private sealed partial class Authority : ITaskRunOriginalResponseAdmissionSource
    {
        public Action? ResponseCallback;
        public async Task ValidateOriginalResponseAdmissionAsync(TaskRunAttemptAdmission sameAdmission,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task? actual = null;
            scope(() => { actual = sameAdmission.Lease.RevalidateAsync(token).AsTask(); retain(actual); });
            await actual!;
            scope(() => { ResponseCallback?.Invoke(); DemandOriginalResponseAdmission(sameAdmission); });
        }
        public void DemandOriginalResponseAdmission(TaskRunAttemptAdmission sameAdmission)
        {
            if (!AllowCurrentActor || !ReferenceEquals(Lease, sameAdmission.Lease) || Lease.Disposes != 0)
                throw new UnauthorizedAccessException("Actual controlled response admission changed.");
        }
    }

    private sealed partial class Provider
    {
        public bool BindResponse;
        public Task<OllamaToolResponse>? ControlledResponse;
        public Task<TaskRunOriginalResponseActionAcknowledgment>? ActualResponseBind;
        public TaskRunOriginalResponseActionAcknowledgment? ResponseAck;
        public TaskRunAttemptAdmission? ResponseAdmission;
        public readonly List<Task> ResponseSources = [];
        public Action<Action>? ResponseScope;
        public Func<Task>? BeforeResponseBind;
        public Func<object, TaskRunAttemptAdmission, ITaskRunOriginalResponseActionSource,
            TaskRunOriginalResponseActionAcknowledgment, Task>? AfterResponseBind;
        private async Task BindActualResponseAsync(object request, TaskRunAttemptAdmission admission, CancellationToken token)
        {
            if (!BindResponse) return;
            var row = (await coordinator.GetAsync(admission.Snapshot.TaskId, token))!;
            var source = request is OllamaChatRequest chat ? coordinator.TryGetOriginalResponseActionSource(chat)
                : coordinator.TryGetOriginalResponseActionSource((OllamaToolRequest)request);
            if (BeforeResponseBind is not null) await BeforeResponseBind();
            ResponseAdmission = admission;
            ActualResponseBind = request is OllamaChatRequest actualChat
                ? source!.BindOriginalResponseAsync(actualChat, admission, row, ResponseScope ?? (callback => callback()), actual => ResponseSources.Add(actual), token)
                : source!.BindOriginalResponseAsync((OllamaToolRequest)request, admission, row, ResponseScope ?? (callback => callback()), actual => ResponseSources.Add(actual), token);
            ResponseAck = await ActualResponseBind;
            if (AfterResponseBind is not null) await AfterResponseBind(request, admission, source!, ResponseAck);
        }
    }
}
