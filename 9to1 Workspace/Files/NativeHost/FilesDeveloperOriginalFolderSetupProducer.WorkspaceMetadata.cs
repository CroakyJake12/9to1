using System.Collections.ObjectModel;
using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
{
    private readonly Func<IDeveloperOriginalWorkspaceSetupStore>? _originalWorkspaceStore;

    public FilesDeveloperOriginalFolderSetupProducer(FilesDeveloperOriginalSetupScopeSource originalScopes,
        Func<HomeDeveloperProjectSetupJournal> originalJournal,
        Func<IDeveloperProjectOriginalSetupPermissionSource> originalPermissions,
        Func<IDeveloperProjectOriginalDirectoryObservationSource> originalDirectories,
        Func<IDeveloperOriginalWorkspaceSetupStore> originalWorkspaceStore)
        : this(originalScopes, originalJournal, originalPermissions, originalDirectories)
        => _originalWorkspaceStore = originalWorkspaceStore;

    public sealed class WorkspaceMetadataStep
    {
        internal WorkspaceMetadataStep(HomeDeveloperProjectSetupJournal.OriginalStepAdmission admission,
            Task<DeveloperOriginalWorkspaceSetupResult> actualStoreTask, DeveloperOriginalWorkspaceSetupResult result,
            DeveloperProjectSetupCheckpoint checkpoint)
        { OriginalAdmission = admission; OriginalStoreTask = actualStoreTask; OriginalStoreResult = result;
          AcknowledgedCheckpoint = checkpoint; }
        private readonly Task<DeveloperProjectSetupCheckpoint>? _actualAcknowledgement;
        internal WorkspaceMetadataStep(HomeDeveloperProjectSetupJournal.OriginalStepAdmission admission,
            Task<DeveloperOriginalWorkspaceSetupResult> actualStoreTask, DeveloperOriginalWorkspaceSetupResult result,
            DeveloperProjectSetupCheckpoint checkpoint, Task<DeveloperProjectSetupCheckpoint> actualAcknowledgement)
            : this(admission, actualStoreTask, result, checkpoint)
            => _actualAcknowledgement = actualAcknowledgement ?? throw new ArgumentNullException(nameof(actualAcknowledgement));
        public Task<DeveloperProjectSetupCheckpoint> OriginalJournalAcknowledgementTask => _actualAcknowledgement
            ?? throw new UnauthorizedAccessException("No actual original journal acknowledgement Task exists on this product.");
        public HomeDeveloperProjectSetupJournal.OriginalStepAdmission OriginalAdmission { get; }
        public Task<DeveloperOriginalWorkspaceSetupResult> OriginalStoreTask { get; }
        public DeveloperOriginalWorkspaceSetupResult OriginalStoreResult { get; }
        public DeveloperProjectSetupCheckpoint AcknowledgedCheckpoint { get; }
    }

    /// <summary>Create the once-declared Dev record through the SAME configured strict store
    /// and native descriptor owner. Existing records are never overwritten. Every earlier
    /// physical producer and actual journal ACK must precede this final step.</summary>
    public Task<WorkspaceMetadataStep> ExecuteOriginalWorkspaceMetadataStepAsync(
        HomeDeveloperProjectSetupJournal.Prepared prepared, IDeveloperProjectOriginalSourceCapture capture,
        IDeveloperProjectOriginalSetupPermission permission, DeveloperProjectSetupStep step,
        CancellationToken token = default) => Start(async sources =>
    {
        if (step.Kind != DeveloperProjectSetupStepKind.SaveDevWorkspace)
            throw new NotSupportedException("This original stage saves only the once-declared workspace metadata.");
        var store = sources.Invoke(() => _originalWorkspaceStore?.Invoke())
            ?? throw new InvalidOperationException("The actual strict configured Dev store is unavailable; no pending step was admitted.");
        var native = sources.Invoke(() => _originalDirectories?.Invoke()) as IDeveloperProjectOriginalWorkspaceMetadataSource
            ?? throw new InvalidOperationException("The SAME native metadata owner is unavailable; no pending step was admitted.");
        var owner = sources.Invoke(journal) ?? throw new InvalidOperationException("The SAME setup journal is unavailable.");
        lock (_gate) _journals.Add(owner);
        await sources.ObserveVoid(() => owner.ValidateOriginalPreparationAsync(prepared, capture, token)).ConfigureAwait(false);
        var intent = sources.Invoke(() => prepared.Intent);
        if (intent.Mode != DeveloperProjectSetupMode.RegisterExisting || intent.ExpectedWorkspaceRevision != 0 ||
            !ReferenceEquals(intent.Steps[^1], step) || step.FolderId != intent.ProjectFolderId ||
            capture is not IDeveloperProjectOriginalExistingSourceCapture existing ||
            sources.Invoke(() => existing.OriginalExistingProjectRoot) != intent.OriginalExistingProjectRoot)
            throw new UnauthorizedAccessException("Retain the SAME reviewed create-only existing-project workspace step.");
        await sources.ObserveVoid(() => scopes.RevalidateOriginalSetupAsync(intent, capture, intent.OriginalActor, token)).ConfigureAwait(false);
        var destination = sources.Invoke(() => scopes.GetOriginalBoundDestination(intent, capture));
        var issuer = sources.Invoke(permissions) ?? throw new InvalidOperationException("The actual Home setup issuer is unavailable.");
        await sources.ObserveVoid(() => issuer.ValidateOriginalAsync(intent, permission, token)).ConfigureAwait(false);
        // The public plan/record cannot substitute for the exact earlier source-issued
        // physical results. Journal admission below separately requires their actual ACKs.
        foreach (var declared in intent.Steps.Take(intent.Steps.Length - 1))
        {
            Physical? previous; lock (_gate) _steps.TryGetValue((intent.SetupId, declared.StepId), out previous);
            if (previous is null || !ReferenceEquals(previous.Intent, intent) || !ReferenceEquals(previous.Capture, capture) ||
                !ReferenceEquals(previous.Step, declared) || !previous.HomeValidated || !previous.PhysicalConfirmed ||
                previous.ActualTask is null || previous.ActualOutcome is null)
                throw new UnauthorizedAccessException("The complete SAME-owner original physical cohort is required before saving the workspace.");
            await sources.ObserveVoid(() => ValidateOriginalStepOutcomeAsync(intent, capture, declared,
                previous.ActualTask, previous.ActualOutcome, token)).ConfigureAwait(false);
        }
        foreach (var file in intent.Files)
        {
            var current = await sources.Observe(() => destination.Workspace.Provider.GetForOriginalStoreAtRevisionAsync(
                intent.OriginalFilesStoreId, new(file.FileId), new(file.RevisionId), token)).ConfigureAwait(false);
            if (!current.IsSuccess || current.Value is not { Kind: HostedItemKind.File } row ||
                row.ParentId?.Value != file.ParentFolderId || row.OwnerPrincipalId != intent.OriginalActor.ActorId || row.IsShared ||
                row.LocationId != destination.Workspace.Configuration.LocationId || row.SizeBytes != file.SizeBytes ||
                row.ContentHash != "sha256:" + file.ContentSha256)
                throw new UnauthorizedAccessException("An actual canonical source file changed before the workspace save.");
            var mapping = await sources.Observe(() => destination.Workspace.Materializations.GetExistingByItemIdAsync(new(file.FileId), token)).ConfigureAwait(false);
            var exactPath = Path.GetFullPath(Path.Combine(intent.OriginalExistingProjectRoot!, file.RelativePath));
            if (mapping is null || mapping.ItemId.Value != file.FileId || mapping.BaseRemoteRevisionId.Value != file.RevisionId ||
                mapping.LocalPath != exactPath || mapping.ContentHash != "sha256:" + file.ContentSha256 || mapping.SizeBytes != file.SizeBytes)
                throw new UnauthorizedAccessException("The original existing canonical materialization changed before the workspace save.");
        }
        await sources.ObserveVoid(() => scopes.RevalidateOriginalSetupAsync(intent, capture, intent.OriginalActor, token)).ConfigureAwait(false);
        var physical = new Physical(intent, capture, step, permission, issuer, owner,
            _executing.Value ?? throw new InvalidOperationException("No admitted original workspace step exists.")) { MetadataSource = native };
        lock (_gate)
        {
            if (_steps.Count >= DeveloperProjectSetupIntent.MaximumSteps || !_steps.TryAdd((intent.SetupId, step.StepId), physical))
                throw new InvalidOperationException("Original workspace step custody is full or already attempted; no remint or replay.");
        }
        var errors = new List<Exception>();
        try
        {
            // Actual configured store/root/profile acquisition precedes held Home entry.
            await sources.ObserveProduct(() => native.PrepareOriginalWorkspaceMetadataAsync(store, intent, capture,
                permission, step, token), actual => physical.MetadataPreparation = actual).ConfigureAwait(false);
            physical.Admission = await sources.Observe(() => owner.AdmitOriginalStepAsync(prepared, step, token)).ConfigureAwait(false);
            if (!ReferenceEquals(prepared.Intent, intent) || !ReferenceEquals(physical.Admission.Step, step))
                throw new UnauthorizedAccessException("Pending workspace admission changed the SAME reviewed intent/step.");
            var onceWorkspace = sources.Invoke(() => CreateOriginalDeclaredWorkspace(intent, DateTimeOffset.UtcNow));
            await sources.ObserveProduct(() => permission.EnterOriginalStepAsync(step, token), actual => physical.ActualEntry = actual).ConfigureAwait(false);
            var entry = physical.ActualEntry ?? throw new InvalidOperationException("No actual held Home entry was captured.");
            if (!sources.Invoke(() => permission.IsIssuedOriginalStepEntry(step, entry)) || entry is not IDeveloperProjectOriginalSetupScopedStepEntry)
                throw new UnauthorizedAccessException("The SAME held entry lacks private issuance or nested callback custody.");
            sources.Invoke(() => { entry.DemandOriginalStepEntry(step); return true; });
            var preparation = physical.MetadataPreparation!;
            void RetainActual(Task actual) { lock (_gate) physical.OriginalOwner.Sources.Add(actual); }
            Task<DeveloperOriginalWorkspaceSetupResult>? actualStore = null;
            try
            {
                var returned = sources.Invoke(() => entry.RunOriginalStep(step, () =>
                {
                    actualStore = store.CreateOriginalSetupAsync(onceWorkspace, native, preparation, entry,
                        Scope, RetainActual, token);
                    physical.ActualWorkspaceStoreTask = actualStore; RetainActual(actualStore); return actualStore;
                }, token));
                if (!ReferenceEquals(returned, actualStore)) throw new UnauthorizedAccessException("The held entry substituted the actual store Task.");
            }
            catch (Exception error) { Add(errors, error); }
            if (actualStore is not null)
                try { physical.ActualWorkspaceStoreResult = await sources.Observe(() => actualStore).ConfigureAwait(false); }
                catch (Exception error) { AddTask(errors, actualStore, error); }
            if (physical.ActualWorkspaceStoreResult is { } result)
            {
                if (!ReferenceEquals(result.Workspace, onceWorkspace) || !ReferenceEquals(result.OriginalPreparation, preparation))
                    throw new UnauthorizedAccessException("The actual store substituted its original workspace/preparation result.");
                physical.ActualWorkspaceNativeTask = result.OriginalNativeWriteTask;
                physical.ActualWorkspaceNativeObservation = result.OriginalObservation;
                var actualObservation = await sources.Observe(() => result.OriginalNativeWriteTask).ConfigureAwait(false);
                if (!ReferenceEquals(actualObservation, result.OriginalObservation))
                    throw new UnauthorizedAccessException("The actual store substituted its native result observation.");
            }
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            // Independent original cleanup, including when the store committed then faulted.
            if (physical.ActualEntry is { } entry)
                try { await sources.ObserveVoid(() => physical.ActualEntryClose = entry.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
            if (physical.MetadataPreparation is { } preparation)
                try { await sources.ObserveVoid(() => physical.ActualWorkspacePreparationClose = preparation.CloseAndDrainAsync()).ConfigureAwait(false); }
                catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original workspace write/readback/cleanup is failed or unknown; no ACK or automatic repeat.", errors);
        if (physical.ActualWorkspaceStoreResult is null || !sources.Invoke(() => native.IsIssuedOriginalWorkspaceMetadataOutcome(
            physical.MetadataPreparation!, physical.ActualWorkspaceNativeTask!, physical.ActualWorkspaceNativeObservation!)))
            throw new UnauthorizedAccessException("No SAME private native write/readback/cleanup result exists.");
        await sources.ObserveVoid(() => native.ValidateOriginalWorkspaceMetadataOutcomeAsync(physical.MetadataPreparation!,
            physical.ActualWorkspaceNativeTask!, physical.ActualWorkspaceNativeObservation!, token)).ConfigureAwait(false);
        physical.PhysicalConfirmed = true;
        physical.Observation = new("dev-original-workspace:" + Guid.NewGuid().ToString("D"),
            physical.ActualWorkspaceNativeObservation!.OriginalDocumentSha256);
        await sources.ObserveVoid(() => permission.ValidateOriginalStepResultAsync(step, physical.ActualEntry!,
            physical.ActualWorkspaceStoreTask!, physical.ActualWorkspaceStoreResult, token)).ConfigureAwait(false);
        physical.HomeValidated = true;
        Task<DeveloperProjectSetupCheckpoint>? acknowledgement = null;
        var acknowledged = await sources.Observe(() => acknowledgement = owner.AcknowledgeOriginalStepAsync(physical.Admission!, capture,
            physical.ActualWorkspaceStoreTask!, physical.ActualWorkspaceStoreResult, token)).ConfigureAwait(false);
        var saved = new WorkspaceMetadataStep(physical.Admission!, physical.ActualWorkspaceStoreTask!, physical.ActualWorkspaceStoreResult,
            acknowledged, acknowledgement ?? throw new InvalidOperationException("No actual journal ACK Task was captured."));
        // Custody of the exact actual ACK result, not authority from public success/fields.
        // Already-admitted saves still report their actual historical outcome during drain;
        // all later binding issuance/recognition separately refuses a retired owner.
        lock (_gate) _originalSavedSteps.Add(saved, new SavedWorkspaceProduct(physical, acknowledgement, acknowledged));
        return saved;
    });

    private static DeveloperWorkspace CreateOriginalDeclaredWorkspace(DeveloperProjectSetupIntent intent, DateTimeOffset timestamp)
    {
        var empty = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        return new DeveloperWorkspace(intent.WorkspaceId,
            Array.AsReadOnly(new[] { new DeveloperWorkspaceRoot(intent.RootId, intent.OriginalExistingProjectRoot!) }),
            Array.AsReadOnly(new[] { new DeveloperProject(intent.ProjectId, "existing", intent.ProjectName, [intent.RootId],
                "unknown", null, "unconfigured", [], [], [], [], null) }),
            Array.AsReadOnly(intent.Files.Select((file, order) => new DeveloperOpenEditor(file.FileId, intent.ProjectId,
                file.FileId.ToString("D"), "main", order, false, false)).ToArray()),
            [], [], [], [], [], [], [], new DeveloperWorkspaceSettings(intent.ProjectId.ToString("D"), "local", "review-first", empty, empty),
            timestamp, timestamp, 1);
    }
}
