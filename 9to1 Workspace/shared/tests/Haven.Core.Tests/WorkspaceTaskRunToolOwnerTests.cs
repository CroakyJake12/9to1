using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Real coordinator, issuer, central permission engine, runtime and frame owner controls.
/// Actor/provider/repository/physical receipt producer are explicitly synthetic fixture owners;
/// actual temporary-file reads/moves and original Tasks are exercised, no production grant issued.</summary>
public sealed class WorkspaceTaskRunToolOwnerTests
{
    [Fact]
    public async Task Held_original_read_and_duplicate_runtime_entry_share_the_actual_task_then_record_readonly_without_acceptance()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
        File.WriteAllText(Path.Combine(rig.Root, "document.txt"), "actual before");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); rig.Service.HoldRead = release;
        var call = Call("read_file", new { path = "document.txt" }); var prep = await rig.PrepareAsync(call);
        Task<WorkspaceToolResult>? first = null; Task<WorkspaceToolResult>? duplicate = null;
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct =>
        {
            var actual = rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct); first = actual;
            duplicate = rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct);
            captured.TrySetResult(); // Both SAME returned task references are published before observer assertions.
            return actual;
        }, default));
        try
        {
            await Task.WhenAny(captured.Task, run);
            if (!captured.Task.IsCompleted) await run; // Preserve an actual pre-callback failure; never wait on an unissued capture.
            await captured.Task;
            await rig.Service.ReadEntered.Task;
            Assert.Same(first, duplicate); Assert.False(run.IsCompleted); Assert.Equal(1, rig.Service.Reads);
            Assert.Same(run, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new InvalidOperationException("Must not redispatch"), default));
            release.TrySetResult();
            var original = await run;
            Assert.Equal("actual before", original.OriginalResult.Output); Assert.True(original.ReadOnlyObservationComplete);
            Assert.Null(original.OwnerReceiptReference);
            var acknowledged = await rig.Coordinator.RecordObservedActionOutcomeAsync(prep, original, default);
            Assert.Equal(TaskPlanNodeState.Completed, acknowledged.Plan.Single().State);
            Assert.Null(acknowledged.Plan.Single().Acceptance); Assert.Null(acknowledged.LastCheckpointActionId);
            await rig.Owner.RetireAcknowledgedOriginalAsync(prep, acknowledged, default);
        }
        finally { release.TrySetResult(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forged_or_copied_result_cannot_complete_a_real_registered_action(bool copyActual)
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
        File.WriteAllText(Path.Combine(rig.Root, "document.txt"), "actual source");
        var call = Call("read_file", new { path = "document.txt" }); var prep = await rig.PrepareAsync(call);
        var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, async ct => copyActual
            ? (await rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct)) with { Output = "fabricated copied output" }
            : new WorkspaceToolResult(new(Guid.NewGuid(), "Fake", "Fake", true, TimeSpan.Zero, DateTimeOffset.UnixEpoch), "fabricated")
                { OriginalEffectBodyCompleted = true }, default));
        var error = await Record.ExceptionAsync(() => run); Assert.NotNull(error); rig.Expect(error!);
        Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
        Assert.Same(run, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new Exception("Must not rerun"), default));
        var retained = await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default);
        Assert.Equal(TaskPlanNodeState.Running, retained!.Plan.Single().State); Assert.Null(retained.Plan.Single().Acceptance);
        Assert.Equal(copyActual ? 1 : 0, rig.Service.Reads);
        });
    }

    [Fact]
    public async Task Caller_argument_mutation_does_not_rewrite_the_detached_registered_intent()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
        File.WriteAllText(Path.Combine(rig.Root, "document.txt"), "before");
        var args = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("document.txt") };
        var call = new OllamaToolCall("read_file", args); var prep = await rig.PrepareAsync(call);
        var captured = prep.OriginalToolIntent;
        args["path"] = JsonSerializer.SerializeToElement("different.txt");
        var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct => rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct), default));
        var error = await Record.ExceptionAsync(() => run); Assert.NotNull(error); rig.Expect(error!);
        Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException); Assert.Equal(0, rig.Service.Reads);
        Assert.Equal(captured, (await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default))!.Plan.Single().OriginalToolIntent);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_move_is_acknowledged_separately_from_all_compound_history_faults_and_never_replayed(bool firstIsCancellation)
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
        Exception first = firstIsCancellation ? new OperationCanceledException("Actual FAULTED history OCE") : new IOException("Actual original history one");
        var second = new IOException("Actual original history two");
        var actualHistory = Task.WhenAll(Task.FromException(first), Task.FromException(second));
        Assert.True(actualHistory.IsFaulted); // OCE is a direct fault here, not actual Task cancellation.
        var history = new History(actualHistory);
        var runtime = new WorkspaceToolRuntime(rig.Service, history);
        rig.Policy.Grant("capability:write-file");
        var call = Call("write_file", new { path = "created.txt", content = "actual native after" }); var prep = await rig.PrepareAsync(call);
        var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct => runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct), default));
        var observed = await run;
        Assert.False(observed.OriginalResult.Activity.Succeeded); Assert.True(observed.OriginalResult.OriginalEffectBodyCompleted);
        Assert.NotNull(observed.OriginalResult.OriginalRuntimeError); Assert.NotNull(observed.OwnerReceiptReference);
        Assert.Contains(Leaves(observed.OriginalResult.OriginalRuntimeError!), value => ReferenceEquals(value, first));
        Assert.Contains(Leaves(observed.OriginalResult.OriginalRuntimeError!), value => ReferenceEquals(value, second));
        Assert.Equal("actual native after", File.ReadAllText(Path.Combine(rig.Root, "created.txt"))); Assert.Equal(1, rig.Service.Moves);
        var ack = await rig.Coordinator.AcceptActionAsync(rig.Admission.Snapshot.TaskId, rig.Admission.Snapshot.ExecutionId,
            rig.Admission.AttemptId, prep.ActionId, observed.OwnerReceiptReference!, default);
        Assert.Equal(observed.OwnerReceiptReference, ack.Plan.Single().Acceptance!.OwnerReceiptReference);
        Assert.Same(run, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new Exception("Known move cannot replay"), default));
        Assert.Equal(1, rig.Service.Moves);
        rig.Expect(first); rig.Expect(second);
        var close = rig.Frames.CloseAndDrainAsync();
        var closeError = await Record.ExceptionAsync(() => close); Assert.NotNull(closeError); rig.Expect(closeError!);
        Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(closeError!), value => ReferenceEquals(value, second));
        });
    }

    [Fact]
    public async Task Lost_acceptance_CAS_reply_keeps_known_native_receipt_and_same_execution_without_replay()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        { rig.Policy.Grant("capability:write-file");
        var call = Call("write_file", new { path = "document.txt", content = "after" }); var prep = await rig.PrepareAsync(call);
        var original = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct => rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct), default));
        var result = await original; var failure = new IOException("Actual storage ACK reply lost after durable write");
        rig.Repository.ThrowAfterNextWrite = failure;
        var error = await Assert.ThrowsAsync<IOException>(() => rig.Coordinator.AcceptActionAsync(rig.Admission.Snapshot.TaskId,
            rig.Admission.Snapshot.ExecutionId, rig.Admission.AttemptId, prep.ActionId, result.OwnerReceiptReference!, default));
        Assert.Same(failure, error); Assert.Equal(1, rig.Service.Moves);
        Assert.Same(original, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new Exception("Unknown ACK cannot replay"), default));
        var acknowledged = await rig.Coordinator.AcceptActionAsync(rig.Admission.Snapshot.TaskId, rig.Admission.Snapshot.ExecutionId,
            rig.Admission.AttemptId, prep.ActionId, result.OwnerReceiptReference!, default); // Audit-only reread of durable SAME receipt.
        Assert.Equal(result.OwnerReceiptReference, acknowledged.Plan.Single().Acceptance!.OwnerReceiptReference);
        Assert.Equal(1, rig.Service.Moves); await rig.Owner.RetireAcknowledgedOriginalAsync(prep, acknowledged, default);
        });
    }

    [Fact]
    public async Task Canonical_availability_is_deny_only_and_does_not_claim_other_runtime_owners()
    {
        await RunOriginalOwnerControlAsync(rig =>
        {
            Assert.True(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "read_file"));
            Assert.True(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "write_file"));
            Assert.False(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "unknown_tool"));
            Assert.False(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "list_files"));
            Assert.False(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "search_files"));
            foreach (var runtime in Enum.GetValues<ToolRuntimeKind>().Where(value => value != ToolRuntimeKind.Workspace))
                Assert.False(rig.Owner.SupportsCanonicalInvocation(runtime, "read_file"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Actual_physical_cleanup_fault_after_a_successful_read_cannot_complete_the_observation()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
            File.WriteAllText(Path.Combine(rig.Root, "document.txt"), "actual readable source");
            var cleanup = new IOException("Actual retained physical-owner cleanup task failed");
            rig.Service.OriginalCleanup = Task.FromException(cleanup); rig.Expect(cleanup);
            var call = Call("read_file", new { path = "document.txt" }); var prep = await rig.PrepareAsync(call);
            var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep,
                ct => rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct), default));
            var error = await Record.ExceptionAsync(() => run); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, cleanup));
            Assert.Equal(1, rig.Service.Reads); Assert.True(rig.Service.OriginalCleanup.IsFaulted);
            var current = await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default);
            Assert.Equal(TaskPlanNodeState.Running, current!.Plan.Single().State);
            Assert.Null(current.Plan.Single().Acceptance);
            Assert.Same(run, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new Exception("No read replay"), default));
        });
    }

    private static async Task RunOriginalOwnerControlAsync(Func<RuntimeRig, Task> control)
    {
        RuntimeRig? rig = null; Task? original = null; Task? close = null; var errors = new List<Exception>();
        try { rig = await RuntimeRig.CreateAsync(); original = control(rig); await original; }
        catch (Exception error) { errors.Add((Exception?)original?.Exception ?? error); }
        finally
        {
            if (rig is not null)
            {
                try { close = rig.DisposeAsync().AsTask(); await close; }
                catch (Exception error) { var actual = (Exception?)close?.Exception ?? error; if (!errors.Any(value => ReferenceEquals(value, actual))) errors.Add(actual); }
            }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original control and independent fixture cleanup failed.", errors);
    }

    private static OllamaToolCall Call(string name, object args) => new(name,
        JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(value => value.Name, value => value.Value.Clone()));
    private static IEnumerable<Exception> Leaves(Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
            foreach (var child in group.InnerExceptions) foreach (var leaf in Leaves(child)) yield return leaf;
        else yield return error;
    }
    private sealed class RuntimeRig : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "haven-task-owner-control-" + Guid.NewGuid().ToString("N"));
        public Repository Repository { get; } = new(); public PermissionDecisionEngine Policy { get; } = new();
        public ModelInputs Models { get; }
        private TaskExecutionCoordinator? _coordinator;
        public TaskExecutionCoordinator Coordinator => _coordinator ?? throw new InvalidOperationException("Actual coordinator has not been constructed.");
        public TaskRunOriginalFrameOwner Frames { get; }
        public WorkspaceTaskRunToolActionOwner Owner { get; }
        public PhysicalControlSource Service { get; }
        public WorkspaceToolRuntime Runtime { get; }
        public TaskRunAttemptAdmission Admission { get; private set; } = null!;
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        private readonly List<Task> _originals = [];
        public Task<T> Track<T>(Task<T> original) { _originals.Add(original); return original; }
        private RuntimeRig()
        {
            var receipts = new WorkspaceTaskRunReceiptAuthority(); Models = new(receipts);
            var effects = new WorkspaceTaskRunEffectAuthority(Models.Authority, Policy, Policy);
            Service = new(effects); Runtime = new(Service);
            Frames = new((task, run, attempt, ct) => Coordinator.TryGetIssuedAttemptAsync(task, run, attempt, ct));
            Owner = new(() => Coordinator, Frames, Service, new(new Capabilities()), effects, CapabilityPlatform.Windows, receipts);
            _coordinator = new(Repository, new Events(), admissionAuthority: Models.Authority, runtimeSettlement: Frames, toolActionOwner: Owner);
            Directory.CreateDirectory(Root);
        }
        public static async Task<RuntimeRig> CreateAsync()
        {
            var rig = new RuntimeRig();
            try
            {
                var task = await rig.Coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "Synthetic fixture task",
                    TaskExecutionDurability.PersistedPlan, [], default);
                var route = await rig.Models.CaptureAsync(task, rig.Models.Local.Model);
                rig.Admission = await rig.Coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId, route, default);
                await rig.Frames.RegisterOriginalAttemptAsync(rig.Admission, default);
                return rig;
            }
            catch (Exception original)
            {
                Task? close = null;
                try { close = rig.DisposeAsync().AsTask(); await close; }
                catch (Exception cleanup) { throw new AggregateException("Actual fixture acquisition and independent retirement failed.", original, (Exception?)close?.Exception ?? cleanup); }
                throw;
            }
        }
        public async Task<ITaskRunToolActionPreparation> PrepareAsync(OllamaToolCall call)
        {
            var current = (await Coordinator.GetAsync(Admission.Snapshot.TaskId, default))!;
            var prepared = await Owner.PrepareOriginalAsync(Admission, current, Guid.NewGuid(), call,
                ToolRuntimeKind.Workspace, PermissionMode.Ask, Root, default);
            await Coordinator.RegisterOriginalToolActionAsync(prepared, null, "Actual exact fixture intent", default);
            return prepared;
        }
        public void Expect(Exception original) { foreach (var leaf in Leaves(original)) _expected.Add(leaf); }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            foreach (var original in _originals)
            {
                try { await original; }
                catch (Exception error)
                {
                    var actual = (Exception?)original.Exception ?? error;
                    if (Leaves(actual).Any(leaf => !_expected.Contains(leaf))) errors.Add(actual);
                }
            }
            Task? close = null;
            try { close = Frames.CloseAndDrainAsync(); await close; }
            catch (Exception error)
            {
                var actual = (Exception?)close?.Exception ?? error;
                if (Leaves(actual).Any(leaf => !_expected.Contains(leaf))) errors.Add(actual);
            }
            try { Directory.Delete(Root, true); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count > 1) throw new AggregateException("Independent original fixture drains and directory cleanup failed.", errors);
        }
    }
    private sealed class Events : IExecutionEventSink { public bool TryPublish(ExecutionEvent original) => true; }
    private sealed class Capabilities : ICapabilityRepository
    {
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken token) => Task.FromResult(CapabilityRegistryCatalog.BuiltIns);
        public Task UpsertCapabilityAsync(CapabilityDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Repository : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _rows = [];
        public Exception? ThrowAfterNextWrite;
        public Task UpsertAsync(TaskExecutionSnapshot next, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var before = _rows.TryGetValue(next.TaskId, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null;
            if (next.PersistenceRevision != (before?.PersistenceRevision ?? 0) + 1)
                throw new TaskExecutionRevisionConflictException(next.TaskId, next.PersistenceRevision - 1, before?.PersistenceRevision ?? 0);
            _rows[next.TaskId] = JsonSerializer.Serialize(next);
            if (ThrowAfterNextWrite is { } failure) { ThrowAfterNextWrite = null; return Task.FromException(failure); }
            return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) =>
            Task.FromResult(_rows.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null);
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) =>
            (await GetResumableAsync(token)).FirstOrDefault(value => value.ContextId == id);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(json => JsonSerializer.Deserialize<TaskExecutionSnapshot>(json)!).ToArray());
    }
    private sealed class PhysicalControlSource(IWorkspaceToolFinalFenceAuthority issuer) : IWorkspaceOriginalInvocationSource
    {
        private readonly HashSet<Invocation> _issued = [];
        public int Reads; public int Moves;
        public Task OriginalCleanup = Task.CompletedTask;
        public TaskCompletionSource? HoldRead;
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ResolveWorkspacePath(string root, string path)
        {
            var actual = Path.GetFullPath(Path.Combine(root, path));
            if (!actual.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
            return actual;
        }
        public async Task<string> ReadTextAsync(string root, string path, CancellationToken token)
        {
            Reads++; ReadEntered.TrySetResult(); if (HoldRead is { } held) await held.Task;
            return await File.ReadAllTextAsync(ResolveWorkspacePath(root, path), token);
        }
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => throw new UnauthorizedAccessException("Synthetic source only writes inside its issued original wrapper.");
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw new NotSupportedException();
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw new NotSupportedException();
        public IWorkspaceOriginalInvocation AcquireOriginalInvocation(IWorkspaceToolFinalFence fence)
        {
            if (!issuer.IsIssuedOriginal(fence)) throw new UnauthorizedAccessException();
            var original = new Invocation(this, fence); _issued.Add(original); return original;
        }
        public bool IsIssuedOriginal(IWorkspaceOriginalInvocation original) => original is Invocation own && _issued.Contains(own);
        public bool ValidateOriginalOutcome(IWorkspaceOriginalInvocation original, WorkspaceToolPhysicalOutcome outcome) =>
            original is Invocation own && _issued.Contains(own) && ReferenceEquals(own.Outcome, outcome);
        private sealed class Invocation(PhysicalControlSource owner, IWorkspaceToolFinalFence fence) : IWorkspaceOriginalInvocation, IWorkspaceToolService
        {
            public IWorkspaceToolService Tools => this;
            public WorkspaceToolPhysicalOutcome? Outcome;
            private WorkspaceToolPhysicalEffect? _actualMove;
            private Task<WorkspaceToolPhysicalOutcome>? _complete; private Task? _close;
            private readonly List<Exception> _cleanupErrors = [];
            public string ResolveWorkspacePath(string root, string path) => owner.ResolveWorkspacePath(root, path);
            public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => owner.ReadTextAsync(root, path, token);
            public async Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token)
            {
                if (_actualMove is not null || fence.OriginalCall.Name != "write_file" ||
                    fence.OriginalCall.Arguments["path"].GetString() != path || fence.OriginalCall.Arguments["content"].GetString() != content)
                    throw new UnauthorizedAccessException("Actual synthetic fixture call/preimage mismatch.");
                var target = ResolveWorkspacePath(root, path); var temporary = target + ".fixture-staged";
                await File.WriteAllTextAsync(temporary, content, token);
                try
                {
                    await using var pin = await fence.AcquireOriginalCommitPinAsync(token) ?? throw new UnauthorizedAccessException();
                    token.ThrowIfCancellationRequested();
                    var sha = WorkspaceToolOriginalDigest.Text(content);
                    fence.RunOriginalEffect(root, WorkspaceToolEffectKind.AtomicWrite, target, sha,
                        () => { File.Move(temporary, target, true); owner.Moves++; return 1; });
                    _actualMove = new(WorkspaceToolEffectKind.AtomicWrite, target, sha, true, true, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw new NotSupportedException();
            public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task<WorkspaceToolPhysicalOutcome> CompleteOriginalAsync(bool bodySucceeded, CancellationToken token) =>
                _complete ??= CompleteCoreAsync(bodySucceeded);
            private async Task<WorkspaceToolPhysicalOutcome> CompleteCoreAsync(bool bodySucceeded)
            {
                await CloseAndDrainAsync();
                var actual = new WorkspaceToolPhysicalOutcome(bodySucceeded && _actualMove is not null && _cleanupErrors.Count == 0 ? "synthetic-actual-move:" + Guid.NewGuid().ToString("N") : null,
                    _actualMove is { } moved ? new[] { moved } : [], _cleanupErrors.ToArray(), _actualMove is null, !bodySucceeded && _actualMove is not null);
                Outcome = actual;
                return actual;
            }
            public Task CloseAndDrainAsync() => _close ??= CloseCoreAsync();
            private async Task CloseCoreAsync()
            {
                try { await owner.OriginalCleanup; }
                catch (Exception error)
                {
                    var original = owner.OriginalCleanup.Exception;
                    if (original is null) _cleanupErrors.Add(error); else _cleanupErrors.AddRange(original.InnerExceptions);
                }
            }
            public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
        }
    }
    private sealed class History(Task original) : IWorkspaceStateRepository
    {
        public Task AddVersionAsync(WorkspaceVersion version, CancellationToken token) => original;
        public Task<IReadOnlyList<ReusableTaskDefinition>> GetReusableTasksAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertReusableTaskAsync(ReusableTaskDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteReusableTaskAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? id, string? path, int count, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertDecisionAsync(DecisionRecord value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteDecisionAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ModelInputs
    {
        public Actors Actors { get; } = new(); public Provider Local { get; } = new("ollama", true);
        public Provider Remote { get; } = new("synthetic-remote", false);
        public Configurations Configurations { get; } = new(); public Privacy Privacy { get; } = new();
        public Permissions Permissions { get; } = new(); public Cloud Cloud { get; } = new();
        public TaskRunPermissionAuthority Authority { get; }
        public ModelInputs(ITaskRunActionReceiptAuthority receipts)
        {
            foreach (var provider in new[] { Local, Remote }) Configurations.Rows[provider.Id] = new(provider.Id,
                provider.Kind, provider.DisplayName, provider.IsLocal ? "http://127.0.0.1:11434" : "https://synthetic.invalid",
                true, provider.IsLocal, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
            Authority = new(Actors, new Registry(Local, Remote), Configurations, Privacy, new(Permissions),
                cloud: null, receipts: receipts);
        }
        public async Task<TaskExecutionSnapshot> StartAsync()
        {
            var task = new TaskExecutionSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Synthetic task",
                TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 1, [], [], [],
                ["client-claimed-all-scopes"], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            return task with { OwnerBinding = await Authority.AuthorizeStartAsync(task, default) };
        }
        public Task<TaskRunRouteCandidate> CaptureAsync(TaskExecutionSnapshot task, ProviderModelDescriptor model) =>
            Authority.CaptureSelectedRouteAsync(task, model, [ToolCapability.Text], []);
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Actor = new("synthetic-product-owner", "synthetic-profile", null, null, "revision-one");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Actor); }
    }
    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public string Id => id; public string DisplayName => id; public bool IsLocal => local; public bool CanManageModels => false;
        public ModelProviderKind Kind => local ? ModelProviderKind.Ollama : ModelProviderKind.OpenAI;
        public ProviderModelDescriptor Model = new(id, local, new ModelDescriptor("synthetic-model", 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Func<Task>? HeldRead = null;
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { if (HeldRead is { } read) await read(); token.ThrowIfCancellationRequested(); return [Model]; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Registry(params IModelProvider[] providers) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => providers;
        public IModelProvider? Find(string id) => providers.SingleOrDefault(provider => provider.Id == id);
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { var result = new List<ProviderModelDescriptor>(); foreach (var provider in providers) result.AddRange(await provider.GetModelsAsync(token)); return result; }
    }
    private sealed class Configurations : IProviderConfigurationStore
    {
        public readonly Dictionary<string, ProviderConfiguration> Rows = new(StringComparer.Ordinal);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(Rows.Values.ToArray());
        public Task UpsertAsync(ProviderConfiguration config, CancellationToken token) { Rows[config.Id] = config; return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken token) { Rows.Remove(id); return Task.CompletedTask; }
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
    }
    private sealed class Permissions : IModelPermissionStore
    {
        public ModelPermissionPolicy Policy = ModelPermissionPolicy.Empty;
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(Policy);
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) { Policy = value; return Task.CompletedTask; }
    }
    private sealed class Cloud : ITaskRunCloudAdmissionSource
    {
        public readonly CloudLease Lease = new(); public int Acquisitions;
        public ValueTask<ITaskRunCloudAdmissionLease?> AcquireOriginalAsync(TaskExecutionOwnerBinding owner,
            ProviderModelDescriptor model, ProviderConfiguration configuration, TaskRunRouteCandidate candidate, CancellationToken token)
        { Acquisitions++; return ValueTask.FromResult<ITaskRunCloudAdmissionLease?>(Lease); }
    }
    private sealed class CloudLease : ITaskRunCloudAdmissionLease
    {
        public int Closes; public Task OriginalClose = Task.CompletedTask;
        public ValueTask RevalidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Closes++; return new(OriginalClose); }
    }
    private sealed class Routes : ITaskRunRouteObservationSource
    {
        public long Revision = 12;
        public ValueTask<TaskRunRouteCandidate?> ObserveOriginalAsync(TaskExecutionSnapshot task,
            ProviderModelDescriptor model, IReadOnlyList<string> required, CancellationToken token) =>
            ValueTask.FromResult<TaskRunRouteCandidate?>(new("real-configured-route", Revision, model.ProviderId, model.Name, null, !model.IsLocal, required));
        public ValueTask DemandOriginalCurrentAsync(TaskExecutionSnapshot task, TaskRunRouteCandidate original, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (original.RouteRevision != Revision) throw new UnauthorizedAccessException("synthetic route changed"); return ValueTask.CompletedTask; }
    }
}
