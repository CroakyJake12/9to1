using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Core.Tests;

/// <summary>Actual canonical coordinator, admission authority, central policy, action/frame owners,
/// runtime and retained-handle WorkspaceToolService. Actor/model/repository inputs are controlled
/// fixture owners; these tests issue no installed Home authority or production grant.</summary>
public sealed class WorkspaceTaskRunTraversalTests
{
    [TraversalTheory]
    [InlineData("list_files")]
    [InlineData("search_files")]
    public async Task Real_traversal_keeps_the_same_task_run_action_and_coalesces_without_mutation_acceptance(string name)
    {
        await RunControlAsync(async rig =>
        {
            Directory.CreateDirectory(Path.Combine(rig.Root, "src"));
            var file = Path.Combine(rig.Root, "src", "code.cs"); File.WriteAllText(file, "first\nneedle actual source\n");
            var before = File.ReadAllBytes(file);
            var call = name == "list_files" ? Call(name, new { path = ".", max_depth = 3 })
                : Call(name, new { path = ".", query = "needle", max_results = 10 });
            Assert.True(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, name));
            var prep = await rig.PrepareAsync(call);
            Assert.Same(rig.Admission, prep.OriginalAttempt);
            var originalPlan = (await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default))!;
            Assert.Equal(prep.ActionId, originalPlan.Plan.Single().ActionId);
            Task<WorkspaceToolResult>? first = null; Task<WorkspaceToolResult>? duplicate = null;
            var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, token =>
            {
                var original = rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, token); first = original;
                duplicate = rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, token);
                captured.TrySetResult(); return original;
            }, default));
            await Task.WhenAny(captured.Task, run); if (!captured.Task.IsCompleted) await run; await captured.Task;
            Assert.Same(first, duplicate);
            Assert.Same(run, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new InvalidOperationException("Same action must not redispatch traversal."), default));
            var observed = await run;
            Assert.True(observed.OriginalResult.Activity.Succeeded); Assert.True(observed.ReadOnlyObservationComplete);
            Assert.True(observed.KnownNoEffect); Assert.Null(observed.OwnerReceiptReference);
            Assert.Contains("src", observed.OriginalResult.Output, StringComparison.Ordinal);
            Assert.Contains("code.cs", observed.OriginalResult.Output, StringComparison.Ordinal);
            Assert.Contains("read coverage:", observed.OriginalResult.Output, StringComparison.Ordinal);
            if (name == "search_files") Assert.Contains("needle actual source", observed.OriginalResult.Output, StringComparison.Ordinal);
            var workspace = Assert.IsAssignableFrom<IWorkspaceToolActionPreparation>(prep);
            var physical = await workspace.OriginalInvocation!.CompleteOriginalAsync(true, default);
            Assert.True(rig.Physical.ValidateOriginalOutcome(workspace.OriginalInvocation, physical));
            Assert.Empty(physical.Effects); Assert.Empty(physical.OriginalErrors); Assert.Null(physical.OriginalReceiptReference);
            var acknowledged = await rig.Coordinator.RecordObservedActionOutcomeAsync(prep, observed, default);
            Assert.Equal(originalPlan.TaskId, acknowledged.TaskId); Assert.Equal(originalPlan.ContextId, acknowledged.ContextId);
            Assert.Equal(originalPlan.ExecutionId, acknowledged.ExecutionId);
            Assert.Equal(prep.ActionId, acknowledged.Plan.Single().ActionId); Assert.Equal(TaskPlanNodeState.Completed, acknowledged.Plan.Single().State);
            Assert.Null(acknowledged.Plan.Single().Acceptance); Assert.Null(acknowledged.LastCheckpointActionId);
            Assert.Equal(before, File.ReadAllBytes(file));
            await rig.Owner.RetireAcknowledgedOriginalAsync(prep, acknowledged, default);
        });
    }

    [TraversalFact]
    public async Task Fresh_actor_revocation_after_registered_intent_refuses_the_actual_physical_read_without_acceptance()
    {
        await RunControlAsync(async rig =>
        {
            var file = Path.Combine(rig.Root, "source.txt"); File.WriteAllText(file, "needle actual source");
            var call = Call("search_files", new { path = ".", query = "needle", max_results = 10 });
            var prep = await rig.PrepareAsync(call);
            var roots = CaptureActualNativeRoots(prep); rig.Models.Actors.Actor = null;
            var actual = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, token => rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, token), default));
            var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
            AssertAutomaticPreBodyClose(prep, roots, rig.Root);
            Assert.Same(actual, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new InvalidOperationException("Revoked action must not redispatch."), default));
            var retained = await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default);
            Assert.Equal(rig.Admission.Snapshot.ExecutionId, retained!.ExecutionId);
            Assert.Equal(TaskPlanNodeState.Running, retained.Plan.Single().State); Assert.Null(retained.Plan.Single().Acceptance);
            Assert.Null(retained.LastCheckpointActionId); Assert.Equal("needle actual source", File.ReadAllText(file));
        });
    }

    [TraversalFact]
    public async Task Held_actual_provider_validation_and_faulted_oce_siblings_cannot_become_an_accepted_read()
    {
        await RunControlAsync(async rig =>
        {
            File.WriteAllText(Path.Combine(rig.Root, "source.txt"), "actual source");
            var call = Call("list_files", new { path = ".", max_depth = 1 }); var prep = await rig.PrepareAsync(call);
            var roots = CaptureActualNativeRoots(prep);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var raw = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = new OperationCanceledException("FAULTED original provider validation"); var sibling = new IOException("Original provider validation sibling");
            rig.Models.Local.HeldRead = () => { entered.TrySetResult(); return raw.Task; };
            var dispatched = false;
            var actual = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, token =>
            { dispatched = true; return rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, token); }, default));
            try
            {
                await Task.WhenAny(entered.Task, actual); if (!entered.Task.IsCompleted) await actual; await entered.Task;
                Assert.False(actual.IsCompleted); Assert.False(dispatched);
                Assert.Same(actual, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new InvalidOperationException("Held action must coalesce."), default));
            }
            finally { raw.TrySetException([first, sibling]); }
            var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
            Assert.True(raw.Task.IsFaulted); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(error!), value => ReferenceEquals(value, sibling));
            AssertAutomaticPreBodyClose(prep, roots, rig.Root);
            var retained = await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default);
            Assert.Equal(TaskPlanNodeState.Running, retained!.Plan.Single().State); Assert.Null(retained.Plan.Single().Acceptance);
            Assert.False(dispatched); Assert.Equal("actual source", File.ReadAllText(Path.Combine(rig.Root, "source.txt")));
        });
    }

    [TraversalFact]
    public async Task Actually_canceled_prebody_frame_releases_the_actual_native_root_and_retains_its_original_cause()
    {
        await RunControlAsync(async rig =>
        {
            File.WriteAllText(Path.Combine(rig.Root, "source.txt"), "actual source");
            var call = Call("list_files", new { path = ".", max_depth = 1 });
            var prep = await rig.PrepareAsync(call); var roots = CaptureActualNativeRoots(prep);
            var token = new CancellationToken(true);
            var raw = Task.FromCanceled<IReadOnlyList<ProviderModelDescriptor>>(token);
            rig.Models.Local.HeldRead = () => raw;
            var dispatched = false;
            var actual = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct =>
            { dispatched = true; return rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct); }, default));
            var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
            Assert.True(raw.IsCanceled); Assert.True(actual.IsCanceled); Assert.False(actual.IsFaulted); Assert.False(dispatched);
            var frame = Assert.IsAssignableFrom<Task>(Field(prep, "OriginalToolFrame"));
            Assert.True(frame.IsCanceled); Assert.False(frame.IsFaulted);
            var originalCause = await Record.ExceptionAsync(() => frame); Assert.NotNull(originalCause); rig.Expect(originalCause!);
            var retainedErrors = Assert.IsAssignableFrom<IEnumerable<Exception>>(Field(prep, "Errors"));
            Assert.Contains(retainedErrors, value => ReferenceEquals(value, originalCause));
            AssertAutomaticPreBodyClose(prep, roots, rig.Root);
            var retained = await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default);
            Assert.Equal(rig.Admission.Snapshot.ExecutionId, retained!.ExecutionId);
            Assert.Equal(TaskPlanNodeState.Running, retained.Plan.Single().State); Assert.Null(retained.Plan.Single().Acceptance);
            Assert.Null(retained.LastCheckpointActionId); Assert.Equal("actual source", File.ReadAllText(Path.Combine(rig.Root, "source.txt")));
        });
    }

    [TraversalFact]
    public async Task Prebody_fault_waits_for_the_same_physical_cleanup_task_and_conserves_all_cleanup_faults()
    {
        var cleanup = new CloseFaultControl();
        await RunControlAsync(async rig =>
        {
            File.WriteAllText(Path.Combine(rig.Root, "source.txt"), "actual source");
            var call = Call("list_files", new { path = ".", max_depth = 1 });
            var prep = await rig.PrepareAsync(call); var roots = CaptureActualNativeRoots(prep);
            rig.Models.Actors.Actor = null;
            var first = new IOException("Original prebody cleanup fault"); var sibling = new OperationCanceledException("FAULTED cleanup sibling");
            var dispatched = false;
            var actual = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct =>
            { dispatched = true; return rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct); }, default));
            try
            {
                await Task.WhenAny(cleanup.Entered.Task, actual); if (!cleanup.Entered.Task.IsCompleted) await actual; await cleanup.Entered.Task;
                Assert.False(actual.IsCompleted); Assert.False(dispatched);
                Assert.All(roots, root => Assert.True(root.IsClosed)); // actual native drain happened before controlled late cleanup
                var retainedClose = Assert.IsAssignableFrom<Task>(Field(prep, "OriginalPreBodyClose"));
                Assert.False(retainedClose.IsCompleted);
                var physical = Assert.IsType<FaultingCloseInvocation>(Assert.IsAssignableFrom<IWorkspaceToolActionPreparation>(prep).OriginalInvocation);
                Assert.Same(retainedClose, physical.CloseAndDrainAsync());
                Assert.Same(actual, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new InvalidOperationException("Cleanup-held action must not replay."), default));
            }
            finally { cleanup.Raw.TrySetException([first, sibling]); }
            var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.False(dispatched);
            Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(error!), value => ReferenceEquals(value, sibling));
            Assert.True(cleanup.Raw.Task.IsFaulted); Assert.True(((Task)Field(prep, "OriginalPreBodyClose")!).IsFaulted);
            var retained = await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default);
            Assert.Equal(rig.Admission.Snapshot.ExecutionId, retained!.ExecutionId);
            Assert.Equal(TaskPlanNodeState.Running, retained.Plan.Single().State); Assert.Null(retained.Plan.Single().Acceptance);
            Assert.Null(retained.LastCheckpointActionId); Assert.Equal("actual source", File.ReadAllText(Path.Combine(rig.Root, "source.txt")));
        }, cleanup);
    }

    [TraversalFact]
    public async Task Traversal_availability_requires_the_actual_optional_source_and_remains_deny_only()
    {
        await RunControlAsync(rig =>
        {
            var source = Assert.IsAssignableFrom<IWorkspaceOriginalTraversalSource>(rig.Physical);
            Assert.True(source.SupportsOriginalTraversal("list_files")); Assert.True(source.SupportsOriginalTraversal("search_files"));
            Assert.False(source.SupportsOriginalTraversal("unknown_tool"));
            var missingAuthority = new WorkspaceToolService();
            Assert.False(missingAuthority.SupportsOriginalTraversal("list_files"));
            Assert.False(missingAuthority.SupportsOriginalTraversal("search_files"));
            Assert.True(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "list_files"));
            Assert.False(rig.Owner.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "unknown_tool"));
            foreach (var kind in Enum.GetValues<ToolRuntimeKind>().Where(value => value != ToolRuntimeKind.Workspace))
                Assert.False(rig.Owner.SupportsCanonicalInvocation(kind, "list_files"));
            var unavailable = new WorkspaceTaskRunToolActionOwner(() => rig.Coordinator, rig.Frames,
                new LegacySource(rig.Physical), new(new Capabilities()), rig.Effects, rig.Platform, rig.Receipts);
            Assert.True(unavailable.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "read_file"));
            Assert.False(unavailable.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "list_files"));
            Assert.False(unavailable.SupportsCanonicalInvocation(ToolRuntimeKind.Workspace, "search_files"));
            return Task.CompletedTask;
        });
    }

    private static object? Field(object actual, string name) => actual.GetType()
        .GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(actual);
    private static SafeFileHandle[] CaptureActualNativeRoots(ITaskRunToolActionPreparation prep)
    {
        var invocation = Assert.IsAssignableFrom<IWorkspaceToolActionPreparation>(prep).OriginalInvocation!;
        if (invocation is FaultingCloseInvocation controlled) invocation = controlled.Original;
        SafeFileHandle[] roots;
        if (OperatingSystem.IsLinux())
        {
            var lease = Field(invocation, "_linuxRoot"); Assert.NotNull(lease);
            roots = [Assert.IsType<SafeFileHandle>(Field(lease!, "_rootHandle"))];
        }
        else
        {
            var lease = Field(invocation, "_physicalRoot"); Assert.NotNull(lease);
            var directories = Assert.IsAssignableFrom<IDictionary<string, SafeFileHandle>>(Field(lease!, "_directories"));
            roots = directories.Values.ToArray();
        }
        Assert.NotEmpty(roots); Assert.All(roots, root => Assert.False(root.IsClosed)); return roots;
    }
    private static void AssertAutomaticPreBodyClose(ITaskRunToolActionPreparation prep, SafeFileHandle[] roots, string root)
    {
        Assert.All(roots, actual => Assert.True(actual.IsClosed, "The SAME actual OS root handle must be released before the owner frame result settles."));
        Assert.Equal(0, Assert.IsType<int>(Field(prep, "OriginalToolBodyEntered")));
        var close = Assert.IsAssignableFrom<Task>(Field(prep, "OriginalPreBodyClose"));
        Assert.True(close.IsCompletedSuccessfully);
        var invocation = Assert.IsAssignableFrom<IWorkspaceToolActionPreparation>(prep).OriginalInvocation!;
        Assert.Same(close, Field(invocation, "_close")); // no manual close call may make this assertion pass
        var moved = root + "-closed-control";
        Directory.Move(root, moved); Directory.Move(moved, root); // Windows share-delete exclusion proves automatic release
    }

    private static async Task RunControlAsync(Func<Rig, Task> control, CloseFaultControl? cleanup = null)
    {
        Rig? rig = null; Task? original = null; Task? close = null; var errors = new List<Exception>();
        try { rig = await Rig.CreateAsync(cleanup); original = control(rig); await original; }
        catch (Exception error) { errors.Add((Exception?)original?.Exception ?? error); }
        finally
        {
            if (rig is not null)
            {
                try { close = rig.DisposeAsync().AsTask(); await close; }
                catch (Exception error) { errors.Add((Exception?)close?.Exception ?? error); }
            }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original canonical traversal control and independent fixture drain failed.", errors);
    }
    private static OllamaToolCall Call(string name, object args) => new(name,
        JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(value => value.Name, value => value.Value.Clone(), StringComparer.Ordinal));
    private static IEnumerable<Exception> Leaves(Exception error)
    {
        if (error is AggregateException group) foreach (var child in group.InnerExceptions) foreach (var leaf in Leaves(child)) yield return leaf;
        else yield return error;
    }
    public sealed class TraversalFactAttribute : FactAttribute
    { public TraversalFactAttribute() { if (!SupportedHost) Skip = "Requires actual Windows or Linux x64/arm64 original traversal; unavailable native ABI remains a failure."; } }
    public sealed class TraversalTheoryAttribute : TheoryAttribute
    { public TraversalTheoryAttribute() { if (!SupportedHost) Skip = "Requires actual Windows or Linux x64/arm64 original traversal; unavailable native ABI remains a failure."; } }
    private static bool SupportedHost => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    private sealed class Rig : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "haven-canonical-traversal-" + Guid.NewGuid().ToString("N"));
        public Repository Repository { get; } = new(); public PermissionDecisionEngine Policy { get; } = new();
        public WorkspaceTaskRunReceiptAuthority Receipts { get; } = new(); public ModelInputs Models { get; }
        public WorkspaceTaskRunEffectAuthority Effects { get; } public WorkspaceToolService Physical { get; }
        public WorkspaceToolRuntime Runtime { get; } public TaskRunOriginalFrameOwner Frames { get; }
        public WorkspaceTaskRunToolActionOwner Owner { get; }
        public CapabilityPlatform Platform { get; } = OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Linux;
        private TaskExecutionCoordinator? _coordinator;
        public TaskExecutionCoordinator Coordinator => _coordinator ?? throw new InvalidOperationException("Actual fixture coordinator not constructed.");
        public TaskRunAttemptAdmission Admission { get; private set; } = null!;
        private readonly List<Task> _originals = []; private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        private Rig(CloseFaultControl? cleanup)
        {
            Models = new(Receipts); Effects = new(Models.Authority, Policy, Policy); Physical = new(Effects);
            IWorkspaceToolService source = cleanup is null ? Physical : new FaultingCloseSource(Physical, cleanup);
            Runtime = new(source);
            Frames = new((task, run, attempt, token) => Coordinator.TryGetIssuedAttemptAsync(task, run, attempt, token));
            Owner = new(() => Coordinator, Frames, source, new(new Capabilities()), Effects, Platform, Receipts);
            _coordinator = new(Repository, new Events(), admissionAuthority: Models.Authority, runtimeSettlement: Frames, toolActionOwner: Owner);
            Directory.CreateDirectory(Root);
        }
        public static async Task<Rig> CreateAsync(CloseFaultControl? cleanup)
        {
            var rig = new Rig(cleanup);
            try
            {
                var task = await rig.Coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "Controlled original traversal task",
                    TaskExecutionDurability.PersistedPlan, [], default);
                var route = await rig.Models.Authority.CaptureSelectedRouteAsync(task, rig.Models.Local.Model, [ToolCapability.Text], []);
                rig.Admission = await rig.Coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId, route, default);
                await rig.Frames.RegisterOriginalAttemptAsync(rig.Admission, default); return rig;
            }
            catch (Exception error)
            {
                Task? close = null;
                try { close = rig.DisposeAsync().AsTask(); await close; }
                catch (Exception cleanupError) { throw new AggregateException("Canonical traversal fixture acquisition and retirement failed.", error, (Exception?)close?.Exception ?? cleanupError); }
                throw;
            }
        }
        public async Task<ITaskRunToolActionPreparation> PrepareAsync(OllamaToolCall call)
        {
            var current = (await Coordinator.GetAsync(Admission.Snapshot.TaskId, default))!;
            var actual = await Owner.PrepareOriginalAsync(Admission, current, Guid.NewGuid(), call, ToolRuntimeKind.Workspace, PermissionMode.Ask, Root, default);
            await Coordinator.RegisterOriginalToolActionAsync(actual, null, "Actual captured traversal intent", default); return actual;
        }
        public Task<T> Track<T>(Task<T> original) { _originals.Add(original); return original; }
        public void Expect(Exception error) { foreach (var leaf in Leaves(error)) _expected.Add(leaf); }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            foreach (var original in _originals)
            {
                try { await original; }
                catch (Exception error)
                { var whole = (Exception?)original.Exception ?? error; if (Leaves(whole).Any(leaf => !_expected.Contains(leaf))) errors.Add(whole); }
            }
            Task? close = null;
            try { close = Frames.CloseAndDrainAsync(); await close; }
            catch (Exception error)
            { var whole = (Exception?)close?.Exception ?? error; if (Leaves(whole).Any(leaf => !_expected.Contains(leaf))) errors.Add(whole); }
            try { Directory.Delete(Root, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Original canonical traversal fixture drains and physical cleanup failed.", errors);
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
        public Task UpsertAsync(TaskExecutionSnapshot next, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var before = _rows.TryGetValue(next.TaskId, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null;
            if (next.PersistenceRevision != (before?.PersistenceRevision ?? 0) + 1)
                throw new TaskExecutionRevisionConflictException(next.TaskId, next.PersistenceRevision - 1, before?.PersistenceRevision ?? 0);
            _rows[next.TaskId] = JsonSerializer.Serialize(next); return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(_rows.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null);
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => (await GetResumableAsync(token)).FirstOrDefault(value => value.ContextId == id);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(json => JsonSerializer.Deserialize<TaskExecutionSnapshot>(json)!).ToArray());
    }
    private sealed class LegacySource(WorkspaceToolService actual) : IWorkspaceOriginalInvocationSource
    {
        public string ResolveWorkspacePath(string root, string path) => actual.ResolveWorkspacePath(root, path);
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => actual.ReadTextAsync(root, path, token);
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => actual.WriteTextAtomicAsync(root, path, content, token);
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => actual.SearchFilesAsync(root, pattern, token);
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => actual.RunProcessAsync(request, token);
        public IWorkspaceOriginalInvocation AcquireOriginalInvocation(IWorkspaceToolFinalFence fence) => actual.AcquireOriginalInvocation(fence);
        public bool IsIssuedOriginal(IWorkspaceOriginalInvocation original) => actual.IsIssuedOriginal(original);
        public bool ValidateOriginalOutcome(IWorkspaceOriginalInvocation original, WorkspaceToolPhysicalOutcome outcome) => actual.ValidateOriginalOutcome(original, outcome);
    }
    // Controlled cleanup producer over the genuine physical invocation. It holds/faults
    // only AFTER the actual native close has joined; it cannot mint a receipt or actor grant.
    private sealed class CloseFaultControl
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Raw { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class FaultingCloseSource(WorkspaceToolService actual, CloseFaultControl cleanup) : IWorkspaceOriginalTraversalSource
    {
        private readonly HashSet<FaultingCloseInvocation> _issued = [];
        public bool SupportsOriginalTraversal(string name) => actual.SupportsOriginalTraversal(name);
        public string ResolveWorkspacePath(string root, string path) => actual.ResolveWorkspacePath(root, path);
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => actual.ReadTextAsync(root, path, token);
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => actual.WriteTextAtomicAsync(root, path, content, token);
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => actual.SearchFilesAsync(root, pattern, token);
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => actual.RunProcessAsync(request, token);
        public IWorkspaceOriginalInvocation AcquireOriginalInvocation(IWorkspaceToolFinalFence fence)
        { var issued = new FaultingCloseInvocation(actual.AcquireOriginalInvocation(fence), cleanup); _issued.Add(issued); return issued; }
        public bool IsIssuedOriginal(IWorkspaceOriginalInvocation value) => value is FaultingCloseInvocation original &&
            _issued.Contains(original) && actual.IsIssuedOriginal(original.Original);
        public bool ValidateOriginalOutcome(IWorkspaceOriginalInvocation value, WorkspaceToolPhysicalOutcome outcome) =>
            IsIssuedOriginal(value) && actual.ValidateOriginalOutcome(((FaultingCloseInvocation)value).Original, outcome);
    }
    private sealed class FaultingCloseInvocation(IWorkspaceOriginalInvocation original, CloseFaultControl cleanup) : IWorkspaceOriginalInvocation
    {
        private readonly object _gate = new(); private Task? _close;
        public IWorkspaceOriginalInvocation Original => original;
        public IWorkspaceToolService Tools => original.Tools;
        public Task<WorkspaceToolPhysicalOutcome> CompleteOriginalAsync(bool success, CancellationToken token) => original.CompleteOriginalAsync(success, token);
        public Task CloseAndDrainAsync()
        {
            TaskCompletionSource completion;
            lock (_gate)
            {
                if (_close is not null) return _close;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = completion.Task;
            }
            _ = ClosePublishedAsync(completion); return completion.Task;
        }
        private async Task ClosePublishedAsync(TaskCompletionSource completion)
        {
            Task? actual = null;
            try
            {
                actual = original.CloseAndDrainAsync(); await actual;
                cleanup.Entered.TrySetResult(); actual = cleanup.Raw.Task; await actual;
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException((Exception?)actual?.Exception ?? error); }
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    }

    private sealed class ModelInputs
    {
        public Actors Actors { get; } = new(); public Provider Local { get; } = new(); public TaskRunPermissionAuthority Authority { get; }
        public ModelInputs(ITaskRunActionReceiptAuthority receipts)
        {
            var configurations = new Configurations();
            configurations.Rows[Local.Id] = new(Local.Id, Local.Kind, Local.DisplayName, "http://127.0.0.1:11434", true, true, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
            Authority = new(Actors, new Registry(Local), configurations, new Privacy(), new(new Permissions()), cloud: null, receipts: receipts);
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Actor = new("controlled-owner", "controlled-profile", null, null, "revision-one");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Actor); }
    }
    private sealed class Provider : IModelProvider
    {
        public string Id => "ollama"; public string DisplayName => "controlled local"; public bool IsLocal => true; public bool CanManageModels => false; public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public ProviderModelDescriptor Model { get; } = new("ollama", true, new ModelDescriptor("controlled-model", 123, "controlled", "7B", "Q8", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Func<Task<IReadOnlyList<ProviderModelDescriptor>>>? HeldRead;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { if (HeldRead is { } read) return read(); token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(Id, true, "controlled", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Registry(IModelProvider actual) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => [actual]; public IModelProvider? Find(string id) => actual.Id == id ? actual : null;
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => actual.GetModelsAsync(token);
        // Controlled original catalogue input: preserve the SAME selected provider Task.
        // The production interface default has separate fault-conservation limitations;
        // this fixture certifies the owning action/frame boundary, not that catalogue implementation.
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(ModelCataloguePolicy policy, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!(actual.IsLocal ? policy.AllowLocal : policy.AllowRemote) ||
                policy.AllowedProviderIds is { } ids && !ids.Contains(actual.Id))
                return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]);
            return actual.GetModelsAsync(token);
        }
    }
    private sealed class Configurations : IProviderConfigurationStore
    {
        public Dictionary<string, ProviderConfiguration> Rows { get; } = new(StringComparer.Ordinal);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(Rows.Values.ToArray());
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) { Rows[value.Id] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken token) { Rows.Remove(id); return Task.CompletedTask; }
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
    }
    private sealed class Permissions : IModelPermissionStore
    {
        private ModelPermissionPolicy _policy = ModelPermissionPolicy.Empty;
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(_policy);
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) { _policy = value; return Task.CompletedTask; }
    }
}
