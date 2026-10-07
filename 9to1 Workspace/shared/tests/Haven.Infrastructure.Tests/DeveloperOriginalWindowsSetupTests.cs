using Haven.Application;
using Xunit;
using static Haven.Infrastructure.Tests.DeveloperOriginalWindowsTestHost;

namespace Haven.Infrastructure.Tests;

public sealed class DeveloperOriginalWindowsSetupTests
{
    [WindowsNativeFact]
    public async Task Exact_native_create_only_metadata_requires_durability_readback_and_same_task_result_cleanup()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var retained = new List<Task>();
        var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), retained.Add, CancellationToken.None);
        var observation = await write;
        var path = Path.Combine(rig.Store.OriginalWorkspaceMetadataDirectory, original.Intent.WorkspaceId.ToString("N") + ".json");
        Assert.Equal(Document(original.Intent), File.ReadAllBytes(path));
        Assert.True(rig.Metadata.IsIssuedOriginalWorkspaceMetadataOutcome(original.Preparation, write, observation));
        Assert.False(rig.Metadata.IsIssuedOriginalWorkspaceMetadataOutcome(original.Preparation, Task.CompletedTask, observation));
        Assert.False(rig.Metadata.IsIssuedOriginalWorkspaceMetadataOutcome(original.Preparation, write, new CopiedObservation(observation)));
        Assert.All(retained, task => Assert.True(task.IsCompletedSuccessfully)); Assert.True(retained.Count >= 5);
        await entry.DisposeAsync(); var close = original.Preparation.CloseAndDrainAsync(); Assert.Same(close, original.Preparation.CloseAndDrainAsync()); await close;
        await rig.Metadata.ValidateOriginalWorkspaceMetadataOutcomeAsync(original.Preparation, write, observation, CancellationToken.None);
        Assert.Throws<UnauthorizedAccessException>(() => { _ = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), _ => { }, CancellationToken.None); });
        File.WriteAllText(path, "independent replacement after original cleanup");
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Metadata.ValidateOriginalWorkspaceMetadataOutcomeAsync(original.Preparation, write, observation, CancellationToken.None));
    }
    [WindowsNativeFact]
    public async Task Existing_workspace_identity_is_preserved_by_actual_native_create_only_collision()
    {
        await using var rig = new Rig(); var original = await Prepare(rig);
        Directory.CreateDirectory(rig.Store.OriginalWorkspaceMetadataDirectory);
        var path = Path.Combine(rig.Store.OriginalWorkspaceMetadataDirectory, original.Intent.WorkspaceId.ToString("N") + ".json");
        File.WriteAllText(path, "existing independent identity");
        var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var task = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), _ => { }, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => task); Assert.True(task.IsFaulted);
        Assert.Equal("existing independent identity", File.ReadAllText(path));
        await entry.DisposeAsync(); await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync());
    }
    [WindowsNativeFact]
    public async Task Original_held_check_fault_siblings_and_canceled_task_remain_distinct_without_native_effect()
    {
        await using (var rig = new Rig())
        {
            var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
            var first = new OperationCanceledException("original fault, no canceled task"); var second = new IOException("original sibling");
            var failed = new TaskCompletionSource<bool>(); failed.SetException([first, second]); entry.HeldCheck = failed.Task;
            var retained = new List<Task>(); var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), retained.Add, CancellationToken.None);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => write); Assert.True(write.IsFaulted);
            Assert.True(Contains(error, first)); Assert.True(Contains(error, second)); Assert.Contains(failed.Task, retained);
            Assert.False(Directory.Exists(rig.Store.OriginalWorkspaceMetadataDirectory));
            await entry.DisposeAsync(); var closed = await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync()); Assert.True(Contains(closed, first)); Assert.True(Contains(closed, second));
        }
        await using (var rig = new Rig())
        {
            var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var raw = Task.FromCanceled<bool>(cancellation.Token); entry.HeldCheck = raw;
            var retained = new List<Task>(); var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), retained.Add, CancellationToken.None);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
            Assert.True(write.IsCanceled); Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Contains(raw, retained);
            Assert.False(Directory.Exists(rig.Store.OriginalWorkspaceMetadataDirectory)); await entry.DisposeAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync());
        }
    }
    [WindowsNativeFact]
    public async Task Whole_preparation_close_waits_for_actual_admitted_held_check()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var check = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); entry.HeldCheck = check.Task;
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), task => { if (ReferenceEquals(task, check.Task)) acquired.TrySetResult(); }, CancellationToken.None);
        Task? close = null; var errors = new List<Exception>();
        try
        {
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5)); close = original.Preparation.CloseAndDrainAsync();
            Assert.False(close.IsCompleted); Assert.False(write.IsCompleted);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            // A failing wait/assertion must still release THIS original held check
            // before the await-using owner attempts its independent whole drain.
            check.TrySetResult(true);
            try { await write; } catch (Exception error) { errors.AddRange(Causes(error)); }
            try { close ??= original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.AddRange(Causes(error)); }
            if (close is not null) try { await close; } catch (Exception error) { errors.AddRange(Causes(error)); }
            try { await entry.DisposeAsync(); } catch (Exception error) { errors.AddRange(Causes(error)); }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    [WindowsNativeFact]
    public async Task Directory_and_file_registration_use_retained_native_versions_and_exact_original_outcomes()
    {
        await using var rig = new Rig(); var capture = await rig.Capture(CancellationToken.None); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var observeStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory);
        var preparation = await rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, observeStep, CancellationToken.None);
        var observeEntry = await permission.EnterOriginalStepAsync(observeStep, CancellationToken.None);
        var observationTask = preparation.ObserveOriginalDirectoryAsync(observeEntry, CancellationToken.None); var observation = await observationTask;
        await observeEntry.DisposeAsync(); await preparation.CloseAndDrainAsync();
        await rig.Directories.ValidateOriginalDirectoryOutcomeAsync(preparation, observationTask, observation, CancellationToken.None);
        var directories = (IDeveloperProjectOriginalDirectoryRegistrationSource)rig.Source;
        var step = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder);
        var registration = await directories.PrepareOriginalDirectoryRegistrationAsync(intent, capture, permission, step, preparation, observationTask, observation, CancellationToken.None);
        var entry = await permission.EnterOriginalStepAsync(step, CancellationToken.None); var result = new object(); var raw = Task.FromResult(result);
        var driver = registration.RunOriginalDirectoryRegistrationAsync(entry, () => raw, CancellationToken.None); Assert.Same(result, await driver);
        await registration.CloseAndDrainAsync(); await entry.DisposeAsync();
        Assert.True(directories.IsIssuedOriginalDirectoryRegistrationOutcome(registration, driver, raw, result));
        Assert.False(directories.IsIssuedOriginalDirectoryRegistrationOutcome(registration, Task.CompletedTask, raw, result));
        await directories.ValidateOriginalDirectoryRegistrationOutcomeAsync(registration, driver, raw, result, CancellationToken.None);
        var files = (IDeveloperProjectOriginalFileRegistrationSource)rig.Source;
        var fileStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata);
        var file = await files.PrepareOriginalFileRegistrationAsync(intent, capture, permission, fileStep, CancellationToken.None);
        var fileEntry = await permission.EnterOriginalStepAsync(fileStep, CancellationToken.None); FileStream? actualStream = null;
        var fileDriver = file.RunOriginalFileRegistrationAsync(fileEntry, stream => { actualStream = stream; Assert.Equal(0, stream.Position); using var reader = new StreamReader(stream, leaveOpen: true); Assert.Equal("original source stays in place", reader.ReadToEnd()); return raw; }, CancellationToken.None);
        Assert.Same(result, await fileDriver); Assert.NotNull(actualStream);
        await file.CloseAndDrainAsync(); await fileEntry.DisposeAsync();
        Assert.True(files.IsIssuedOriginalFileRegistrationOutcome(file, fileDriver, raw, result));
        Assert.False(files.IsIssuedOriginalFileRegistrationOutcome(file, fileDriver, Task.CompletedTask, result));
        await files.ValidateOriginalFileRegistrationOutcomeAsync(file, fileDriver, raw, result, CancellationToken.None);
    }
    private sealed class CopiedObservation(IDeveloperProjectOriginalWorkspaceMetadataObservation original) : IDeveloperProjectOriginalWorkspaceMetadataObservation
    { public string OriginalCommittedDocument => original.OriginalCommittedDocument; public string OriginalDocumentSha256 => original.OriginalDocumentSha256; }
}
