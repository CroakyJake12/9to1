using System.Collections.Immutable;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Home-owned intent and step-admission custody. It creates no folders, source bytes,
/// mappings, Dev workspace, permission, effect receipt, or replay permission. Physical effects
/// require the separate genuine Home review/entry plus the owning Files producer.</summary>
public sealed partial class HomeDeveloperProjectSetupJournal(
    IHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
    IDeveloperProjectOriginalCaptureAuthority captures,
    Func<IDeveloperProjectOriginalSetupStepOutcomeSource>? originalStepOutcomes = null)
{
    private const string RecordType = "home.dev-project-setup";
    private readonly HomeLocalProfileIdentity _profileAuthority = profiles;
    private readonly IDeveloperProjectOriginalCaptureAuthority _captureAuthority = captures;
    private readonly object _gate = new();
    private readonly List<Original> _originals = [];
    private readonly Dictionary<Guid, Prepared> _prepared = [];
    private readonly HashSet<Guid> _reservedPreparations = [];
    private readonly AsyncLocal<Original?> _executing = new();
    [ThreadStatic] private static List<HomeDeveloperProjectSetupJournal>? _callbacks;
    private bool _retiring;
    private Exception? _capacityFault;
    private Task? _close;

    public sealed class Prepared
    {
        internal Prepared(HomeDeveloperProjectSetupJournal owner, DeveloperProjectSetupCheckpoint state,
            IDeveloperProjectOriginalSourceCapture source)
        { Owner = owner; Checkpoint = state; Source = source; }
        internal HomeDeveloperProjectSetupJournal Owner { get; } = null!;
        internal IDeveloperProjectOriginalSourceCapture Source { get; } = null!;
        internal DeveloperProjectSetupCheckpoint Checkpoint { get; set; } = null!;
        internal readonly HashSet<Guid> OriginalAdmissions = [];
        public DeveloperProjectSetupIntent Intent => Checkpoint.Intent;
    }
    public sealed class OriginalStepAdmission
    {
        internal OriginalStepAdmission(Prepared original, DeveloperProjectSetupStep step, DeveloperProjectSetupCheckpoint acknowledged)
        { Original = original; Step = step; AcknowledgedCheckpoint = acknowledged; }
        internal Prepared Original { get; }
        public DeveloperProjectSetupStep Step { get; }
        public DeveloperProjectSetupCheckpoint AcknowledgedCheckpoint { get; }
        // This acknowledged journal write is only pending metadata. It never authorizes an effect.
    }
    private sealed class Original
    {
        internal Task Task = null!;
        internal readonly List<Task> Sources = [];
        internal readonly List<Exception> Causes = [];
        internal Original? Parent;
        internal bool Live;
    }
    public sealed class StorageFailure(HomeCoreFailure originalFailure) : IOException(originalFailure.Message)
    { public HomeCoreFailure OriginalFailure { get; } = originalFailure; }

    /// <summary>The caller retains SetupId before this method. A failed/uncertain original is not
    /// restarted with new IDs or repeated under the same ID. Reopening reads the retained journal;
    /// a new process/session must obtain genuine recovery inspection and a fresh Home review.</summary>
    public Task<Prepared> PrepareOriginalAsync(Guid setupId, AuthenticatedResourceActor originalActor,
        Guid originalFilesStoreId, string originalFilesConfigurationDigest,
        Guid originalDestinationFolderId, string originalDestinationFolderRevision,
        IDeveloperProjectOriginalSourceCapture originalSource, Guid workspaceId, long expectedWorkspaceRevision,
        string projectName, CancellationToken cancellationToken = default) => Run(original => PrepareBodyAsync(original,
            setupId, originalActor, originalFilesStoreId, originalFilesConfigurationDigest, originalDestinationFolderId,
            originalDestinationFolderRevision, originalSource, workspaceId, expectedWorkspaceRevision, projectName, cancellationToken));

    /// <summary>Register an existing selected project without copying/overwriting its bytes or
    /// changing its original working root/Task/repository. A public path/manifest is never capture authority.</summary>
    public Task<Prepared> PrepareExistingOriginalAsync(Guid setupId, AuthenticatedResourceActor originalActor,
        Guid originalFilesStoreId, string originalFilesConfigurationDigest,
        Guid originalDestinationFolderId, string originalDestinationFolderRevision,
        IDeveloperProjectOriginalExistingSourceCapture originalSource, Guid workspaceId, long expectedWorkspaceRevision,
        string projectName, CancellationToken cancellationToken = default) => Run(original => PrepareBodyAsync(original,
            setupId, originalActor, originalFilesStoreId, originalFilesConfigurationDigest, originalDestinationFolderId,
            originalDestinationFolderRevision, originalSource, workspaceId, expectedWorkspaceRevision, projectName,
            cancellationToken, DeveloperProjectSetupMode.RegisterExisting));

    private async Task<Prepared> PrepareBodyAsync(Original original, Guid setupId, AuthenticatedResourceActor actor,
        Guid filesStore, string filesDigest, Guid destination, string destinationRevision,
        IDeveloperProjectOriginalSourceCapture capture, Guid workspaceId, long expectedWorkspaceRevision,
        string projectName, CancellationToken ct, DeveloperProjectSetupMode mode = DeveloperProjectSetupMode.CopyIntoFiles)
    {
        if (!_profileAuthority.IsBoundToStore(store) || setupId == Guid.Empty || capture is null || !Acquire(original, () => _captureAuthority.IsIssuedOriginal(capture)))
            throw new UnauthorizedAccessException("Retain the genuine source capture and once-created setup identity.");
        await RequireActorAsync(original, actor, ct).ConfigureAwait(false);
        // Reserve BEFORE the first original Home write. A persist-then-fault result cannot cause
        // another creation or minted replacement IDs in this process, even if a later read is absent.
        lock (_gate)
        {
            if (!_reservedPreparations.Add(setupId))
                throw new InvalidOperationException("This original setup preparation is already owned; inspect/recover the same journal without repeating preparation.");
            if (_reservedPreparations.Count > 128)
                throw _capacityFault ??= new InvalidOperationException("Dev setup custody reached its finite 128-original runtime limit.");
        }
        await Await(original, () => _captureAuthority.RevalidateOriginalAsync(capture, actor, ct)).ConfigureAwait(false);
        var read = await Await(original, () => store.ReadAsync(ct)).ConfigureAwait(false);
        if (!read.IsSuccess) throw new StorageFailure(read.Failure!);
        var id = RecordId(actor, setupId);
        var oldRecord = read.State!.Records.SingleOrDefault(value => value.RecordId == id);
        DeveloperProjectSetupCheckpoint checkpoint;
        if (oldRecord is not null)
        {
            checkpoint = Decode(oldRecord);
            var intent = checkpoint.Intent;
            if (intent.OriginalActor != actor || intent.OriginalFilesStoreId != filesStore || intent.OriginalFilesConfigurationDigest != filesDigest ||
                intent.OriginalDestinationFolderId != destination || intent.OriginalDestinationFolderRevision != destinationRevision ||
                intent.OriginalSourceCaptureReference != Acquire(original, () => capture.OriginalCaptureReference) ||
                intent.OriginalSourceDigest != Acquire(original, () => capture.OriginalCaptureDigest) || intent.WorkspaceId != workspaceId ||
                intent.ExpectedWorkspaceRevision != expectedWorkspaceRevision || intent.ProjectName != projectName || intent.Mode != mode ||
                (mode == DeveloperProjectSetupMode.RegisterExisting && (capture is not IDeveloperProjectOriginalExistingSourceCapture existing ||
                    intent.OriginalExistingProjectRoot != Acquire(original, () => existing.OriginalExistingProjectRoot))) ||
                checkpoint.Observations.Any(value => value.State != DeveloperProjectSetupStepState.NotStarted))
                throw new InvalidOperationException("The retained setup needs original recovery inspection; do not rebind its actor, repeat admitted steps or create new IDs.");
            VerifyCapturedManifest(original, intent, capture);
        }
        else
        {
            var intent = CreateIntent(original, setupId, actor, filesStore, filesDigest, destination,
                destinationRevision, capture, workspaceId, expectedWorkspaceRevision, projectName, mode);
            checkpoint = new(intent, 1, intent.Steps.Select(value => new DeveloperProjectSetupStepObservation(
                value.StepId, DeveloperProjectSetupStepState.NotStarted, null, null, null)).ToImmutableArray());
            await Await(original, () => _captureAuthority.RevalidateOriginalAsync(capture, actor, ct)).ConfigureAwait(false);
            var record = new HomeCoreStateRecord(id, RecordType, 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, checkpoint.Revision, JsonSerializer.SerializeToElement(checkpoint));
            var write = await Await(original, () => store.WriteGuardedAsync(record, 0, actor,
                new OriginalGuard(this, original, actor, capture, setupId, null), ct)).ConfigureAwait(false);
            if (!write.IsSuccess) throw new StorageFailure(write.Failure!);
            var expectedCheckpoint = checkpoint;
            var acknowledged = write.State!.Records.Single(value => value.RecordId == id);
            checkpoint = Decode(acknowledged);
            if (JsonSerializer.Serialize(checkpoint) != JsonSerializer.Serialize(expectedCheckpoint))
                throw new InvalidDataException("The acknowledged original setup differs from the once-created intent.");
        }
        await Await(original, () => _captureAuthority.RevalidateOriginalAsync(capture, actor, ct)).ConfigureAwait(false);
        await RequireActorAsync(original, actor, ct).ConfigureAwait(false);
        var prepared = new Prepared(this, checkpoint, capture);
        lock (_gate) _prepared.Add(setupId, prepared);
        return prepared;
    }

    /// <summary>Read-only recovery metadata. It never issues a Prepared object, held Home entry,
    /// source capture, acknowledgement of a resource effect, or automatic retry permission.</summary>
    public Task<DeveloperProjectSetupCheckpoint?> ReadCheckpointAsync(Guid setupId, AuthenticatedResourceActor actor,
        CancellationToken ct = default) => Run(async original =>
    {
        await RequireActorAsync(original, actor, ct).ConfigureAwait(false);
        var read = await Await(original, () => store.ReadAsync(ct)).ConfigureAwait(false);
        if (!read.IsSuccess) throw new StorageFailure(read.Failure!);
        var record = read.State!.Records.SingleOrDefault(value => value.RecordId == RecordId(actor, setupId));
        var result = record is null ? null : Decode(record);
        await RequireActorAsync(original, actor, ct).ConfigureAwait(false);
        return result;
    });

    public Task<OriginalStepAdmission> AdmitOriginalStepAsync(Prepared prepared, DeveloperProjectSetupStep actualStep,
        CancellationToken ct = default) => Run(async original =>
    {
        DeveloperProjectSetupCheckpoint before;
        lock (_gate)
        {
            if (prepared is null || !ReferenceEquals(prepared.Owner, this) ||
                !_prepared.TryGetValue(prepared.Intent.SetupId, out var issued) || !ReferenceEquals(issued, prepared) ||
                actualStep is null || !prepared.Intent.Steps.Any(value => ReferenceEquals(value, actualStep)))
                throw new UnauthorizedAccessException("Retain the SAME privately issued original preparation and step.");
            before = prepared.Checkpoint;
            var index = before.Intent.Steps.IndexOf(actualStep);
            if (before.Observations[index].State != DeveloperProjectSetupStepState.NotStarted ||
                before.Observations.Take(index).Any(value => value.State != DeveloperProjectSetupStepState.Acknowledged) ||
                !prepared.OriginalAdmissions.Add(actualStep.StepId))
                throw new InvalidOperationException("An admitted/unknown step cannot be repeated, and prior original effects must be acknowledged first.");
        }
        await RequireActorAsync(original, before.Intent.OriginalActor, ct).ConfigureAwait(false);
        await Await(original, () => _captureAuthority.RevalidateOriginalAsync(prepared.Source, before.Intent.OriginalActor, ct)).ConfigureAwait(false);
        var indexOfStep = before.Intent.Steps.IndexOf(actualStep);
        var next = before with { Revision = checked(before.Revision + 1), Observations = before.Observations.SetItem(indexOfStep,
            new(actualStep.StepId, DeveloperProjectSetupStepState.Admitted, null, null, null)) };
        var record = new HomeCoreStateRecord(RecordId(before.Intent.OriginalActor, before.Intent.SetupId), RecordType, 1,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, next.Revision, JsonSerializer.SerializeToElement(next));
        var write = await Await(original, () => store.WriteGuardedAsync(record, before.Revision, before.Intent.OriginalActor,
            new OriginalGuard(this, original, before.Intent.OriginalActor, prepared.Source, before.Intent.SetupId, before), ct)).ConfigureAwait(false);
        if (!write.IsSuccess) throw new StorageFailure(write.Failure!);
        var acknowledged = Decode(write.State!.Records.Single(value => value.RecordId == record.RecordId));
        if (JsonSerializer.Serialize(acknowledged) != JsonSerializer.Serialize(next))
            throw new InvalidDataException("The actual acknowledged pending step differs from its owning admission.");
        acknowledged = acknowledged with { Intent = before.Intent };
        lock (_gate) prepared.Checkpoint = acknowledged;
        return new OriginalStepAdmission(prepared, actualStep, acknowledged);
    });

    private DeveloperProjectSetupIntent CreateIntent(Original original, Guid setupId, AuthenticatedResourceActor actor,
        Guid storeId, string configDigest, Guid destination, string destinationRevision,
        IDeveloperProjectOriginalSourceCapture capture, Guid workspaceId, long workspaceRevision, string name, DeveloperProjectSetupMode mode)
    {
        var paths = Acquire(original, () => capture.OriginalFolderPaths).ToArray();
        var sourceFiles = Acquire(original, () => capture.OriginalFiles).ToArray();
        if (paths.Length > DeveloperProjectSetupIntent.MaximumFolders || sourceFiles.Length > DeveloperProjectSetupIntent.MaximumFiles ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new InvalidDataException("Source capture exceeds the documented bounded import plan.");
        var projectFolderId = Guid.NewGuid(); var folderIds = paths.ToDictionary(value => value, _ => Guid.NewGuid(), StringComparer.Ordinal);
        Guid Parent(string path)
        {
            var slash = path.LastIndexOf('/'); if (slash < 0) return projectFolderId;
            if (!folderIds.TryGetValue(path[..slash], out var parent)) throw new InvalidDataException("Captured folder hierarchy is incomplete.");
            return parent;
        }
        var folders = paths.Select(path => new DeveloperProjectSetupFolder(folderIds[path], Parent(path), path)).ToImmutableArray();
        var files = sourceFiles.Select(file => new DeveloperProjectSetupFile(Guid.NewGuid(), Parent(file.RelativePath), Guid.NewGuid(),
            file.RelativePath, file.SizeBytes, file.ContentSha256, file.OriginalSourceReference)).ToImmutableArray();
        var steps = ImmutableArray.CreateBuilder<DeveloperProjectSetupStep>();
        void Add(DeveloperProjectSetupStepKind kind, Guid? file, Guid? folder) => steps.Add(new(Guid.NewGuid(), kind, file, folder));
        Add(DeveloperProjectSetupStepKind.CreateProjectFolder, null, projectFolderId);
        Add(mode == DeveloperProjectSetupMode.RegisterExisting ? DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory : DeveloperProjectSetupStepKind.CreateProjectDirectory, null, projectFolderId);
        foreach (var folder in folders.OrderBy(value => value.RelativePath.Count(c => c == '/')).ThenBy(value => value.RelativePath, StringComparer.Ordinal))
        { Add(DeveloperProjectSetupStepKind.CreateChildFolder, null, folder.FolderId); Add(mode == DeveloperProjectSetupMode.RegisterExisting ? DeveloperProjectSetupStepKind.ObserveExistingChildDirectory : DeveloperProjectSetupStepKind.CreateChildDirectory, null, folder.FolderId); }
        Add(DeveloperProjectSetupStepKind.RegisterProjectFolder, null, projectFolderId);
        foreach (var file in files.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            if (mode == DeveloperProjectSetupMode.RegisterExisting)
                Add(DeveloperProjectSetupStepKind.RegisterExistingFileMetadata, file.FileId, file.ParentFolderId);
            else
            {
                Add(DeveloperProjectSetupStepKind.PublishImmutableSource, file.FileId, file.ParentFolderId);
                Add(DeveloperProjectSetupStepKind.PublishMaterializedFile, file.FileId, file.ParentFolderId);
                Add(DeveloperProjectSetupStepKind.PublishFileRevision, file.FileId, file.ParentFolderId);
            }
            Add(DeveloperProjectSetupStepKind.RegisterMaterialization, file.FileId, file.ParentFolderId);
        }
        Add(DeveloperProjectSetupStepKind.SaveDevWorkspace, null, projectFolderId);
        var result = new DeveloperProjectSetupIntent(setupId, actor, storeId, configDigest, destination, destinationRevision,
            Acquire(original, () => capture.OriginalCaptureReference), Acquire(original, () => capture.OriginalCaptureDigest),
            workspaceId, workspaceRevision, Guid.NewGuid(), Guid.NewGuid(), projectFolderId, name, folders, files, steps.ToImmutable())
        {
            Mode = mode,
            OriginalExistingProjectRoot = mode == DeveloperProjectSetupMode.RegisterExisting && capture is IDeveloperProjectOriginalExistingSourceCapture existing
                ? Acquire(original, () => existing.OriginalExistingProjectRoot)
                : null
        };
        if (result.Validate() is { } invalid) throw new InvalidDataException(invalid);
        return result;
    }
    private void VerifyCapturedManifest(Original original, DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
    {
        var paths = Acquire(original, () => capture.OriginalFolderPaths);
        var files = Acquire(original, () => capture.OriginalFiles);
        if (!intent.Folders.Select(value => value.RelativePath).Order().SequenceEqual(paths.Order()) ||
            !intent.Files.Select(value => new DeveloperProjectCapturedSourceFile(value.RelativePath, value.SizeBytes, value.ContentSha256,
                value.OriginalSourceReference)).OrderBy(value => value.RelativePath).SequenceEqual(files.OrderBy(value => value.RelativePath)))
            throw new InvalidDataException("The retained setup manifest differs from the actual original source capture.");
    }
    private static string RecordId(AuthenticatedResourceActor actor, Guid setupId) => RecordType + ":" + actor.ProfileId + ":" + setupId.ToString("N");
    private static DeveloperProjectSetupCheckpoint Decode(HomeCoreStateRecord record)
    {
        if (record.RecordType != RecordType || record.SchemaVersion != 1 || record.Scope != HomeDataScope.DeviceLocal ||
            record.Authority != HomeRecordAuthority.LocalCanonical) throw new InvalidDataException("The Home setup record contract changed.");
        var value = record.Payload.Deserialize<DeveloperProjectSetupCheckpoint>() ?? throw new InvalidDataException("The setup journal is empty.");
        if (value.Intent.Validate() is { } invalid) throw new InvalidDataException(invalid);
        if (value.Revision != record.Revision || record.RecordId != RecordId(value.Intent.OriginalActor, value.Intent.SetupId) ||
            value.Observations.Length != value.Intent.Steps.Length ||
            !value.Observations.Select(row => row.StepId).SequenceEqual(value.Intent.Steps.Select(row => row.StepId)) ||
            value.Observations.Any(row => !Enum.IsDefined(row.State))) throw new InvalidDataException("The journal identity, revision or complete step observations are inconsistent.");
        return value;
    }
    private sealed class OriginalGuard(HomeDeveloperProjectSetupJournal owner, Original original,
        AuthenticatedResourceActor actor, IDeveloperProjectOriginalSourceCapture capture,
        Guid setupId, DeveloperProjectSetupCheckpoint? expected) : IHomeStateCommitActorGuard
    {
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expectedActor,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            if (actor != expectedActor || !owner.Acquire(original, () => owner._captureAuthority.IsIssuedOriginal(capture))) return false;
            if (!await owner.Await(original, () => owner._profileAuthority.CheckAsync(state, actor, phase, ct).AsTask()).ConfigureAwait(false)) return false;
            var record = state.Records.SingleOrDefault(value => value.RecordId == RecordId(actor, setupId));
            return owner.Acquire(original, () => owner._captureAuthority.IsIssuedOriginal(capture)) &&
                (expected is null ? record is null : record is not null && JsonSerializer.Serialize(Decode(record)) == JsonSerializer.Serialize(expected));
        }
    }
    private async Task RequireActorAsync(Original original, AuthenticatedResourceActor actor, CancellationToken ct)
    {
        if (await Await(original, () => _profileAuthority.GetCurrentAsync(ct).AsTask()).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The original authenticated local actor changed.");
    }
    private Task<T> Run<T>(Func<Original, Task<T>> body)
    {
        TaskCompletionSource start; Original original; Task<T> actual;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(HomeDeveloperProjectSetupJournal));
            if (_capacityFault is { } sticky) throw new AggregateException("Original Dev setup custody is unavailable.", sticky);
            _originals.RemoveAll(value => value.Task.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw _capacityFault ??= new InvalidOperationException("Original Dev setup task custody reached its finite 128-original runtime bound.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously); original = new() { Parent = _executing.Value };
            actual = Drive(start.Task, original, body); original.Task = actual; _originals.Add(original);
        }
        start.TrySetResult(); return actual;
    }
    private async Task<T> Drive<T>(Task start, Original original, Func<Original, Task<T>> body)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = original; Volatile.Write(ref original.Live, true);
        try { return await Await(original, () => body(original)).ConfigureAwait(false); }
        finally { Volatile.Write(ref original.Live, false); _executing.Value = previous; }
    }
    private T Acquire<T>(Original original, Func<T> actual)
    {
        (_callbacks ??= []).Add(this);
        try { return actual(); }
        catch (Exception error)
        {
            lock (_gate) Add(original.Causes, error);
            if (error is OperationCanceledException) throw new AggregateException("An actual synchronous Dev setup source returned no canceled original Task.", error);
            throw;
        }
        finally { _callbacks.RemoveAt(_callbacks.Count - 1); }
    }
    private async Task<T> Await<T>(Original original, Func<Task<T>> acquire)
    {
        var actual = Acquire(original, acquire); Retain(original, actual);
        try { return await actual.ConfigureAwait(false); }
        catch (Exception error) { Capture(original, actual, error); if (actual.IsFaulted) throw actual.Exception!; throw; }
    }
    private async Task Await(Original original, Func<Task> acquire)
    {
        var actual = Acquire(original, acquire); Retain(original, actual);
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) { Capture(original, actual, error); if (actual.IsFaulted) throw actual.Exception!; throw; }
    }
    private void Retain(Original original, Task actual)
    { lock (_gate) if (!original.Sources.Any(value => ReferenceEquals(value, actual))) original.Sources.Add(actual); }
    private void Capture(Original original, Task actual, Exception observed)
    { lock (_gate) foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { observed }.AsEnumerable()) Add(original.Causes, cause); }
    private static void Add(List<Exception> values, Exception error)
    { if (!values.Any(value => ReferenceEquals(value, error))) values.Add(error); }
    public void RequestRetirement() { lock (_gate) _retiring = true; }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_callbacks?.Any(value => ReferenceEquals(value, this)) == true) throw new InvalidOperationException("The actual Dev setup callback must return before joining its original.");
        for (var current = _executing.Value; current is not null; current = current.Parent)
            if (Volatile.Read(ref current.Live)) throw new InvalidOperationException("An original Dev setup producer cannot join its own retirement.");
        _captureAuthority.DemandExternalOriginalCaptureJoin();
        if (_originalStepOutcomeSource is not null)
        {
            (_callbacks ??= []).Add(this);
            try { _originalStepOutcomeSource().DemandExternalOriginalSetupStepOutcomeJoin(); }
            finally { _callbacks.RemoveAt(_callbacks.Count - 1); }
        }
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin(); TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drain(start.Task, _originals.ToArray()); _close = actual;
        }
        start.TrySetResult(); return actual;
    }
    private async Task Drain(Task start, Original[] originals)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        if (_capacityFault is { } sticky) Add(errors, sticky);
        foreach (var original in originals)
        {
            try { await original.Task.ConfigureAwait(false); } catch (Exception error) { Capture(original, original.Task, error); }
            Task[] sources; lock (_gate) sources = original.Sources.ToArray();
            foreach (var actual in sources)
                try { await actual.ConfigureAwait(false); } catch (Exception error) { Capture(original, actual, error); }
            lock (_gate) foreach (var cause in original.Causes) Add(errors, cause);
        }
        if (errors.Count != 0) throw new AggregateException("Actual Dev setup preparation/admission originals did not drain cleanly.", errors);
    }
}
