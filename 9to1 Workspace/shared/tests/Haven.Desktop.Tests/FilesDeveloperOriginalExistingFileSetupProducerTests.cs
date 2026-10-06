using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Real configured Files metadata/mapping/journal/kernel writes. Read/setup
/// approval issuers remain synthetic; saved Dev workspace, execution trust and complete
/// setup are separate unimplemented stages. No immutable historical bytes are created.</summary>
public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Actual_existing_file_metadata_and_mapping_preserve_once_created_ids_original_bytes_and_unavailable_immutable_revision()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token);
        var intent = rig.Prepared.Intent;
        var registration = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder);
        await rig.Effector.ExecuteOriginalProjectRegistrationStepAsync(rig.Prepared, rig.Capture, rig.Permission, registration, token);
        var file = Assert.Single(intent.Files); var originalBytes = File.ReadAllBytes(rig.SourcePath);
        var metadataStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata);
        var mappedStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterMaterialization);
        var metadata = await rig.Effector.ExecuteOriginalExistingFileStepAsync(rig.Prepared, rig.Capture, rig.Permission, metadataStep, token);
        var revision = Assert.IsType<FilesResult<FilesRevision>>(metadata.OriginalMetadataResult);
        Assert.True(revision.IsSuccess); Assert.Equal(file.FileId, revision.Value!.ItemId.Value); Assert.Equal(file.RevisionId, revision.Value.Id.Value);
        Assert.Equal("sha256:" + file.ContentSha256, revision.Value.ContentHash);
        var mapped = await rig.Effector.ExecuteOriginalExistingFileStepAsync(rig.Prepared, rig.Capture, rig.Permission, mappedStep, token);
        var proof = Assert.IsType<FilesMaterializationProof>(mapped.OriginalMetadataResult);
        Assert.Equal(file.FileId, proof.ItemId.Value); Assert.Equal(file.RevisionId, proof.RemoteRevisionId.Value);
        Assert.Same(intent, metadata.AcknowledgedCheckpoint.Intent); Assert.Same(intent, mapped.AcknowledgedCheckpoint.Intent);
        Assert.True(rig.Effector.IsIssuedOriginalStepOutcome(intent, rig.Capture, mappedStep, mapped.OriginalKernelTask, proof));
        Assert.False(rig.Effector.IsIssuedOriginalStepOutcome(intent, rig.Capture, mappedStep, mapped.OriginalMetadataTask, proof));
        var workspace = (await rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token))!;
        var current = await workspace.Provider.GetForOriginalStoreAtRevisionAsync(intent.OriginalFilesStoreId,
            new(file.FileId), new(file.RevisionId), token);
        Assert.True(current.IsSuccess); Assert.Equal(file.ParentFolderId, current.Value!.ParentId!.Value.Value);
        var existing = await workspace.Materializations.GetExistingByItemIdAsync(new(file.FileId), token);
        Assert.NotNull(existing); Assert.Equal(rig.SourcePath, existing.LocalPath); Assert.Equal(file.RevisionId, existing.BaseRemoteRevisionId.Value);
        var reopened = new FilesMaterializationRegistry(workspace.Configuration.RootDirectory,
            Path.Combine(workspace.Configuration.RootDirectory, ".9to1-files", "materializations.json"));
        Assert.Equal(existing, await reopened.GetExistingByItemIdAsync(new(file.FileId), token));
        var historical = await workspace.Provider.GetArtifactRevisionContentAsync(new(file.FileId), new(file.RevisionId), token);
        Assert.False(historical.IsSuccess); Assert.Equal(FilesErrorCode.InvalidState, historical.Error!.Code);
        Assert.Equal(originalBytes, File.ReadAllBytes(rig.SourcePath)); Assert.True(Directory.Exists(Path.Combine(intent.OriginalExistingProjectRoot!, ".git")));
        Assert.All(mapped.AcknowledgedCheckpoint.Observations.Take(intent.Steps.IndexOf(mappedStep) + 1),
            value => Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, value.State));
        Assert.Equal(DeveloperProjectSetupStepState.NotStarted, Assert.Single(mapped.AcknowledgedCheckpoint.Observations.Skip(intent.Steps.IndexOf(mappedStep) + 1)).State);
        Assert.Equal(DeveloperProjectSetupStepKind.SaveDevWorkspace, intent.Steps[^1].Kind);
    }

    [LinuxDirectoryFact]
    public async Task Actual_file_write_after_held_home_check_joins_same_native_stream_and_refuses_restored_callback_self_join()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token);
        var intent = rig.Prepared.Intent;
        await rig.Effector.ExecuteOriginalProjectRegistrationStepAsync(rig.Prepared, rig.Capture, rig.Permission,
            intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder), token);
        var context = ExecutionContext.Capture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var nested = Task.FromResult(true);
        Task<FilesDeveloperOriginalFolderSetupProducer.ExistingFileStep>? actual = null; Exception? refusal = null;
        var errors = new List<Exception>(); var once = 0;
        rig.Permission.ScopedCheck = async (step, scope, retain, ct) =>
        {
            if (Interlocked.Increment(ref once) == 1)
            {
                entered.TrySetResult(); await release.Task;
                scope(() => ExecutionContext.Run(context!, _ =>
                {
                    retain(nested);
                    try { rig.Kernel.CloseAndDrainOriginalCapturesAsync().GetAwaiter().GetResult(); }
                    catch (Exception error) { refusal = error; }
                }, null));
                await nested;
            }
            ct.ThrowIfCancellationRequested(); return true;
        };
        try
        {
            var step = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata);
            actual = rig.Effector.ExecuteOriginalExistingFileStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token); Assert.False(actual.IsCompleted);
            var filePreparation = SingleOriginalFilePreparation(rig.Kernel);
            var handle = (Microsoft.Win32.SafeHandles.SafeFileHandle)filePreparation.GetType().GetField("Handle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(filePreparation)!;
            Assert.False(handle.IsClosed);
            release.TrySetResult(); var result = await actual;
            Assert.IsType<InvalidOperationException>(refusal); Assert.True(handle.IsClosed);
            Assert.Contains(nested, OriginalFileKernelTasks(filePreparation));
            Assert.True(Assert.IsType<FilesResult<FilesRevision>>(result.OriginalMetadataResult).IsSuccess);
            Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, result.AcknowledgedCheckpoint.Observations[intent.Steps.IndexOf(step)].State);
            Assert.Equal("original selected source", File.ReadAllText(rig.SourcePath));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (actual is not null) try { await actual; } catch (Exception error) { errors.Add(error); }
            rig.Permission.ScopedCheck = null;
        }
        if (errors.Count != 0) throw new AggregateException("Actual file write/held Home check/native task cleanup control failed.", errors);
    }

    private static object SingleOriginalFilePreparation(IDeveloperProjectOriginalPhysicalCaptureSource kernel)
    {
        var field = kernel.GetType().GetField("_fileRegistrations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return Assert.Single(((System.Collections.IEnumerable)field.GetValue(kernel)!).Cast<object>());
    }
    private static Task[] OriginalFileKernelTasks(object preparation)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var operation = preparation.GetType().GetField("Operation", flags)!.GetValue(preparation)!;
        var original = operation.GetType().GetField("Original", flags)!.GetValue(operation)!;
        return ((IEnumerable<Task>)original.GetType().GetField("Sources", flags)!.GetValue(original)!).ToArray();
    }

    [LinuxDirectoryFact]
    public async Task Actual_local_metadata_update_is_retained_and_joined_even_when_parent_scope_faults_after_factory_return()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token);
        var intent = rig.Prepared.Intent; var file = Assert.Single(intent.Files);
        var workspace = (await rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token))!;
        var folder = (await workspace.Provider.GetForOriginalStoreAsync(intent.OriginalFilesStoreId, new(file.ParentFolderId), token)).Value!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopeFailure = new IOException("Exact parent scope failed after returning its actual metadata Update task.");
        var raw = new List<Task>(); var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FilesResult<FilesRevision>>? actual = null; var errors = new List<Exception>();
        var authority = new FilesCommitAuthorityGuard(intent.OriginalActor.ActorId, async ct =>
        { entered.TrySetResult(); await release.Task; ct.ThrowIfCancellationRequested(); return true; });
        try
        {
            var observed = new FilesOriginalLocalDeveloperSource(new(file.FileId), new(file.ParentFolderId), "Code.cs", null,
                new(file.RevisionId), intent.OriginalActor.ActorId, DateTimeOffset.UtcNow, file.SizeBytes, file.ContentSha256);
            actual = workspace.Provider.RegisterOriginalLocalDeveloperFileAsync(observed,
                [new(new(file.ParentFolderId), folder.CurrentRevisionId)], intent.OriginalFilesStoreId, authority,
                callback => { callback(); throw scopeFailure; }, task => { raw.Add(task); enrolled.TrySetResult(); }, token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.False(actual.IsCompleted); Assert.False(Assert.Single(raw).IsCompleted);
            release.TrySetResult(); var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, scopeFailure)); Assert.True(Assert.Single(raw).IsCompletedSuccessfully);
            var committed = await workspace.Provider.GetForOriginalStoreAtRevisionAsync(intent.OriginalFilesStoreId,
                new(file.FileId), new(file.RevisionId), token);
            Assert.True(committed.IsSuccess); Assert.Equal(file.FileId, committed.Value!.Id.Value);
            Assert.Equal(DeveloperProjectSetupStepState.NotStarted,
                (await rig.Journal.ReadCheckpointAsync(intent.SetupId, intent.OriginalActor, token))!.Observations[
                    intent.Steps.IndexOf(intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata))].State);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (actual is not null) try { await actual; } catch (Exception error) { if (Causes(error).Any(cause => !ReferenceEquals(cause, scopeFailure))) errors.Add(error); }
            foreach (var task in raw) try { await task; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual raw metadata Update/post-scope failure custody control failed.", errors);
    }

    [LinuxDirectoryFact]
    public async Task Actual_materialization_hash_and_update_survive_post_scope_fault_without_disposing_borrowed_content_or_acknowledging_setup()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        var intent = rig.Prepared.Intent; var file = Assert.Single(intent.Files);
        var workspace = (await rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token))!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopeFailure = new IOException("Exact parent scope failed after returning materialization Update task.");
        var raw = new List<Task>(); var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FilesMaterializationProof>? actual = null; FileStream? stream = null;
        var errors = new List<Exception>(); var calls = 0;
        var authority = new FilesCommitAuthorityGuard(intent.OriginalActor.ActorId, async ct =>
        { entered.TrySetResult(); await release.Task; ct.ThrowIfCancellationRequested(); return true; });
        try
        {
            stream = new FileStream(rig.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var proof = new FilesMaterializationProof(new(file.FileId), new(file.RevisionId), "sha256:" + file.ContentSha256, file.SizeBytes, DateTimeOffset.UtcNow);
            actual = workspace.Materializations.RegisterOriginalDeveloperMaterializationAsync(rig.SourcePath, stream, proof,
                SyncAvailability.AvailableOffline, authority, callback =>
                { callback(); if (Interlocked.Increment(ref calls) == 2) throw scopeFailure; }, task => { raw.Add(task); if (raw.Count == 2) enrolled.TrySetResult(); }, token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), token); Assert.False(actual.IsCompleted);
            Assert.Equal(2, raw.Count); Assert.True(raw[0].IsCompletedSuccessfully); Assert.False(raw[1].IsCompleted);
            release.TrySetResult(); var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, scopeFailure)); Assert.True(raw[1].IsCompletedSuccessfully);
            Assert.True(stream.CanRead); Assert.False(stream.SafeFileHandle.IsClosed);
            Assert.NotNull(await workspace.Materializations.GetExistingByItemIdAsync(new(file.FileId), token));
            Assert.All((await rig.Journal.ReadCheckpointAsync(intent.SetupId, intent.OriginalActor, token))!.Observations,
                value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (actual is not null) try { await actual; } catch (Exception error) { if (Causes(error).Any(cause => !ReferenceEquals(cause, scopeFailure))) errors.Add(error); }
            foreach (var task in raw) try { await task; } catch (Exception error) { errors.Add(error); }
            if (stream is not null) try { await stream.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual borrowed materialization/hash/raw Update control failed.", errors);
    }
    [LinuxDirectoryFact]
    public async Task Delayed_original_source_scope_cannot_start_metadata_update_after_its_owner_has_settled()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token); var intent = rig.Prepared.Intent; var file = Assert.Single(intent.Files);
        var workspace = (await rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token))!;
        var folder = (await workspace.Provider.GetForOriginalStoreAsync(intent.OriginalFilesStoreId, new(file.ParentFolderId), token)).Value!;
        var observed = new FilesOriginalLocalDeveloperSource(new(file.FileId), new(file.ParentFolderId), "Code.cs", null,
            new(file.RevisionId), intent.OriginalActor.ActorId, DateTimeOffset.UtcNow, file.SizeBytes, file.ContentSha256);
        Action? delayed = null; var raw = new List<Task>(); var originalBytes = File.ReadAllBytes(rig.SourcePath);
        var authority = new FilesCommitAuthorityGuard(intent.OriginalActor.ActorId, ct => ValueTask.FromResult(true));
        Task<FilesResult<FilesRevision>>? actual = null; Exception? expected = null; var errors = new List<Exception>();
        try
        {
            actual = workspace.Provider.RegisterOriginalLocalDeveloperFileAsync(observed,
                [new(new(file.ParentFolderId), folder.CurrentRevisionId)], intent.OriginalFilesStoreId, authority,
                callback => delayed = callback, raw.Add, token);
            expected = await Assert.ThrowsAnyAsync<Exception>(() => actual); Assert.NotNull(delayed); Assert.Empty(raw);
            Assert.Throws<InvalidOperationException>(() => { delayed!(); }); Assert.Empty(raw);
            var absent = await workspace.Provider.GetForOriginalStoreAsync(intent.OriginalFilesStoreId, new(file.FileId), token);
            Assert.False(absent.IsSuccess); Assert.Equal(originalBytes, File.ReadAllBytes(rig.SourcePath));
            Assert.Equal(DeveloperProjectSetupStepState.NotStarted,
                (await rig.Journal.ReadCheckpointAsync(intent.SetupId, intent.OriginalActor, token))!.Observations[
                    intent.Steps.IndexOf(intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata))].State);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (actual is not null) try { await actual; } catch (Exception error)
            { if (expected is null || !Causes(error).All(cause => Causes(expected).Any(known => ReferenceEquals(cause, known)))) errors.Add(error); }
            // A deliberately regressed delayed callback may acquire a real Update Task even
            // after the primary assertion fails. Join every such original before rig teardown.
            foreach (var task in raw) try { await task; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Delayed source refusal and independent actual acquired-task cleanup failed.", errors);
    }

    [LinuxDirectoryFact]
    public async Task Repeated_original_source_scope_refuses_second_factory_and_joins_the_actual_first_metadata_update()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token); var intent = rig.Prepared.Intent; var file = Assert.Single(intent.Files);
        var workspace = (await rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token))!;
        var folder = (await workspace.Provider.GetForOriginalStoreAsync(intent.OriginalFilesStoreId, new(file.ParentFolderId), token)).Value!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var raw = new List<Task>();
        Task<FilesResult<FilesRevision>>? actual = null; var errors = new List<Exception>(); Exception? refused = null;
        var authority = new FilesCommitAuthorityGuard(intent.OriginalActor.ActorId, async ct =>
        { entered.TrySetResult(); await release.Task; ct.ThrowIfCancellationRequested(); return true; });
        try
        {
            var observed = new FilesOriginalLocalDeveloperSource(new(file.FileId), new(file.ParentFolderId), "Code.cs", null,
                new(file.RevisionId), intent.OriginalActor.ActorId, DateTimeOffset.UtcNow, file.SizeBytes, file.ContentSha256);
            actual = workspace.Provider.RegisterOriginalLocalDeveloperFileAsync(observed,
                [new(new(file.ParentFolderId), folder.CurrentRevisionId)], intent.OriginalFilesStoreId, authority,
                callback => { callback(); callback(); }, task => { raw.Add(task); enrolled.TrySetResult(); }, token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token); await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.False(actual.IsCompleted); Assert.False(Assert.Single(raw).IsCompleted);
            release.TrySetResult(); refused = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.Contains(Causes(refused), cause => cause is InvalidOperationException && cause.Message.Contains("single-use", StringComparison.Ordinal));
            Assert.True(Assert.Single(raw).IsCompletedSuccessfully);
            var committed = await workspace.Provider.GetForOriginalStoreAtRevisionAsync(intent.OriginalFilesStoreId,
                new(file.FileId), new(file.RevisionId), token);
            Assert.True(committed.IsSuccess); Assert.Equal(file.FileId, committed.Value!.Id.Value);
            Assert.Equal(DeveloperProjectSetupStepState.NotStarted,
                (await rig.Journal.ReadCheckpointAsync(intent.SetupId, intent.OriginalActor, token))!.Observations[
                    intent.Steps.IndexOf(intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata))].State);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (actual is not null) try { await actual; } catch (Exception error) { if (refused is null || !Causes(error).SequenceEqual(Causes(refused), ReferenceEqualityComparer.Instance)) errors.Add(error); }
            foreach (var task in raw) try { await task; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual one-use metadata source/raw first-task custody control failed.", errors);
    }
}
