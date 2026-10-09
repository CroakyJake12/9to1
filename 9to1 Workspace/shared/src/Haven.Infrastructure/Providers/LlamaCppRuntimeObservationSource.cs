using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Per-request observation for an already loaded and genuinely probed local GGUF.
/// Automatic/LlamaCpp observes the SAME configured provider. Explicit Strata preserves its
/// protected per-admission source. Neither branch grants use or runs a capability benchmark.</summary>
public sealed class LlamaCppRuntimeObservationSource : IInferenceRuntimeObservationSource,
    IInferenceEngineRuntimeObservationSource, IManagedOriginalRequestRuntimeObservationSource
{
    private readonly LlamaCppModelProvider _provider;
    private readonly TaskExecutionCoordinator _coordinator;
    private readonly StrataRuntimeObservationSource? _strata;
    private readonly ModelIdentity? _boundModel;
    private readonly TaskRunAttemptAdmission? _admission;

    public LlamaCppRuntimeObservationSource(LlamaCppModelProvider provider, TaskExecutionCoordinator coordinator,
        StrataRuntimeObservationSource? strata = null) : this(provider, coordinator, strata, null, null) { }
    private LlamaCppRuntimeObservationSource(LlamaCppModelProvider provider, TaskExecutionCoordinator coordinator,
        StrataRuntimeObservationSource? strata, ModelIdentity? model, TaskRunAttemptAdmission? admission)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _strata = strata; _boundModel = model; _admission = admission;
    }
    public IInferenceRuntimeObservationSource BindOriginalRequest(ModelIdentity sameModel,
        TaskRunAttemptAdmission sameAdmission, IModelProvider sameObservedProvider)
    {
        ArgumentNullException.ThrowIfNull(sameModel); ArgumentNullException.ThrowIfNull(sameAdmission);
        if (!ReferenceEquals(sameObservedProvider, _provider))
            throw new InvalidOperationException("The actual configured raw provider differs from this observation owner.");
        // Immutable closure only. No global latest-admission slot, lookup or new engine owner.
        return new LlamaCppRuntimeObservationSource(_provider, _coordinator, _strata, sameModel, sameAdmission);
    }
    public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,
        IInferenceEngineOriginalSourceScope scope, CancellationToken token) =>
        ObserveOriginalForEngineAsync(model, InferenceEngine.Automatic, scope, token);

    public async Task<InferenceRuntimeObservation> ObserveOriginalForEngineAsync(ModelIdentity model,
        InferenceEngine requestedEngine, IInferenceEngineOriginalSourceScope scope, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(scope);
        if (requestedEngine is not (InferenceEngine.Automatic or InferenceEngine.LlamaCpp or InferenceEngine.Strata))
            throw new ArgumentOutOfRangeException(nameof(requestedEngine));
        var admission = _admission;
        if (admission is null || _boundModel != model)
            throw Unavailable(model, "LLAMA_ORIGINAL_REQUEST_MODEL_USE_ADMISSION_REQUIRED");
        if (requestedEngine == InferenceEngine.Strata)
        {
            if (_strata is null) throw Unavailable(model, "STRATA_CONFIGURED_PROTECTED_OBSERVATION_SOURCE_REQUIRED");
            return await _strata.BindOriginalRequest(admission).ObserveOriginalAsync(model, scope, token).ConfigureAwait(false);
        }
        if (admission.Lease is not ITaskRunOriginalInferenceLeaseSource authority
            || admission.Lease is not ITaskRunOriginalInferenceLeaseCurrentnessSource current)
            throw Unavailable(model, "LLAMA_GENUINE_SCOPED_MODEL_USE_SOURCE_REQUIRED");
        var original = new ObservationScope(scope, () =>
        {
            token.ThrowIfCancellationRequested();
            current.DemandOriginalInferenceWithinSource(admission);
        });
        InferenceRuntimeObservation? result = null; LlamaCppOriginalRuntimeObservation? actualObservation = null;
        try
        {
            var issued = await original.Read(() => _coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission,
                original.Callback, original.RetainOriginalTask, token)).ConfigureAwait(false);
            if (!ReferenceEquals(issued, admission)) throw new UnauthorizedAccessException("The SAME privately issued current attempt is required.");
            original.InvokeOriginalFactory(() =>
            {
                var candidate = admission.Lease.Candidate;
                if (candidate.ProviderId != _provider.Id || candidate.ProviderId != model.ProviderId
                    || candidate.ModelId != model.ModelId || candidate.ArtifactIdentity != model.ArtifactRevision || candidate.UsesCloud)
                    throw new UnauthorizedAccessException("The original observed model differs from its actual admitted local candidate.");
                return 0;
            });
            await original.Read(() => authority.RevalidateOriginalInferenceWithinSourceAsync(admission,
                original.Callback, original.RetainOriginalTask, token)).ConfigureAwait(false);
            var actual = actualObservation = await original.Read(() => _provider.ObserveOriginalRuntimeWithinSourceAsync(model, original, token)).ConfigureAwait(false);
            // The last awaited permission/configuration reading precedes a final pure liveness
            // check and SAME provider-issued current observation. No later async lookup opens a gap.
            await original.Read(() => authority.RevalidateOriginalInferenceWithinSourceAsync(admission,
                original.Callback, original.RetainOriginalTask, token)).ConfigureAwait(false);
            result = original.InvokeOriginalFactory(() =>
            {
                if (!_provider.IsIssuedOriginalRuntimeObservation(actual))
                    throw new InvalidOperationException("The actual loaded process/model observation is no longer current.");
                var candidate = admission.Lease.Candidate;
                var required = candidate.RequiredCapabilities.ToFrozenSet(StringComparer.Ordinal);
                var observed = actual.Endpoint.ObservedCapabilities.Select(value => value.ToString()).ToFrozenSet(StringComparer.Ordinal);
                var quantization = "gguf.file-type:" + actual.GgufFileType.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // This is an observation of one already initialized SAME model, not a general
                // package support manifest. Its actual architecture serves as the family key.
                // Zero additional model memory means this borrowed session performs no reload;
                // it does not certify fit for another artifact or a fresh allocation.
                var requirements = new InferenceModelRequirements(model, actual.Endpoint.ConfiguredModelSha256,
                    actual.GgufArchitecture, actual.GgufArchitecture, "GGUF", quantization, required,
                    0, 0, actual.ContextTokens, [InferenceEngine.LlamaCpp]);
                var support = new InferenceEngineSupport(InferenceEngine.LlamaCpp,
                    "llama.cpp:exe-sha256:" + actual.Endpoint.ExecutableSha256, true, null,
                    Set(actual.GgufArchitecture), Set(actual.GgufArchitecture), Set("GGUF"), Set(quantization),
                    observed, Set(actual.Hardware.OperatingSystem), Set(actual.Hardware.CpuArchitecture), FrozenSet<string>.Empty);
                return new InferenceRuntimeObservation(requirements, actual.Hardware, [support]);
            });
        }
        catch (Exception error) { original.RecordDriverFailure(error); }
        finally { await original.JoinOriginals().ConfigureAwait(false); }
        original.ThrowFailures();
        // Raw joining itself may await a late child. Recheck the private issuer and endpoint
        // after that join, under the SAME finite scope, before the returned metadata is disclosed.
        return original.InvokeOriginalFactory(() =>
        {
            if (actualObservation is null || !_provider.IsIssuedOriginalRuntimeObservation(actualObservation))
                throw new InvalidOperationException("The SAME loaded model retired during the original observation join.");
            return result ?? throw new InvalidDataException("No actual local runtime observation exists.");
        });
    }

    private static FrozenSet<string> Set(string value) => new[] { value }.ToFrozenSet(StringComparer.Ordinal);
    private static InferenceEngineException Unavailable(ModelIdentity model, string reason) => new(new(
        DulcheErrorCode.ProviderUnavailable, reason, model.StableKey, false));

    /// <summary>Finite admitted callbacks and permanent raw-source custody for this ONE observation.
    /// The parent remains the actual dispatcher/process lifetime owner.</summary>
    private sealed class ObservationScope(IInferenceEngineOriginalSourceScope parent, Action demand)
        : IInferenceEngineOriginalSourceScope
    {
        private readonly object _gate = new();
        private readonly List<Task> _originals = [];
        private readonly HashSet<Task> _joined = new(ReferenceEqualityComparer.Instance);
        private readonly List<Exception> _failures = [];
        private bool _faulted;
        private bool _canceled;
        private bool _withdrawn;
        public T InvokeOriginalFactory<T>(Func<T> factory) => Invoke(factory, false);
        public T InvokeOriginalCleanup<T>(Func<T> factory) => Invoke(factory, true);
        public void Callback(Action callback) => _ = InvokeOriginalFactory(() => { callback(); return 0; });
        public void RetainOriginalTask(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (_gate)
            {
                if (!_originals.Any(known => ReferenceEquals(known, actual)))
                {
                    // An already acquired child must always remain owned, even if external
                    // enrollment throws. Productive admission is bounded BEFORE its factory.
                    _originals.Add(actual);
                }
            }
            parent.RetainOriginalTask(actual);
        }
        private T Invoke<T>(Func<T> factory, bool cleanup)
        {
            var thread = Environment.CurrentManagedThreadId; var live = 1; var once = 0;
            T captured = default!; Exception? callbackFailure = null;
            try
            {
                lock (_gate) if (!cleanup && (_withdrawn || _originals.Count >= 4096))
                    throw new InvalidOperationException("Original runtime observation retired or requires retirement before another factory.");
                T Actual()
                {
                    if (Volatile.Read(ref live) != 1 || Environment.CurrentManagedThreadId != thread
                        || Interlocked.Exchange(ref once, 1) != 0)
                    {
                        var refusal = new InvalidOperationException("The original observation factory is deferred, foreign-thread or already consumed.");
                        Interlocked.CompareExchange(ref callbackFailure, refusal, null); throw refusal;
                    }
                    try
                    {
                        lock (_gate) if (!cleanup && _withdrawn) throw new InvalidOperationException("The original observation factory retired before entry.");
                        if (!cleanup) demand(); captured = factory(); if (captured is Task task) RetainOriginalTask(task); return captured;
                    }
                    catch (Exception error) { Interlocked.CompareExchange(ref callbackFailure, error, null); throw; }
                }
                _ = cleanup ? parent.InvokeOriginalCleanup(Actual) : parent.InvokeOriginalFactory(Actual);
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                if (Volatile.Read(ref once) != 1) throw new InvalidOperationException("The actual observation factory was not invoked synchronously.");
                if (!cleanup) demand();
                return captured;
            }
            catch (Exception error)
            {
                lock (_gate) { _faulted = true; _withdrawn = true; Add(error); if (callbackFailure is not null) Add(callbackFailure); }
                // Synchronous OCE, swallowed/replaced callback failure and failed cleanup stay faults.
                throw new AggregateException("The original synchronous observation callback failed.",
                    callbackFailure is not null && !ReferenceEquals(callbackFailure, error) ? new[] { callbackFailure, error } : new[] { error });
            }
            finally { Interlocked.Exchange(ref live, 0); }
        }
        public async Task<T> Read<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null; Exception? invocationFailure = null;
            try { _ = InvokeOriginalFactory(() => { actual = factory() ?? throw new InvalidOperationException("No original observation Task exists."); RetainOriginalTask(actual); return actual; }); }
            catch (Exception error) { invocationFailure = error; }
            T result = default!;
            if (actual is not null) result = await Await(actual).ConfigureAwait(false);
            if (invocationFailure is not null) ExceptionDispatchInfo.Capture(invocationFailure).Throw();
            return actual is null ? throw new InvalidOperationException("No actual source Task was acquired.") : result;
        }
        public async Task Read(Func<Task> factory)
        {
            Task? actual = null; Exception? invocationFailure = null;
            try { _ = InvokeOriginalFactory(() => { actual = factory() ?? throw new InvalidOperationException("No original observation Task exists."); RetainOriginalTask(actual); return actual; }); }
            catch (Exception error) { invocationFailure = error; }
            if (actual is not null) await Await(actual).ConfigureAwait(false);
            if (invocationFailure is not null) ExceptionDispatchInfo.Capture(invocationFailure).Throw();
            if (actual is null) throw new InvalidOperationException("No actual source Task was acquired.");
        }
        private async Task<T> Await<T>(Task<T> actual)
        {
            lock (_gate) _joined.Add(actual);
            try { return await actual.ConfigureAwait(false); }
            catch (Exception error) { Record(actual, error); if (actual.IsFaulted) throw actual.Exception!; throw; }
        }
        private async Task Await(Task actual)
        {
            lock (_gate) _joined.Add(actual);
            try { await actual.ConfigureAwait(false); }
            catch (Exception error) { Record(actual, error); if (actual.IsFaulted) throw actual.Exception!; throw; }
        }
        public async Task JoinOriginals()
        {
            // A late child may be published while an original is still pending. Enumerate until
            // every actual retained identity has settled; never re-await one canceled original.
            while (true)
            {
                Task? actual;
                lock (_gate) { actual = _originals.FirstOrDefault(task => !_joined.Contains(task)); if (actual is not null) _joined.Add(actual); }
                if (actual is null) return;
                try { await actual.ConfigureAwait(false); } catch (Exception error) { Record(actual, error); }
            }
        }
        private void Record(Task actual, Exception error)
        {
            lock (_gate)
            {
                if (actual.IsCanceled) { _canceled = true; Add(error); }
                else { _faulted = true; Add(error); }
                _withdrawn = true;
                if (actual.Exception is { } group) { Add(group); foreach (var cause in group.InnerExceptions) Add(cause); }
            }
        }
        public void RecordDriverFailure(Exception error)
        {
            lock (_gate)
            {
                // Only an already recorded genuinely canceled original can account for an
                // outward cancellation. Faulted OCE and all unrecognized body faults remain faults.
                if (!_canceled || error is not OperationCanceledException) _faulted = true;
                _withdrawn = true;
                Add(error);
            }
        }
        private void Add(Exception error) { if (!_failures.Any(known => ReferenceEquals(known, error))) _failures.Add(error); }
        public void ThrowFailures()
        {
            Exception[] errors; bool canceled;
            lock (_gate) { errors = _failures.ToArray(); canceled = _canceled && !_faulted; }
            if (errors.Length == 0) return;
            var group = new AggregateException("The SAME local observation or original source failed.", errors);
            if (canceled) throw new OperationCanceledException("Actual observation sources were canceled.", group);
            throw group;
        }
    }
}
