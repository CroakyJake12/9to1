using System.Buffers.Binary;
using System.Collections.Frozen;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

/// <summary>Actual protected Linux descriptor/file producer. The SAME existing Task permission
/// issuer supplies current model use, and a separate real installation issuer supplies trusted
/// installation/read provenance. Neither paths/digests nor this kernel observation creates either
/// grant. Taskless/global eager acquisition is unsupported. Constructor performs no I/O.</summary>
public sealed class StrataNativeArtifactSource : IOriginalStrataModelSource, IOriginalStrataWorkerSource, IAsyncDisposable
{
    private readonly TaskExecutionCoordinator _tasks;
    private readonly TaskRunPermissionAuthority _authority;
    private readonly IStrataVerifiedInstallationSource? _installations;
    private readonly object _gate = new();
    private readonly List<Work> _work = [];
    private readonly List<HeldLease> _leases = [];
    private readonly Dictionary<TaskRunAttemptAdmission, SharedPin> _pins = new(ReferenceEqualityComparer.Instance);
    private readonly AsyncLocal<Work?> _executing = new();
    private readonly AsyncLocal<ClosePhase?> _closingExecuting = new();
    [ThreadStatic] private static List<StrataNativeArtifactSource>? _physical;
    private bool _sealed; private Task? _close;
    public StrataNativeArtifactSource(TaskExecutionCoordinator tasks, TaskRunPermissionAuthority authority,
        IStrataVerifiedInstallationSource? installations = null)
    { _tasks = tasks; _authority = authority; _installations = installations; }

    public Task<StrataOriginalModelLease> AcquireOriginalAsync(ModelIdentity sameModel, TaskRunAttemptAdmission sameAdmission,
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken token) =>
        Start(originalScope, work => AcquireAsync(work, sameModel, sameAdmission, worker: false, token))
            .ContinueModel();

    public Task<StrataOriginalWorkerLease> AcquireOriginalAsync(TaskRunAttemptAdmission sameAdmission,
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken token) =>
        Start(originalScope, work => AcquireAsync(work,
            new ModelIdentity(sameAdmission.Lease.Candidate.ProviderId, sameAdmission.Lease.Candidate.ModelId, sameAdmission.Lease.Candidate.ArtifactIdentity), sameAdmission, worker: true, token))
            .ContinueWorker();

    public bool IsIssuedOriginalModelLease(StrataOriginalModelLease lease, ModelIdentity model, TaskRunAttemptAdmission admission)
    { lock (_gate) return lease is ModelLease actual && _leases.Contains(actual.Held) &&
        ReferenceEquals(actual.Held.Admission, admission) && actual.Held.Model == model && ReferenceEquals(actual.Held.Issuer, this); }
    public bool IsIssuedOriginalWorkerLease(StrataOriginalWorkerLease lease, TaskRunAttemptAdmission admission)
    { lock (_gate) return lease is WorkerLease actual && _leases.Contains(actual.Held) &&
        ReferenceEquals(actual.Held.Admission, admission) && ReferenceEquals(actual.Held.Issuer, this); }

    private Task<object> Start(IInferenceEngineOriginalSourceScope scope, Func<Work, Task<object>> body)
    {
        ArgumentNullException.ThrowIfNull(scope); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Work work; lock (_gate)
        {
            if (_sealed) throw new ObjectDisposedException(nameof(StrataNativeArtifactSource));
            if (_work.Count >= 128) throw new InvalidOperationException("Original protected artifact custody is full.");
            work = new Work(this, scope); _work.Add(work); work.Driver = RunAsync(work, start.Task, body);
        }
        try { InvokePhysical(() => { scope.RetainOriginalTask(work.Driver); return 0; }); } catch (Exception error) { work.Add(null, error); }
        finally { start.SetResult(); }
        return work.Driver;
    }
    private T InvokePhysical<T>(Func<T> callback)
    {
        var physical = _physical ??= []; physical.Add(this);
        try { return callback(); }
        catch (OperationCanceledException cause) { throw new AggregateException("Actual synchronous protected artifact callback fault.", cause); }
        finally { physical.RemoveAt(physical.Count - 1); }
    }
    private async Task<object> RunAsync(Work work, Task start, Func<Work, Task<object>> body)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = work;
        object? result = null;
        try { work.Throw(); result = await body(work).ConfigureAwait(false); }
        catch (Exception error) { work.Add(null, error); }
        finally { work.Live = false; _executing.Value = previous; }
        work.Throw(); return result ?? throw new InvalidDataException("No original artifact product was acquired.");
    }
    private async Task<object> AcquireAsync(Work work, ModelIdentity requested, TaskRunAttemptAdmission admission, bool worker, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (_installations is null) throw Setup(requested, "STRATA_VERIFIED_INSTALLATION_SOURCE_REQUIRED");
        if (_tasks is not ITaskRunOriginalInferenceAttemptSource attempts)
            throw Setup(requested, "STRATA_ORIGINAL_COORDINATOR_SOURCE_SCOPE_REQUIRED");
        if (_authority is not ITaskRunOriginalInferenceAdmissionSource permissions)
            throw Setup(requested, "STRATA_ORIGINAL_PERMISSION_SOURCE_SCOPE_REQUIRED");
        StrataVerifiedInstallationLease? installation = null; IAsyncDisposable? pin = null; ProtectedRoot? root = null;
        HeldLease? held = null;
        try
        {
            var current = await work.ReadAsync(() => attempts.GetIssuedAttemptWithinOriginalSourceAsync(admission,
                work.Run, work.Retain, token)).ConfigureAwait(false);
            if (!ReferenceEquals(current, admission) || admission.Lease.Candidate.UsesCloud ||
                admission.Lease.Candidate.ProviderId != requested.ProviderId || admission.Lease.Candidate.ModelId != requested.ModelId)
                throw new UnauthorizedAccessException("The SAME current original local model admission is required.");
            await work.ReadAsync(() => permissions.ValidateOriginalInferenceAdmissionAsync(admission,
                work.Run, work.Retain, token)).ConfigureAwait(false);
            // Resource capture occurs inside callback before a caller scope can fault after return.
            await work.AcquireAsync(() => _installations.AcquireOriginalAsync(requested, admission, work, token),
                value => installation = value).ConfigureAwait(false);
            var metadata = work.Invoke(() =>
            {
                if (installation is null || !_installations.IsIssuedOriginalInstallation(installation, requested, admission) ||
                    !ReferenceEquals(installation.OriginalAdmission, admission) || installation.OriginalModel.ProviderId != requested.ProviderId ||
                    installation.OriginalModel.ModelId != requested.ModelId || installation.OriginalModel != requested)
                    throw new UnauthorizedAccessException("SAME private verified installation/model revision provenance is required.");
                installation.DemandCurrentOriginalInstallation(); return Capture(installation);
            });
            var model = metadata.Model;
            await work.AcquireAsync(() => AcquireSharedPinAsync(work, admission, token), value => pin = value).ConfigureAwait(false);
            if (pin is null) throw new UnauthorizedAccessException("Original model-use lifetime pin was refused.");
            work.Invoke(() => { permissions.DemandOriginalInferenceAdmission(admission); return 0; });
            _ = work.Invoke(() => { root = new ProtectedRoot(worker ? metadata.WorkerRoot : metadata.CheckpointRoot,
                immutableDirectory: !worker); return root; });
            if (worker)
                await root!.ReadFileAsync(metadata.WorkerFile, work, token, worker: true).ConfigureAwait(false);
            else
                await root!.ReadCheckpointAsync(metadata.Files, metadata.Requirements, model, work, token).ConfigureAwait(false);
            StrataOriginalHardwareObservation? hardware = null;
            if (!worker)
            {
                hardware = await work.ReadAsync(() => new StrataHardwareObservationSource(this).ObserveOriginalAsync(admission, work, token)).ConfigureAwait(false);
                var indices = metadata.Devices;
                if (indices.Length == 0 || indices.Distinct().Count() != indices.Length ||
                    indices.Any(index => !hardware.ActualDeviceIndices.Contains(index)))
                    throw new UnauthorizedAccessException("The actual selected CUDA devices are absent or ambiguous in the genuine hardware probe.");
            }
            // Fresh policy/actor and current private row after ALL artifact reads, outside a
            // Home/store/CAS lock. The actual attempt lifetime pin is not a transaction lock.
            await work.ReadAsync(() => permissions.ValidateOriginalInferenceAdmissionAsync(admission, work.Run, work.Retain, token)).ConfigureAwait(false);
            current = await work.ReadAsync(() => attempts.GetIssuedAttemptWithinOriginalSourceAsync(admission, work.Run, work.Retain, token)).ConfigureAwait(false);
            if (!ReferenceEquals(current, admission)) throw new UnauthorizedAccessException("The original model admission changed during artifact reads.");
            work.Invoke(() => { installation!.DemandCurrentOriginalInstallation(); root!.DemandCurrent(); permissions.DemandOriginalInferenceAdmission(admission); return 0; });
            held = new HeldLease(this, work, model, admission, installation!, pin, root!, hardware, metadata);
            lock (_gate) { if (_sealed) throw new ObjectDisposedException(nameof(StrataNativeArtifactSource)); _leases.Add(held); }
            installation = null; pin = null; root = null;
            work.Published = true; // Future live lease callbacks use own physical source phase, never revive completed initialization ancestry.
            return worker ? new WorkerLease(held) : new ModelLease(held);
        }
        catch (Exception cause) { work.Add(null, cause); }
        finally
        {
            // Whole originals and late products independently settle before failure disclosure.
            await work.JoinRawAsync().ConfigureAwait(false);
            if (held is not null && work.Errors.Count != 0)
            {
                try { await held.CloseOriginalAsync(sourceOwned: true).ConfigureAwait(false); }
                catch (Exception cleanup) { work.Add(null, cleanup); }
            }
            if (root is not null) work.Cleanup(() => root.Dispose());
            if (pin is not null) await work.CloseAsync(pin).ConfigureAwait(false);
            if (installation is not null) await work.CloseAsync(installation).ConfigureAwait(false);
        }
        work.Throw(); throw new InvalidDataException("No original protected artifact lease was published.");
    }
    private static InferenceEngineException Setup(ModelIdentity model, string missing) =>
        new(new(DulcheErrorCode.ProviderUnavailable, missing, model.StableKey, false));
    private sealed record InstallationCapture(ModelIdentity Model, string WorkerRoot, StrataInstalledFile WorkerFile,
        string CheckpointRoot, IReadOnlyList<StrataInstalledFile> Files, InferenceModelRequirements Requirements, int[] Devices,
        InferenceEngineSupport? BuildSupport);
    private static InstallationCapture Capture(StrataVerifiedInstallationLease installation)
    {
        var requirements = installation.OriginalRequirements;
        return new(installation.OriginalModel, installation.OriginalWorkerRoot, installation.OriginalWorkerFile,
            installation.OriginalCheckpointRoot, Array.AsReadOnly(installation.OriginalCheckpointFiles.ToArray()),
            requirements with { RequiredFeatures = requirements.RequiredFeatures.ToFrozenSet(StringComparer.Ordinal),
                PreferredEngines = requirements.PreferredEngines is null ? null : Array.AsReadOnly(requirements.PreferredEngines.ToArray()) },
            installation.OriginalCudaDeviceIndices.ToArray(), installation.OriginalBuildSupport is not { } support ? null : support with
            { Architectures = support.Architectures.ToFrozenSet(StringComparer.Ordinal), Families = support.Families.ToFrozenSet(StringComparer.Ordinal),
                WeightFormats = support.WeightFormats.ToFrozenSet(StringComparer.Ordinal), Quantizations = support.Quantizations.ToFrozenSet(StringComparer.Ordinal),
                Features = support.Features.ToFrozenSet(StringComparer.Ordinal), OperatingSystems = support.OperatingSystems.ToFrozenSet(StringComparer.Ordinal),
                CpuArchitectures = support.CpuArchitectures.ToFrozenSet(StringComparer.Ordinal), RequiredCpuFeatures = support.RequiredCpuFeatures.ToFrozenSet(StringComparer.Ordinal),
                NativeRegistrations = support.NativeRegistrations?.ToFrozenSet(StringComparer.Ordinal) });
    }
    // Worker + model + no-model probe share ONE original attempt pin. Acquiring the same
    // non-reentrant lease semaphore twice would otherwise block model initialization behind
    // its own held worker. Only private same-source borrows are coalesced; no ID reconstruction.
    private async Task<IAsyncDisposable?> AcquireSharedPinAsync(Work work, TaskRunAttemptAdmission admission, CancellationToken token)
    {
        SharedPin state; TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_sealed) throw new ObjectDisposedException(nameof(StrataNativeArtifactSource));
            if (!_pins.TryGetValue(admission, out state!) || state.Closing)
            {
                if (admission.Lease is not ITaskRunAdmissionCommitLease commit)
                    throw new InvalidOperationException("The genuine original inference commit lease is unavailable.");
                state = new SharedPin(this, work); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                state.Driver = AcquirePinPublishedAsync(start.Task, state, commit, token); _pins[admission] = state;
            }
            state.Borrows++;
        }
        try { work.Retain(state.Driver); }
        catch (Exception error) { work.Add(state.Driver, error); }
        finally { start?.SetResult(); }
        try
        {
            await state.Driver.ConfigureAwait(false);
            work.Throw();
            if (state.Original is null) throw new UnauthorizedAccessException("The original inference attempt pin was refused.");
            return new PinBorrow(state);
        }
        catch (Exception cause)
        {
            work.Add(state.Driver, cause);
            try { await state.ReleaseAsync().ConfigureAwait(false); } catch (Exception error) { work.Add(null, error); }
            work.Throw(); throw;
        }
    }
    private static async Task AcquirePinPublishedAsync(Task start, SharedPin state, ITaskRunAdmissionCommitLease commit, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        await state.Work.AcquireAsync(() => commit.AcquireOriginalCommitPinAsync(token).AsTask(), value => state.Original = value).ConfigureAwait(false);
        if (state.Original is null) throw new UnauthorizedAccessException("The genuine model-use pin was refused.");
    }
    private sealed class SharedPin(StrataNativeArtifactSource owner, Work work)
    {
        public Work Work { get; } = work;
        public Task Driver = null!; public IAsyncDisposable? Original; public int Borrows; public bool Closing; private Task? _close;
        public Task ReleaseAsync()
        {
            TaskCompletionSource? start = null; Task actual;
            lock (owner._gate)
            {
                if (--Borrows < 0) throw new InvalidOperationException("An original model-use pin borrow was released twice.");
                if (Borrows != 0) return Task.CompletedTask;
                Closing = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublishedAsync(start.Task); actual = _close;
            }
            start.SetResult(); return actual;
        }
        private async Task ClosePublishedAsync(Task start)
        {
            await start.ConfigureAwait(false);
            await owner.RunClosePhaseAsync(async () =>
            {
                try { await Driver.ConfigureAwait(false); } catch (Exception cause) { Work.Add(Driver, cause); }
                if (Original is not null) await Work.CloseAsync(Original).ConfigureAwait(false);
                Work.Throw();
            }).ConfigureAwait(false);
        }
    }
    private sealed class PinBorrow(SharedPin state) : IAsyncDisposable
    {
        private readonly object _gate = new(); private Task? _close;
        public ValueTask DisposeAsync()
        { lock (_gate) { _close ??= state.ReleaseAsync(); return new(_close); } }
    }
    public void RequestOriginalRetirement() { lock (_gate) _sealed = true; }
    public void DemandExternalOriginalJoin()
    {
        if (_physical?.Contains(this) == true || _executing.Value is { Live: true })
            throw new InvalidOperationException("A protected artifact original cannot join its encompassing source.");
        for (var phase = _closingExecuting.Value; phase is not null; phase = phase.Parent)
            if (phase.Live) throw new InvalidOperationException("An actual protected artifact close cannot join its containing source/lease close.");
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            _sealed = true;
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = CloseAsync(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return new(actual);
    }
    private async Task CloseAsync(Task start)
    {
        await start.ConfigureAwait(false);
        await RunClosePhaseAsync(async () =>
        {
            var errors = new List<Exception>(); Work[] work;
            lock (_gate) work = _work.ToArray();
            foreach (var original in work) await Work.JoinAsync(original.Driver, errors).ConfigureAwait(false);
            HeldLease[] leases; lock (_gate) leases = _leases.ToArray();
            foreach (var lease in leases) await Work.JoinAsync(lease.CloseOriginalAsync(sourceOwned: true), errors).ConfigureAwait(false);
            foreach (var original in work) { await original.JoinRawAsync().ConfigureAwait(false); foreach (var cause in original.Errors) Work.AddCause(errors, cause); }
            if (errors.Count != 0) throw new AggregateException("Actual protected artifact source close failed.", errors);
        }).ConfigureAwait(false);
    }
    private sealed class ClosePhase(ClosePhase? parent)
    { public ClosePhase? Parent { get; } = parent; public volatile bool Live = true; }
    private async Task RunClosePhaseAsync(Func<Task> body)
    {
        var previous = _closingExecuting.Value; var actual = new ClosePhase(previous); _closingExecuting.Value = actual;
        try { await body().ConfigureAwait(false); }
        finally { actual.Live = false; _closingExecuting.Value = previous; }
    }

    private sealed class HeldLease
    {
        private readonly StrataNativeArtifactSource issuer;
        private readonly Work work;
        private readonly TaskRunAttemptAdmission admission;
        private readonly StrataVerifiedInstallationLease installation;
        private readonly IAsyncDisposable pin;
        private readonly ProtectedRoot root;
        public HeldLease(StrataNativeArtifactSource issuer, Work work, ModelIdentity model,
            TaskRunAttemptAdmission admission, StrataVerifiedInstallationLease installation, IAsyncDisposable pin, ProtectedRoot root,
            StrataOriginalHardwareObservation? hardware, InstallationCapture metadata)
        {
            this.issuer = issuer; this.work = work; this.admission = admission;
            this.installation = installation; this.pin = pin; this.root = root;
            Issuer = issuer; Model = model; Admission = admission; Installation = installation;
            Root = root; Hardware = hardware; Metadata = metadata;
        }
        public StrataNativeArtifactSource Issuer { get; }
        public ModelIdentity Model { get; }
        public TaskRunAttemptAdmission Admission { get; }
        public StrataVerifiedInstallationLease Installation { get; }
        public ProtectedRoot Root { get; }
        public StrataOriginalHardwareObservation? Hardware { get; }
        public InstallationCapture Metadata { get; }
        public bool Closing; private Task? _close;
        public void Demand()
        {
            lock (issuer._gate) if (Closing || issuer._sealed) throw new ObjectDisposedException("Original Strata artifact lease");
            work.Invoke(() => { ((ITaskRunOriginalInferenceAdmissionSource)issuer._authority).DemandOriginalInferenceAdmission(admission);
                installation.DemandCurrentOriginalInstallation(); root.DemandCurrent(); return 0; });
        }
        public Task CloseOriginalAsync(bool sourceOwned = false)
        {
            if (!sourceOwned) issuer.DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
            lock (issuer._gate)
            { Closing = true; if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublishedAsync(start.Task); } actual = _close; }
            start?.SetResult(); return actual;
        }
        private async Task ClosePublishedAsync(Task start)
        {
            await start.ConfigureAwait(false);
            await issuer.RunClosePhaseAsync(async () =>
            {
                await work.JoinRawAsync().ConfigureAwait(false);
                work.Cleanup(root.Dispose);
                await work.CloseAsync(pin).ConfigureAwait(false);
                await work.CloseAsync(installation).ConfigureAwait(false);
                work.Throw();
            }).ConfigureAwait(false);
        }
    }
    private sealed class WorkerLease : StrataOriginalWorkerLease
    {
        private readonly HeldLease held;
        public WorkerLease(HeldLease held) { this.held = held; Held = held; }
        public HeldLease Held { get; }
        // Source-created descriptor path, never a model/config controlled executable path.
        public override StrataBundledWorker OriginalWorker => new(held.Root.SingleFilePath, held.Metadata.WorkerFile.Sha256);
        public override void DemandCurrentOriginalBinding() => held.Demand();
        public override ValueTask DisposeAsync() => new(held.CloseOriginalAsync());
    }
    private sealed class ModelLease : StrataOriginalModelLease, IStrataOriginalArtifactBinding, IStrataOriginalHardwareBinding, IStrataOriginalModelSizeBinding
    {
        private readonly HeldLease held;
        public ModelLease(HeldLease held) { this.held = held; Held = held; }
        public HeldLease Held { get; }
        public ModelIdentity OriginalModel => held.Model;
        public TaskRunAttemptAdmission OriginalAdmission => held.Admission;
        public StrataOriginalHardwareObservation OriginalHardwareProbe => held.Hardware ?? throw new InvalidDataException("No actual hardware probe was retained.");
        public override InferenceEngineSupport? OriginalBuildSupport => held.Metadata.BuildSupport;
        public InferenceHardware OriginalHardwareObservation => OriginalHardwareProbe.Hardware;
        public long OriginalModelSizeBytes
        {
            get
            {
                held.Demand();
                var bytes = held.Metadata.Files.Aggregate(0L, (sum, file) => checked(sum + file.Length));
                held.Demand(); return bytes;
            }
        }
        public override InferenceModelRequirements Requirements => held.Metadata.Requirements;
        public override string OriginalCheckpointDirectory => held.Root.DescriptorPath;
        public override IReadOnlyList<int> ActualCudaDeviceIndices => Array.AsReadOnly(held.Metadata.Devices);
        public override void DemandCurrentOriginalBinding() => held.Demand();
        public override ValueTask DisposeAsync() => new(held.CloseOriginalAsync());
    }

    private sealed class Work(StrataNativeArtifactSource owner, IInferenceEngineOriginalSourceScope scope) : IInferenceEngineOriginalSourceScope
    {
        public Task<object> Driver = null!; public volatile bool Live = true; public volatile bool Published;
        private readonly object _gate = new(); public readonly List<Task> Raw = []; public readonly List<Exception> Errors = [];
        public T Invoke<T>(Func<T> factory, bool cleanup = false) => owner.InvokePhysical(() => InvokeScoped(factory, cleanup));
        private T InvokeScoped<T>(Func<T> factory, bool cleanup)
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId; T result = default!;
            try
            {
                T InvokeOnce()
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                        throw new InvalidOperationException("The actual artifact callback is inactive, foreign-thread or consumed.");
                    result = factory(); return result;
                }
                result = Published ? InvokeOnce() : cleanup ? scope.InvokeOriginalCleanup(InvokeOnce) : scope.InvokeOriginalFactory(InvokeOnce);
                if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("The actual artifact callback was not invoked.");
                return result;
            }
            catch (OperationCanceledException error) { throw new AggregateException("Actual synchronous artifact factory fault.", error); }
            finally { Interlocked.Exchange(ref active, 0); }
        }
        public void Run(Action body) => Invoke(() => { body(); return 0; });
        public void Retain(Task raw)
        { lock (_gate) if (!Raw.Any(item => ReferenceEquals(item, raw))) Raw.Add(raw);
            if (!Published) owner.InvokePhysical(() => { scope.RetainOriginalTask(raw); return 0; }); }
        public void RetainOriginalTask(Task task) => Retain(task);
        public T InvokeOriginalFactory<T>(Func<T> factory) => Invoke(factory);
        public T InvokeOriginalCleanup<T>(Func<T> cleanup) => Invoke(cleanup, cleanup: true);
        public void Add(Task? raw, Exception error)
        {
            lock (_gate)
            {
                AddCause(Errors, error);
                if (raw?.Exception is { } group) { AddCause(Errors, group); foreach (var cause in group.InnerExceptions) AddCause(Errors, cause); }
            }
        }
        public static void AddCause(List<Exception> errors, Exception error)
        { if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error); }
        public void Throw()
        {
            lock (_gate)
            {
                if (Errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Errors[0]).Throw();
                if (Errors.Count != 0) throw new AggregateException("Actual protected artifact originals failed.", Errors.ToArray());
            }
        }
        public async Task<T> ReadAsync<T>(Func<Task<T>> factory)
        { T result = default!; await AcquireAsync(factory, value => result = value).ConfigureAwait(false); return result; }
        public async Task AcquireAsync<T>(Func<Task<T>> factory, Action<T> retainProduct)
        {
            Task<T>? raw = null;
            try { _ = Invoke(() => { raw = factory(); Retain(raw); return raw; }); } catch (Exception error) { Add(raw, error); }
            if (raw is not null)
            { try { var product = await raw.ConfigureAwait(false); retainProduct(product); } catch (Exception error) { Add(raw, error); } }
            Throw(); if (raw is null) throw new InvalidOperationException("No original artifact Task was acquired.");
        }
        public async Task ReadAsync(Func<Task> factory)
        {
            Task? raw = null;
            try { _ = Invoke(() => { raw = factory(); Retain(raw); return raw; }); } catch (Exception error) { Add(raw, error); }
            if (raw is not null) { try { await raw.ConfigureAwait(false); } catch (Exception error) { Add(raw, error); } }
            Throw(); if (raw is null) throw new InvalidOperationException("No original artifact Task was acquired.");
        }
        public void Cleanup(Action close)
        { try { _ = Invoke(() => { close(); return 0; }, cleanup: true); } catch (Exception error) { Add(null, error); } }
        public async Task CloseAsync(IAsyncDisposable resource)
        {
            Task? raw = null;
            try { _ = Invoke(() => { raw = resource.DisposeAsync().AsTask(); Retain(raw); return raw; }, cleanup: true); }
            catch (Exception error) { Add(raw, error); }
            if (raw is not null) { try { await raw.ConfigureAwait(false); } catch (Exception error) { Add(raw, error); } }
        }
        public async Task JoinRawAsync()
        { Task[] raw; lock (_gate) raw = Raw.ToArray(); foreach (var actual in raw) { try { await actual.ConfigureAwait(false); } catch (Exception error) { Add(actual, error); } } }
        public static async Task JoinAsync(Task actual, List<Exception> errors)
        {
            try { await actual.ConfigureAwait(false); }
            catch (Exception error) { AddCause(errors, error); if (actual.Exception is { } group) { AddCause(errors, group); foreach (var cause in group.InnerExceptions) AddCause(errors, cause); } }
        }
    }

    // Exact Linux UAPI observations, reused from the maintained Workspace original boundary.
    // fs-verity/immutable files and immutable checkpoint directory are REQUIRED here: descriptor
    // equality and mode checks alone cannot exclude another writer during native model loading.
    private sealed class ProtectedRoot : IDisposable
    {
        private readonly SafeFileHandle _root;
        private readonly string _path;
        private readonly Identity _identity;
        private readonly List<(SafeFileHandle Handle, Identity Version, string Name)> _files = [];
        private bool _closed;
        public string DescriptorPath => $"/proc/{Environment.ProcessId}/fd/{Fd(_root)}";
        public string SingleFilePath => _files.Count == 1 ? $"/proc/{Environment.ProcessId}/fd/{Fd(_files[0].Handle)}" : throw new InvalidOperationException("No SAME single executable is held.");
        public ProtectedRoot(string path, bool immutableDirectory)
        {
            if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                throw new PlatformNotSupportedException("Protected Strata artifacts require the supported Linux64 kernel.");
            _path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (_path == "/") throw new UnauthorizedAccessException("A bounded original installed root is required.");
            using var volume = new SafeFileHandle((IntPtr)Open("/", 0x10000 | 0x80000), true);
            if (volume.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            _root = OpenAt(volume, _path.TrimStart('/'), directory: true, crossing: true);
            try { _identity = Observe(_root, directory: true); DemandPath(_root, _path);
                if (immutableDirectory && (Flags(_root) & 0x10) == 0) throw new UnauthorizedAccessException("The original model directory must have actual kernel immutable protection."); }
            catch { _root.Dispose(); throw; }
        }
        public void DemandCurrent()
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            DemandPath(_root, _path);
            if (Observe(_root, true) != _identity) throw new IOException("The original installed root changed.");
            foreach (var file in _files)
            {
                if (Observe(file.Handle, false) != file.Version || (Flags(file.Handle) & (0x10 | 0x100000)) == 0)
                    throw new IOException("The original protected artifact bytes/identity changed.");
                using var fresh = OpenAt(_root, file.Name, false, crossing: false);
                if (Observe(fresh, false) != file.Version) throw new IOException("The original artifact directory entry changed.");
            }
        }
        public async Task ReadFileAsync(StrataInstalledFile expected, Work work, CancellationToken token, bool worker)
        {
            ValidateFile(expected); DemandCurrent();
            SafeFileHandle? handle = null; Identity before;
            try { _ = work.Invoke(() => { handle = OpenAt(_root, expected.RelativeName, false, crossing: false); return handle; });
                before = Observe(handle!, false); if (before.Size != (ulong)expected.Length ||
                    (Flags(handle!) & (0x10 | 0x100000)) == 0 || worker && (before.Mode & 0x49) == 0)
                    throw new UnauthorizedAccessException("Actual immutable/verity installed bytes/size/executable mode are required."); }
            catch { handle?.Dispose(); throw; }
            _files.Add((handle!, before, expected.RelativeName)); // Retained before any async read.
            // Only a duplicate is transferred to the reader. The original descriptor stays
            // owned by this actual lease until native worker close.
            SafeFileHandle? readHandle = null; FileStream? reader = null;
            try
            {
                _ = work.Invoke(() =>
                {
                    var duplicate = Dup(Fd(handle!));
                    if (duplicate < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                    readHandle = new SafeFileHandle((IntPtr)duplicate, true);
                    reader = new FileStream(readHandle, FileAccess.Read, 65536, isAsync: false); return reader;
                });
                var hash = await work.ReadAsync(() => SHA256.HashDataAsync(reader!, token).AsTask()).ConfigureAwait(false);
                if (!Convert.ToHexString(hash).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Actual installed artifact digest does not match its genuine issuer observation.");
                if (expected.RelativeName.EndsWith(".safetensors", StringComparison.Ordinal))
                    await ValidateSafetensorsAsync(handle!, before.Size, work, token).ConfigureAwait(false);
            }
            finally
            {
                if (reader is not null) await work.CloseAsync(reader).ConfigureAwait(false);
                if (readHandle is not null) work.Cleanup(readHandle.Dispose);
            }
            if (Observe(handle!, false) != before) throw new IOException("Actual protected artifact changed while read.");
        }
        public async Task ReadCheckpointAsync(IReadOnlyList<StrataInstalledFile> files, InferenceModelRequirements requirements,
            ModelIdentity model, Work work, CancellationToken token)
        {
            var inventory = files.ToArray();
            if (inventory.Length is 0 or > 128 || inventory.Select(file => file.RelativeName).Distinct(StringComparer.Ordinal).Count() != inventory.Length ||
                !inventory.Any(file => file.RelativeName == "config.json") || !inventory.Any(file => file.RelativeName.EndsWith(".safetensors", StringComparison.Ordinal)) ||
                requirements.Model != model || requirements.WeightFormat != "Safetensors" || requirements.Topology != "centralized" ||
                requirements.ContextTokens <= 0 || string.IsNullOrWhiteSpace(requirements.NativeRegistration) ||
                requirements.MinimumRamBytes < 0 || requirements.MinimumVramBytes < 0)
                throw new InvalidDataException("The genuine complete original Safetensors inventory/requirements are unsupported.");
            foreach (var file in inventory) ValidateFile(file);
            if (inventory.Aggregate(0L, (sum, file) => checked(sum + file.Length)) > 64L * 1024 * 1024 * 1024)
                throw new InvalidDataException("The actual checkpoint exceeds bounded source custody.");
            var names = work.Invoke(() => Directory.EnumerateFileSystemEntries(DescriptorPath).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
            if (!names.SequenceEqual(inventory.Select(file => file.RelativeName).Order(StringComparer.Ordinal)))
                throw new InvalidDataException("The actual immutable checkpoint inventory is incomplete or contains undeclared entries.");
            foreach (var file in inventory) await ReadFileAsync(file, work, token, worker: false).ConfigureAwait(false);
            var config = _files.Single(file => file.Name == "config.json");
            if (config.Version.Size > 1024 * 1024) throw new InvalidDataException("The actual model configuration is oversized.");
            var bytes = new byte[checked((int)config.Version.Size)]; await ReadExactlyAsync(config.Handle, bytes, 0, work, token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            if (root.GetProperty("model_type").GetString() != requirements.Family ||
                !root.GetProperty("architectures").EnumerateArray().Any(value => value.GetString() == requirements.Architecture) ||
                !root.GetProperty("max_position_embeddings").TryGetInt32(out var context) || context < requirements.ContextTokens)
                throw new InvalidDataException("Actual model configuration differs from the privately issued original model requirements.");
            DemandCurrent();
        }
        private static void ValidateFile(StrataInstalledFile file)
        {
            if (string.IsNullOrWhiteSpace(file.RelativeName) || file.RelativeName is "." or ".." || file.RelativeName.Length > 255 ||
                file.RelativeName.IndexOfAny(['/', '\\', '\0']) >= 0 || file.Length <= 0 || file.Length > 64L * 1024 * 1024 * 1024 ||
                file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("The original installed file observation is invalid.");
        }
        private static async Task ValidateSafetensorsAsync(SafeFileHandle handle, ulong length, Work work, CancellationToken token)
        {
            var prefix = new byte[8]; await ReadExactlyAsync(handle, prefix, 0, work, token).ConfigureAwait(false);
            var headerLength = BinaryPrimitives.ReadUInt64LittleEndian(prefix);
            if (headerLength is 0 or > 8 * 1024 * 1024 || headerLength > length - Math.Min(length, 8UL))
                throw new InvalidDataException("The actual Safetensors header is truncated or oversized.");
            var header = new byte[checked((int)headerLength)]; await ReadExactlyAsync(handle, header, 8, work, token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(header); var count = 0; var ranges = new List<(ulong Start, ulong End)>();
            foreach (var item in document.RootElement.EnumerateObject())
            {
                if (item.Name == "__metadata__") continue;
                if (++count > 65536) throw new InvalidDataException("The actual tensor inventory exceeds its bounded source.");
                var offsets = item.Value.GetProperty("data_offsets").EnumerateArray().Select(value => value.GetUInt64()).ToArray();
                var shape = item.Value.GetProperty("shape").EnumerateArray().Select(value => value.GetUInt64()).ToArray();
                var width = item.Value.GetProperty("dtype").GetString() switch
                { "BOOL" or "U8" or "I8" or "F8_E4M3" or "F8_E5M2" => 1UL,
                    "F16" or "BF16" or "U16" or "I16" => 2UL, "F32" or "U32" or "I32" => 4UL,
                    "F64" or "U64" or "I64" => 8UL, _ => throw new InvalidDataException("Actual tensor dtype is unsupported.") };
                if (offsets.Length != 2 || offsets[0] > offsets[1] || offsets[1] > length - headerLength - 8 || shape.Length > 16 ||
                    string.IsNullOrWhiteSpace(item.Value.GetProperty("dtype").GetString())) throw new InvalidDataException("Actual tensor offsets/shape/dtype are malformed.");
                var payload = shape.Aggregate(width, (product, dimension) => checked(product * dimension));
                if (payload != offsets[1] - offsets[0]) throw new InvalidDataException("Actual tensor shape/dtype does not match payload bytes.");
                ranges.Add((offsets[0], offsets[1]));
            }
            if (count == 0) throw new InvalidDataException("No actual model tensors are present.");
            ulong end = 0; foreach (var range in ranges.OrderBy(value => value.Start))
            { if (range.Start != end) throw new InvalidDataException("Actual tensors contain holes, overlaps or aliased payloads."); end = range.End; }
            if (end != length - headerLength - 8) throw new InvalidDataException("Actual tensor payload has undeclared bytes.");
        }
        private static async Task ReadExactlyAsync(SafeFileHandle handle, byte[] bytes, long offset, Work work, CancellationToken token)
        {
            var consumed = 0;
            while (consumed < bytes.Length)
            {
                var count = await work.ReadAsync(() => RandomAccess.ReadAsync(handle, bytes.AsMemory(consumed), offset + consumed, token).AsTask()).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("The actual original artifact is truncated."); consumed += count;
            }
        }
        public void Dispose()
        { if (_closed) return; _closed = true; var errors = new List<Exception>(); foreach (var file in _files) try { file.Handle.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _root.Dispose(); } catch (Exception error) { errors.Add(error); } if (errors.Count != 0) throw new AggregateException(errors); }
        private readonly record struct Identity(ulong Inode, uint Major, uint Minor, uint Uid, ushort Mode, uint Links, ulong Size, long Mtime, uint MtimeNs, long Ctime, uint CtimeNs);
        private static Identity Observe(SafeFileHandle handle, bool directory)
        {
            var bytes = new byte[256]; if (Statx(Fd(handle), "", 0x1000, 0x7ff, bytes) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            const uint required = 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x40 | 0x80 | 0x100 | 0x200;
            if ((BitConverter.ToUInt32(bytes, 0) & required) != required) throw new PlatformNotSupportedException("Required actual statx fields are absent.");
            var identity = new Identity(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140),
                BitConverter.ToUInt32(bytes, 20), BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 16), BitConverter.ToUInt64(bytes, 40),
                BitConverter.ToInt64(bytes, 112), BitConverter.ToUInt32(bytes, 120), BitConverter.ToInt64(bytes, 96), BitConverter.ToUInt32(bytes, 104));
            if ((identity.Mode & 0xf000) != (directory ? 0x4000 : 0x8000) || identity.Uid != 0 && identity.Uid != GetEuid() ||
                (identity.Mode & 0x12) != 0 || !directory && identity.Links != 1)
                throw new UnauthorizedAccessException("The actual installed artifact is not an owned non-writable regular/directory binding.");
            return identity;
        }
        private static int Flags(SafeFileHandle handle)
        { if (Ioctl(Fd(handle), 0x80086601UL, out var flags) != 0) throw new PlatformNotSupportedException("Actual immutable/verity protection cannot be observed.", new Win32Exception(Marshal.GetLastPInvokeError())); return checked((int)flags); }
        private static SafeFileHandle OpenAt(SafeFileHandle root, string relative, bool directory, bool crossing)
        {
            var how = new OpenHow { Flags = 0x80000UL | 0x800UL | (directory ? 0x10000UL : 0), Resolve = 0xeUL | (crossing ? 0UL : 1UL) };
            var fd = OpenAt2(437, Fd(root), relative, ref how, 24);
            if (fd < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Actual protected artifact openat2 refused.");
            return new((IntPtr)fd, true);
        }
        private static int Fd(SafeFileHandle handle)
        { if (handle.IsClosed || handle.IsInvalid) throw new ObjectDisposedException("Actual protected descriptor"); return handle.DangerousGetHandle().ToInt32(); }
        private static void DemandPath(SafeFileHandle handle, string expected)
        {
            var bytes = new byte[32769]; var size = Readlink($"/proc/self/fd/{Fd(handle)}", bytes, (ulong)bytes.Length);
            if (size < 0 || size >= bytes.Length || Encoding.UTF8.GetString(bytes, 0, checked((int)size)) != expected)
                throw new IOException("The actual original installed root path changed or was removed.");
        }
        [StructLayout(LayoutKind.Sequential)] private struct OpenHow { public ulong Flags, Mode, Resolve; }
        [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)] private static extern long OpenAt2(long number, int dir, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref OpenHow how, ulong size);
        [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int dir, string path, int flags, uint mask, [Out] byte[] result);
        [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern long Readlink(string path, [Out] byte[] buffer, ulong size);
        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] private static extern int Ioctl(int fd, ulong request, out long flags);
        [DllImport("libc", EntryPoint = "dup", SetLastError = true)] private static extern int Dup(int fd);
        [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
    }
}

internal static class StrataOriginalAcquisitionProjection
{
    public static async Task<StrataOriginalModelLease> ContinueModel(this Task<object> original) => (StrataOriginalModelLease)await original.ConfigureAwait(false);
    public static async Task<StrataOriginalWorkerLease> ContinueWorker(this Task<object> original) => (StrataOriginalWorkerLease)await original.ConfigureAwait(false);
}
