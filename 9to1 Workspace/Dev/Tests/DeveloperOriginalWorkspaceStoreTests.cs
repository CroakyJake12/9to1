using System.Text;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>Actual configured managed store protocol. Native/Home ports are synthetic here;
/// the separate real Linux controls prove native I/O, not installed setup/authority.</summary>
public sealed class DeveloperOriginalWorkspaceStoreTests
{
    [Fact]
    public async Task Missing_private_native_preparation_refuses_strict_create_and_preserves_ordinary_store_behavior()
    {
        var root = Path.Combine(Path.GetTempPath(), "dev-original-store-" + Guid.NewGuid().ToString("N"));
        var store = new FileDeveloperWorkspaceStore(root); var workspace = Workspace(root);
        var source = new Source(store, workspace.WorkspaceId) { Issued = false }; var tasks = new List<Task>();
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => store.CreateOriginalSetupAsync(workspace, source, source.Preparation,
                new Entry(), callback => callback(), tasks.Add, CancellationToken.None));
            Assert.Equal(0, source.Preparation.Starts); Assert.True(source.Preparation.Close.IsCompletedSuccessfully);
            Assert.False(Directory.Exists(store.OriginalWorkspaceMetadataDirectory));
            var ordinary = await store.CreateAsync(workspace); Assert.True(ordinary.Succeeded);
            Assert.Equal(workspace.WorkspaceId, (await store.GetAsync(workspace.WorkspaceId)).Value!.WorkspaceId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Actual_store_joins_same_raw_native_write_and_whole_preparation_close_before_returning_result()
    {
        var root = Path.Combine(Path.GetTempPath(), "dev-original-store-" + Guid.NewGuid().ToString("N"));
        var store = new FileDeveloperWorkspaceStore(root); var workspace = Workspace(root); var source = new Source(store, workspace.WorkspaceId);
        var heldWrite = new TaskCompletionSource<IDeveloperProjectOriginalWorkspaceMetadataObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeEnrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeEnrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Preparation.Write = heldWrite.Task; source.Preparation.Close = heldClose.Task;
        Task<DeveloperOriginalWorkspaceSetupResult>? actual = null; var raw = new List<Task>(); var errors = new List<Exception>();
        try
        {
            actual = store.CreateOriginalSetupAsync(workspace, source, source.Preparation, new Entry(), callback => callback(), task =>
            {
                lock (raw) raw.Add(task);
                if (ReferenceEquals(task, heldWrite.Task)) writeEnrolled.TrySetResult();
                if (ReferenceEquals(task, heldClose.Task)) closeEnrolled.TrySetResult();
            }, CancellationToken.None);
            await writeEnrolled.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(actual.IsCompleted); Assert.False(heldWrite.Task.IsCompleted);
            var observation = source.Preparation.Observation = new Observation(Encoding.UTF8.GetString(source.Preparation.Document!));
            heldWrite.TrySetResult(observation); await closeEnrolled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(actual.IsCompleted); Assert.Contains(heldWrite.Task, raw); Assert.Contains(heldClose.Task, raw);
            heldClose.TrySetResult(); var result = await actual;
            Assert.Same(workspace, result.Workspace); Assert.Same(heldWrite.Task, result.OriginalNativeWriteTask);
            Assert.Same(observation, result.OriginalObservation); Assert.Equal(1, source.Preparation.Starts);
            Assert.False(Directory.Exists(store.OriginalWorkspaceMetadataDirectory));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            heldWrite.TrySetResult(source.Preparation.Observation ?? new Observation("unreachable fallback")); heldClose.TrySetResult();
            if (actual is not null) try { await actual; } catch (Exception error) { errors.Add(error); }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        if (errors.Count != 0) throw new AggregateException("Actual store/raw opaque native write and independent whole close control failed.", errors);
    }
    [Fact]
    public async Task Faulted_native_oce_and_sibling_are_retained_after_independent_preparation_cleanup_without_store_ack()
    {
        var root = Path.Combine(Path.GetTempPath(), "dev-original-store-" + Guid.NewGuid().ToString("N"));
        var store = new FileDeveloperWorkspaceStore(root); var workspace = Workspace(root); var source = new Source(store, workspace.WorkspaceId);
        var first = new OperationCanceledException("Original raw native fault, not canceled Task."); var second = new IOException("Original raw native sibling.");
        var failed = new TaskCompletionSource<IDeveloperProjectOriginalWorkspaceMetadataObservation>(); failed.SetException([first, second]); source.Preparation.Write = failed.Task;
        var raw = new List<Task>();
        try
        {
            var actual = store.CreateOriginalSetupAsync(workspace, source, source.Preparation, new Entry(), callback => callback(), raw.Add, CancellationToken.None);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); Assert.True(actual.IsFaulted);
            Assert.Contains(Causes(error), cause => ReferenceEquals(cause, first)); Assert.Contains(Causes(error), cause => ReferenceEquals(cause, second));
            Assert.Contains(failed.Task, raw); Assert.Contains(source.Preparation.Close, raw); Assert.True(source.Preparation.Close.IsCompletedSuccessfully);
            Assert.Equal(1, source.Preparation.Starts); Assert.False(Directory.Exists(store.OriginalWorkspaceMetadataDirectory));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static DeveloperWorkspace Workspace(string root) => DeveloperWorkspace.Create([new DeveloperWorkspaceRoot(Guid.NewGuid(), root)]);
    private static IEnumerable<Exception> Causes(Exception error) => error is AggregateException compound ? compound.InnerExceptions.SelectMany(Causes) : [error];
    private sealed class Source(FileDeveloperWorkspaceStore store, Guid workspaceId) : IDeveloperProjectOriginalWorkspaceMetadataSource
    {
        internal bool Issued = true;
        internal readonly Preparation Preparation = new(store, workspaceId);
        public Task<IDeveloperProjectOriginalWorkspaceMetadataPreparation> PrepareOriginalWorkspaceMetadataAsync(IDeveloperProjectOriginalWorkspaceMetadataStore sameStore,
            DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, IDeveloperProjectOriginalSetupPermission permission,
            DeveloperProjectSetupStep step, CancellationToken token) => throw new NotSupportedException("No native/Home preparation is simulated.");
        public bool IsIssuedOriginalWorkspaceMetadataPreparation(IDeveloperProjectOriginalWorkspaceMetadataPreparation preparation,
            IDeveloperProjectOriginalWorkspaceMetadataStore actualStore, Guid actualWorkspaceId) => Issued && ReferenceEquals(preparation, Preparation) && ReferenceEquals(actualStore, store) && actualWorkspaceId == workspaceId;
        public bool IsIssuedOriginalWorkspaceMetadataOutcome(IDeveloperProjectOriginalWorkspaceMetadataPreparation preparation, Task task,
            IDeveloperProjectOriginalWorkspaceMetadataObservation observation) => ReferenceEquals(preparation, Preparation) && ReferenceEquals(task, Preparation.Write) && ReferenceEquals(observation, Preparation.Observation);
        public Task ValidateOriginalWorkspaceMetadataOutcomeAsync(IDeveloperProjectOriginalWorkspaceMetadataPreparation preparation, Task task,
            IDeveloperProjectOriginalWorkspaceMetadataObservation observation, CancellationToken token) => throw new NotSupportedException("No real native acknowledgement is issued.");
        public void DemandExternalOriginalWorkspaceMetadataJoin() { }
    }
    private sealed class Preparation(FileDeveloperWorkspaceStore store, Guid workspaceId) : IDeveloperProjectOriginalWorkspaceMetadataPreparation
    {
        internal Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> Write = null!; internal Task Close = Task.CompletedTask;
        internal Observation? Observation; internal byte[]? Document; internal int Starts;
        public bool IsBoundToOriginalStore(IDeveloperProjectOriginalWorkspaceMetadataStore actualStore, Guid actualWorkspaceId) => ReferenceEquals(actualStore, store) && actualWorkspaceId == workspaceId;
        public Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> CreateOriginalMetadataAsync(ReadOnlyMemory<byte> document,
            IDeveloperProjectOriginalSetupStepEntry entry, Action<Action> scope, Action<Task> retain, CancellationToken token)
        { Starts++; Document = document.ToArray(); return Write; }
        public bool IsIssuedOriginalMetadata(Task task, IDeveloperProjectOriginalWorkspaceMetadataObservation observation) => ReferenceEquals(task, Write) && ReferenceEquals(observation, Observation);
        public Task CloseAndDrainAsync() => Close;
        public ValueTask DisposeAsync() => new(Close);
    }
    private sealed class Observation(string document) : IDeveloperProjectOriginalWorkspaceMetadataObservation
    { public string OriginalCommittedDocument => document; public string OriginalDocumentSha256 => "synthetic observation, no source receipt"; }
    private sealed class Entry : IDeveloperProjectOriginalSetupStepEntry
    {
        public void DemandOriginalStepEntry(DeveloperProjectSetupStep step) => throw new NotSupportedException();
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep step, CancellationToken token) => throw new NotSupportedException();
        public T RunOriginalStep<T>(DeveloperProjectSetupStep step, Func<T> body, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
