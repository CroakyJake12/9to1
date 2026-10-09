using Haven.Core;

namespace Haven.Application;

/// <summary>The maintained registry's finite original catalogue reads. The
/// registry retains failed provider originals; a catalogue grants no execution.</summary>
public sealed partial class ModelProviderRegistry : IOriginalModelCatalogueSource
{
    private readonly object _catalogueGate = new();
    private readonly List<CatalogueOriginal> _catalogueOriginals = [];
    private Task? _catalogueClose;
    private bool _cataloguesRetiring;

    public Task? OriginalCataloguesClose { get { lock (_catalogueGate) return _catalogueClose; } }

    public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalSourceAsync(
        ModelCataloguePolicy policy, Action<Action> scope, Action<Task> retain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(retain);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CatalogueOriginal original;
        lock (_catalogueGate)
        {
            if (_cataloguesRetiring) throw new ObjectDisposedException(nameof(ModelProviderRegistry));
            // The independent observation has actually awaited this same public
            // driver and its encompassing body; status alone is insufficient.
            _catalogueOriginals.RemoveAll(actual => actual.CanRetireHealthy);
            if (_catalogueOriginals.Count >= 128)
                throw new InvalidOperationException("Original model catalogue custody requires owner drainage.");
            original = new(this, scope, retain);
            _catalogueOriginals.Add(original);
            original.Settlement = ReadCatalogueOriginalAsync(start.Task, original, policy, cancellationToken);
            original.Observation = ObserveCatalogueOriginalAsync(start.Task, original);
        }
        start.SetResult();
        return original.Driver.Task;
    }

    private async Task ReadCatalogueOriginalAsync(Task start, CatalogueOriginal original,
        ModelCataloguePolicy policy, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        IReadOnlyList<ProviderModelDescriptor>? result = null;
        using (CloudflareOriginalExecutionGuard.EnterOriginal(this))
        {
            try
            {
                // Snapshot caller preference metadata inside its productive
                // scope. Registered provider instances are the SAME immutable
                // constructor cohort used by ordinary routing.
                var selected = original.Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var allowed = policy.AllowedProviderIds;
                    return _providers.Where(provider =>
                        (provider.IsLocal ? policy.AllowLocal : policy.AllowRemote) &&
                        (allowed is null || allowed.Contains(provider.Id))).ToArray();
                });
                var models = new List<ProviderModelDescriptor>();
                foreach (var provider in selected)
                {
                    var observed = await original.ReadAsync(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        return provider.GetModelsAsync(token);
                    }).ConfigureAwait(false);
                    original.Invoke(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        // SAME maintained identity/locality/dedup rules as the
                        // compatibility catalogue; this source path preserves
                        // faults instead of returning an unproved partial list.
                        models.AddRange(observed.Where(model =>
                            model.ProviderId.Equals(provider.Id, StringComparison.OrdinalIgnoreCase) &&
                            (model.IsLocal && provider.IsLocal ? policy.AllowLocal : policy.AllowRemote))
                            .Select(model => provider.IsLocal ? model : model with { IsLocal = false }));
                        return true;
                    });
                }
                result = original.Invoke(() => (IReadOnlyList<ProviderModelDescriptor>)
                    Array.AsReadOnly(models.GroupBy(model => model.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First()).ToArray()));
            }
            catch (Exception cause) { original.Retain(cause); }
            // A caller postguard or retainer refusal never abandons a provider
            // Task which was already returned by the SAME configured instance.
            await original.JoinProviderOriginalsAsync().ConfigureAwait(false);
        }
        var errors = original.Errors;
        if (errors.Count != 0)
            original.Driver.TrySetException(new AggregateException("Original model catalogue sources or callbacks failed.", errors));
        else if (result is null)
            original.Driver.TrySetException(new InvalidOperationException("No original model catalogue was captured."));
        else original.Driver.TrySetResult(result);
    }

    private static async Task ObserveCatalogueOriginalAsync(Task start, CatalogueOriginal original)
    {
        await start.ConfigureAwait(false);
        try { await original.Settlement!.ConfigureAwait(false); }
        catch (Exception cause) { original.Capture(original.Settlement!, cause); }
        try { await original.Driver.Task.ConfigureAwait(false); }
        catch { /* Source causes remain in the owning original, not normalized. */ }
        original.MarkIndependentlyObserved();
    }

    /// <summary>Pure owner preflight, including restored-context physical callbacks.</summary>
    public void ThrowIfOriginalCatalogueJoinWouldCycle() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

    /// <summary>Seals only the additive original catalogue cohort. Ordinary
    /// compatibility catalogue/routing behavior is unchanged.</summary>
    public Task CloseOriginalCataloguesAndDrainAsync()
    {
        ThrowIfOriginalCatalogueJoinWouldCycle();
        lock (_catalogueGate)
        {
            if (_catalogueClose is not null) return _catalogueClose;
            _cataloguesRetiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = CloseCatalogueOriginalsAsync(start.Task, _catalogueOriginals.ToArray());
            _catalogueClose = actual;
            start.SetResult();
            return actual;
        }
    }

    private async Task CloseCatalogueOriginalsAsync(Task start, CatalogueOriginal[] originals)
    {
        await start.ConfigureAwait(false);
        using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            if (original.Settlement is { } body)
                try { await body.ConfigureAwait(false); } catch (Exception cause) { original.Capture(body, cause); }
            if (original.Observation is { } observer)
                try { await observer.ConfigureAwait(false); } catch (Exception cause) { original.Capture(observer, cause); }
            await original.JoinProviderOriginalsAsync().ConfigureAwait(false);
            foreach (var cause in original.Errors) AddReference(failures, cause);
            // The driver was independently awaited by the observation. Retain
            // an unexpected driver failure even if a future body misses a cause.
            if (original.Errors.Count == 0 && original.Driver.Task.Exception is { } payload)
                foreach (var cause in payload.InnerExceptions) AddReference(failures, cause);
        }
        if (failures.Count != 0) throw new AggregateException("Original model catalogue drainage failed.", failures);
    }

    private static void AddReference(List<Exception> errors, Exception cause)
    { if (!errors.Any(actual => ReferenceEquals(actual, cause))) errors.Add(cause); }

    private sealed class CatalogueOriginal(ModelProviderRegistry owner, Action<Action> scope, Action<Task> retain)
    {
        private readonly object _gate = new();
        private readonly List<Exception> _errors = [];
        private sealed class ProviderOriginal(Task raw)
        { internal Task Raw { get; } = raw; internal Task? Observation; }
        private readonly Dictionary<Task, ProviderOriginal> _providers = new(ReferenceEqualityComparer.Instance);
        private bool _observed;
        internal TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>> Driver { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Settlement;
        internal Task? Observation;
        internal IReadOnlyList<Exception> Errors { get { lock (_gate) return _errors.ToArray(); } }
        internal bool CanRetireHealthy
        {
            get { lock (_gate) return _observed && _errors.Count == 0 && Driver.Task.IsCompletedSuccessfully &&
                Settlement?.IsCompletedSuccessfully == true && Observation?.IsCompletedSuccessfully == true; }
        }
        internal void MarkIndependentlyObserved() { lock (_gate) _observed = true; }
        internal void Retain(Exception cause)
        {
            lock (_gate) AddReference(_errors, cause);
            // A rejected late use of a once-only callback keeps this original
            // strongly visible even after its earlier healthy observation.
            lock (owner._catalogueGate)
                if (!owner._catalogueOriginals.Contains(this)) owner._catalogueOriginals.Add(this);
        }
        internal void Capture(Task actual, Exception caught)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } payload)
                foreach (var cause in payload.InnerExceptions) Retain(cause);
            else Retain(caught);
        }
        private void ThrowRetained()
        {
            var failures = Errors;
            if (failures.Count != 0) throw new AggregateException("Actual model catalogue callbacks failed.", failures);
        }
        internal T Invoke<T>(Func<T> body)
        {
            T value = default!; var called = 0; var active = 1; var thread = Environment.CurrentManagedThreadId;
            var failures = new List<Exception>();
            void CaptureFailure(Exception cause) { lock (failures) AddReference(failures, cause); Retain(cause); }
            void Run()
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                {
                    if (Volatile.Read(ref active) == 0 || Environment.CurrentManagedThreadId != thread ||
                        Interlocked.Increment(ref called) != 1)
                    {
                        var cause = new InvalidOperationException("The original model catalogue callback must be synchronous and once-only.");
                        CaptureFailure(cause); throw cause;
                    }
                    try { value = body(); }
                    catch (Exception cause) { CaptureFailure(cause); throw; }
                    return true;
                });
            }
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { scope(Run); return true; }); }
            catch (Exception cause) { CaptureFailure(cause); }
            finally { Volatile.Write(ref active, 0); }
            if (Volatile.Read(ref called) == 0)
                CaptureFailure(new InvalidOperationException("The original model catalogue scope did not invoke its callback."));
            lock (failures)
                if (failures.Count != 0) throw new AggregateException("Original model catalogue callback and scope failed.", failures.ToArray());
            return value;
        }
        internal async Task<T> ReadAsync<T>(Func<Task<T>> factory)
        {
            Task<T>? raw = null; T value = default!;
            try
            {
                Invoke(() =>
                {
                    lock (_gate)
                        if (_providers.Count >= 256) throw new InvalidOperationException("The model catalogue provider-source limit was reached.");
                    raw = factory() ?? throw new InvalidOperationException("The configured provider returned no original catalogue Task.");
                    ProviderOriginal? captured = null;
                    lock (_gate)
                        if (!_providers.ContainsKey(raw)) _providers.Add(raw, captured = new(raw));
                    // Start the observer outside the child gate: a terminal raw
                    // may synchronously record into the registry cohort.
                    if (captured is not null) captured.Observation = ObserveProviderAsync(raw);
                    retain(raw); // SAME raw custody is already accepted before this callback.
                    return true;
                });
            }
            catch (Exception cause) { Retain(cause); }
            if (raw is not null)
                try { value = await raw.ConfigureAwait(false); } catch (Exception cause) { Capture(raw, cause); }
            ThrowRetained();
            return raw is null ? throw new InvalidOperationException("No configured provider original was captured.") : value;
        }
        private async Task ObserveProviderAsync(Task actual)
        {
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { Capture(actual, cause); }
        }
        internal async Task JoinProviderOriginalsAsync()
        {
            Task[] originals; Task[] observers;
            lock (_gate)
            {
                originals = _providers.Keys.ToArray();
                observers = _providers.Values.Where(actual => actual.Observation is not null)
                    .Select(actual => actual.Observation!).ToArray();
            }
            foreach (var actual in originals)
                try { await actual.ConfigureAwait(false); } catch (Exception cause) { Capture(actual, cause); }
            foreach (var actual in observers)
                try { await actual.ConfigureAwait(false); } catch (Exception cause) { Capture(actual, cause); }
        }
    }
}
