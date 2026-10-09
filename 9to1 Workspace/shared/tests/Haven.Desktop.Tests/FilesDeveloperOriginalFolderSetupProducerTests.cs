using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperOriginalSetupScopeTests
{
    [Fact]
    public async Task Genuine_provider_folder_step_and_entry_close_precede_same_journal_ack_without_whole_setup_success()
    {
        await using var rig = await Rig.Create(); var token = TestContext.Current.CancellationToken;
        var issuer = await AttachFolderProducer(rig); var destination = await rig.Owner.CaptureOriginalDestinationAsync(rig.Workspace.Configuration.StoreId, rig.Folder.Id, token);
        var prepared = await rig.Prepare(destination, token); await rig.Owner.BindOriginalAsync(destination, prepared, rig.Capture, token);
        var permission = issuer.Issue(prepared.Intent, rig.Capture);
        var step = prepared.Intent.Steps[0]; var result = await rig.FolderProducer!.ExecuteOriginalFolderStepAsync(prepared, rig.Capture, permission, step, token);
        Assert.Same(prepared.Intent, result.AcknowledgedCheckpoint.Intent); Assert.Same(step, result.OriginalAdmission.Step);
        Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, result.AcknowledgedCheckpoint.Observations[0].State);
        Assert.All(result.AcknowledgedCheckpoint.Observations.Skip(1), value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
        Assert.True(result.OriginalProviderResult.IsSuccess); Assert.Equal(FilesOperationState.Committed, result.OriginalProviderResult.Value!.State);
        Assert.Equal(step.StepId, result.OriginalProviderResult.Value.Id.Value);
        var actualFolder = await rig.Workspace.Provider.GetForOriginalStoreAsync(destination.OriginalStoreId, new(prepared.Intent.ProjectFolderId), token);
        Assert.True(actualFolder.IsSuccess); Assert.Equal("ExistingProject", actualFolder.Value!.Name);
        Assert.Equal(rig.Folder.Id, actualFolder.Value.ParentId);
        Assert.True(permission.LastEntry!.Closed); Assert.Equal(1, permission.Validations);
        Assert.True(rig.FolderProducer.IsIssuedOriginalStepOutcome(prepared.Intent, rig.Capture, step, result.OriginalProviderTask, result.OriginalProviderResult));
        Assert.False(rig.FolderProducer.IsIssuedOriginalStepOutcome(prepared.Intent with { }, rig.Capture, step, result.OriginalProviderTask, result.OriginalProviderResult));
        Assert.Equal(0, rig.Captures.ResourceEffects); // No source copy/content effect belongs to the synthetic capture issuer.
    }

    [Fact]
    public async Task Actual_committed_folder_with_failed_entry_cleanup_keeps_pending_and_refuses_same_step_replay()
    {
        await using var rig = await Rig.Create(); var token = TestContext.Current.CancellationToken;
        var issuer = await AttachFolderProducer(rig); var destination = await rig.Owner.CaptureOriginalDestinationAsync(rig.Workspace.Configuration.StoreId, rig.Folder.Id, token);
        var prepared = await rig.Prepare(destination, token); await rig.Owner.BindOriginalAsync(destination, prepared, rig.Capture, token);
        var permission = issuer.Issue(prepared.Intent, rig.Capture); var first = new OperationCanceledException("Faulted original entry cleanup; no actual cancellation.");
        var second = new IOException("Original entry sibling cleanup failure."); var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        failed.SetException([first, second]); permission.Cleanup = failed.Task; rig.ExpectScopeFault = true;
        var step = prepared.Intent.Steps[0]; var actual = rig.FolderProducer!.ExecuteOriginalFolderStepAsync(prepared, rig.Capture, permission, step, token);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.True(actual.IsFaulted); Assert.Contains(Causes(error), value => ReferenceEquals(first, value)); Assert.Contains(Causes(error), value => ReferenceEquals(second, value));
        var created = await rig.Workspace.Provider.GetForOriginalStoreAsync(destination.OriginalStoreId, new(prepared.Intent.ProjectFolderId), token);
        Assert.True(created.IsSuccess); Assert.Equal(DeveloperProjectSetupStepState.Admitted, (await rig.Journal.ReadCheckpointAsync(prepared.Intent.SetupId, prepared.Intent.OriginalActor, token))!.Observations[0].State);
        Assert.Equal(0, permission.Validations);
        var before = await File.ReadAllBytesAsync(rig.DrivePath, token);
        await AssertCause<InvalidOperationException>(() => rig.FolderProducer.ExecuteOriginalFolderStepAsync(prepared, rig.Capture, permission, step, token));
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.DrivePath, token)); Assert.Equal(1, permission.Entries);
        var close = rig.FolderProducer.CloseAndDrainOriginalFolderSetupsAsync(); Assert.Same(close, rig.FolderProducer.CloseAndDrainOriginalFolderSetupsAsync());
        var closeError = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.Contains(Causes(closeError), value => ReferenceEquals(first, value)); Assert.Contains(Causes(closeError), value => ReferenceEquals(second, value));
    }

    [Fact]
    public async Task Unsupported_physical_step_refuses_before_pending_or_home_entry_admission()
    {
        await using var rig = await Rig.Create(); var token = TestContext.Current.CancellationToken;
        var issuer = await AttachFolderProducer(rig); var destination = await rig.Owner.CaptureOriginalDestinationAsync(rig.Workspace.Configuration.StoreId, rig.Folder.Id, token);
        var prepared = await rig.Prepare(destination, token); await rig.Owner.BindOriginalAsync(destination, prepared, rig.Capture, token);
        var permission = issuer.Issue(prepared.Intent, rig.Capture); var home = await File.ReadAllBytesAsync(rig.HomePath, token); var drive = await File.ReadAllBytesAsync(rig.DrivePath, token);
        rig.ExpectScopeFault = true;
        await AssertCause<NotSupportedException>(() => rig.FolderProducer!.ExecuteOriginalFolderStepAsync(prepared, rig.Capture, permission, prepared.Intent.Steps[1], token));
        Assert.Equal(home, await File.ReadAllBytesAsync(rig.HomePath, token)); Assert.Equal(drive, await File.ReadAllBytesAsync(rig.DrivePath, token));
        Assert.Equal(0, permission.Entries); Assert.Equal(0, permission.Validations);
        Assert.All((await rig.Journal.ReadCheckpointAsync(prepared.Intent.SetupId, prepared.Intent.OriginalActor, token))!.Observations,
            value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
    }

    private static async Task<SetupPermissions> AttachFolderProducer(Rig rig)
    {
        await rig.Journal.CloseAndDrainAsync();
        var issuer = new SetupPermissions(); FilesDeveloperOriginalFolderSetupProducer? producer = null;
        rig.Journal = new HomeDeveloperProjectSetupJournal(rig.Home, rig.Profiles, rig.Captures, () => producer!);
        rig.FolderProducer = producer = new(rig.Owner, () => rig.Journal, () => issuer); issuer.Outcomes = producer; return issuer;
    }
    // Trusted configured synthetic issuer; it makes no actual Home broker/claim assertion.
    // Real provider effects/entry-final-guard/receipt and journal tasks are exercised separately.
    private sealed class SetupPermissions : IDeveloperProjectOriginalSetupPermissionSource
    {
        internal IDeveloperProjectOriginalSetupStepOutcomeSource Outcomes = null!;
        private readonly HashSet<SetupPermission> _issued = [];
        internal SetupPermission Issue(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
        { var permission = new SetupPermission(this, intent) { CaptureForValidation = capture }; _issued.Add(permission); return permission; }
        public Task<IDeveloperProjectOriginalSetupPermission> AcquireOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, CancellationToken token)
            => throw new NotSupportedException("This fixture supplies no Home review acquisition.");
        public Task ValidateOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSetupPermission permission, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (permission is not SetupPermission actual || !_issued.Contains(actual) || !ReferenceEquals(actual.Intent, intent)) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public void RequestOriginalSetupRetirement() { }
        public void DemandExternalOriginalSetupJoin() { }
        public Task CloseAndDrainOriginalSetupsAsync() => Task.CompletedTask;
    }
    private sealed class SetupPermission(SetupPermissions issuer, DeveloperProjectSetupIntent intent) : IDeveloperProjectOriginalSetupPermission
    {
        internal DeveloperProjectSetupIntent Intent => intent;
        internal int Entries, Validations; internal Task Cleanup = Task.CompletedTask; internal SetupEntry? LastEntry;
        public Task<IDeveloperProjectOriginalSetupStepEntry> EnterOriginalStepAsync(DeveloperProjectSetupStep step, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!intent.Steps.Any(value => ReferenceEquals(value, step))) throw new UnauthorizedAccessException(); Entries++; return Task.FromResult<IDeveloperProjectOriginalSetupStepEntry>(LastEntry = new(step, Cleanup)); }
        public Task ValidateOriginalStepResultAsync(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry, Task actual, object? result, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!ReferenceEquals(entry, LastEntry) || LastEntry?.Closed != true || !issuer.Outcomes.IsIssuedOriginalStepOutcome(intent, CaptureForValidation!, step, actual, result)) throw new UnauthorizedAccessException(); Validations++; return Task.CompletedTask; }
        internal IDeveloperProjectOriginalSourceCapture? CaptureForValidation;
        public bool IsIssuedOriginalStepEntry(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry)
            => ReferenceEquals(entry, LastEntry) && LastEntry?.Closed == false && intent.Steps.Any(value => ReferenceEquals(value, step));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class SetupEntry(DeveloperProjectSetupStep step, Task cleanup) : IDeveloperProjectOriginalSetupStepEntry
    {
        internal bool Closed; private bool _used;
        public void DemandOriginalStepEntry(DeveloperProjectSetupStep actual)
        { if (Closed || !ReferenceEquals(actual, step)) throw new UnauthorizedAccessException(); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); return ValueTask.FromResult(true); }
        public T RunOriginalStep<T>(DeveloperProjectSetupStep actual, Func<T> source, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); if (_used) throw new InvalidOperationException(); _used = true; return source(); }
        public async ValueTask DisposeAsync()
        { try { await cleanup.ConfigureAwait(false); } catch when (cleanup.IsFaulted) { throw cleanup.Exception!; } Closed = true; }
    }
}
