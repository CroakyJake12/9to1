using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
{
    public sealed class ProjectRegistrationStep
    {
        internal ProjectRegistrationStep(HomeDeveloperProjectSetupJournal.OriginalStepAdmission admission,
            Task actualRegistration, Task actualMetadata, FilesResult<FilesWorkspaceDirectoryBinding> result,
            DeveloperProjectSetupCheckpoint acknowledged)
        { OriginalAdmission = admission; OriginalRegistrationTask = actualRegistration; OriginalMetadataTask = actualMetadata;
          OriginalMetadataResult = result; AcknowledgedCheckpoint = acknowledged; }
        public HomeDeveloperProjectSetupJournal.OriginalStepAdmission OriginalAdmission { get; }
        public Task OriginalRegistrationTask { get; }
        public Task OriginalMetadataTask { get; }
        public FilesResult<FilesWorkspaceDirectoryBinding> OriginalMetadataResult { get; }
        public DeveloperProjectSetupCheckpoint AcknowledgedCheckpoint { get; }
    }

    /// <summary>Register the SAME selected existing project beneath its configured Files
    /// root. The real kernel lease opens before held Home entry, stays through actual
    /// binding producer, and closes independently. No source copy or new root is created.</summary>
    public Task<ProjectRegistrationStep> ExecuteOriginalProjectRegistrationStepAsync(
        HomeDeveloperProjectSetupJournal.Prepared samePrepared, IDeveloperProjectOriginalSourceCapture sameCapture,
        IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
        CancellationToken token = default) => Start(async sources =>
    {
        if (sameStep.Kind != DeveloperProjectSetupStepKind.RegisterProjectFolder)
            throw new NotSupportedException("Only original project binding registration is supported by this stage.");
        var directorySource = sources.Invoke(() => _originalDirectories?.Invoke()) as IDeveloperProjectOriginalDirectoryRegistrationSource ??
            throw new InvalidOperationException("The SAME genuine directory registration owner is unavailable; no pending step was admitted.");
        var owner = sources.Invoke(journal) ?? throw new InvalidOperationException("The SAME setup journal is unavailable.");
        lock (_gate) _journals.Add(owner);
        await sources.ObserveVoid(() => owner.ValidateOriginalPreparationAsync(samePrepared, sameCapture, token)).ConfigureAwait(false);
        var intent = sources.Invoke(() => samePrepared.Intent);
        if (intent.Mode != DeveloperProjectSetupMode.RegisterExisting || sameStep.FolderId != intent.ProjectFolderId ||
            !intent.Steps.Any(value => ReferenceEquals(value, sameStep)) || intent.OriginalActor.AccountId is not null ||
            intent.OriginalActor.OrganisationId is not null || !Guid.TryParse(intent.OriginalActor.ProfileId, out var profile))
            throw new UnauthorizedAccessException("The SAME reviewed local existing-project plan does not own this registration step.");
        await sources.ObserveVoid(() => scopes.RevalidateOriginalSetupAsync(intent, sameCapture, intent.OriginalActor, token)).ConfigureAwait(false);
        var destination = sources.Invoke(() => scopes.GetOriginalBoundDestination(intent, sameCapture));
        var issuer = sources.Invoke(permissions) ?? throw new InvalidOperationException("The genuine setup permission issuer is unavailable.");
        await sources.ObserveVoid(() => issuer.ValidateOriginalAsync(intent, samePermission, token)).ConfigureAwait(false);
        var folderStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.CreateProjectFolder && value.FolderId == intent.ProjectFolderId);
        var directoryStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory && value.FolderId == intent.ProjectFolderId);
        Physical? folder; Physical? observed;
        lock (_gate)
        { _steps.TryGetValue((intent.SetupId, folderStep.StepId), out folder); _steps.TryGetValue((intent.SetupId, directoryStep.StepId), out observed); }
        if (folder is null || observed is null || !ReferenceEquals(folder.Intent, intent) || !ReferenceEquals(observed.Intent, intent) ||
            !ReferenceEquals(folder.Capture, sameCapture) || !ReferenceEquals(observed.Capture, sameCapture) ||
            !folder.HomeValidated || !observed.HomeValidated || folder.ActualResult?.Value is not { State: FilesOperationState.Committed, ResultRevisionId: { } folderRevision } originalFolder ||
            originalFolder.ItemId.Value != intent.ProjectFolderId || observed.DirectoryPreparation is null ||
            observed.ActualDirectoryTask is null || observed.ActualDirectoryResult is null)
            throw new UnauthorizedAccessException("No genuine SAME-owner folder and original directory ACK cohort owns project registration.");
        var physical = new Physical(intent, sameCapture, sameStep, samePermission, issuer, owner,
            _executing.Value ?? throw new InvalidOperationException("No published original owns this registration.")) { RegistrationSource = directorySource };
        lock (_gate)
        {
            if (_steps.Count >= DeveloperProjectSetupIntent.MaximumSteps) throw new InvalidOperationException("Original step outcome custody is full.");
            if (!_steps.TryAdd((intent.SetupId, sameStep.StepId), physical))
                throw new InvalidOperationException("This original project registration was already attempted; no unknown or committed step replays.");
        }
        var errors = new List<Exception>();
        try
        {
            // These actual source/profile/kernel reads and descriptor acquisition occur
            // before the Home entry. Public IDs/path never replace the original observation.
            await sources.ObserveProduct(() => directorySource.PrepareOriginalDirectoryRegistrationAsync(intent, sameCapture,
                samePermission, sameStep, observed.DirectoryPreparation, observed.ActualDirectoryTask, observed.ActualDirectoryResult, token),
                actual => physical.RegistrationPreparation = actual).ConfigureAwait(false);
            physical.Admission = await sources.Observe(() => owner.AdmitOriginalStepAsync(samePrepared, sameStep, token)).ConfigureAwait(false);
            if (!ReferenceEquals(samePrepared.Intent, intent) || !ReferenceEquals(physical.Admission.Step, sameStep))
                throw new UnauthorizedAccessException("Pending admission changed the SAME reviewed intent/step.");
            await sources.ObserveProduct(() => samePermission.EnterOriginalStepAsync(sameStep, token), actual => physical.ActualEntry = actual).ConfigureAwait(false);
            var entry = physical.ActualEntry ?? throw new InvalidOperationException("No actual held Home entry was retained.");
            if (!sources.Invoke(() => samePermission.IsIssuedOriginalStepEntry(sameStep, entry)))
                throw new UnauthorizedAccessException("The genuine SAME permission did not issue this active entry.");
            sources.Invoke(() => { entry.DemandOriginalStepEntry(sameStep); return true; });
            var preparation = physical.RegistrationPreparation!;
            if (entry is not IDeveloperProjectOriginalSetupScopedStepEntry scopedEntry)
                throw new InvalidOperationException("The original held Home entry lacks nested callback/Task custody; no registration body was started.");
            void PairedScope(Action callback) => preparation.RunOriginalDirectorySourceScope(() => Scope(callback));
            void PairedRetain(Task actual)
            {
                var causes = new List<Exception>();
                try { preparation.RetainOriginalDirectoryTask(actual); } catch (Exception error) { Add(causes, error); }
                // Enroll the SAME explicit original even if an actual callback restored
                // its earlier execution context or the paired kernel enrollment failed.
                try { lock (_gate) physical.OriginalOwner.Sources.Add(actual); } catch (Exception error) { Add(causes, error); }
                if (causes.Count != 0) throw new AggregateException("Original registration child enrollment failed.", causes);
            }
            var pairedSources = new FilesOriginalReadSourceScope(PairedScope, PairedRetain);
            var guard = new FilesCommitAuthorityGuard(intent.OriginalActor.ActorId,
                ct => new ValueTask<bool>(pairedSources.Observe(() => scopedEntry.CheckOriginalStepCommitAsync(sameStep,
                    PairedScope, PairedRetain, ct).AsTask())));
            try
            {
                var returned = sources.Invoke(() => entry.RunOriginalStep(sameStep, () =>
                {
                    physical.ActualRegistrationTask = preparation.RunOriginalDirectoryRegistrationAsync(entry, () =>
                    {
                        physical.ActualBindingTask = destination.Workspace.Directories.RegisterOriginalDeveloperProfileAsync(
                            profile, intent.ProjectId, originalFolder.ItemId, folderRevision, preparation.OriginalDirectoryPath,
                            destination.Workspace.Provider, intent.OriginalFilesStoreId, guard,
                            ct => preparation.CheckOriginalDirectoryCurrentAsync(entry, ct), PairedScope, PairedRetain, token);
                        PairedRetain(physical.ActualBindingTask); return physical.ActualBindingTask;
                    }, token);
                    Retain(physical.ActualRegistrationTask); return physical.ActualRegistrationTask;
                }, token));
                if (!ReferenceEquals(returned, physical.ActualRegistrationTask))
                    throw new UnauthorizedAccessException("The original held entry substituted the actual registration Task.");
            }
            catch (Exception error) { Add(errors, error); }
            if (physical.ActualRegistrationTask is { } registration)
                try { physical.ActualBindingResult = await sources.Observe(() => registration).ConfigureAwait(false); }
                catch (Exception error) { AddTask(errors, registration, error); }
            // Independently join the raw metadata Task even if the outer start/scope fails.
            if (physical.ActualBindingTask is { } metadata)
                try
                {
                    var actualResult = await sources.Observe(() => metadata).ConfigureAwait(false);
                    if (physical.ActualBindingResult is not null && !ReferenceEquals(physical.ActualBindingResult, actualResult))
                        throw new UnauthorizedAccessException("The original registration substituted the raw Files result.");
                    physical.ActualBindingResult ??= actualResult;
                }
                catch (Exception error) { AddTask(errors, metadata, error); }
            if (errors.Count == 0 && (physical.ActualBindingResult is not { IsSuccess: true, Value: { } binding } ||
                binding.AccountId != Guid.Empty || binding.ProfileId != profile || binding.FolderId != originalFolder.ItemId ||
                binding.LocationId != destination.Workspace.Configuration.LocationId || binding.OwningAppId != "dev.project." + intent.ProjectId.ToString("N") ||
                binding.DirectoryPath != intent.OriginalExistingProjectRoot))
                throw new InvalidDataException("The actual Files result did not register this exact original project/profile/folder/root.");
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (physical.ActualEntry is { } entry)
                try { await sources.ObserveVoid(() => physical.ActualEntryClose = entry.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
            if (physical.RegistrationPreparation is { } preparation)
                try { await sources.ObserveVoid(() => physical.ActualRegistrationPreparationClose = preparation.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original project binding/result/entry/native cleanup failed or is unknown; no ACK or replay was issued.", errors);
        if (!sources.Invoke(() => directorySource.IsIssuedOriginalDirectoryRegistrationOutcome(physical.RegistrationPreparation!,
            physical.ActualRegistrationTask!, physical.ActualBindingTask!, physical.ActualBindingResult)))
            throw new UnauthorizedAccessException("No privately confirmed SAME registration/metadata Task/result/native cleanup exists.");
        await sources.ObserveVoid(() => directorySource.ValidateOriginalDirectoryRegistrationOutcomeAsync(physical.RegistrationPreparation!,
            physical.ActualRegistrationTask!, physical.ActualBindingTask!, physical.ActualBindingResult, token)).ConfigureAwait(false);
        physical.PhysicalConfirmed = true;
        physical.Observation = new("dev-original-project-registration:" + Guid.NewGuid().ToString("D"),
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(physical.ActualBindingResult!.Value))).ToLowerInvariant());
        await sources.ObserveVoid(() => samePermission.ValidateOriginalStepResultAsync(sameStep, physical.ActualEntry!,
            physical.ActualRegistrationTask!, physical.ActualBindingResult, token)).ConfigureAwait(false);
        physical.HomeValidated = true;
        var acknowledged = await sources.Observe(() => owner.AcknowledgeOriginalStepAsync(physical.Admission!, sameCapture,
            physical.ActualRegistrationTask!, physical.ActualBindingResult, token)).ConfigureAwait(false);
        return new ProjectRegistrationStep(physical.Admission!, physical.ActualRegistrationTask!, physical.ActualBindingTask!, physical.ActualBindingResult!, acknowledged);
    });
}
