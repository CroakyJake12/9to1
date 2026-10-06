using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
{
    private readonly Func<IDeveloperProjectOriginalDirectoryObservationSource>? _originalDirectories;

    /// <summary>Additive same-source composition; the original folder-only constructor
    /// remains available and refuses this new stage before any pending admission.</summary>
    public FilesDeveloperOriginalFolderSetupProducer(FilesDeveloperOriginalSetupScopeSource originalScopes,
        Func<HomeDeveloperProjectSetupJournal> originalJournal,
        Func<IDeveloperProjectOriginalSetupPermissionSource> originalPermissions,
        Func<IDeveloperProjectOriginalDirectoryObservationSource> originalDirectories)
        : this(originalScopes, originalJournal, originalPermissions) => _originalDirectories = originalDirectories;

    public sealed class ExistingDirectoryStep
    {
        internal ExistingDirectoryStep(HomeDeveloperProjectSetupJournal.OriginalStepAdmission admission,
            Task actual, IDeveloperProjectOriginalDirectoryObservation observation, DeveloperProjectSetupCheckpoint acknowledged)
        { OriginalAdmission = admission; OriginalKernelTask = actual; OriginalKernelObservation = observation; AcknowledgedCheckpoint = acknowledged; }
        public HomeDeveloperProjectSetupJournal.OriginalStepAdmission OriginalAdmission { get; }
        public Task OriginalKernelTask { get; }
        public IDeveloperProjectOriginalDirectoryObservation OriginalKernelObservation { get; }
        public DeveloperProjectSetupCheckpoint AcknowledgedCheckpoint { get; }
    }

    public Task<ExistingDirectoryStep> ExecuteOriginalExistingDirectoryStepAsync(
        HomeDeveloperProjectSetupJournal.Prepared samePrepared, IDeveloperProjectOriginalSourceCapture sameCapture,
        IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
        CancellationToken token = default) => Start(async sources =>
    {
        if (sameStep.Kind is not (DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory or DeveloperProjectSetupStepKind.ObserveExistingChildDirectory))
            throw new NotSupportedException("Only exact selected existing-directory observations are supported; no copy/create/import effect was admitted.");
        var directorySource = sources.Invoke(() => _originalDirectories?.Invoke()) ??
            throw new InvalidOperationException("The SAME genuine captured kernel directory owner is not configured; no pending step was admitted.");
        var owner = sources.Invoke(journal) ?? throw new InvalidOperationException("The SAME original setup journal is unavailable.");
        lock (_gate) _journals.Add(owner);
        await sources.ObserveVoid(() => owner.ValidateOriginalPreparationAsync(samePrepared, sameCapture, token)).ConfigureAwait(false);
        var intent = sources.Invoke(() => samePrepared.Intent);
        if (intent.Mode != DeveloperProjectSetupMode.RegisterExisting || !intent.Steps.Any(value => ReferenceEquals(value, sameStep)))
            throw new UnauthorizedAccessException("The SAME once-reviewed register-existing intent does not own this step.");
        await sources.ObserveVoid(() => scopes.RevalidateOriginalSetupAsync(intent, sameCapture, intent.OriginalActor, token)).ConfigureAwait(false);
        var issuer = sources.Invoke(permissions) ?? throw new InvalidOperationException("The genuine Home setup issuer is unavailable.");
        await sources.ObserveVoid(() => issuer.ValidateOriginalAsync(intent, samePermission, token)).ConfigureAwait(false);
        // Existing-directory observation follows the genuine create-only metadata folder
        // operation owned/validated by this SAME producer. Public journal fields alone do
        // not reconstruct the private operation receipt or its declared folder identity.
        var folderKind = sameStep.Kind == DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory
            ? DeveloperProjectSetupStepKind.CreateProjectFolder : DeveloperProjectSetupStepKind.CreateChildFolder;
        var originalFolderStep = intent.Steps.Single(value => value.Kind == folderKind && value.FolderId == sameStep.FolderId);
        Physical? prior;
        lock (_gate) _steps.TryGetValue((intent.SetupId, originalFolderStep.StepId), out prior);
        if (prior is null || !ReferenceEquals(prior.Intent, intent) || !ReferenceEquals(prior.Capture, sameCapture) ||
            !prior.HomeValidated || prior.Admission is null || prior.ActualResult?.Value is not { State: FilesOperationState.Committed } committed ||
            committed.ItemId.Value != sameStep.FolderId)
            throw new UnauthorizedAccessException("No actual SAME-owner acknowledged canonical folder precedes this directory observation.");
        var physical = new Physical(intent, sameCapture, sameStep, samePermission, issuer, owner,
            _executing.Value ?? throw new InvalidOperationException("No actual published directory step owns this body.")) { DirectorySource = directorySource };
        lock (_gate)
        {
            if (_steps.Count >= DeveloperProjectSetupIntent.MaximumSteps) throw new InvalidOperationException("The original step result custody limit is full.");
            if (!_steps.TryAdd((intent.SetupId, sameStep.StepId), physical))
                throw new InvalidOperationException("The SAME directory step was already attempted; unknown or acknowledged work cannot replay.");
        }
        var errors = new List<Exception>();
        try
        {
            // Fresh source/profile/setup validation and handle acquisition precede the
            // held Home entry. Capture any actual returned lease before scope errors escape.
            await sources.ObserveProduct(() => directorySource.PrepareOriginalDirectoryAsync(intent, sameCapture, samePermission, sameStep, token),
                actual => physical.DirectoryPreparation = actual).ConfigureAwait(false);
            physical.Admission = await sources.Observe(() => owner.AdmitOriginalStepAsync(samePrepared, sameStep, token)).ConfigureAwait(false);
            if (!ReferenceEquals(samePrepared.Intent, intent) || !ReferenceEquals(physical.Admission.Step, sameStep))
                throw new UnauthorizedAccessException("The SAME private reviewed intent/step was not conserved during pending admission.");
            await sources.ObserveProduct(() => samePermission.EnterOriginalStepAsync(sameStep, token),
                actual => physical.ActualEntry = actual).ConfigureAwait(false);
            var entry = physical.ActualEntry ?? throw new InvalidOperationException("The genuine Home entry returned no retained product.");
            if (!sources.Invoke(() => samePermission.IsIssuedOriginalStepEntry(sameStep, entry)))
                throw new UnauthorizedAccessException("The SAME configured permission did not privately issue this active entry.");
            sources.Invoke(() => { entry.DemandOriginalStepEntry(sameStep); return true; });
            try
            {
                var returned = sources.Invoke(() => entry.RunOriginalStep(sameStep, () =>
                {
                    physical.ActualDirectoryTask = physical.DirectoryPreparation!.ObserveOriginalDirectoryAsync(entry, token);
                    Retain(physical.ActualDirectoryTask); return physical.ActualDirectoryTask;
                }, token));
                if (!ReferenceEquals(returned, physical.ActualDirectoryTask))
                    throw new UnauthorizedAccessException("The held Home entry substituted the actual kernel directory Task.");
            }
            catch (Exception error) { Add(errors, error); }
            if (physical.ActualDirectoryTask is { } actual)
                try { physical.ActualDirectoryResult = await sources.Observe(() => actual).ConfigureAwait(false); }
                catch (Exception error) { AddTask(errors, actual, error); }
            if (errors.Count == 0 && (physical.ActualDirectoryResult is null || !sources.Invoke(() => directorySource.IsIssuedOriginalDirectoryOutcome(
                physical.DirectoryPreparation!, physical.ActualDirectoryTask!, physical.ActualDirectoryResult))))
                throw new UnauthorizedAccessException("No genuine SAME kernel directory Task/result/handle cleanup was confirmed.");
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            // Start/retain and independently join each actual cleanup despite siblings.
            if (physical.ActualEntry is { } entry)
                try { await sources.ObserveVoid(() => physical.ActualEntryClose = entry.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
            if (physical.DirectoryPreparation is { } preparation)
                try { await sources.ObserveVoid(() => physical.ActualDirectoryPreparationClose = preparation.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count != 0)
            throw new AggregateException("The original directory observation/entry/handle cleanup is failed or unknown; no ACK/replay was issued.", errors);
        await sources.ObserveVoid(() => directorySource.ValidateOriginalDirectoryOutcomeAsync(physical.DirectoryPreparation!,
            physical.ActualDirectoryTask!, physical.ActualDirectoryResult!, token)).ConfigureAwait(false);
        physical.PhysicalConfirmed = true;
        physical.Observation = new("dev-original-directory:" + Guid.NewGuid().ToString("D"),
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { intent.SetupId, sameStep.StepId, sameStep.FolderId, path = physical.ActualDirectoryResult!.OriginalDirectoryPath }))).ToLowerInvariant());
        await sources.ObserveVoid(() => samePermission.ValidateOriginalStepResultAsync(sameStep, physical.ActualEntry!,
            physical.ActualDirectoryTask!, physical.ActualDirectoryResult, token)).ConfigureAwait(false);
        physical.HomeValidated = true;
        var acknowledged = await sources.Observe(() => owner.AcknowledgeOriginalStepAsync(physical.Admission!, sameCapture,
            physical.ActualDirectoryTask!, physical.ActualDirectoryResult, token)).ConfigureAwait(false);
        return new ExistingDirectoryStep(physical.Admission!, physical.ActualDirectoryTask!, physical.ActualDirectoryResult!, acknowledged);
    });
}
