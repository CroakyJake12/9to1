using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual configured Home/profile/Files/provider/journal/kernel and real binding
/// write. Synthetic approval issuers do not prove Home review, complete setup or execution
/// trust. Source/Git/Task root stays the selected original, with remaining steps unstarted.</summary>
public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Actual_guarded_project_registration_persists_original_folder_root_and_private_ack_without_copy_or_command_grant()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token);
        var intent = rig.Prepared.Intent; var step = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder);
        var sourceBefore = File.ReadAllBytes(rig.SourcePath); var driveBefore = File.ReadAllBytes(rig.DrivePath);
        var result = await rig.Effector.ExecuteOriginalProjectRegistrationStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
        Assert.True(result.OriginalMetadataResult.IsSuccess); Assert.True(result.OriginalMetadataTask.IsCompletedSuccessfully);
        Assert.Same(step, result.OriginalAdmission.Step); Assert.Same(intent, result.AcknowledgedCheckpoint.Intent);
        var binding = result.OriginalMetadataResult.Value!;
        Assert.Equal(intent.ProjectFolderId, binding.FolderId.Value); Assert.Equal(intent.OriginalExistingProjectRoot, binding.DirectoryPath);
        Assert.Equal("dev.project." + intent.ProjectId.ToString("N"), binding.OwningAppId);
        var workspace = (await rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token))!;
        var reopened = await workspace.Directories.ResolveOriginalRegisteredFolderAsync(Guid.Parse(intent.OriginalActor.ProfileId),
            binding.OwningAppId, binding.FolderId, workspace.Provider, intent.OriginalFilesStoreId, true, _ => ValueTask.CompletedTask, token);
        Assert.Equal(binding, reopened.Value); Assert.Equal(driveBefore, File.ReadAllBytes(rig.DrivePath)); Assert.Equal(sourceBefore, File.ReadAllBytes(rig.SourcePath));
        Assert.True(Directory.Exists(Path.Combine(intent.OriginalExistingProjectRoot!, ".git")));
        var index = intent.Steps.IndexOf(step);
        Assert.All(result.AcknowledgedCheckpoint.Observations.Take(index + 1), value => Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, value.State));
        Assert.All(result.AcknowledgedCheckpoint.Observations.Skip(index + 1), value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
        Assert.True(rig.Effector.IsIssuedOriginalStepOutcome(intent, rig.Capture, step, result.OriginalRegistrationTask, result.OriginalMetadataResult));
        Assert.False(rig.Effector.IsIssuedOriginalStepOutcome(intent, rig.Capture, step, result.OriginalMetadataTask, result.OriginalMetadataResult));
        Assert.Equal(index + 1, rig.Permission.Starts);
    }

    [LinuxDirectoryFact]
    public async Task Reconstructed_effector_cannot_replay_public_prior_ack_rows_into_project_binding_or_new_pending_step()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token);
        var intent = rig.Prepared.Intent; var step = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder);
        var homeBefore = File.ReadAllBytes(rig.HomePath); var starts = rig.Permission.Starts;
        var other = new FilesDeveloperOriginalFolderSetupProducer(rig.Scopes, () => rig.Journal, () => rig.Setups,
            () => (IDeveloperProjectOriginalDirectoryObservationSource)rig.Kernel); var errors = new List<Exception>();
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => other.ExecuteOriginalProjectRegistrationStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token));
            Assert.Contains(Causes(error), value => value is UnauthorizedAccessException);
            Assert.Equal(homeBefore, File.ReadAllBytes(rig.HomePath)); Assert.Equal(starts, rig.Permission.Starts);
            Assert.Equal(DeveloperProjectSetupStepState.NotStarted,
                (await rig.Journal.ReadCheckpointAsync(intent.SetupId, intent.OriginalActor, token))!.Observations[intent.Steps.IndexOf(step)].State);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            try { await other.CloseAndDrainOriginalFolderSetupsAsync(); }
            catch (Exception error) { if (Causes(error).Any(value => value is not UnauthorizedAccessException)) errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Real reconstructed producer/no-replay control and actual close.", errors);
    }

    [LinuxDirectoryFact]
    public async Task Actual_binding_after_held_home_check_carries_post_await_restored_callback_and_raw_child_into_kernel_owner()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeProjectRegistration(rig, token); var context = ExecutionContext.Capture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nested = Task.FromResult(true); Exception? refusal = null; Task<FilesDeveloperOriginalFolderSetupProducer.ProjectRegistrationStep>? actual = null;
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
            var step = rig.Prepared.Intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder);
            actual = rig.Effector.ExecuteOriginalProjectRegistrationStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token); Assert.False(actual.IsCompleted);
            release.TrySetResult(); var result = await actual;
            Assert.IsType<InvalidOperationException>(refusal); Assert.True(result.OriginalMetadataResult.IsSuccess);
            Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, result.AcknowledgedCheckpoint.Observations[rig.Prepared.Intent.Steps.IndexOf(step)].State);
            Assert.True(OriginalKernelTasks(rig.Kernel).Contains(nested));
            Assert.Equal("original selected source", File.ReadAllText(rig.SourcePath));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (actual is not null) try { await actual; } catch (Exception error) { errors.Add(error); }
            rig.Permission.ScopedCheck = null;
        }
        if (errors.Count != 0) throw new AggregateException("Actual binding/scoped held-check control and original join failed.", errors);
    }

    private static IEnumerable<Task> OriginalKernelTasks(IDeveloperProjectOriginalPhysicalCaptureSource kernel)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var preparations = (System.Collections.IEnumerable)kernel.GetType().GetField("_directoryRegistrations", flags)!.GetValue(kernel)!;
        foreach (var preparation in preparations)
        {
            var operation = preparation.GetType().GetField("Operation", flags)!.GetValue(preparation);
            if (operation is null) continue;
            var original = operation.GetType().GetField("Original", flags)!.GetValue(operation)!;
            foreach (var task in (IEnumerable<Task>)original.GetType().GetField("Sources", flags)!.GetValue(original)!) yield return task;
        }
    }

    private static async Task AcknowledgeBeforeProjectRegistration(Rig rig, CancellationToken token)
    {
        foreach (var step in rig.Prepared.Intent.Steps)
        {
            if (step.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder) return;
            if (step.Kind is DeveloperProjectSetupStepKind.CreateProjectFolder or DeveloperProjectSetupStepKind.CreateChildFolder)
                await rig.Effector.ExecuteOriginalFolderStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
            else if (step.Kind is DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory or DeveloperProjectSetupStepKind.ObserveExistingChildDirectory)
                await rig.Effector.ExecuteOriginalExistingDirectoryStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
            else throw new InvalidOperationException("Unexpected original plan prefix; no unsupported setup step is inferred complete.");
        }
        throw new InvalidDataException("The actual retained setup plan has no project registration step.");
    }
}
