using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual Linux anonymous staging/link/fsync/readback/cleanup. The configured
/// descriptor and read/setup issuers here are synthetic; no installed Home consent, complete
/// import, Windows route or execution trust is certified by this owning native component.</summary>
public sealed class DeveloperOriginalWorkspaceMetadataTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Genuine_anonymous_stage_commits_exact_once_workspace_inode_document_and_independent_cleanup()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var retained = new List<Task>(); Task<IDeveloperProjectOriginalWorkspaceMetadataObservation>? write = null; var errors = new List<Exception>();
        try
        {
            write = entry.RunOriginalStep(original.Step, () => original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry,
                callback => callback(), actual => { lock (retained) retained.Add(actual); }, CancellationToken.None), CancellationToken.None);
            var observation = await write;
            var path = Path.Combine(rig.Store.OriginalWorkspaceMetadataDirectory, original.Intent.WorkspaceId.ToString("N") + ".json");
            Assert.Equal(Document(original.Intent), await System.IO.File.ReadAllBytesAsync(path));
            Assert.Equal(Encoding.UTF8.GetString(Document(original.Intent)), observation.OriginalCommittedDocument);
            Assert.True(rig.Metadata.IsIssuedOriginalWorkspaceMetadataOutcome(original.Preparation, write, observation));
            Assert.False(rig.Metadata.IsIssuedOriginalWorkspaceMetadataOutcome(original.Preparation, Task.CompletedTask, observation));
            Assert.False(rig.Metadata.IsIssuedOriginalWorkspaceMetadataOutcome(original.Preparation, write, new CopiedObservation(observation)));
            Assert.All(OriginalNativeHandles(original.Preparation), handle => Assert.True(handle.IsClosed));
            Assert.True(retained.Count >= 5); Assert.All(retained, actual => Assert.True(actual.IsCompletedSuccessfully));
            var close = original.Preparation.CloseAndDrainAsync(); Assert.Same(close, original.Preparation.CloseAndDrainAsync()); await close;
            await entry.DisposeAsync(); await rig.Metadata.ValidateOriginalWorkspaceMetadataOutcomeAsync(original.Preparation, write, observation, CancellationToken.None);
            Assert.Equal("original source stays in place", System.IO.File.ReadAllText(rig.File));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), _ => { }, CancellationToken.None); });
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (write is not null) try { await write; } catch (Exception error) { errors.Add(error); }
            try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual original native metadata/document/cleanup control failed.", errors);
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Whole_preparation_close_joins_same_held_entry_check_before_retiring_original_ancestor()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); entry.HeldCheck = held.Task;
        Task<IDeveloperProjectOriginalWorkspaceMetadataObservation>? write = null; Task? close = null; var errors = new List<Exception>();
        try
        {
            write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(),
                actual => { if (ReferenceEquals(actual, held.Task)) enrolled.TrySetResult(); }, CancellationToken.None);
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            close = original.Preparation.CloseAndDrainAsync(); Assert.Same(close, original.Preparation.CloseAndDrainAsync());
            Assert.False(close.IsCompleted); Assert.False(write.IsCompleted); Assert.False(OriginalAncestorHandle(original.Preparation).IsClosed);
            held.TrySetResult(true); await write; await close; Assert.True(OriginalAncestorHandle(original.Preparation).IsClosed);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            held.TrySetResult(true);
            if (write is not null) try { await write; } catch (Exception error) { errors.Add(error); }
            try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual held check/whole native preparation close control failed.", errors);
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Restored_native_callback_cannot_join_same_preparation_or_kernel_driver()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var context = ExecutionContext.Capture()!; var childRefusals = 0; var kernelRefusals = 0; var errors = new List<Exception>();
        Task<IDeveloperProjectOriginalWorkspaceMetadataObservation>? write = null;
        try
        {
            write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback =>
            {
                ExecutionContext.Run(context, _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => { _ = original.Preparation.CloseAndDrainAsync(); }); childRefusals++;
                    Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainOriginalCapturesAsync(); }); kernelRefusals++;
                }, null);
                callback();
            }, _ => { }, CancellationToken.None);
            await write; Assert.True(childRefusals > 0); Assert.Equal(childRefusals, kernelRefusals);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (write is not null) try { await write; } catch (Exception error) { errors.Add(error); }
            try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual restored native callback/independent cleanup control failed.", errors);
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Faulted_held_check_keeps_exact_oce_and_sibling_and_no_metadata_receipt_or_effect()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var first = new OperationCanceledException("Original held raw check fault, no canceled Task."); var second = new IOException("Original held raw check sibling.");
        var failed = new TaskCompletionSource<bool>(); failed.SetException([first, second]); entry.HeldCheck = failed.Task;
        var retained = new List<Task>(); var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), retained.Add, CancellationToken.None);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => write);
        Assert.True(write.IsFaulted); Assert.Contains(Causes(error), actual => ReferenceEquals(actual, first)); Assert.Contains(Causes(error), actual => ReferenceEquals(actual, second));
        Assert.Contains(failed.Task, retained); Assert.False(Directory.Exists(rig.Store.OriginalWorkspaceMetadataDirectory));
        await entry.DisposeAsync(); var closeError = await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync());
        Assert.Contains(Causes(closeError), actual => ReferenceEquals(actual, first)); Assert.Contains(Causes(closeError), actual => ReferenceEquals(actual, second));
        Assert.True(OriginalAncestorHandle(original.Preparation).IsClosed);
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Real_canceled_held_check_retains_original_task_token_and_no_native_metadata_effect()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var raw = Task.FromCanceled<bool>(cancellation.Token); entry.HeldCheck = raw;
        var retained = new List<Task>(); var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), retained.Add, CancellationToken.None);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.True(write.IsCanceled); Assert.True(raw.IsCanceled); Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Contains(raw, retained);
        Assert.False(Directory.Exists(rig.Store.OriginalWorkspaceMetadataDirectory)); await entry.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync()); Assert.True(OriginalAncestorHandle(original.Preparation).IsClosed);
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Existing_identity_is_not_overwritten_and_changed_committed_record_refuses_original_acknowledgement()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), _ => { }, CancellationToken.None);
        var observation = await write; await entry.DisposeAsync(); await original.Preparation.CloseAndDrainAsync();
        var path = Path.Combine(rig.Store.OriginalWorkspaceMetadataDirectory, original.Intent.WorkspaceId.ToString("N") + ".json");
        System.IO.File.WriteAllText(path, "actual independent replacement bytes");
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Metadata.ValidateOriginalWorkspaceMetadataOutcomeAsync(original.Preparation, write, observation, CancellationToken.None));
        var other = await rig.Metadata.PrepareOriginalWorkspaceMetadataAsync(rig.Store, original.Intent, original.Capture, original.Permission, original.Step, CancellationToken.None);
        var otherEntry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var attempt = other.CreateOriginalMetadataAsync(Document(original.Intent), otherEntry, callback => callback(), _ => { }, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => attempt); Assert.Equal("actual independent replacement bytes", System.IO.File.ReadAllText(path));
        await otherEntry.DisposeAsync(); await Assert.ThrowsAnyAsync<Exception>(() => other.CloseAndDrainAsync());
    }
    private static IEnumerable<Exception> Causes(Exception actual) => actual is AggregateException compound ? compound.InnerExceptions.SelectMany(Causes) : [actual];
    private static SafeFileHandle OriginalAncestorHandle(IDeveloperProjectOriginalWorkspaceMetadataPreparation preparation)
        => (SafeFileHandle)preparation.GetType().GetField("AncestorHandle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preparation)!;
    private static SafeFileHandle[] OriginalNativeHandles(IDeveloperProjectOriginalWorkspaceMetadataPreparation preparation)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var operation = preparation.GetType().GetField("Operation", flags)!.GetValue(preparation)!;
        return new[] { "LockHandle", "StageHandle", "InputHandle", "BorrowedStageHandle" }.Select(name =>
            (SafeFileHandle)operation.GetType().GetField(name, flags)!.GetValue(operation)!).ToArray();
    }
    private sealed class CopiedObservation(IDeveloperProjectOriginalWorkspaceMetadataObservation actual) : IDeveloperProjectOriginalWorkspaceMetadataObservation
    { public string OriginalCommittedDocument => actual.OriginalCommittedDocument; public string OriginalDocumentSha256 => actual.OriginalDocumentSha256; }
    private static byte[] Document(DeveloperProjectSetupIntent intent) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = 1, workspace = new
        {
            workspaceId = intent.WorkspaceId, revision = 1,
            roots = new[] { new { rootId = intent.RootId, location = intent.OriginalExistingProjectRoot } },
            projects = new[] { new { projectId = intent.ProjectId, name = intent.ProjectName, rootIds = new[] { intent.RootId } } },
            openEditors = intent.Files.Select(file => new { fileId = file.FileId, projectId = intent.ProjectId, canonicalResourceId = file.FileId.ToString("D") }).ToArray()
        }
    });
    private static async Task<(IDeveloperProjectOriginalWorkspaceMetadataPreparation Preparation, Permission Permission,
        DeveloperProjectSetupIntent Intent, IDeveloperProjectOriginalExistingSourceCapture Capture, DeveloperProjectSetupStep Step)> Prepare(Rig rig)
    {
        var capture = await rig.Capture(CancellationToken.None); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var step = intent.Steps[^1]; var preparation = await rig.Metadata.PrepareOriginalWorkspaceMetadataAsync(rig.Store, intent, capture, permission, step, CancellationToken.None);
        return (preparation, permission, intent, capture, step);
    }
    private sealed class Rig : IAsyncDisposable
    {
        internal string Root = Path.Combine(Path.GetTempPath(), "dev-directory-original-" + Guid.NewGuid().ToString("N"));
        internal string Project => Path.Combine(Root, "selected-existing");
        internal MetadataStore Store => _store ??= new MetadataStore(Path.Combine(Root, "app-data"));
        private MetadataStore? _store;
        internal IDeveloperProjectOriginalWorkspaceMetadataSource Metadata => (IDeveloperProjectOriginalWorkspaceMetadataSource)Source;
        internal string File => Path.Combine(Project, "code.cs");
        internal readonly SelectionSource Selections = new(); internal readonly ReadSource Reads = new();
        internal readonly SetupSource Setups = new();
        internal readonly IDeveloperProjectOriginalPhysicalCaptureSource Source;
        internal IDeveloperProjectOriginalDirectoryObservationSource Directories => (IDeveloperProjectOriginalDirectoryObservationSource)Source;
        internal Rig(bool configureStore = true)
        {
            Directory.CreateDirectory(Project); System.IO.File.WriteAllText(File, "original source stays in place");
            Directory.CreateDirectory(Store.OriginalWorkspaceMetadataAncestor);
            Source = configureStore ? new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(Reads, () => Selections, () => Setups, () => Store)
                : new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(Reads, () => Selections, () => Setups);
        }
        internal async Task<IDeveloperProjectOriginalExistingSourceCapture> Capture(CancellationToken token)
        {
            var physical = await Source.OpenOriginalSelectionAsync(Root, Project, token); Selections.Physical = physical;
            return await Source.CaptureOriginalAsync(physical, Selections.Logical, Reads.Admission, token);
        }
        internal DeveloperProjectSetupIntent Intent(IDeveloperProjectOriginalExistingSourceCapture capture)
        {
            var folder = Guid.NewGuid(); var source = Assert.Single(capture.OriginalFiles); var file = Guid.NewGuid();
            return new(Guid.NewGuid(), new("synthetic-profile-actor", Guid.NewGuid().ToString("D"), null, null, "synthetic-revision"),
                Guid.NewGuid(), new string('a', 64), Guid.NewGuid(), Guid.NewGuid().ToString("D"),
                capture.OriginalCaptureReference, capture.OriginalCaptureDigest, Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), folder, "SelectedExisting",
                ImmutableArray<DeveloperProjectSetupFolder>.Empty,
                [new(file, folder, Guid.NewGuid(), source.RelativePath, source.SizeBytes, source.ContentSha256, source.OriginalSourceReference)],
                [new(Guid.NewGuid(), DeveloperProjectSetupStepKind.CreateProjectFolder, null, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory, null, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.RegisterProjectFolder, null, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.RegisterExistingFileMetadata, file, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.RegisterMaterialization, file, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.SaveDevWorkspace, null, folder)])
                { Mode = DeveloperProjectSetupMode.RegisterExisting, OriginalExistingProjectRoot = Project };
        }
        public async ValueTask DisposeAsync()
        { try { await Source.CloseAndDrainOriginalCapturesAsync(); } catch { } if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class Selection : IDeveloperProjectOriginalReadSelection { }
    private sealed class SelectionSource : IDeveloperProjectOriginalPhysicalReadSelectionSource
    {
        internal Selection Logical = new(); internal IDeveloperProjectOriginalPhysicalSelection? Physical;
        public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection value) => ReferenceEquals(value, Logical);
        public bool IsIssuedOriginalPhysicalBinding(IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalPhysicalSelection physical) => IsIssuedOriginal(value) && ReferenceEquals(physical, Physical);
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginal(value)) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection value) => throw new NotSupportedException("Synthetic issuer grants no Home scope.");
        public void DemandExternalOriginalReadSelectionJoin() { }
    }
    private sealed class ReadSource : IDeveloperProjectOriginalReadAdmissionSource, IDeveloperProjectOriginalReadAdmissionJoinGuard
    {
        internal IDeveloperProjectOriginalReadAdmission Admission = new Read();
        public Task<IDeveloperProjectOriginalReadAdmission> AcquireOriginalAsync(IDeveloperProjectOriginalReadSelection value, CancellationToken token) => throw new NotSupportedException("No Home review is claimed.");
        public Task ValidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalReadAdmission actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!ReferenceEquals(actual, Admission)) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public void DemandExternalOriginalReadAdmissionJoin() { }
        private sealed class Read : IDeveloperProjectOriginalReadAdmission
        {
            public Task RevalidateOriginalAsync(CancellationToken token) => Task.CompletedTask;
            public T RunOriginalRead<T>(Func<T> start, CancellationToken token) { token.ThrowIfCancellationRequested(); return start(); }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class SetupSource : IDeveloperProjectOriginalSetupPermissionSource
    {
        private readonly HashSet<Permission> _issued = []; internal Task Validation = Task.CompletedTask; internal Action? BeforeValidation;
        internal Permission Issue(DeveloperProjectSetupIntent intent) { var result = new Permission(intent); _issued.Add(result); return result; }
        public Task<IDeveloperProjectOriginalSetupPermission> AcquireOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, CancellationToken token) => throw new NotSupportedException("No Home setup review is simulated.");
        public Task ValidateOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSetupPermission actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (actual is not Permission permission || !_issued.Contains(permission) || !ReferenceEquals(intent, permission.Intent)) throw new UnauthorizedAccessException(); BeforeValidation?.Invoke(); return Validation; }
        public void RequestOriginalSetupRetirement() { }
        public void DemandExternalOriginalSetupJoin() { }
        public Task CloseAndDrainOriginalSetupsAsync() => Task.CompletedTask;
    }
    private sealed class Permission(DeveloperProjectSetupIntent intent) : IDeveloperProjectOriginalSetupPermission
    {
        internal DeveloperProjectSetupIntent Intent => intent; internal int EntryStarts; private Entry? _entry;
        public Task<Entry> EnterOriginalStepAsync(DeveloperProjectSetupStep step, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!intent.Steps.Any(value => ReferenceEquals(value, step))) throw new UnauthorizedAccessException(); EntryStarts++; return Task.FromResult(_entry = new Entry(step)); }
        async Task<IDeveloperProjectOriginalSetupStepEntry> IDeveloperProjectOriginalSetupPermission.EnterOriginalStepAsync(DeveloperProjectSetupStep step, CancellationToken token) => await EnterOriginalStepAsync(step, token);
        public bool IsIssuedOriginalStepEntry(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry) => ReferenceEquals(entry, _entry) && _entry?.Closed == false && ReferenceEquals(_entry.Step, step);
        public Task ValidateOriginalStepResultAsync(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry, Task actual, object? result, CancellationToken token) => throw new NotSupportedException("Kernel fixture grants no whole Home outcome validation.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Entry(DeveloperProjectSetupStep step) : IDeveloperProjectOriginalSetupStepEntry, IDeveloperProjectOriginalSetupScopedStepEntry
    {
        internal DeveloperProjectSetupStep Step => step; internal bool Closed; internal Action? Check; internal Task<bool>? HeldCheck; private bool _used;
        public void DemandOriginalStepEntry(DeveloperProjectSetupStep actual) { if (Closed || !ReferenceEquals(actual, step)) throw new UnauthorizedAccessException(); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); Check?.Invoke(); return HeldCheck is { } original ? new ValueTask<bool>(original) : ValueTask.FromResult(true); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<bool>? raw = null;
            scope(() => { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); Check?.Invoke(); raw = HeldCheck ?? Task.FromResult(true); retain(raw); });
            return new ValueTask<bool>(raw ?? throw new InvalidOperationException("No original held check was returned."));
        }
        public T RunOriginalStep<T>(DeveloperProjectSetupStep actual, Func<T> body, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); if (_used) throw new InvalidOperationException(); _used = true; return body(); }
        public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
    }
    private sealed class MetadataStore(string ancestor) : IDeveloperProjectOriginalWorkspaceMetadataStore
    {
        public string OriginalWorkspaceMetadataAncestor => ancestor;
        public string OriginalWorkspaceMetadataDirectory => Path.Combine(ancestor, "Dev", "workspaces");
    }
}
