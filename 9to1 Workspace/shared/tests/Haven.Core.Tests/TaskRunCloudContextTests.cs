using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Synthetic current actor, repository, credential and issued-attempt fixtures exercise
/// the actual cloud/context source. No real credential/provider, Home grant, paid request,
/// native session, browser login, quota balance or domain egress authority is manufactured.</summary>
public sealed partial class TaskRunCloudContextTests
{
    [Fact]
    public async Task Same_original_context_supports_healthy_frames_without_releasing_attempt_lease()
    {
        await RunAsync(async rig =>
        {
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, rig.Inventory(), default);
            await using (var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, original, rig.Route(original), default))
                await frame.RevalidateAsync(default);
            var next = rig.Request("next actual prompt");
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, next, rig.Inventory(), default);
            await using (var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, next, rig.Route(next), default))
                await frame.RevalidateAsync(default);
            Assert.Equal(0, rig.Lease.Closes);
            Assert.True(rig.Secrets.Reads > 0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copied_request_or_admission_does_not_reconstruct_private_context_issuance(bool copyAdmission)
    {
        await RunAsync(async rig =>
        {
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, rig.Inventory(), default);
            var request = copyAdmission ? original : original with { };
            var admission = copyAdmission ? rig.Issued with { } : rig.Issued;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.AcquireOriginalFrameAsync(
                admission, rig.Snapshot, request, rig.Route(request), default).AsTask());
            Assert.Equal(0, rig.Lease.Closes);
        });
    }

    [Fact]
    public async Task Actual_mutable_transcript_substitution_is_refused_before_remote_frame()
    {
        await RunAsync(async rig =>
        {
            var messages = new List<OllamaMessage> { new("user", "actual selected prompt") };
            var original = rig.Request() with { Messages = messages };
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, rig.Inventory(), default);
            messages[0] = new("user", "other data not captured");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.AcquireOriginalFrameAsync(
                rig.Issued, rig.Snapshot, original, rig.Route(original), default).AsTask());
        });
    }

    [Fact]
    public async Task Genuine_temporary_request_custody_requires_no_invented_conversation_row()
    {
        await RunAsync(async rig =>
        {
            rig.Conversation = rig.Conversation with { IsTemporary = true };
            rig.Conversations.Row = null;
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, rig.Inventory(), default);
            await using var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, original, rig.Route(original), default);
            await frame.RevalidateAsync(default);
            Assert.Equal(0, rig.Conversations.Writes);
            Assert.Equal(0, rig.Conversations.Reads);
            var copy = original with { };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.AcquireOriginalFrameAsync(
                rig.Issued, rig.Snapshot, copy, rig.Route(copy), default).AsTask());
        });
    }

    [Theory]
    [InlineData("project")]
    [InlineData("registered")]
    [InlineData("workspace")]
    [InlineData("image")]
    [InlineData("tool-output")]
    public async Task Missing_actual_context_domain_owner_refuses_remote_without_disabling_local_capture(string kind)
    {
        await RunAsync(async rig =>
        {
            var original = rig.Request();
            var inventory = rig.Inventory() with
            {
                ProjectContext = kind == "project" ? "unproved source text" : null,
                RegisteredContext = kind == "registered" ? "unproved registered resource" : null,
                OriginalWorkspaceRoot = kind == "workspace" ? "/selected/workspace" : null
            };
            if (kind == "image") original = original with { Messages = [new("user", "actual prompt", ["synthetic image bytes"])] };
            if (kind == "tool-output") original = original with { Messages = [new("tool", "actual selected workspace output without egress owner")] };
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, inventory, default);
            await Assert.ThrowsAsync<NotSupportedException>(() => rig.Source.AcquireOriginalFrameAsync(
                rig.Issued, rig.Snapshot, original, rig.Route(original), default).AsTask());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actor_or_configuration_retirement_during_actual_held_credential_read_refuses(bool changeActor)
    {
        await RunAsync(async rig =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ITaskRunCloudAdmissionLease?>? acquisition = null;
            Exception? expected = null; var errors = new List<Exception>();
            try
            {
                rig.Secrets.BeforeRead = async () => { entered.TrySetResult(); await release.Task; };
                acquisition = rig.Source.AcquireOriginalAsync(rig.Owner, rig.Model, rig.Configurations.Row, rig.Candidate, default).AsTask();
                await Task.WhenAny(entered.Task, acquisition);
                if (!entered.Task.IsCompleted) await acquisition;
                Assert.False(acquisition.IsCompleted);
                if (changeActor) rig.Actors.Actor = rig.Actors.Actor! with { AuthenticationRevision = "retired" };
                else rig.Configurations.Row = rig.Configurations.Row with { IsEnabled = false };
                release.TrySetResult();
                expected = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => acquisition!);
            }
            catch (Exception error) { Add(errors, null, error); }
            finally
            {
                release.TrySetResult();
                if (acquisition is not null) await ObserveCleanupAsync(acquisition, expected, errors);
                rig.Secrets.BeforeRead = null;
            }
            Throw(errors);
        });
    }

    [Fact]
    public async Task Background_policy_revoked_during_actual_held_knowledge_read_is_checked_after_read()
    {
        await RunAsync(async rig =>
        {
            var record = rig.KnowledgeRecord(KnowledgeCategory.Project);
            rig.Knowledge.Row = record;
            rig.Privacy.Current = rig.Privacy.Current with { BackgroundLearningEnabled = true, BackgroundLearningCloudDisclosureEnabled = true };
            var inventory = rig.Inventory() with { SelectedBackgroundLearning = [record], OriginalBackgroundScopes = new HashSet<string> { "global" } };
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, inventory, default);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ITaskRunProviderContextFrame>? acquisition = null;
            Exception? expected = null; var errors = new List<Exception>();
            try
            {
                rig.Knowledge.BeforeRead = async () => { entered.TrySetResult(); await release.Task; };
                acquisition = rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, original, rig.Route(original), default).AsTask();
                await Task.WhenAny(entered.Task, acquisition);
                if (!entered.Task.IsCompleted) await acquisition;
                Assert.False(acquisition.IsCompleted);
                rig.Privacy.Current = rig.Privacy.Current with { BackgroundLearningCloudDisclosureEnabled = false };
                release.TrySetResult();
                expected = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => acquisition!);
            }
            catch (Exception error) { Add(errors, null, error); }
            finally
            {
                release.TrySetResult();
                if (acquisition is not null) await ObserveCleanupAsync(acquisition, expected, errors);
                rig.Knowledge.BeforeRead = null;
            }
            Throw(errors);
        });
    }

    [Fact]
    public async Task Persistent_memory_does_not_borrow_background_opt_in_and_actual_forget_still_refuses()
    {
        await RunAsync(async rig =>
        {
            var memory = rig.KnowledgeRecord(KnowledgeCategory.LearnMe);
            rig.Knowledge.Row = memory;
            var inventory = rig.Inventory() with { SelectedPersistentMemory = [memory] };
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, inventory, default);
            await using (var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, original, rig.Route(original), default))
                await frame.RevalidateAsync(default);
            Assert.False(rig.Privacy.Current.BackgroundLearningCloudDisclosureEnabled);
            rig.Knowledge.Row = null;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.AcquireOriginalFrameAsync(
                rig.Issued, rig.Snapshot, original, rig.Route(original), default).AsTask());
        });
    }

    [Fact]
    public async Task Changed_background_scope_cannot_report_duplicate_capture_success()
    {
        await RunAsync(async rig =>
        {
            var original = rig.Request();
            var inventory = rig.Inventory() with { BackgroundAppId = "actual-app", OriginalBackgroundScopes = new HashSet<string> { "global" } };
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, inventory, default);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.CaptureOriginalAsync(rig.Snapshot,
                original, inventory with { OriginalBackgroundScopes = new HashSet<string> { "other" } }, default).AsTask());
        });
    }

    [Fact]
    public async Task Actual_routed_model_and_current_action_are_bound_to_same_original_admission()
    {
        await RunAsync(async rig =>
        {
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, rig.Inventory(), default);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.AcquireOriginalFrameAsync(
                rig.Issued, rig.Snapshot, original, rig.Route(original) with { Model = "other-model" }, default).AsTask());
            var wrong = rig.Route(original) with { ExecutionContext = rig.Context() with { ActionId = Guid.NewGuid() } };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.AcquireOriginalFrameAsync(
                rig.Issued, rig.Snapshot, original, wrong, default).AsTask());
        });
    }

    [Fact]
    public async Task Missing_actual_key_has_no_upstream_or_budget_success_substitute()
    {
        await RunAsync(async rig =>
        {
            rig.Secrets.Value = null;
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Source.AcquireOriginalAsync(
                rig.Owner, rig.Model, rig.Configurations.Row, rig.Candidate, default).AsTask());
            var observation = new TaskRunCloudConfigurationObservation("synthetic-provider", "synthetic-model", false,
                null, null, null, "No monetary policy observation exists in this fixture.");
            Assert.Null(observation.KnownCost);
            Assert.Null(observation.Currency);
            Assert.Null(observation.KnownRemainingBudget);
            Assert.Equal(0, rig.Lease.Closes);
        });
    }

    [Fact]
    public async Task Frame_admits_same_actual_raw_task_once_and_independent_close_does_not_dispose_the_attempt()
    {
        await RunAsync(async rig =>
        {
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, rig.Inventory(), default);
            var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, original, rig.Route(original), default);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? actual = null;
            var errors = new List<Exception>();
            try
            {
                await frame.RevalidateAsync(default);
                var fence = Assert.IsAssignableFrom<ITaskRunProviderInvocationFence>(frame);
                actual = fence.RunOriginalInvocation(() => release.Task);
                Assert.Same(release.Task, actual);
                var repeats = 0;
                Assert.Throws<InvalidOperationException>(() => { _ = fence.RunOriginalInvocation(() => { repeats++; return Task.CompletedTask; }); });
                Assert.Equal(0, repeats);
                Assert.False(actual.IsCompleted);
            }
            catch (Exception error) { Add(errors, null, error); }
            finally
            {
                release.TrySetResult();
                if (actual is not null)
                    try { await actual; } catch (Exception error) { Add(errors, actual, error); }
                Task? close = null;
                try { close = frame.DisposeAsync().AsTask(); await close; }
                catch (Exception error) { Add(errors, close, error); }
            }
            Throw(errors);
            Assert.Equal(0, rig.Lease.Closes);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => frame.RevalidateAsync(default).AsTask());
        });
    }

    [Fact]
    public async Task Revocation_in_frame_revalidation_to_raw_start_interval_prevents_actual_invocation()
    {
        await RunAsync(async rig =>
        {
            var original = rig.Request();
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, original, rig.Inventory(), default);
            await using var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, original, rig.Route(original), default);
            await frame.RevalidateAsync(default);
            rig.Policy.Revoke(TaskRunCentralCloudUsePermissionSource.ScopeFor(rig.Owner, rig.Candidate));
            var calls = 0;
            var fence = Assert.IsAssignableFrom<ITaskRunProviderInvocationFence>(frame);
            Assert.Throws<UnauthorizedAccessException>(() => { _ = fence.RunOriginalInvocation(() => { calls++; return Task.CompletedTask; }); });
            Assert.Equal(0, calls);
            Assert.Equal(0, rig.Lease.Closes);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_owner_expiration_rule_distinguishes_expired_transient_memory_from_durable_memory(bool durable)
    {
        await RunAsync(async rig =>
        {
            var memory = rig.KnowledgeRecord(KnowledgeCategory.LearnMe) with
            { ExpiresAt = DateTimeOffset.UnixEpoch, Freshness = durable ? KnowledgeFreshnessClass.Durable : KnowledgeFreshnessClass.ShortLived };
            rig.Knowledge.Row = memory;
            var request = rig.Request();
            var inventory = rig.Inventory() with { SelectedPersistentMemory = [memory] };
            if (!durable)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Source.CaptureOriginalAsync(rig.Snapshot, request, inventory, default).AsTask());
            else
            {
                await rig.Source.CaptureOriginalAsync(rig.Snapshot, request, inventory, default);
                await using var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, request, rig.Route(request), default);
                await frame.RevalidateAsync(default);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_actual_public_GenUi_directive_is_classified_without_authorizing_attached_suffix(bool attachSuffix)
    {
        await RunAsync(async rig =>
        {
            var publicDirective = GenUiChatDirectiveParser.ModelInstructionFor(GenerativeUiResponseMode.Auto);
            var actual = attachSuffix ? publicDirective + "\n\nActual selected private resource without owner proof" : publicDirective;
            var request = rig.Request() with { SystemPrompt = actual };
            var inventory = rig.Inventory() with { RegisteredContext = actual };
            await rig.Source.CaptureOriginalAsync(rig.Snapshot, request, inventory, default);
            if (attachSuffix)
                await Assert.ThrowsAsync<NotSupportedException>(() => rig.Source.AcquireOriginalFrameAsync(
                    rig.Issued, rig.Snapshot, request, rig.Route(request), default).AsTask());
            else
            {
                await using var frame = await rig.Source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, request, rig.Route(request), default);
                await frame.RevalidateAsync(default);
                Assert.Equal(0, rig.Lease.Closes);
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Actual_compound_permission_acquisition_retains_every_direct_cause_before_any_resource_exists(bool frame, bool firstIsCancellation)
    {
        await RunAsync(async rig =>
        {
            Exception one = firstIsCancellation ? new OperationCanceledException("Actual FAULTED permission original") : new IOException("Actual permission original one");
            var two = new IOException("Actual permission original two");
            var supplied = new TaskCompletionSource<ITaskRunCloudUsePermissionLease>(TaskCreationOptions.RunContinuationsAsynchronously);
            supplied.SetException([one, two]);
            Assert.True(supplied.Task.IsFaulted);
            var permission = new SuppliedPermission(supplied.Task);
            var source = new TaskRunConfiguredCloudAdmissionSource(rig.Actors, rig.Configurations, rig.Secrets,
                rig.Privacy, rig.Conversations, (_, _, _, _) => Task.FromResult<TaskRunAttemptAdmission?>(rig.Issued), rig.Knowledge, permission);
            Task actual;
            if (frame)
            {
                var request = rig.Request();
                await source.CaptureOriginalAsync(rig.Snapshot, request, rig.Inventory(), default);
                actual = source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, request, rig.Route(request), default).AsTask();
            }
            else actual = source.AcquireOriginalAsync(rig.Owner, rig.Model, rig.Configurations.Row, rig.Candidate, default).AsTask();
            var error = await Record.ExceptionAsync(() => actual);
            var compound = Assert.IsType<AggregateException>(error);
            Assert.True(actual.IsFaulted); // Even first-OCE is actual fault evidence, not caller cancellation.
            Assert.Equal(2, compound.InnerExceptions.Count);
            Assert.Same(one, compound.InnerExceptions[0]);
            Assert.Same(two, compound.InnerExceptions[1]);
            Assert.Equal(1, permission.Calls);
            Assert.Equal(0, rig.Lease.Closes);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Genuinely_canceled_permission_original_is_not_promoted_to_fault_or_success(bool frame)
    {
        await RunAsync(async rig =>
        {
            var stopped = new CancellationToken(canceled: true);
            var original = Task.FromCanceled<ITaskRunCloudUsePermissionLease>(stopped);
            var permission = new SuppliedPermission(original);
            var source = new TaskRunConfiguredCloudAdmissionSource(rig.Actors, rig.Configurations, rig.Secrets,
                rig.Privacy, rig.Conversations, (_, _, _, _) => Task.FromResult<TaskRunAttemptAdmission?>(rig.Issued), rig.Knowledge, permission);
            Task actual;
            if (frame)
            {
                var request = rig.Request();
                await source.CaptureOriginalAsync(rig.Snapshot, request, rig.Inventory(), default);
                actual = source.AcquireOriginalFrameAsync(rig.Issued, rig.Snapshot, request, rig.Route(request), default).AsTask();
            }
            else actual = source.AcquireOriginalAsync(rig.Owner, rig.Model, rig.Configurations.Row, rig.Candidate, default).AsTask();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            Assert.True(original.IsCanceled);
            Assert.True(actual.IsCanceled);
            Assert.Null(actual.Exception);
            Assert.Equal(1, permission.Calls);
            Assert.Equal(0, rig.Lease.Closes);
        });
    }

    private sealed class SuppliedPermission(Task<ITaskRunCloudUsePermissionLease> actual) : ITaskRunCloudUsePermissionSource
    {
        public int Calls;
        public ValueTask<ITaskRunCloudUsePermissionLease> AcquireOriginalAsync(TaskExecutionOwnerBinding owner,
            TaskRunRouteCandidate candidate, CancellationToken token) { Calls++; return new(actual); }
    }

    private static async Task RunAsync(Func<Rig, Task> body)
    {
        Rig? rig = null; Task? original = null; var errors = new List<Exception>();
        try { rig = await Rig.CreateAsync(); original = body(rig); await original; }
        catch (Exception error) { Add(errors, original, error); }
        finally
        {
            if (rig is not null)
            {
                Task? close = null;
                try { close = rig.Lease.DisposeAsync().AsTask(); await close; }
                catch (Exception error) { Add(errors, close, error); }
            }
        }
        Throw(errors);
    }
    private static void Throw(List<Exception> errors)
    {
        var actual = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (actual.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual[0]).Throw();
        if (actual.Length > 1) throw new AggregateException(actual);
    }
    private static void Add(List<Exception> errors, Task? original, Exception observed)
    {
        if (original?.Exception is { } compound) errors.AddRange(compound.InnerExceptions);
        else errors.Add(observed);
    }
    private static async Task ObserveCleanupAsync<T>(Task<T> original, Exception? expected, List<Exception> errors)
    {
        try
        {
            var acquired = await original;
            // If the expected refusal fails, still release the actual accidentally acquired original.
            if (acquired is IAsyncDisposable owner)
            {
                Task? close = null;
                try { close = owner.DisposeAsync().AsTask(); await close; }
                catch (Exception error) { Add(errors, close, error); }
            }
        }
        catch (Exception error)
        {
            IEnumerable<Exception> causes = original.Exception is { } payload ? payload.InnerExceptions : [error];
            foreach (var cause in causes) if (!ReferenceEquals(cause, expected)) errors.Add(cause);
        }
    }

    private sealed class Rig
    {
        public readonly Actors Actors = new();
        public readonly Configurations Configurations = new();
        public readonly Secrets Secrets = new();
        public readonly Privacy Privacy = new();
        public readonly Conversations Conversations = new();
        public readonly Knowledge Knowledge = new();
        public readonly PermissionDecisionEngine Policy = new();
        public readonly TaskRunConfiguredCloudAdmissionSource Source;
        public TaskExecutionSnapshot Snapshot { get; private set; }
        public TaskExecutionOwnerBinding Owner => Snapshot.OwnerBinding!;
        public TaskRunRouteCandidate Candidate { get; } = new("actual-fixture-route", 1, "synthetic-provider", "synthetic-model", null, true, ["Text"]);
        public ProviderModelDescriptor Model { get; } = new("synthetic-provider", false, new ModelDescriptor("synthetic-model", 1,
            "synthetic", "synthetic", "synthetic", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));
        public Conversation Conversation;
        public OriginalLease Lease { get; private set; } = null!;
        public TaskRunAttemptAdmission Issued { get; private set; } = null!;
        private Rig()
        {
            var task = Guid.NewGuid(); var context = Guid.NewGuid(); var run = Guid.NewGuid();
            var actor = Actors.Actor!;
            var binding = new TaskExecutionOwnerBinding(task, context, run, actor.ActorId, actor.ProfileId,
                null, null, actor.AuthenticationRevision, "synthetic-original-owner");
            Snapshot = new(task, context, run, "Synthetic actual context", TaskExecutionLifecycle.Running,
                TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            { PersistenceRevision = 1, OwnerBinding = binding };
            Conversation = new(context, HavenMode.Chat, ConversationKind.Chat, "Actual fixture context", null, null,
                false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            Conversations.Row = Conversation;
            Source = new(Actors, Configurations, Secrets, Privacy, Conversations,
                (t, r, a, ct) => Task.FromResult<TaskRunAttemptAdmission?>(Issued is { } issued && t == task && r == run && a == issued.AttemptId ? issued : null), Knowledge,
                new TaskRunCentralCloudUsePermissionSource(Actors, Policy));
        }
        public static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            rig.Policy.Grant(TaskRunCentralCloudUsePermissionSource.ScopeFor(rig.Owner, rig.Candidate)); // Synthetic owning approval, never a production bootstrap.
            var cloud = await rig.Source.AcquireOriginalAsync(rig.Owner, rig.Model, rig.Configurations.Row, rig.Candidate, default)
                ?? throw new InvalidOperationException("Synthetic configured source unexpectedly refused.");
            var id = Guid.NewGuid();
            rig.Lease = new(rig.Owner, id, rig.Candidate, cloud);
            rig.Snapshot = rig.Snapshot with { Attempts = [new(id, rig.Candidate, TaskRunAttemptState.Running,
                "synthetic-original-attempt", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)] };
            rig.Issued = new(rig.Snapshot, id, rig.Lease);
            return rig;
        }
        public ProviderExecutionContext Context() => new(Snapshot.TaskId, Snapshot.ContextId, Snapshot.ExecutionId,
            Issued.AttemptId, Snapshot.PersistenceRevision);
        public OllamaChatRequest Request(string prompt = "actual selected prompt") =>
            new("synthetic-provider:synthetic-model", [new("user", prompt)], EffortLevel.Low) { ExecutionContext = Context() };
        public OllamaChatRequest Route(OllamaChatRequest original) => original with { Model = "synthetic-model", ExecutionContext = Context() };
        public TaskRunContextInventory Inventory() => new(Conversation, [], [], [], null, null, null, null, null);
        public KnowledgeRecord KnowledgeRecord(KnowledgeCategory category) => new(Guid.NewGuid(), category, "synthetic",
            "actual source title", "actual source summary", KnowledgePrivacyClass.Normal, 1, false,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "synthetic source", []);
    }

    private sealed class OriginalLease(TaskExecutionOwnerBinding owner, Guid attempt,
        TaskRunRouteCandidate candidate, ITaskRunCloudAdmissionLease original) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => attempt;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic-process-local-original";
        public int Closes;
        public ValueTask RevalidateAsync(CancellationToken token) => original.RevalidateAsync(token);
        public ValueTask DisposeAsync() { Closes++; return original.DisposeAsync(); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Actor = new("synthetic-task-actor", "synthetic-task-profile", null, null, "one");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Actor); }
    }
    private sealed class Configurations : IProviderConfigurationStore
    {
        public ProviderConfiguration Row = new("synthetic-provider", ModelProviderKind.OpenAI, "Synthetic", "https://example.invalid/",
            true, false, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult<ProviderConfiguration?>(id == Row.Id ? Row : null);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Row]);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) { Row = value; return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken token) { Row = Row with { IsEnabled = false }; return Task.CompletedTask; }
    }
    private sealed class Secrets : IProviderSecretStore
    {
        public string? Value = "synthetic-not-an-actual-credential";
        public Func<Task>? BeforeRead = null;
        public int Reads;
        public async Task<string?> GetAsync(string provider, string name, CancellationToken token)
        { Reads++; if (BeforeRead is { } held) await held(); token.ThrowIfCancellationRequested(); return Value; }
        public Task SetAsync(string provider, string name, string value, CancellationToken token) { Value = value; return Task.CompletedTask; }
        public Task DeleteAsync(string provider, string name, CancellationToken token) { Value = null; return Task.CompletedTask; }
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
    }
    private sealed class Conversations : IConversationRepository
    {
        public Conversation? Row;
        public int Reads; public int Writes;
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) { Reads++; return Task.FromResult(Row?.Id == id ? Row : null); }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>(Row is { } row ? [row] : []);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) { Writes++; Row = conversation; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { Writes++; return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException("This fixture does not delete conversations.");
    }
    private sealed class Knowledge : IKnowledgeLibrary
    {
        public KnowledgeRecord? Row;
        public Func<Task>? BeforeRead = null;
        public async Task<KnowledgeRecord?> GetAsync(Guid id, CancellationToken token)
        { if (BeforeRead is { } held) await held(); token.ThrowIfCancellationRequested(); return Row?.Id == id ? Row : null; }
        public Task<IReadOnlyList<KnowledgeRecord>> SearchMetadataAsync(string? query, KnowledgeCategory? category, CancellationToken token) => Task.FromResult<IReadOnlyList<KnowledgeRecord>>(Row is { } row ? [row] : []);
        public Task<KnowledgeRecord> UpsertAsync(KnowledgeRecord record, string indexedText, CancellationToken token) { Row = record; return Task.FromResult(record); }
        public Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken token) => Task.FromResult(false);
        public Task<KnowledgeRecord> CorrectAsync(Guid id, string correctedSummary, string? reason, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> RejectAsync(Guid id, string? reason, CancellationToken token) => Task.FromResult(false);
        public Task<bool> ForgetAsync(Guid id, CancellationToken token, bool preserveRejection = false) { Row = null; return Task.FromResult(true); }
        public Task<int> ForgetCategoryAsync(KnowledgeCategory category, CancellationToken token) => Task.FromResult(0);
    }
}
