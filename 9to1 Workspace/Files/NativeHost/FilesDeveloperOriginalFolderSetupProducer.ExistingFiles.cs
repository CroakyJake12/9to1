using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
{
    public sealed class ExistingFileStep
    {
        internal ExistingFileStep(HomeDeveloperProjectSetupJournal.OriginalStepAdmission admission, Task driver,
            Task metadata, object result, DeveloperProjectSetupCheckpoint checkpoint)
        { OriginalAdmission = admission; OriginalKernelTask = driver; OriginalMetadataTask = metadata;
          OriginalMetadataResult = result; AcknowledgedCheckpoint = checkpoint; }
        public HomeDeveloperProjectSetupJournal.OriginalStepAdmission OriginalAdmission { get; }
        public Task OriginalKernelTask { get; }
        public Task OriginalMetadataTask { get; }
        public object OriginalMetadataResult { get; }
        public DeveloperProjectSetupCheckpoint AcknowledgedCheckpoint { get; }
    }

    /// <summary>Register once-created canonical metadata or existing materialization for
    /// the exact captured working file. No source copy/overwrite or immutable history is
    /// created. The original native stream, Home entry and actual write all settle before ACK.</summary>
    public Task<ExistingFileStep> ExecuteOriginalExistingFileStepAsync(
        HomeDeveloperProjectSetupJournal.Prepared prepared, IDeveloperProjectOriginalSourceCapture capture,
        IDeveloperProjectOriginalSetupPermission permission, DeveloperProjectSetupStep step, CancellationToken token = default)
        => Start(async sources =>
    {
        if (step.Kind is not (DeveloperProjectSetupStepKind.RegisterExistingFileMetadata or DeveloperProjectSetupStepKind.RegisterMaterialization))
            throw new NotSupportedException("This original stage supports captured existing-source metadata/materialization only.");
        var fileSource = sources.Invoke(() => _originalDirectories?.Invoke()) as IDeveloperProjectOriginalFileRegistrationSource
            ?? throw new InvalidOperationException("The SAME genuine captured-file owner is unavailable; no pending step was admitted.");
        var owner = sources.Invoke(journal) ?? throw new InvalidOperationException("The SAME original setup journal is unavailable.");
        lock (_gate) _journals.Add(owner);
        await sources.ObserveVoid(() => owner.ValidateOriginalPreparationAsync(prepared, capture, token)).ConfigureAwait(false);
        var intent = sources.Invoke(() => prepared.Intent);
        if (intent.Mode != DeveloperProjectSetupMode.RegisterExisting || !intent.Steps.Any(actual => ReferenceEquals(actual, step)))
            throw new UnauthorizedAccessException("Retain the SAME original reviewed existing-project file step.");
        var file = intent.Files.Single(value => value.FileId == step.FileId && value.ParentFolderId == step.FolderId);
        await sources.ObserveVoid(() => scopes.RevalidateOriginalSetupAsync(intent, capture, intent.OriginalActor, token)).ConfigureAwait(false);
        var destination = sources.Invoke(() => scopes.GetOriginalBoundDestination(intent, capture));
        var issuer = sources.Invoke(permissions) ?? throw new InvalidOperationException("The original Home setup permission issuer is unavailable.");
        await sources.ObserveVoid(() => issuer.ValidateOriginalAsync(intent, permission, token)).ConfigureAwait(false);
        var registrationStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterProjectFolder);
        Physical? project; lock (_gate) _steps.TryGetValue((intent.SetupId, registrationStep.StepId), out project);
        if (project is null || !ReferenceEquals(project.Intent, intent) || !ReferenceEquals(project.Capture, capture) ||
            !project.HomeValidated || project.ActualBindingResult is not { IsSuccess: true, Value: { } registered } ||
            registered.FolderId.Value != intent.ProjectFolderId || registered.DirectoryPath != intent.OriginalExistingProjectRoot)
            throw new UnauthorizedAccessException("Only a genuine SAME-owner project binding ACK owns captured file registration.");
        var parents = new List<FilesItemRevisionPrecondition>(); var parent = file.ParentFolderId; var seen = new HashSet<Guid>();
        while (true)
        {
            if (!seen.Add(parent) || seen.Count > 65) throw new InvalidDataException("Original canonical parent ancestry is cyclic or unsupported.");
            var folderStep = intent.Steps.Single(value => value.FolderId == parent &&
                value.Kind is DeveloperProjectSetupStepKind.CreateProjectFolder or DeveloperProjectSetupStepKind.CreateChildFolder);
            Physical? prior; lock (_gate) _steps.TryGetValue((intent.SetupId, folderStep.StepId), out prior);
            if (prior is null || !ReferenceEquals(prior.Intent, intent) || !ReferenceEquals(prior.Capture, capture) || !prior.HomeValidated ||
                prior.ActualResult?.Value is not { State: FilesOperationState.Committed, ResultRevisionId: { } revision } folder || folder.ItemId.Value != parent)
                throw new UnauthorizedAccessException("No genuine SAME-owner parent folder result/ACK owns this source file.");
            var row = await sources.Observe(() => destination.Workspace.Provider.GetForOriginalStoreAtRevisionAsync(
                intent.OriginalFilesStoreId, new(parent), revision, token)).ConfigureAwait(false);
            if (!row.IsSuccess || row.Value is not { Kind: HostedItemKind.Folder } actual || actual.OwnerPrincipalId != intent.OriginalActor.ActorId ||
                actual.IsShared || actual.LocationId != destination.Workspace.Configuration.LocationId)
                throw new UnauthorizedAccessException("An actual canonical project parent changed before file setup.");
            parents.Add(new(new(parent), revision));
            if (parent == intent.ProjectFolderId) break;
            parent = intent.Folders.Single(value => value.FolderId == parent).ParentFolderId;
        }
        Physical? metadata = null;
        if (step.Kind == DeveloperProjectSetupStepKind.RegisterMaterialization)
        {
            var metadataStep = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata && value.FileId == file.FileId);
            lock (_gate) _steps.TryGetValue((intent.SetupId, metadataStep.StepId), out metadata);
            if (metadata is null || !ReferenceEquals(metadata.Intent, intent) || !ReferenceEquals(metadata.Capture, capture) || !metadata.HomeValidated ||
                metadata.ActualFileResult is not FilesResult<FilesRevision> { IsSuccess: true, Value: { } revision } ||
                revision.Id.Value != file.RevisionId || revision.ItemId.Value != file.FileId || revision.ContentHash != "sha256:" + file.ContentSha256)
                throw new UnauthorizedAccessException("No genuine original local-file metadata ACK owns this materialization.");
            var row = await sources.Observe(() => destination.Workspace.Provider.GetForOriginalStoreAtRevisionAsync(
                intent.OriginalFilesStoreId, new(file.FileId), new(file.RevisionId), token)).ConfigureAwait(false);
            if (!row.IsSuccess || row.Value is not { Kind: HostedItemKind.File } current || current.ParentId?.Value != file.ParentFolderId ||
                current.OwnerPrincipalId != intent.OriginalActor.ActorId || current.IsShared || current.ContentHash != "sha256:" + file.ContentSha256 ||
                current.SizeBytes != file.SizeBytes || current.LocationId != destination.Workspace.Configuration.LocationId)
                throw new UnauthorizedAccessException("The actual canonical local-file metadata changed before mapping.");
        }
        var physical = new Physical(intent, capture, step, permission, issuer, owner,
            _executing.Value ?? throw new InvalidOperationException("No admitted original file step exists.")) { FileSource = fileSource };
        lock (_gate)
        {
            if (_steps.Count >= DeveloperProjectSetupIntent.MaximumSteps || !_steps.TryAdd((intent.SetupId, step.StepId), physical))
                throw new InvalidOperationException("Original file step custody is full or already attempted; no remint or replay.");
        }
        var errors = new List<Exception>();
        try
        {
            // Fresh native source/hash/profile reads happen before held Home entry.
            await sources.ObserveProduct(() => fileSource.PrepareOriginalFileRegistrationAsync(intent, capture, permission, step, token),
                actual => physical.FilePreparation = actual).ConfigureAwait(false);
            physical.Admission = await sources.Observe(() => owner.AdmitOriginalStepAsync(prepared, step, token)).ConfigureAwait(false);
            if (!ReferenceEquals(prepared.Intent, intent) || !ReferenceEquals(physical.Admission.Step, step))
                throw new UnauthorizedAccessException("Pending file admission did not conserve the SAME reviewed intent/step.");
            await sources.ObserveProduct(() => permission.EnterOriginalStepAsync(step, token), actual => physical.ActualEntry = actual).ConfigureAwait(false);
            var entry = physical.ActualEntry ?? throw new InvalidOperationException("No original held Home entry was retained.");
            if (!sources.Invoke(() => permission.IsIssuedOriginalStepEntry(step, entry)) || entry is not IDeveloperProjectOriginalSetupScopedStepEntry scopedEntry)
                throw new UnauthorizedAccessException("The SAME genuine held entry lacks original nested callback/Task custody.");
            sources.Invoke(() => { entry.DemandOriginalStepEntry(step); return true; });
            var preparation = physical.FilePreparation!;
            void PairedScope(Action callback) => preparation.RunOriginalFileSourceScope(() => Scope(callback));
            void PairedRetain(Task actual)
            {
                var causes = new List<Exception>();
                try { preparation.RetainOriginalFileTask(actual); } catch (Exception error) { Add(causes, error); }
                try { lock (_gate) physical.OriginalOwner.Sources.Add(actual); } catch (Exception error) { Add(causes, error); }
                if (causes.Count != 0) throw new AggregateException("Original captured-file child enrollment failed.", causes);
            }
            var paired = new FilesOriginalReadSourceScope(PairedScope, PairedRetain);
            var guard = new FilesCommitAuthorityGuard(intent.OriginalActor.ActorId, async ct =>
            {
                var home = await paired.Observe(() => scopedEntry.CheckOriginalStepCommitAsync(step, PairedScope, PairedRetain, ct).AsTask()).ConfigureAwait(false);
                if (!home) return false;
                return await paired.Observe(() => preparation.CheckOriginalFileCurrentAsync(entry, ct).AsTask()).ConfigureAwait(false);
            });
            if (step.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata)
            {
                var observed = new FilesOriginalLocalDeveloperSource(new(file.FileId), new(file.ParentFolderId),
                    file.RelativePath.Split('/')[^1], null, new(file.RevisionId), intent.OriginalActor.ActorId, DateTimeOffset.UtcNow,
                    file.SizeBytes, file.ContentSha256);
                await RunActual(stream => destination.Workspace.Provider.RegisterOriginalLocalDeveloperFileAsync(observed, parents,
                    intent.OriginalFilesStoreId, guard, PairedScope, PairedRetain, token),
                    actual => actual is { IsSuccess: true, Value: { } revision } && revision.Id.Value == file.RevisionId &&
                        revision.ItemId.Value == file.FileId && revision.ContentHash == "sha256:" + file.ContentSha256);
            }
            else
            {
                var proof = new FilesMaterializationProof(new(file.FileId), new(file.RevisionId),
                    "sha256:" + file.ContentSha256, file.SizeBytes, DateTimeOffset.UtcNow);
                await RunActual(stream => destination.Workspace.Materializations.RegisterOriginalDeveloperMaterializationAsync(
                    preparation.OriginalFilePath, stream, proof, SyncAvailability.AvailableOffline, guard, PairedScope, PairedRetain, token),
                    actual => ReferenceEquals(actual, proof));
            }

            async Task RunActual<T>(Func<FileStream, Task<T>> factory, Func<T, bool> validate) where T : class
            {
                Task<T>? driver = null; Task<T>? raw = null; T? result = null;
                try
                {
                    var returned = sources.Invoke(() => entry.RunOriginalStep(step, () =>
                    {
                        driver = preparation.RunOriginalFileRegistrationAsync(entry, stream =>
                        {
                            raw = factory(stream); physical.ActualFileMetadataTask = raw;
                            PairedRetain(raw); return raw;
                        }, token);
                        physical.ActualFileTask = driver; Retain(driver); return driver;
                    }, token));
                    if (!ReferenceEquals(returned, driver)) throw new UnauthorizedAccessException("The original entry substituted the captured-file driver.");
                }
                catch (Exception error) { Add(errors, error); }
                if (driver is not null)
                    try { result = await sources.Observe(() => driver).ConfigureAwait(false); }
                    catch (Exception error) { AddTask(errors, driver, error); }
                if (raw is not null)
                    try
                    {
                        var actual = await sources.Observe(() => raw).ConfigureAwait(false);
                        if (result is not null && !ReferenceEquals(result, actual)) throw new UnauthorizedAccessException("The captured-file driver substituted the raw metadata result.");
                        result ??= actual;
                    }
                    catch (Exception error) { AddTask(errors, raw, error); }
                physical.ActualFileResult = result;
                if (errors.Count == 0 && (result is null || !validate(result)))
                    throw new InvalidDataException("The actual metadata/mapping result did not acknowledge this exact original file/revision.");
            }
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (physical.ActualEntry is { } entry)
                try { await sources.ObserveVoid(() => physical.ActualEntryClose = entry.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
            if (physical.FilePreparation is { } preparation)
                try { await sources.ObserveVoid(() => physical.ActualFilePreparationClose = preparation.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original captured-file result/entry/native cleanup failed or is unknown; no ACK or replay.", errors);
        if (!sources.Invoke(() => fileSource.IsIssuedOriginalFileRegistrationOutcome(physical.FilePreparation!, physical.ActualFileTask!,
            physical.ActualFileMetadataTask!, physical.ActualFileResult)))
            throw new UnauthorizedAccessException("No SAME privately confirmed file metadata Task/result/native cleanup exists.");
        await sources.ObserveVoid(() => fileSource.ValidateOriginalFileRegistrationOutcomeAsync(physical.FilePreparation!,
            physical.ActualFileTask!, physical.ActualFileMetadataTask!, physical.ActualFileResult, token)).ConfigureAwait(false);
        physical.PhysicalConfirmed = true;
        physical.Observation = new("dev-original-file:" + Guid.NewGuid().ToString("D"),
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(physical.ActualFileResult))).ToLowerInvariant());
        await sources.ObserveVoid(() => permission.ValidateOriginalStepResultAsync(step, physical.ActualEntry!,
            physical.ActualFileTask!, physical.ActualFileResult, token)).ConfigureAwait(false);
        physical.HomeValidated = true;
        var acknowledged = await sources.Observe(() => owner.AcknowledgeOriginalStepAsync(physical.Admission!, capture,
            physical.ActualFileTask!, physical.ActualFileResult, token)).ConfigureAwait(false);
        return new ExistingFileStep(physical.Admission!, physical.ActualFileTask!, physical.ActualFileMetadataTask!, physical.ActualFileResult!, acknowledged);
    });
}
