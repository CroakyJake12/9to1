using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
    : IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource,
      IDeveloperWorkspaceOriginalExecutionCommitBindingSource
{
    // Products and bindings have the existing setup owner's finite fail-stop custody.
    // Neither deserialised complete checkpoints nor ordinary store reads mint these refs.
    private sealed record SavedWorkspaceProduct(Physical Physical,
        Task<DeveloperProjectSetupCheckpoint> Acknowledgement, DeveloperProjectSetupCheckpoint Checkpoint);
    private readonly Dictionary<WorkspaceMetadataStep, SavedWorkspaceProduct> _originalSavedSteps = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ExecutionBinding> _originalExecutionBindings = [];
    private readonly HashSet<ExecutionPin> _originalExecutionPins = [];
    private sealed class ExecutionBinding(FilesDeveloperOriginalFolderSetupProducer owner, WorkspaceMetadataStep saved,
        Physical physical, FilesDeveloperOriginalSetupScopeSource.Destination destination,
        FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot registration,
        IDeveloperOriginalWorkspaceSetupStore store, IDeveloperWorkspaceOriginalExecutionCommitBindingSource native)
        : IDeveloperWorkspaceOriginalExecutionBinding, IDeveloperWorkspaceOriginalExecutionDescriptorEvidence
    {
        internal FilesDeveloperOriginalFolderSetupProducer Owner => owner;
        internal WorkspaceMetadataStep Saved => saved;
        internal Physical Physical => physical;
        internal FilesDeveloperOriginalSetupScopeSource.Destination Destination => destination;
        internal FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot Registration => registration;
        internal IDeveloperOriginalWorkspaceSetupStore Store => store;
        internal IDeveloperWorkspaceOriginalExecutionCommitBindingSource Native => native;
        internal string Receipt { get; } = "dev-execution-root:" + Guid.NewGuid().ToString("D");
        public Guid WorkspaceId => physical.Intent.WorkspaceId;
        public Guid ProjectId => physical.Intent.ProjectId;
        public Guid RootId => physical.Intent.RootId;
        public long WorkspaceRevision => saved.OriginalStoreResult.Workspace.Revision;
        public string CanonicalRoot => physical.Intent.OriginalExistingProjectRoot!;
        public AuthenticatedResourceActor OriginalActor => physical.Intent.OriginalActor;
        public IDeveloperProjectOriginalWorkspaceMetadataPreparation OriginalMetadataPreparation => physical.MetadataPreparation!;
        public Task OriginalMetadataWriteTask => saved.OriginalStoreResult.OriginalNativeWriteTask;
        public IDeveloperProjectOriginalWorkspaceMetadataObservation OriginalMetadataObservation => saved.OriginalStoreResult.OriginalObservation;
        public string OriginalFilesRoot => destination.Workspace.Configuration.RootDirectory;
        public string OriginalRegistrationStatePath => registration.StatePath;
        public string OriginalRegistrationStateJson => registration.StateJson;
    }
    private sealed class ExecutionPin(FilesDeveloperOriginalFolderSetupProducer owner, ExecutionBinding binding,
        IDeveloperWorkspaceOriginalExecutionCommitPin native, Original original)
        : IDeveloperWorkspaceOriginalExecutionCommitPin
    {
        internal FilesDeveloperOriginalFolderSetupProducer Owner => owner;
        internal ExecutionBinding Binding => binding;
        internal IDeveloperWorkspaceOriginalExecutionCommitPin Native => native;
        internal Original Original => original;
        internal Task? Close;
        internal bool Sealed;
        internal readonly List<Task> Cleanup = [];
        public void DemandOriginalExecutionBinding()
        {
            lock (owner._gate)
            {
                if (owner._retiring || Sealed || !owner._originalExecutionPins.Contains(this))
                    throw new UnauthorizedAccessException("The actual saved-root pin has retired.");
                // Native-only predicate. No Home/profile/Files reads beneath Home or Start.
                native.DemandOriginalExecutionBinding();
            }
        }
        public ValueTask DisposeAsync() => new(owner.CloseOriginalExecutionPin(this));
    }

    private Physical RequireOriginalSavedStep(WorkspaceMetadataStep saved)
    {
        lock (_gate)
            if (_originalSavedSteps.TryGetValue(saved, out var product) && product.Physical is { } physical &&
                ReferenceEquals(saved.OriginalAdmission, physical.Admission) &&
                ReferenceEquals(saved.OriginalStoreTask, physical.ActualWorkspaceStoreTask) &&
                ReferenceEquals(saved.OriginalStoreResult, physical.ActualWorkspaceStoreResult) &&
                ReferenceEquals(saved.OriginalJournalAcknowledgementTask, product.Acknowledgement) &&
                ReferenceEquals(saved.AcknowledgedCheckpoint, product.Checkpoint) &&
                product.Acknowledgement.IsCompletedSuccessfully && physical.OriginalOwner.Driver.IsCompletedSuccessfully &&
                saved.OriginalStoreTask.IsCompletedSuccessfully && physical.HomeValidated && physical.PhysicalConfirmed &&
                ReferenceEquals(saved.AcknowledgedCheckpoint.Intent, physical.Intent) &&
                saved.AcknowledgedCheckpoint.Observations.All(value => value.State == DeveloperProjectSetupStepState.Acknowledged))
                return physical;
        throw new UnauthorizedAccessException("No SAME private saved workspace and actual journal ACK product exists.");
    }
    public bool IsIssuedOriginalSavedWorkspaceStep(WorkspaceMetadataStep saved)
    { try { lock (_gate) if (_retiring) return false; RequireOriginalSavedStep(saved); return true; } catch (UnauthorizedAccessException) { return false; } }

    /// <summary>Resolve a private acknowledged setup product into an execution ownership
    /// observation. Home must separately review dev.workspace.execute for this exact root;
    /// Task/action/model permissions and the final native pin remain mandatory.</summary>
    public Task<IDeveloperWorkspaceOriginalExecutionBinding> AcquireOriginalExecutionBindingAsync(
        WorkspaceMetadataStep saved, DeveloperProjectReference project, CancellationToken cancellationToken)
        => Start<IDeveloperWorkspaceOriginalExecutionBinding>(async sources =>
    {
        var physical = sources.Invoke(() => RequireOriginalSavedStep(saved)); var intent = physical.Intent;
        if (project.WorkspaceId != intent.WorkspaceId || project.ProjectId != intent.ProjectId || project.RootId != intent.RootId ||
            project.WorkspaceRevision != saved.OriginalStoreResult.Workspace.Revision || project.ProjectRevision != 1 || project.RepositoryBindingId is not null)
            throw new UnauthorizedAccessException("The actual project/root/revisions differ from the acknowledged original workspace.");
        var store = sources.Invoke(() => _originalWorkspaceStore?.Invoke())
            ?? throw new InvalidOperationException("The SAME configured saved store is unavailable.");
        var native = sources.Invoke(() => _originalDirectories?.Invoke()) as IDeveloperWorkspaceOriginalExecutionCommitBindingSource
            ?? throw new PlatformNotSupportedException("No genuine saved-root descriptor pin owner is configured.");
        var destination = sources.Invoke(() => scopes.GetOriginalBoundDestination(intent, physical.Capture));
        // Fresh personal actor/config/root ownership; original file hashes are not execution
        // authority and are deliberately not revalidated after legitimate source edits.
        await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationAsync(destination, cancellationToken)).ConfigureAwait(false);
        var actual = await sources.Observe(() => store.GetAsync(intent.WorkspaceId, cancellationToken)).ConfigureAwait(false);
        RequireSameSavedWorkspace(saved, actual);
        if (!Guid.TryParse(destination.Workspace.Configuration.ProfileId, out var profile))
            throw new UnauthorizedAccessException("The actual personal Files profile is unavailable.");
        var registration = await sources.Observe(() => destination.Workspace.Directories.ObserveOriginalExecutionRegistrationAsync(
            profile, "dev.project." + intent.ProjectId.ToString("N"), new(intent.ProjectFolderId), destination.Workspace.Provider,
            intent.OriginalFilesStoreId, intent.OriginalExistingProjectRoot!, Scope, Retain, cancellationToken)).ConfigureAwait(false);
        await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationAsync(destination, cancellationToken)).ConfigureAwait(false);
        var binding = new ExecutionBinding(this, saved, physical, destination, registration, store, native);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_retiring || _originalExecutionBindings.Count >= 128)
                throw new InvalidOperationException("Saved-root binding custody is sealed or full; no execution grant was issued.");
            _originalExecutionBindings.Add(binding);
        }
        return binding;
    });
    private static void RequireSameSavedWorkspace(WorkspaceMetadataStep saved, DeveloperOperationResult<DeveloperWorkspace> actual)
    {
        if (!actual.Succeeded || actual.Value is null ||
            JsonSerializer.Serialize(actual.Value) != JsonSerializer.Serialize(saved.OriginalStoreResult.Workspace))
            throw new UnauthorizedAccessException("The complete actual saved workspace changed after original acknowledgement.");
    }
    private ExecutionBinding RequireOriginalExecutionBinding(IDeveloperWorkspaceOriginalExecutionBinding value)
    {
        lock (_gate)
            if (!_retiring && value is ExecutionBinding binding && ReferenceEquals(binding.Owner, this) &&
                _originalExecutionBindings.Contains(binding)) return binding;
        throw new UnauthorizedAccessException("The saved-root binding was copied, retired or issued by another owner.");
    }
    public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
    { try { RequireOriginalExecutionBinding(sameBinding); return true; } catch (UnauthorizedAccessException) { return false; } }
    public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        AuthenticatedResourceActor sameActor, CancellationToken cancellationToken) => Start(async sources =>
    {
        var binding = sources.Invoke(() => RequireOriginalExecutionBinding(sameBinding));
        if (sameActor != binding.OriginalActor) throw new UnauthorizedAccessException("The execution observation actor changed.");
        await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationAsync(binding.Destination, cancellationToken)).ConfigureAwait(false);
        if (!ReferenceEquals(sources.Invoke(() => _originalWorkspaceStore?.Invoke()), binding.Store) ||
            !ReferenceEquals(sources.Invoke(() => _originalDirectories?.Invoke()), binding.Native))
            throw new UnauthorizedAccessException("The configured saved store/native source changed.");
        var current = await sources.Observe(() => binding.Store.GetAsync(binding.WorkspaceId, cancellationToken)).ConfigureAwait(false);
        RequireSameSavedWorkspace(binding.Saved, current);
        var profile = Guid.Parse(binding.Destination.Workspace.Configuration.ProfileId);
        var registration = await sources.Observe(() => binding.Destination.Workspace.Directories.ObserveOriginalExecutionRegistrationAsync(
            profile, binding.Registration.Binding.OwningAppId, binding.Registration.Binding.FolderId, binding.Destination.Workspace.Provider,
            binding.Physical.Intent.OriginalFilesStoreId, binding.CanonicalRoot, Scope, Retain, cancellationToken)).ConfigureAwait(false);
        if (registration.StatePath != binding.Registration.StatePath || registration.StateJson != binding.Registration.StateJson ||
            registration.Binding != binding.Registration.Binding)
            throw new UnauthorizedAccessException("The exact saved-root registration changed.");
        await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationAsync(binding.Destination, cancellationToken)).ConfigureAwait(false);
        sources.Invoke(() => { cancellationToken.ThrowIfCancellationRequested(); RequireOriginalExecutionBinding(binding); return true; }); return true;
    });
    public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
    {
        var binding = RequireOriginalExecutionBinding(sameBinding);
        return [new ResourceScope("dev.workspace.execute", binding.Receipt,
            binding.WorkspaceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Execute)];
    }
    public IDeveloperWorkspaceOriginalExecutionDescriptorEvidence GetOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
        => RequireOriginalExecutionBinding(sameBinding);
    public bool IsIssuedOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionDescriptorEvidence sameEvidence)
        => ReferenceEquals(sameBinding, sameEvidence) && IsIssuedOriginalBinding(sameBinding);
    public void DemandExternalOriginalExecutionBindingJoin() => DemandExternalOriginalSetupStepOutcomeJoin();

    public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinAsync(
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, CancellationToken cancellationToken)
        => Start<IDeveloperWorkspaceOriginalExecutionCommitPin>(async sources =>
    {
        var binding = sources.Invoke(() => RequireOriginalExecutionBinding(sameBinding));
        await sources.ObserveVoid(() => RevalidateOriginalAsync(binding, binding.OriginalActor, cancellationToken)).ConfigureAwait(false);
        IDeveloperWorkspaceOriginalExecutionCommitPin? acquired = null; ExecutionPin? retained = null;
        var errors = new List<Exception>(); Task<IDeveloperWorkspaceOriginalExecutionCommitPin>? nativeAcquisition = null;
        try
        {
            await sources.ObserveProduct(() => nativeAcquisition = binding.Native.AcquireOriginalExecutionPinAsync(binding, cancellationToken), actual =>
            {
                acquired = actual;
                var original = _executing.Value ?? throw new InvalidOperationException("No original saved-root acquisition owns this pin.");
                retained = new ExecutionPin(this, binding, actual, original);
                lock (_gate) _originalExecutionPins.Add(retained);
            }).ConfigureAwait(false);
            if (acquired is null || !sources.Invoke(() => binding.Native.IsIssuedOriginalExecutionPin(binding, acquired)))
                throw new UnauthorizedAccessException("No SAME private configured native saved-root pin exists.");
            lock (_gate) { cancellationToken.ThrowIfCancellationRequested(); RequireOriginalExecutionBinding(binding); }
            return retained!;
        }
        catch (Exception error) { if (nativeAcquisition is not null) AddTask(errors, nativeAcquisition, error); else Add(errors, error); }
        if (retained is not null) try { await sources.ObserveVoid(() => CloseOriginalExecutionPin(retained, true)).ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
        else if (acquired is not null) try { await sources.ObserveVoid(() => acquired.DisposeAsync().AsTask()).ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
        if (nativeAcquisition?.IsCanceled == true && errors.All(value => value is OperationCanceledException))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("Saved-root pin acquisition/publication/cleanup failed; no execution grant.", errors);
    });
    public bool IsIssuedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin)
    {
        lock (_gate) return !_retiring && samePin is ExecutionPin pin && ReferenceEquals(pin.Owner, this) && !pin.Sealed &&
            ReferenceEquals(pin.Binding, sameBinding) && _originalExecutionPins.Contains(pin) &&
            _originalExecutionBindings.Contains(pin.Binding);
    }
    private Task CloseOriginalExecutionPin(ExecutionPin pin, bool acquisitionCleanup = false)
    {
        if (acquisitionCleanup && !ReferenceEquals(_executing.Value, pin.Original))
            throw new InvalidOperationException("Only the SAME private acquiring original may close its unpublished pin.");
        if (!acquisitionCleanup && _physical?.ContainsKey(this) == true) throw new InvalidOperationException("An actual pin callback cannot join its owner.");
        for (var current = _executing.Value; current is not null; current = current.Parent)
            if (!acquisitionCleanup && Volatile.Read(ref current.Live) && ReferenceEquals(current, pin.Original))
                throw new InvalidOperationException("The actual pin acquisition cannot join itself.");
        TaskCompletionSource start; Task close;
        lock (_gate)
        {
            if (pin.Close is not null) return pin.Close;
            pin.Sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pin.Close = close = DrainOriginalExecutionPin(start.Task, pin);
        }
        start.TrySetResult(); return close;
    }
    private async Task DrainOriginalExecutionPin(Task start, ExecutionPin pin)
    {
        await start.ConfigureAwait(false); Task? raw = null; var errors = new List<Exception>();
        try { Scope(() => { raw = pin.Native.DisposeAsync().AsTask(); lock (_gate) pin.Cleanup.Add(raw); }); }
        catch (Exception error) { Add(errors, error); }
        if (raw is not null) try { await raw.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, raw, error); }
        if (errors.Count != 0) throw new AggregateException("The actual saved-root pin cleanup did not settle cleanly.", errors);
    }
    private async Task DrainOriginalExecutionPins(List<Exception> errors)
    {
        ExecutionPin[] pins; lock (_gate) pins = _originalExecutionPins.ToArray(); var closes = new List<Task>();
        foreach (var pin in pins) try { closes.Add(CloseOriginalExecutionPin(pin)); } catch (Exception error) { Add(errors, error); }
        foreach (var raw in closes) try { await raw.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, raw, error); }
    }
}
