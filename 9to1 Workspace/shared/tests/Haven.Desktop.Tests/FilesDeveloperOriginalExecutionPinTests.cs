using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Tests;

/// <summary>Real Files/provider/journal/strict store/kernel descriptors. Fixture READ and
/// setup permission are synthetic; these observations grant no Home execution consent.</summary>
public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Genuine_saved_step_retains_exact_actual_journal_ack_and_native_pin_survives_source_edit_only()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        Assert.True(saved.OriginalJournalAcknowledgementTask.IsCompletedSuccessfully);
        Assert.Same(saved.AcknowledgedCheckpoint, await saved.OriginalJournalAcknowledgementTask);
        Assert.True(rig.Effector.IsIssuedOriginalSavedWorkspaceStep(saved));
        var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
        Assert.True(rig.Effector.IsIssuedOriginalBinding(binding)); Assert.Equal(intent.OriginalActor, binding.OriginalActor);
        Assert.Equal(intent.OriginalExistingProjectRoot, binding.CanonicalRoot); Assert.Equal(intent.RootId, binding.RootId);
        Assert.Equal(ResourceAccess.Execute, Assert.Single(rig.Effector.GetOriginalExecutionScopes(binding)).Access);
        IDeveloperWorkspaceOriginalExecutionCommitPin? pin = null; var errors = new List<Exception>();
        try
        {
            pin = await rig.Effector.AcquireOriginalExecutionPinAsync(binding, token);
            Assert.True(rig.Effector.IsIssuedOriginalExecutionPin(binding, pin)); pin.DemandOriginalExecutionBinding();
            File.WriteAllText(rig.SourcePath, "legitimate edit after setup");
            await rig.Effector.RevalidateOriginalAsync(binding, intent.OriginalActor, token);
            pin.DemandOriginalExecutionBinding(); Assert.Equal("legitimate edit after setup", File.ReadAllText(rig.SourcePath));
        }
        catch (Exception error) { errors.Add(error); }
        finally { if (pin is not null) try { await pin.DisposeAsync(); } catch (Exception error) { errors.Add(error); } }
        if (errors.Count != 0) throw new AggregateException(errors);
        Assert.False(rig.Effector.IsIssuedOriginalExecutionPin(binding, pin!));
        Assert.Throws<UnauthorizedAccessException>(() => pin!.DemandOriginalExecutionBinding());
    }

    [LinuxDirectoryFact]
    public async Task Saved_descriptor_pin_refuses_same_bytes_on_a_replacement_inode()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
        IDeveloperWorkspaceOriginalExecutionCommitPin? first = null; var errors = new List<Exception>();
        try
        {
            first = await rig.Effector.AcquireOriginalExecutionPinAsync(binding, token); first.DemandOriginalExecutionBinding();
            var metadata = Path.Combine(rig.WorkspaceStore.OriginalWorkspaceMetadataDirectory, intent.WorkspaceId.ToString("N") + ".json");
            var bytes = File.ReadAllBytes(metadata); var moved = metadata + ".original";
            File.Move(metadata, moved); File.WriteAllBytes(metadata, bytes);
            Assert.Throws<UnauthorizedAccessException>(() => first.DemandOriginalExecutionBinding());
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (first is not null) try { await first.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
    }

    [LinuxDirectoryFact]
    public async Task Actual_registration_descriptor_version_change_refuses_before_native_start()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
        var evidence = rig.Effector.GetOriginalDescriptorEvidence(binding);
        IDeveloperWorkspaceOriginalExecutionCommitPin? pin = null; var errors = new List<Exception>();
        try
        {
            pin = await rig.Effector.AcquireOriginalExecutionPinAsync(binding, token); pin.DemandOriginalExecutionBinding();
            var registration = File.ReadAllBytes(evidence.OriginalRegistrationStatePath);
            File.WriteAllBytes(evidence.OriginalRegistrationStatePath, registration.Concat(new byte[] { (byte)' ' }).ToArray());
            Assert.Throws<IOException>(() => pin.DemandOriginalExecutionBinding());
        }
        catch (Exception error) { errors.Add(error); }
        finally { if (pin is not null) try { await pin.DisposeAsync(); } catch (Exception error) { errors.Add(error); } }
        if (errors.Count != 0) throw new AggregateException(errors);
    }

    [LinuxDirectoryFact]
    public async Task Copied_saved_step_and_root_reference_cannot_issue_binding_and_retired_source_refuses_disclosure()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var copy = new FilesDeveloperOriginalFolderSetupProducer.WorkspaceMetadataStep(saved.OriginalAdmission,
            saved.OriginalStoreTask, saved.OriginalStoreResult, saved.AcknowledgedCheckpoint, saved.OriginalJournalAcknowledgementTask);
        Assert.False(rig.Effector.IsIssuedOriginalSavedWorkspaceStep(copy)); rig.ExpectedFault = true;
        var denied = await Assert.ThrowsAnyAsync<Exception>(() => rig.Effector.AcquireOriginalExecutionBindingAsync(copy, Reference(intent), token));
        Assert.Contains(Causes(denied), value => value is UnauthorizedAccessException);
        denied = await Assert.ThrowsAnyAsync<Exception>(() => rig.Effector.AcquireOriginalExecutionBindingAsync(saved,
            Reference(intent) with { RootId = Guid.NewGuid() }, token));
        Assert.Contains(Causes(denied), value => value is UnauthorizedAccessException);
        var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
        rig.Effector.RequestOriginalFolderSetupRetirement(); Assert.False(rig.Effector.IsIssuedOriginalBinding(binding));
        Assert.False(rig.Effector.IsIssuedOriginalSavedWorkspaceStep(saved));
        Assert.Throws<UnauthorizedAccessException>(() => rig.Effector.GetOriginalDescriptorEvidence(binding));
    }

    [LinuxDirectoryFact]
    public async Task Real_short_registration_scope_rejects_delayed_factory_before_raw_admission()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
        var destination = rig.Scopes.GetOriginalBoundDestination(intent, rig.Capture);
        var raw = new List<Task>(); Action? delayed = null;
        Task<HavenOS.Files.FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot>? actual = null;
        var errors = new List<Exception>();
        try
        {
            actual = destination.Workspace.Directories.ObserveOriginalExecutionRegistrationAsync(Guid.Parse(destination.Workspace.Configuration.ProfileId),
                "dev.project." + intent.ProjectId.ToString("N"), new(intent.ProjectFolderId), destination.Workspace.Provider,
                intent.OriginalFilesStoreId, binding.CanonicalRoot, callback => delayed = callback, raw.Add, token);
            var denied = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.Contains(Causes(denied), value => value is InvalidOperationException);
            Assert.NotNull(delayed); Assert.Throws<InvalidOperationException>(() => delayed!()); Assert.Empty(raw);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (actual is not null) try { await actual; } catch { }
            foreach (var task in raw) try { await task; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        // A genuine fresh read proves the prior direct scope did not strand the store gate.
        await rig.Effector.RevalidateOriginalAsync(binding, intent.OriginalActor, token);
    }

    [LinuxDirectoryFact]
    public async Task Real_short_registration_acquires_and_closes_exact_lease_even_when_enrollment_scope_faults()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
        var destination = rig.Scopes.GetOriginalBoundDestination(intent, rig.Capture);
        var raw = new List<Task>(); var fault = new OperationCanceledException("Actual enrollment fault, no canceled repository Task.");
        var errors = new List<Exception>(); Task? actual = null; var retained = 0;
        try
        {
            actual = destination.Workspace.Directories.ObserveOriginalExecutionRegistrationAsync(Guid.Parse(destination.Workspace.Configuration.ProfileId),
                "dev.project." + intent.ProjectId.ToString("N"), new(intent.ProjectFolderId), destination.Workspace.Provider,
                intent.OriginalFilesStoreId, binding.CanonicalRoot, callback => callback(), task =>
                { raw.Add(task); if (++retained == 3) throw fault; }, token);
            var denied = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.True(actual.IsFaulted); Assert.Contains(Causes(denied), value => ReferenceEquals(value, fault));
            Assert.True(raw[2].IsCompletedSuccessfully); Assert.True(raw.Count >= 4);
            Assert.All(raw, value => Assert.True(value.IsCompletedSuccessfully));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (actual is not null) try { await actual; } catch { }
            foreach (var task in raw) try { await task; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        await rig.Effector.RevalidateOriginalAsync(binding, intent.OriginalActor, token);
    }

    [LinuxDirectoryFact]
    public async Task Actual_canceled_provider_read_remains_canceled_and_retains_the_same_raw_store_task()
    {
        await using var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
        var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
        var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
        var destination = rig.Scopes.GetOriginalBoundDestination(intent, rig.Capture);
        // Control the actual existing provider store semaphore, never a fabricated task or
        // resolver. Reflection observes only this private owning metadata/lifetime boundary.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var store = destination.Workspace.Provider.GetType().GetField("_store", flags)!.GetValue(destination.Workspace.Provider)!;
        var gate = Assert.IsType<SemaphoreSlim>(store.GetType().GetField("_gate", flags)!.GetValue(store));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var raw = new List<Task>(); Task? actual = null; var errors = new List<Exception>(); var held = false;
        try
        {
            await gate.WaitAsync(token); held = true;
            actual = destination.Workspace.Directories.ObserveOriginalExecutionRegistrationAsync(Guid.Parse(destination.Workspace.Configuration.ProfileId),
                "dev.project." + intent.ProjectId.ToString("N"), new(intent.ProjectFolderId), destination.Workspace.Provider,
                intent.OriginalFilesStoreId, binding.CanonicalRoot, callback => callback(), task =>
                { raw.Add(task); if (raw.Count == 1) cancelled.Cancel(); }, cancelled.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            Assert.True(actual.IsCanceled); Assert.NotEmpty(raw); Assert.True(raw[0].IsCanceled);
            Assert.All(raw, task => Assert.True(task.IsCanceled));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (held) gate.Release();
            if (actual is not null) try { await actual; } catch (OperationCanceledException) when (actual.IsCanceled) { }
                catch (Exception error) { errors.Add(error); }
            foreach (var task in raw) try { await task; } catch (OperationCanceledException) when (task.IsCanceled) { }
                catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        await rig.Effector.RevalidateOriginalAsync(binding, intent.OriginalActor, token);
    }

    private static DeveloperProjectReference Reference(DeveloperProjectSetupIntent intent)
        => new(intent.WorkspaceId, 1, intent.ProjectId, 1, intent.RootId);
}
