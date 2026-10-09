using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>App-owned lazy lease over the actual personal Den. Construction only captures
/// the configured path and SAME Home profiles; it performs no profile read or Den creation.
/// The App must join all bridge/business borrowers before retiring this store owner.</summary>
public sealed partial class OriginalAssistantPersonalDenHost : IHomeOriginalScopedLocalStoreEvidenceProvider, IAsyncDisposable
{
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly OriginalProfileSource _actors;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly SemaphoreSlim _acquisitions = new(1, 1);
    private readonly object _gate = new();
    private readonly List<Task> _sources = [];
    private readonly HashSet<Task> _successfullyJoinedSources = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> _sourceFailures = [];
    private readonly AsyncLocal<ExternalSource?> _externalSource = new();
    [ThreadStatic] private static Dictionary<OriginalAssistantPersonalDenHost, int>? _physicalSources;
    private HomeDenStoreEvidenceProvider? _provider;
    private Task? _providerClose;
    private sealed record ExternalSource(Action<Action> Scope, Action<Task> Retain);

    public OriginalAssistantPersonalDenHost(IAppPaths actualPaths, HomeLocalProfileIdentity sameHomeProfiles)
    {
        ArgumentNullException.ThrowIfNull(actualPaths);
        _profiles = sameHomeProfiles ?? throw new ArgumentNullException(nameof(sameHomeProfiles));
        CapturedRoot = Path.GetFullPath(Path.Combine(actualPaths.DataDirectory, "Dens", "personal"));
        _actors = new(this);
        _work = new(() => Task.CompletedTask, CleanupOriginalAsync);
    }

    public string ResourceKind => "den";
    public string CapturedRoot { get; }
    public Task? OriginalClose => _work.OriginalClose;
    public bool HasAcquiredProvider { get { lock (_gate) return _provider is not null; } }

    /// <summary>Owning app initialization/setup may create only its actually missing empty location.
    /// A present manifest takes the genuine Open path; corruption never becomes a creation fallback.</summary>
    public Task<HomeDenStoreEvidenceProvider> OpenOrCreateMissingOwnedOriginalAsync(AuthenticatedResourceActor displayedActor,
        HomeLocalStoreOwnership sameHomeOwnership, CancellationToken token = default) =>
        _work.RunAsync(async original =>
        {
            var manifestMissing = Invoke(() =>
            {
                try { _ = File.GetAttributes(Path.Combine(CapturedRoot, "den.json")); return false; }
                catch (FileNotFoundException) { return true; }
                catch (DirectoryNotFoundException) { return true; }
            });
            if (!manifestMissing)
                return await ReadOriginalSourceAsync(original, _ => OpenExistingOriginalAsync(token)).ConfigureAwait(false);
            await ReadOriginalSourceAsync(original, _ => CreateAndBindNewEmptyOriginalAsync(displayedActor, sameHomeOwnership, token)).ConfigureAwait(false);
            lock (_gate) return _provider ?? throw new InvalidOperationException("The actual created personal Den lease was not retained.");
        });

    /// <summary>Missing or corrupt existing storage is preserved and reported. This never falls back to Create.</summary>
    public Task<HomeDenStoreEvidenceProvider> OpenExistingOriginalAsync(CancellationToken token = default) =>
        OpenExistingOriginalAsync(null, token);

    internal Task<HomeDenStoreEvidenceProvider> OpenExistingWithinOriginalSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        return OpenExistingOriginalAsync(new(originalSynchronousScope, retainOriginalTask), token);
    }

    private Task<HomeDenStoreEvidenceProvider> OpenExistingOriginalAsync(ExternalSource? external, CancellationToken token) =>
        _work.RunAsync(async original =>
        {
            var previous = _externalSource.Value; _externalSource.Value = external;
            var acquired = false;
            try
            {
                await _acquisitions.WaitAsync(token).ConfigureAwait(false); acquired = true;
                lock (_gate) if (_provider is { } existing) return existing;
                return await ReadOriginalSourceAsync(original,
                    _ => HomeDenStoreEvidenceProvider.OpenAsync(CapturedRoot, _actors, token),
                    provider => { lock (_gate) _provider = provider; }).ConfigureAwait(false);
            }
            finally { if (acquired) _acquisitions.Release(); _externalSource.Value = previous; }
        });

    /// <summary>Only an explicit owning product setup/create action calls this. Existing storage
    /// cannot gain ownership by being empty or by sharing a configured path.</summary>
    public Task<HomeLocalStoreBinding> CreateAndBindNewEmptyOriginalAsync(AuthenticatedResourceActor displayedActor,
        HomeLocalStoreOwnership sameHomeOwnership, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(displayedActor); ArgumentNullException.ThrowIfNull(sameHomeOwnership);
        return _work.RunAsync(async original =>
        {
            await _acquisitions.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_gate) if (_provider is not null)
                    throw new InvalidOperationException("The actual personal Den is already open. Inspect its existing binding or explicit Home import.");
                Invoke(() => { DemandActualEmptyCreationLocation(); return true; });
                var current = await ReadOriginalSourceAsync(original, _ => _actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
                if (current != displayedActor || current is null || current.AccountId is not null || current.OrganisationId is not null)
                    throw new UnauthorizedAccessException("The displayed local Home profile changed. Reopen Assistants setup.");
                var provider = await ReadOriginalSourceAsync(original,
                    _ => HomeDenStoreEvidenceProvider.CreateAsync(CapturedRoot, _actors, token),
                    actual => { lock (_gate) _provider = actual; }).ConfigureAwait(false);
                return await ReadOriginalSourceAsync(original, source => sameHomeOwnership.BindNewEmptyWithinOriginalSourceAsync(displayedActor,
                    ResourceKind, provider.Store.Manifest.DenId, source.Scope, source.Retain, token)).ConfigureAwait(false);
            }
            finally { _acquisitions.Release(); }
        });
    }

    /// <summary>Factory construction grants no access. Its SAME OpenAsync verifies current Home ownership.
    /// The root composes the canonical bridge only after acquiring this actual app-owned lease.</summary>
    public HomePersonalDenFactory CreateOriginalFactory(IResourceStoreOwnershipReceiptAuthority sameHomeOwnership)
    {
        ArgumentNullException.ThrowIfNull(sameHomeOwnership);
        HomePersonalDenFactory? factory = null;
        _work.RunSynchronous(_ =>
        {
            HomeDenStoreEvidenceProvider provider;
            lock (_gate) provider = _provider ?? throw new InvalidOperationException("Open or explicitly create the actual personal Den first.");
            factory = new(provider, sameHomeOwnership, _actors);
        });
        return factory!;
    }

    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) =>
        new(ReadOriginalAsync(storeId, null, token));
    public ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        return new(ReadOriginalAsync(storeId, new(originalSynchronousScope, retainOriginalTask), token));
    }
    private Task<HomeLocalStoreEvidence?> ReadOriginalAsync(string storeId, ExternalSource? external, CancellationToken token) =>
        _work.RunAsync(async original =>
        {
            var previous = _externalSource.Value; _externalSource.Value = external;
            try
            {
                HomeDenStoreEvidenceProvider? provider; lock (_gate) provider = _provider;
                if (provider is null) return null; // Evidence inspection does not acquire/create a Den or read a profile.
                return await ReadOriginalSourceAsync(original, source => provider.ReadWithinOriginalSourceAsync(
                    storeId, source.Scope, source.Retain, token).AsTask()).ConfigureAwait(false);
            }
            finally { _externalSource.Value = previous; }
        });

    private void DemandActualEmptyCreationLocation()
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(CapturedRoot); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0 ||
            Directory.EnumerateFileSystemEntries(CapturedRoot).Any())
            throw new UnauthorizedAccessException("The configured personal Den location contains existing or linked storage. Preserve it for explicit Home recovery/import.");
    }

    private sealed class OriginalProfileSource(OriginalAssistantPersonalDenHost owner) : IOriginalScopedResourceActorSource
    {
        public Task<AuthenticatedResourceActor?> GetCurrentWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            return owner._work.RunAsync(async original =>
            {
                var previous = owner._externalSource.Value;
                owner._externalSource.Value = new(scope, retain);
                try { return await owner.ReadProfileOriginalAsync(original, token).ConfigureAwait(false); }
                finally { owner._externalSource.Value = previous; }
            });
        }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            var original = owner._work.Executing;
            return original is not null ? new(owner.ReadProfileOriginalAsync(original, token)) :
                new(owner._work.RunAsync(actual => owner.ReadProfileOriginalAsync(actual, token)));
        }
    }
    private async Task<AuthenticatedResourceActor?> ReadProfileOriginalAsync(DesktopOriginalWorkLifetime.Original original,
        CancellationToken token)
    {
        return await ReadOriginalSourceAsync(original,
            source => _profiles.GetCurrentAsync(source.Scope, source.Retain, token).AsTask()).ConfigureAwait(false);
    }

    public void RequestRetirement() => _work.RequestRetirement();
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.ContainsKey(this) == true)
            throw new InvalidOperationException("An actual personal Den source cannot join its own app lease.");
        _work.DemandExternalClose();
    }
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task CleanupOriginalAsync()
    {
        var errors = new List<Exception>(); Task[] sources;
        lock (_gate) sources = _sources.ToArray();
        foreach (var source in sources) await JoinOriginalSourceAsync(source, errors).ConfigureAwait(false);
        HomeDenStoreEvidenceProvider? provider; lock (_gate) provider = _provider;
        if (provider is not null)
        {
            try
            {
                await ReadOriginalSourceAsync<bool>(null, _ =>
                {
                    Task? actual; lock (_gate) actual = _providerClose;
                    if (actual is null)
                    {
                        actual = provider.DisposeAsync().AsTask();
                        lock (_gate) _providerClose = actual;
                    }
                    return CompleteOriginalProviderCloseAsync(actual);
                }).ConfigureAwait(false);
            }
            catch (Exception error) { Add(errors, error); }
            if (_providerClose is not null) await JoinOriginalSourceAsync(_providerClose, errors).ConfigureAwait(false);
        }
        lock (_gate) errors.AddRange(_sourceFailures);
        if (errors.Count != 0) throw new AggregateException("Actual personal Den source/lease cleanup failed.", errors);
    }
    private async Task<bool> CompleteOriginalProviderCloseAsync(Task actual)
    {
        // Retain the SAME raw close independently of the encompassing typed driver.
        TrackOriginalSource(actual);
        var errors = new List<Exception>();
        await JoinOriginalSourceAsync(actual, errors).ConfigureAwait(false);
        ThrowOriginalSourceFailures(errors);
        return true;
    }
    private T Invoke<T>(Func<T> callback) => InvokeWithinOriginalSourceLineage(callback, _externalSource.Value);
    private void Retain(Task actual) => RetainWithinOriginalSourceLineage(actual, _externalSource.Value);
    private async Task JoinOriginalSourceAsync(Task source, List<Exception> errors)
    {
        try
        {
            await source.ConfigureAwait(false);
            lock (_gate) _successfullyJoinedSources.Add(source);
        }
        catch (Exception error)
        { foreach (var cause in source.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) Add(errors, cause); }
    }
    private static void Add(List<Exception> errors, Exception cause)
    { if (!errors.Any(error => ReferenceEquals(error, cause))) errors.Add(cause); }
}
