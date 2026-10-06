using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

/// <summary>First real setup effector stage: create-only canonical folder metadata through
/// the existing Files provider. It keeps one actual declared operation, Home entry, provider
/// result and pending/ACK journal lineage. Unsupported physical/map/workspace steps refuse
/// BEFORE pending admission; this partial producer cannot claim whole-project setup success.</summary>
public sealed class FilesDeveloperOriginalFolderSetupProducer(FilesDeveloperOriginalSetupScopeSource scopes,
    Func<HomeDeveloperProjectSetupJournal> journal,
    Func<IDeveloperProjectOriginalSetupPermissionSource> permissions)
    : IDeveloperProjectOriginalSetupStepOutcomeSource, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly AsyncLocal<Original?> _executing = new();
    [ThreadStatic] private static Dictionary<FilesDeveloperOriginalFolderSetupProducer, int>? _physical;
    [ThreadStatic] private static HashSet<FilesDeveloperOriginalFolderSetupProducer>? _checkingDependencies;
    private readonly HashSet<Original> _originals = [];
    private readonly HashSet<HomeDeveloperProjectSetupJournal> _journals = [];
    private readonly Dictionary<(Guid Setup, Guid Step), Physical> _steps = [];
    private bool _retiring;
    private Task? _close;
    private sealed class Original(Original? parent)
    {
        internal Original? Parent => parent;
        internal Task Driver = null!;
        internal bool Live;
        internal bool Healthy;
        internal readonly List<Task> Sources = [];
    }
    private sealed class Physical(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
        DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupPermission permission,
        IDeveloperProjectOriginalSetupPermissionSource permissionSource, HomeDeveloperProjectSetupJournal owner, Original originalOwner)
    {
        internal DeveloperProjectSetupIntent Intent => intent;
        internal IDeveloperProjectOriginalSourceCapture Capture => capture;
        internal DeveloperProjectSetupStep Step => step;
        internal IDeveloperProjectOriginalSetupPermission Permission => permission;
        internal IDeveloperProjectOriginalSetupPermissionSource PermissionSource => permissionSource;
        internal HomeDeveloperProjectSetupJournal Journal => owner;
        internal Original OriginalOwner => originalOwner;
        internal HomeDeveloperProjectSetupJournal.OriginalStepAdmission? Admission;
        internal Task<FilesResult<FilesOperation>>? ActualProviderTask;
        internal FilesResult<FilesOperation>? ActualResult;
        internal IDeveloperProjectOriginalSetupStepEntry? ActualEntry;
        internal Task? ActualEntryClose;
        internal volatile bool PhysicalConfirmed;
        internal volatile bool HomeValidated;
        internal DeveloperProjectOriginalStepOutcomeObservation? Observation;
    }
    public sealed class FolderStep
    {
        internal FolderStep(HomeDeveloperProjectSetupJournal.OriginalStepAdmission admission, Task actualTask,
            FilesResult<FilesOperation> result, DeveloperProjectSetupCheckpoint checkpoint)
        { OriginalAdmission = admission; OriginalProviderTask = actualTask; OriginalProviderResult = result; AcknowledgedCheckpoint = checkpoint; }
        public HomeDeveloperProjectSetupJournal.OriginalStepAdmission OriginalAdmission { get; }
        public Task OriginalProviderTask { get; }
        public FilesResult<FilesOperation> OriginalProviderResult { get; }
        public DeveloperProjectSetupCheckpoint AcknowledgedCheckpoint { get; }
    }
    private void Scope(Action source)
    {
        var values = _physical ??= []; values.TryGetValue(this, out var before); values[this] = before + 1;
        try { source(); }
        finally { if (before == 0) values.Remove(this); else values[this] = before; }
    }
    private void Retain(Task actual)
    {
        var original = _executing.Value ?? throw new InvalidOperationException("No actual folder original owns this returned Task.");
        lock (_gate) original.Sources.Add(actual);
    }
    public void DemandExternalOriginalSetupStepOutcomeJoin()
    {
        if (_physical?.ContainsKey(this) == true) throw new InvalidOperationException("An actual setup callback cannot join its folder producer.");
        for (var original = _executing.Value; original is not null; original = original.Parent)
            if (Volatile.Read(ref original.Live)) throw new InvalidOperationException("An actual folder original cannot join itself.");
        var visited = _checkingDependencies ??= [];
        if (!visited.Add(this)) return;
        try
        {
            scopes.DemandExternalOriginalSetupScopeJoin();
            HomeDeveloperProjectSetupJournal[] owners; IDeveloperProjectOriginalSetupPermissionSource[] issuers;
            lock (_gate) { owners = _journals.ToArray(); issuers = _steps.Values.Select(value => value.PermissionSource).Distinct().ToArray(); }
            // The scope owner is already walking these journal dependencies in the
            // recursive source/journal/outcome path. It has checked actual live contexts.
            if (!scopes.IsCheckingOriginalJournalDependencies)
                foreach (var owner in owners) owner.DemandExternalOriginalRetirementJoin();
            foreach (var issuer in issuers) issuer.DemandExternalOriginalSetupJoin();
        }
        finally { visited.Remove(this); }
    }
    private Task<T> Start<T>(Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        TaskCompletionSource start; Task<T> driver;
        lock (_gate)
        {
            var parent = _executing.Value;
            if (_retiring && (parent is null || !Volatile.Read(ref parent.Live))) throw new InvalidOperationException("The setup folder producer is retiring.");
            _originals.RemoveWhere(value => value.Healthy && value.Driver.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unresolved setup folder task custody is full.");
            var original = new Original(parent); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            driver = Run(start.Task, original, body); original.Driver = driver; _originals.Add(original);
        }
        start.TrySetResult(); return driver;
    }
    private async Task<T> Run<T>(Task start, Original original, Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = original; Volatile.Write(ref original.Live, true);
        var sources = new FilesOriginalReadSourceScope(Scope, Retain);
        Task<T>? actualBody = null; T result = default!; var errors = new List<Exception>();
        try
        {
            try { result = await sources.Observe(() => actualBody = body(sources)).ConfigureAwait(false); }
            catch (Exception error) { Add(errors, error); }
            Task[] actual; lock (_gate) actual = original.Sources.ToArray();
            foreach (var task in actual) try { await task.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, task, error); }
            if (errors.Count != 0)
            {
                if (actualBody?.IsCanceled == true && actual.All(task => !task.IsFaulted) && errors.All(value => value is OperationCanceledException))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
                throw new AggregateException("Original folder setup did not settle cleanly; retain its SAME pending/known outcome.", errors);
            }
            original.Healthy = true; return result;
        }
        finally { Volatile.Write(ref original.Live, false); _executing.Value = previous; }
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (error is AggregateException group && group.InnerExceptions.Count != 0) foreach (var cause in group.InnerExceptions) Add(errors, cause);
      else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error); }
    private static void AddTask(List<Exception> errors, Task actual, Exception caught)
    { foreach (var error in actual.Exception?.InnerExceptions ?? new[] { caught }.AsEnumerable()) Add(errors, error); }

    public Task<FolderStep> ExecuteOriginalFolderStepAsync(HomeDeveloperProjectSetupJournal.Prepared samePrepared,
        IDeveloperProjectOriginalSourceCapture sameCapture, IDeveloperProjectOriginalSetupPermission samePermission,
        DeveloperProjectSetupStep sameStep, CancellationToken token = default) => Start(async sources =>
    {
        if (sameStep.Kind is not (DeveloperProjectSetupStepKind.CreateProjectFolder or DeveloperProjectSetupStepKind.CreateChildFolder))
            throw new NotSupportedException("This original stage creates canonical folder metadata only; no unsupported physical step was admitted.");
        var owner = sources.Invoke(journal) ?? throw new InvalidOperationException("The actual setup journal is unavailable.");
        lock (_gate) _journals.Add(owner);
        await sources.ObserveVoid(() => owner.ValidateOriginalPreparationAsync(samePrepared, sameCapture, token)).ConfigureAwait(false);
        var intent = sources.Invoke(() => samePrepared.Intent);
        if (!intent.Steps.Any(value => ReferenceEquals(value, sameStep))) throw new UnauthorizedAccessException("Retain the SAME actual declared folder step.");
        await sources.ObserveVoid(() => scopes.RevalidateOriginalSetupAsync(intent, sameCapture, intent.OriginalActor, token)).ConfigureAwait(false);
        var destination = sources.Invoke(() => scopes.GetOriginalBoundDestination(intent, sameCapture));
        var issuer = sources.Invoke(permissions) ?? throw new InvalidOperationException("The genuine Home setup issuer is not configured.");
        await sources.ObserveVoid(() => issuer.ValidateOriginalAsync(intent, samePermission, token)).ConfigureAwait(false);
        var physical = new Physical(intent, sameCapture, sameStep, samePermission, issuer, owner,
            _executing.Value ?? throw new InvalidOperationException("The actual published step producer is unavailable."));
        lock (_gate)
        {
            if (_steps.Count >= DeveloperProjectSetupIntent.MaximumSteps) throw new InvalidOperationException("The explicit original step receipt custody limit is full.");
            if (!_steps.TryAdd((intent.SetupId, sameStep.StepId), physical)) throw new InvalidOperationException("This SAME original step is already attempted; unknown or committed work cannot be replayed.");
        }
        var target = new HostedItemId(sameStep.FolderId ?? throw new InvalidDataException("The declared folder ID is absent."));
        var parent = new HostedItemId(intent.OriginalDestinationFolderId); string name = intent.ProjectName;
        var expectedParentRevision = destination.Folder.CurrentRevisionId!.Value;
        if (sameStep.Kind == DeveloperProjectSetupStepKind.CreateChildFolder)
        {
            var declared = intent.Folders.Single(value => value.FolderId == target.Value); parent = new(declared.ParentFolderId);
            name = declared.RelativePath.Split('/')[^1];
            // An actual prior SAME-source folder commit, subsequently journal-acknowledged,
            // must own a newly declared parent. Persisted/public IDs alone cannot supply it.
            var parentStep = intent.Steps.Single(value => value.FolderId == parent.Value &&
                value.Kind is DeveloperProjectSetupStepKind.CreateProjectFolder or DeveloperProjectSetupStepKind.CreateChildFolder);
            Physical? prior; lock (_gate) _steps.TryGetValue((intent.SetupId, parentStep.StepId), out prior);
            if (prior is null || !prior.HomeValidated || prior.Admission is null || prior.ActualResult?.Value?.ResultRevisionId is null)
                throw new UnauthorizedAccessException("No actual acknowledged SAME-owner parent folder exists.");
            var originalParentResult = prior.ActualResult!.Value!;
            expectedParentRevision = originalParentResult.ResultRevisionId!.Value;
            var index = intent.Steps.IndexOf(parentStep);
            if (samePrepared.Intent != intent || samePrepared.Intent.Steps[index] != parentStep)
                throw new UnauthorizedAccessException("The private project plan changed.");
        }
        var parentRow = await sources.Observe(() => destination.Workspace.Provider.GetForOriginalStoreAtRevisionAsync(
            intent.OriginalFilesStoreId, parent, expectedParentRevision, token)).ConfigureAwait(false);
        if (!parentRow.IsSuccess || parentRow.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: { } parentRevision } row ||
            row.OwnerPrincipalId != intent.OriginalActor.ActorId || row.IsShared || row.LocationId != destination.Workspace.Configuration.LocationId ||
            sameStep.Kind == DeveloperProjectSetupStepKind.CreateProjectFolder && row != destination.Folder)
            throw new UnauthorizedAccessException("The actual canonical parent folder is unavailable or changed.");
        physical.Admission = await sources.Observe(() => owner.AdmitOriginalStepAsync(samePrepared, sameStep, token)).ConfigureAwait(false);
        if (!ReferenceEquals(samePrepared.Intent, intent) || !ReferenceEquals(physical.Admission.Step, sameStep))
            throw new UnauthorizedAccessException("Pending admission did not conserve the SAME reviewed intent and step.");
        var errors = new List<Exception>();
        try
        {
            await sources.ObserveProduct(() => samePermission.EnterOriginalStepAsync(sameStep, token),
                actualEntry => physical.ActualEntry = actualEntry).ConfigureAwait(false);
            var entry = physical.ActualEntry ?? throw new InvalidOperationException("The actual Home entry supplied no retained product.");
            if (!sources.Invoke(() => samePermission.IsIssuedOriginalStepEntry(sameStep, entry)))
                throw new UnauthorizedAccessException("The SAME configured permission did not privately issue this active step entry.");
            sources.Invoke(() => { entry.DemandOriginalStepEntry(sameStep); return true; });
            var now = DateTimeOffset.UtcNow;
            var operation = new FilesOperation(new(sameStep.StepId), intent.OriginalActor.ActorId, target,
                null, parent, "CreateFolder", parentRevision, null, FilesOperationState.Pending, now, now, null, null);
            var guard = new FilesCommitAuthorityGuard(intent.OriginalActor.ActorId, ct => new ValueTask<bool>(
                sources.Observe(() => entry.CheckOriginalStepCommitAsync(sameStep, ct).AsTask())));
            // SAME validated permission issued this entry. Capture its exact returned native
            // provider Task INSIDE the finite callback, before entry/scope exit can fail.
            try
            {
                var returned = sources.Invoke(() => entry.RunOriginalStep(sameStep, () =>
                {
                    physical.ActualProviderTask = destination.Workspace.Provider.CreateOriginalDeveloperFolderAsync(operation,
                        name, intent.OriginalFilesStoreId, [new(parent, parentRevision)], guard, token);
                    Retain(physical.ActualProviderTask);
                    return physical.ActualProviderTask;
                }, token));
                if (!ReferenceEquals(returned, physical.ActualProviderTask))
                    throw new UnauthorizedAccessException("The held original entry substituted the returned Files Task.");
            }
            catch (Exception error) { Add(errors, error); }
            if (physical.ActualProviderTask is { } actual)
                try { physical.ActualResult = await sources.Observe(() => actual).ConfigureAwait(false); }
                catch (Exception error) { AddTask(errors, actual, error); }
            if (errors.Count == 0 && (physical.ActualResult is not { IsSuccess: true, Value: { State: FilesOperationState.Committed, ResultRevisionId: { } revision } committed } ||
                revision.Value == Guid.Empty || committed.Id.Value != sameStep.StepId || committed.ItemId != target || committed.DestinationParentId != parent ||
                committed.ActorId != intent.OriginalActor.ActorId))
                throw new InvalidDataException("The actual Files producer did not confirm this exact declared canonical folder operation.");
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (physical.ActualEntry is { } entry)
            {
                try
                {
                    await sources.ObserveVoid(() => physical.ActualEntryClose = entry.DisposeAsync().AsTask()).ConfigureAwait(false);
                }
                catch (Exception error) { Add(errors, error); }
            }
        }
        if (errors.Count != 0) throw new AggregateException("The original folder operation/entry cleanup is failed or unknown; no ACK or replay was issued.", errors);
        physical.PhysicalConfirmed = true;
        physical.Observation = new("dev-original-folder:" + Guid.NewGuid().ToString("D"),
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(physical.ActualResult!.Value))).ToLowerInvariant());
        await sources.ObserveVoid(() => samePermission.ValidateOriginalStepResultAsync(sameStep, physical.ActualEntry!,
            physical.ActualProviderTask!, physical.ActualResult, token)).ConfigureAwait(false);
        physical.HomeValidated = true;
        var checkpoint = await sources.Observe(() => owner.AcknowledgeOriginalStepAsync(physical.Admission!, sameCapture,
            physical.ActualProviderTask!, physical.ActualResult, token)).ConfigureAwait(false);
        return new FolderStep(physical.Admission!, physical.ActualProviderTask!, physical.ActualResult!, checkpoint);
    });

    private Physical RequirePhysical(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
        DeveloperProjectSetupStep step, Task actual, object? result)
    {
        lock (_gate)
            if (_steps.TryGetValue((intent.SetupId, step.StepId), out var physical) && ReferenceEquals(physical.Intent, intent) &&
                ReferenceEquals(physical.Capture, capture) && ReferenceEquals(physical.Step, step) &&
                ReferenceEquals(physical.ActualProviderTask, actual) && ReferenceEquals(physical.ActualResult, result) && physical.PhysicalConfirmed)
                return physical;
        throw new UnauthorizedAccessException("No SAME privately confirmed actual folder Task/result/entry cleanup exists.");
    }
    public bool IsIssuedOriginalStepOutcome(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
        DeveloperProjectSetupStep step, Task actual, object? result)
    { try { RequirePhysical(intent, capture, step, actual, result); return true; } catch (UnauthorizedAccessException) { return false; } }
    public Task ValidateOriginalStepOutcomeAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
        DeveloperProjectSetupStep step, Task actual, object? result, CancellationToken token) => Start(async sources =>
    {
        var physical = RequirePhysical(intent, capture, step, actual, result);
        await sources.Observe(() => physical.ActualProviderTask!).ConfigureAwait(false);
        await sources.ObserveVoid(() => physical.ActualEntryClose!).ConfigureAwait(false);
        await sources.ObserveVoid(() => scopes.RevalidateOriginalSetupAsync(intent, capture, intent.OriginalActor, token)).ConfigureAwait(false);
        RequirePhysical(intent, capture, step, actual, result); return true;
    });
    public DeveloperProjectOriginalStepOutcomeObservation GetOriginalStepOutcomeObservation(DeveloperProjectSetupIntent intent,
        IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task actual, object? result)
    {
        var physical = RequirePhysical(intent, capture, step, actual, result);
        if (!physical.HomeValidated || physical.Observation is null) throw new UnauthorizedAccessException("The genuine Home validator has not acknowledged this physical step.");
        return physical.Observation;
    }
    public void RequestOriginalFolderSetupRetirement() { lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalFolderSetupsAsync()
    {
        DemandExternalOriginalSetupStepOutcomeJoin(); TaskCompletionSource start; Task actual; Original[] originals;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; originals = _originals.Concat(_steps.Values.Select(value => value.OriginalOwner)).Distinct().ToArray(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drain(start.Task, originals); _close = actual;
        }
        start.TrySetResult(); return actual;
    }
    private static async Task Drain(Task start, Original[] originals)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        foreach (var original in originals) try { await original.Driver.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, original.Driver, error); }
        if (errors.Count != 0) throw new AggregateException("Actual folder setup originals failed; partial outcomes remain retained.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalFolderSetupsAsync());
}
