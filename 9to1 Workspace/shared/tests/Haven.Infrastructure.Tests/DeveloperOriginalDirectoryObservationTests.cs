using System.Collections.Immutable;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual maintained Linux handles, root/path checks and original tasks; synthetic
/// configured read/setup issuers establish only this boundary, never real Home consent.</summary>
public sealed partial class DeveloperOriginalDirectoryObservationTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Same_existing_directory_observation_closes_actual_handle_without_source_copy_or_new_root()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token;
        var capture = await rig.Capture(token); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var step = intent.Steps[1]; var before = File.ReadAllBytes(rig.File);
        var prepared = await rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, step, token);
        try
        {
            var entry = await permission.EnterOriginalStepAsync(step, token);
            var actual = entry.RunOriginalStep(step, () => prepared.ObserveOriginalDirectoryAsync(entry, token), token);
            var observation = await actual;
            await entry.DisposeAsync();
            Assert.Equal(rig.Project, observation.OriginalDirectoryPath);
            Assert.True(rig.Directories.IsIssuedOriginalDirectoryOutcome(prepared, actual, observation));
            Assert.False(rig.Directories.IsIssuedOriginalDirectoryOutcome(prepared, Task.CompletedTask, observation));
            Assert.False(rig.Directories.IsIssuedOriginalDirectoryOutcome(prepared, actual, new CopiedObservation(rig.Project)));
            await rig.Directories.ValidateOriginalDirectoryOutcomeAsync(prepared, actual, observation, token);
            Assert.Equal(before, File.ReadAllBytes(rig.File)); Assert.Equal(1, permission.EntryStarts);
            Assert.Same(prepared.CloseAndDrainAsync(), prepared.CloseAndDrainAsync());
            Assert.True(prepared.CloseAndDrainAsync().IsCompletedSuccessfully);
        }
        finally { await prepared.DisposeAsync(); }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Replaced_actual_directory_refuses_prior_handle_observation_without_replaying_original_step()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token;
        var capture = await rig.Capture(token); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var step = intent.Steps[1]; var prepared = await rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, step, token);
        try
        {
            Directory.Move(rig.Project, rig.Project + ".old"); Directory.CreateDirectory(rig.Project);
            File.WriteAllText(rig.File, "different physical directory");
            var entry = await permission.EnterOriginalStepAsync(step, token);
            try
            {
                var actual = entry.RunOriginalStep(step, () => prepared.ObserveOriginalDirectoryAsync(entry, token), token);
                await Assert.ThrowsAnyAsync<Exception>(() => actual);
                Assert.True(actual.IsFaulted);
                Assert.Throws<UnauthorizedAccessException>((Action)(() => { _ = prepared.ObserveOriginalDirectoryAsync(entry, token); }));
                Assert.Equal(1, permission.EntryStarts); Assert.Equal("different physical directory", File.ReadAllText(rig.File));
            }
            finally { await entry.DisposeAsync(); }
        }
        finally { await prepared.DisposeAsync(); }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Held_actual_setup_validation_keeps_same_source_close_pending_and_retains_faulted_oce_siblings()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token;
        var capture = await rig.Capture(token); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new OperationCanceledException("Actual setup validation Task is faulted, not canceled.");
        var second = new IOException("Original validation sibling.");
        rig.Setups.Validation = held.Task; rig.Setups.BeforeValidation = () => entered.TrySetResult();
        Task<IDeveloperProjectOriginalDirectoryPreparation>? actual = null; Task? close = null;
        try
        {
            actual = rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, intent.Steps[1], token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            close = rig.Source.CloseAndDrainOriginalCapturesAsync(); Assert.False(close.IsCompleted);
            held.TrySetException([first, second]);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.True(actual.IsFaulted); Assert.Contains(Causes(error), cause => ReferenceEquals(first, cause));
            Assert.Contains(Causes(error), cause => ReferenceEquals(second, cause));
            var cleanup = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(cleanup), cause => ReferenceEquals(first, cause)); Assert.Contains(Causes(cleanup), cause => ReferenceEquals(second, cause));
            Assert.Same(close, rig.Source.CloseAndDrainOriginalCapturesAsync());
            Assert.Equal(0, permission.EntryStarts);
        }
        finally
        {
            held.TrySetException([first, second]); rig.Setups.BeforeValidation = null;
            if (actual is not null) try { await actual; } catch { }
            if (close is not null) try { await close; } catch { }
        }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Restored_actual_held_entry_callback_cannot_join_same_directory_source_original()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token;
        var capture = await rig.Capture(token); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var oldContext = ExecutionContext.Capture(); Exception? refusal = null;
        var prepared = await rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, intent.Steps[1], token);
        try
        {
            var entry = await permission.EnterOriginalStepAsync(intent.Steps[1], token);
            entry.Check = () => ExecutionContext.Run(oldContext!, _ =>
            {
                try { rig.Source.CloseAndDrainOriginalCapturesAsync().GetAwaiter().GetResult(); }
                catch (Exception error) { refusal = error; }
            }, null);
            try
            {
                var actual = entry.RunOriginalStep(intent.Steps[1], () => prepared.ObserveOriginalDirectoryAsync(entry, token), token);
                var observation = await actual;
                Assert.IsType<InvalidOperationException>(refusal);
                Assert.True(rig.Directories.IsIssuedOriginalDirectoryOutcome(prepared, actual, observation));
            }
            finally { entry.Check = null; await entry.DisposeAsync(); }
        }
        finally { await prepared.DisposeAsync(); }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Public_preparation_close_joins_same_held_observation_before_actual_handle_cleanup()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token;
        var capture = await rig.Capture(token); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var step = intent.Steps[1]; var prepared = await rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, step, token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Entry? entry = null; Task<IDeveloperProjectOriginalDirectoryObservation>? actual = null; Task? close = null;
        try
        {
            entry = await permission.EnterOriginalStepAsync(step, token); entry.HeldCheck = held.Task; entry.Check = () => entered.TrySetResult();
            actual = entry.RunOriginalStep(step, () => prepared.ObserveOriginalDirectoryAsync(entry, token), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            close = prepared.CloseAndDrainAsync(); Assert.Same(close, prepared.CloseAndDrainAsync());
            Assert.False(close.IsCompleted); Assert.False(actual.IsCompleted);
            var originalHandle = (Microsoft.Win32.SafeHandles.SafeFileHandle)prepared.GetType().GetField("Handle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(prepared)!;
            Assert.False(originalHandle.IsClosed);
            held.TrySetResult(true); var observation = await actual; await close;
            Assert.True(originalHandle.IsClosed); Assert.True(rig.Directories.IsIssuedOriginalDirectoryOutcome(prepared, actual, observation));
        }
        finally
        {
            held.TrySetResult(true);
            if (actual is not null) try { await actual; } catch { }
            if (close is not null) try { await close; } catch { }
            if (entry is not null) { entry.Check = null; await entry.DisposeAsync(); }
            await prepared.DisposeAsync();
        }
    }

    private static IEnumerable<Exception> Causes(Exception error) => error is AggregateException group && group.InnerExceptions.Count != 0
        ? group.InnerExceptions.SelectMany(Causes) : [error];
    private sealed record CopiedObservation(string OriginalDirectoryPath) : IDeveloperProjectOriginalDirectoryObservation;
    private sealed class Rig : IAsyncDisposable
    {
        internal string Root = Path.Combine(Path.GetTempPath(), "dev-directory-original-" + Guid.NewGuid().ToString("N"));
        internal string Project => Path.Combine(Root, "selected-existing");
        internal string File => Path.Combine(Project, "code.cs");
        internal readonly SelectionSource Selections = new(); internal readonly ReadSource Reads = new();
        internal readonly SetupSource Setups = new();
        internal readonly IDeveloperProjectOriginalPhysicalCaptureSource Source;
        internal IDeveloperProjectOriginalDirectoryObservationSource Directories => (IDeveloperProjectOriginalDirectoryObservationSource)Source;
        internal Rig(IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource? originalBindings = null)
        {
            Directory.CreateDirectory(Project); System.IO.File.WriteAllText(File, "original source stays in place");
            Source = originalBindings is null
                ? new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(Reads, () => Selections, () => Setups)
                : new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(Reads, () => Selections, () => Setups,
                    () => throw new NotSupportedException("This leaf-only fixture issues no workspace metadata store or saved-root authority."),
                    () => originalBindings);
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
    private sealed class Entry(DeveloperProjectSetupStep step) : IDeveloperProjectOriginalSetupStepEntry
    {
        internal DeveloperProjectSetupStep Step => step; internal bool Closed; internal Action? Check; internal Task<bool>? HeldCheck; private bool _used;
        public void DemandOriginalStepEntry(DeveloperProjectSetupStep actual) { if (Closed || !ReferenceEquals(actual, step)) throw new UnauthorizedAccessException(); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); Check?.Invoke(); return HeldCheck is { } original ? new ValueTask<bool>(original) : ValueTask.FromResult(true); }
        public T RunOriginalStep<T>(DeveloperProjectSetupStep actual, Func<T> body, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); if (_used) throw new InvalidOperationException(); _used = true; return body(); }
        public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
    }
}
