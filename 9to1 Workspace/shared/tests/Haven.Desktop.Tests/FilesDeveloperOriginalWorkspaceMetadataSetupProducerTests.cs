using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Tests;

/// <summary>Real configured Files/provider/journal/native metadata/store sequence.
/// The read and high-risk setup issuers are synthetic. These controls do not certify
/// an installed Home review, final Home completion or execution trust.</summary>
public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Actual_workspace_save_reopens_same_project_file_ids_root_and_full_ack_lineage_without_source_copy_or_replay()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var before = File.ReadAllBytes(rig.SourcePath); var step = intent.Steps[^1];
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
        Assert.Same(intent, saved.AcknowledgedCheckpoint.Intent); Assert.Same(step, saved.OriginalAdmission.Step);
        Assert.All(saved.AcknowledgedCheckpoint.Observations, value => Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, value.State));
        Assert.True(rig.Effector.IsIssuedOriginalStepOutcome(intent, rig.Capture, step, saved.OriginalStoreTask, saved.OriginalStoreResult));
        Assert.False(rig.Effector.IsIssuedOriginalStepOutcome(intent, rig.Capture, step, saved.OriginalStoreResult.OriginalNativeWriteTask, saved.OriginalStoreResult));
        Assert.False(rig.Effector.IsIssuedOriginalStepOutcome(intent, rig.Capture, step, saved.OriginalStoreTask, saved.OriginalStoreResult with { }));
        var reopened = await new FileDeveloperWorkspaceStore(Path.Combine(rig.Root, "Dev")).GetAsync(intent.WorkspaceId, token);
        Assert.True(reopened.Succeeded); Assert.Equal(intent.WorkspaceId, reopened.Value!.WorkspaceId); Assert.Equal(1, reopened.Value.Revision);
        var root = Assert.Single(reopened.Value.Roots); Assert.Equal(intent.RootId, root.RootId); Assert.Equal(intent.OriginalExistingProjectRoot, root.Location);
        var project = Assert.Single(reopened.Value.Projects); Assert.Equal(intent.ProjectId, project.ProjectId); Assert.Equal(intent.RootId, Assert.Single(project.RootIds));
        var file = Assert.Single(intent.Files); var editor = Assert.Single(reopened.Value.OpenEditors);
        Assert.Equal(file.FileId, editor.FileId); Assert.Equal(intent.ProjectId, editor.ProjectId); Assert.Equal(file.FileId.ToString("D"), editor.CanonicalResourceId);
        Assert.Empty(reopened.Value.Toolchains); Assert.Empty(reopened.Value.Tasks); Assert.Empty(reopened.Value.SourceControlBindings);
        Assert.Equal(before, File.ReadAllBytes(rig.SourcePath)); Assert.True(Directory.Exists(Path.Combine(root.Location, ".git")));
        var document = File.ReadAllBytes(Path.Combine(rig.WorkspaceStore.OriginalWorkspaceMetadataDirectory, intent.WorkspaceId.ToString("N") + ".json"));
        var starts = rig.Permission.Starts; rig.ExpectedFault = true;
        var denied = await Assert.ThrowsAnyAsync<Exception>(() => rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(
            rig.Prepared, rig.Capture, rig.Permission, step, token));
        Assert.Contains(Causes(denied), cause => cause is InvalidOperationException);
        Assert.Equal(starts, rig.Permission.Starts); Assert.Equal(document, File.ReadAllBytes(Path.Combine(rig.WorkspaceStore.OriginalWorkspaceMetadataDirectory, intent.WorkspaceId.ToString("N") + ".json")));
    }

    [LinuxDirectoryFact]
    public async Task Missing_strict_workspace_source_refuses_before_final_pending_entry_or_metadata_creation()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var home = File.ReadAllBytes(rig.HomePath); var starts = rig.Permission.Starts; rig.ExpectedFault = true;
        var actual = rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var denied = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.Contains(Causes(denied), cause => cause is InvalidOperationException);
        Assert.Equal(home, File.ReadAllBytes(rig.HomePath)); Assert.Equal(starts, rig.Permission.Starts);
        Assert.False(Directory.Exists(rig.WorkspaceStore.OriginalWorkspaceMetadataDirectory));
        Assert.Equal(DeveloperProjectSetupStepState.NotStarted,
            (await rig.Journal.ReadCheckpointAsync(intent.SetupId, intent.OriginalActor, token))!.Observations[^1].State);
    }

    [LinuxDirectoryFact]
    public async Task Held_actual_workspace_check_keeps_same_parent_close_pending_and_restored_callback_cannot_self_join()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var context = ExecutionContext.Capture()!; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FilesDeveloperOriginalFolderSetupProducer.WorkspaceMetadataStep>? actual = null; Task? close = null;
        Exception? refused = null; var errors = new List<Exception>(); var once = 0;
        rig.Permission.ScopedCheck = async (step, scope, retain, ct) =>
        {
            if (Interlocked.Increment(ref once) == 1)
            {
                entered.TrySetResult(); await release.Task;
                scope(() => ExecutionContext.Run(context, _ =>
                {
                    try { rig.Effector.CloseAndDrainOriginalFolderSetupsAsync().GetAwaiter().GetResult(); }
                    catch (Exception error) { refused = error; }
                }, null));
            }
            ct.ThrowIfCancellationRequested(); return true;
        };
        try
        {
            actual = rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            close = rig.Effector.CloseAndDrainOriginalFolderSetupsAsync(); Assert.Same(close, rig.Effector.CloseAndDrainOriginalFolderSetupsAsync());
            Assert.False(close.IsCompleted); Assert.False(actual.IsCompleted);
            release.TrySetResult(); var result = await actual; await close;
            Assert.IsType<InvalidOperationException>(refused); Assert.True(result.OriginalStoreTask.IsCompletedSuccessfully);
            Assert.True(result.OriginalStoreResult.OriginalNativeWriteTask.IsCompletedSuccessfully);
            Assert.All(result.AcknowledgedCheckpoint.Observations, value => Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, value.State));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (actual is not null) try { await actual; } catch (Exception error) { errors.Add(error); }
            if (close is not null) try { await close; } catch (Exception error) { errors.Add(error); }
            rig.Permission.ScopedCheck = null;
        }
        if (errors.Count != 0) throw new AggregateException("Actual held workspace check/parent drain/native cleanup control failed.", errors);
    }

    private static async Task AcknowledgeBeforeWorkspaceSave(Rig rig, CancellationToken token)
    {
        await AcknowledgeBeforeProjectRegistration(rig, token); var intent = rig.Prepared.Intent;
        await rig.Effector.ExecuteOriginalProjectRegistrationStepAsync(rig.Prepared, rig.Capture, rig.Permission,
            intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder), token);
        foreach (var step in intent.Steps.Where(value => value.Kind is DeveloperProjectSetupStepKind.RegisterExistingFileMetadata or DeveloperProjectSetupStepKind.RegisterMaterialization))
            await rig.Effector.ExecuteOriginalExistingFileStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
    }
}
