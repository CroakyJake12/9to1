using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.AIStudio;

/// <summary>Register this single evidence provider in the host's existing Home graph.
/// Paths come only from explicit native configuration; durable Den identity grants no ownership.</summary>
public sealed class StudioDenLifetime(
    IAuthenticatedResourceActorSource actors,
    Func<CancellationToken, Task> retirePresentation) : IHomeLocalStoreEvidenceProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly SemaphoreSlim _evidence = new(1, 1);
    private HomeDenStoreEvidenceProvider? _provider;
    private StudioNativeWindow? _originalNativePresentation;
    private readonly StudioOriginalCallbackLifetime _originals = new();
    private readonly object _closeSync = new();
    private readonly List<ProviderOriginal> _acquiredProviders = [];
    private bool _disposed, _closing;
    private Task? _close;
    public Task? OriginalCloseTask { get { lock (_closeSync) return _close; } }
    private string? _pendingImport;
    private string? _pendingImportAudit;
    public string ResourceKind => "den";

    internal void BindOriginalNativePresentation(StudioNativeWindow actualWindow)
    {
        ArgumentNullException.ThrowIfNull(actualWindow);
        lock (_closeSync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_originalNativePresentation is not null && !ReferenceEquals(_originalNativePresentation, actualWindow))
                throw new InvalidOperationException("The original native Den presentation is already bound.");
            _originalNativePresentation = actualWindow;
        }
    }

    private Task RetireOriginalPresentationAsync(CancellationToken token)
    {
        StudioNativeWindow? actualWindow;
        lock (_closeSync) actualWindow = _originalNativePresentation;
        // A native acquisition binds the exact Window, including a partial constructor.
        // Pure nonnative owners retain their existing supplied presentation callback.
        return actualWindow is null ? retirePresentation(token) : actualWindow.RetireWorkspaceAsync(token);
    }

    public Task<string> SelectAsync(string nativeSelectedRoot, bool createNew, HomeLocalStoreOwnership ownership, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSelectedRoot);
        ObjectDisposedException.ThrowIf(_closing, this);
        var capturedRoot = nativeSelectedRoot; var capturedOwnership = ownership; var capturedCreate = createNew;
        return _originals.RunResult(token => SelectOriginalAsync(capturedRoot, capturedCreate, capturedOwnership, token), ct);
    }
    private async Task<string> SelectOriginalAsync(string nativeSelectedRoot, bool createNew, HomeLocalStoreOwnership ownership, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSelectedRoot);
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ObjectDisposedException.ThrowIf(_closing, this);
            _acquiredProviders.RemoveAll(item => item.Close is { IsCompletedSuccessfully: true } &&
                item.ActualClose is { IsCompletedSuccessfully: true });
            if (_acquiredProviders.Count >= 64)
                throw new InvalidOperationException("Original Den provider custody is full; failures remain retained.");
            // Opening is an observation. Even an existing empty Den is never marked newly created.
            var candidate = createNew
                ? await HomeDenStoreEvidenceProvider.CreateAsync(nativeSelectedRoot, actors, ct)
                : await HomeDenStoreEvidenceProvider.OpenAsync(nativeSelectedRoot, actors, ct);
            var acquired = new ProviderOriginal(candidate);
            _acquiredProviders.Add(acquired);
            List<Exception> acquisitionErrors = [];
            try
            {
                // The old bitmap/decoder/agent lease must retire before a replacement is visible.
                var actualRetirement = RetireOriginalPresentationAsync(ct)
                    ?? throw new InvalidOperationException("The original presentation supplied no retirement task.");
                await actualRetirement;
                ObjectDisposedException.ThrowIf(_closing, this);
                await _evidence.WaitAsync(ct);
                try
                {
                    var previous = _provider;
                    _provider = candidate;
                    _pendingImport = null; _pendingImportAudit = null;
                    candidate = null!;
                    if (previous is not null) await CloseAcquiredProviderAsync(FindOriginalProvider(previous));
                }
                finally { _evidence.Release(); }
            }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(acquisitionErrors, error); }
            finally
            {
                if (candidate is not null)
                    try { await CloseAcquiredProviderAsync(acquired); }
                    catch (Exception error) { StudioOriginalCallbackLifetime.Add(acquisitionErrors, error); }
            }
            StudioOriginalCallbackLifetime.Throw(acquisitionErrors);
            var selected = _provider!;
            if (createNew)
                await ownership.BindNewEmptyAsync(ResourceKind, selected.Store.Manifest.DenId, ct);
            return selected.Store.Manifest.DenId;
        }
        finally { _changes.Release(); }
    }

    public Task<HomePermissionAuthorization> RequestExistingImportAsync(HomeLocalStoreOwnership ownership, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        return _originals.RunResult(token => RequestExistingImportOriginalAsync(ownership, token), ct);
    }
    private async Task<HomePermissionAuthorization> RequestExistingImportOriginalAsync(HomeLocalStoreOwnership ownership, CancellationToken ct)
    {
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _provider ?? throw new InvalidOperationException("Select a Den first.");
            if (_pendingImportAudit is not null) throw new InvalidOperationException("Recover the acknowledged import audit; do not import again.");
            var actor = await actors.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("A current Home profile is required.");
            var review = await ownership.RequestImportAsync(ResourceKind, selected.Store.Manifest.DenId, actor.AuthenticationRevision, ct);
            _pendingImport = review.RequestId;
            return review;
        }
        finally { _changes.Release(); }
    }

    public Task CompleteExistingImportAsync(string displayedRequestId, HomeLocalStoreOwnership ownership, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        return _originals.Run(token => CompleteExistingImportOriginalAsync(displayedRequestId, ownership, token), ct);
    }
    private async Task CompleteExistingImportOriginalAsync(string displayedRequestId, HomeLocalStoreOwnership ownership, CancellationToken ct)
    {
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _provider ?? throw new InvalidOperationException("Select a Den first.");
            if (_pendingImport is null || displayedRequestId != _pendingImport)
                throw new UnauthorizedAccessException("This is not the displayed Den import review.");
            HomeLocalStoreBinding binding;
            try { binding = await ownership.CompleteImportAsync(_pendingImport, ct); }
            catch (HomeStoreImportAuditPendingException pending)
            {
                if (pending.RequestId != _pendingImport || pending.Binding.ResourceKind != ResourceKind || pending.Binding.StoreId != selected.Store.Manifest.DenId)
                    throw new UnauthorizedAccessException("The pending audit belongs to a different Den import.");
                _pendingImport = null;
                _pendingImportAudit = pending.RequestId;
                throw;
            }
            if (binding.ResourceKind != ResourceKind || binding.StoreId != selected.Store.Manifest.DenId)
                throw new UnauthorizedAccessException("The approved import belongs to a different Den.");
            _pendingImport = null;
        }
        finally { _changes.Release(); }
    }

    public Task RetryExistingImportAuditAsync(string displayedRequestId, HomeLocalStoreOwnership ownership, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        return _originals.Run(token => RetryExistingImportAuditOriginalAsync(displayedRequestId, ownership, token), ct);
    }
    private async Task RetryExistingImportAuditOriginalAsync(string displayedRequestId, HomeLocalStoreOwnership ownership, CancellationToken ct)
    {
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _provider ?? throw new InvalidOperationException("Select a Den first.");
            if (_pendingImportAudit is null || displayedRequestId != _pendingImportAudit)
                throw new UnauthorizedAccessException("This is not the displayed Den import audit recovery.");
            var binding = await ownership.RetryImportAuditAsync(displayedRequestId, ct);
            if (binding.ResourceKind != ResourceKind || binding.StoreId != selected.Store.Manifest.DenId)
                throw new UnauthorizedAccessException("The recovered audit belongs to a different Den.");
            _pendingImportAudit = null;
        }
        finally { _changes.Release(); }
    }

    public Task<HomePersonalDenSession> OpenBoundSessionAsync(IResourceStoreOwnershipReceiptAuthority receipts, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        return _originals.RunResult(token => OpenBoundSessionOriginalAsync(receipts, token), ct);
    }
    private async Task<HomePersonalDenSession> OpenBoundSessionOriginalAsync(IResourceStoreOwnershipReceiptAuthority receipts, CancellationToken ct)
    {
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _provider ?? throw new InvalidOperationException("Select a Den first.");
            return await new HomePersonalDenFactory(selected, receipts, actors).OpenAsync(ct);
        }
        finally { _changes.Release(); }
    }

    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        return new(_originals.RunResult(token => ReadOriginalAsync(storeId, token), ct));
    }
    private async Task<HomeLocalStoreEvidence?> ReadOriginalAsync(string storeId, CancellationToken ct)
    {
        await _evidence.WaitAsync(ct);
        try { return _disposed || _provider is null ? null : await _provider.ReadAsync(storeId, ct); }
        finally { _evidence.Release(); }
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    public Task CloseAndDrainAsync()
    {
        lock (_closeSync)
        {
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(start.Task);
            _closing = true;
            start.SetResult();
            return _close;
        }
    }

    private async Task CloseOriginalAsync(Task start)
    {
        await start;
        List<Exception> errors = [];
        Task? actualCallbacks = null, actualRetirement = null;
        try { actualCallbacks = _originals.CloseAndDrainAsync(); }
        catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        try
        {
            actualRetirement = RetireOriginalPresentationAsync(CancellationToken.None)
                ?? throw new InvalidOperationException("The original presentation supplied no close task.");
        }
        catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (actualCallbacks is not null)
            try { await actualCallbacks; }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (actualRetirement is not null)
            try { await actualRetirement; }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (actualCallbacks is null || !_originals.OriginalsCapturedAndSettled ||
            actualRetirement is not { IsCompletedSuccessfully: true })
            StudioOriginalCallbackLifetime.Add(errors, new InvalidOperationException("Actual Den/presentation settlement is missing."));
        // A missing or failed presentation close cannot authorize shared provider retirement.
        StudioOriginalCallbackLifetime.Throw(errors);
        await _changes.WaitAsync();
        try
        {
            await _evidence.WaitAsync();
            try
            {
                foreach (var acquired in _acquiredProviders.ToArray())
                    try { await CloseAcquiredProviderAsync(acquired); }
                    catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
                if (errors.Count == 0)
                {
                    _provider = null; _disposed = true; _acquiredProviders.Clear();
                }
            }
            finally { _evidence.Release(); }
        }
        finally { _changes.Release(); }
        StudioOriginalCallbackLifetime.Throw(errors);
    }

    private ProviderOriginal FindOriginalProvider(HomeDenStoreEvidenceProvider provider) =>
        _acquiredProviders.SingleOrDefault(item => ReferenceEquals(item.Provider, provider))
        ?? throw new InvalidOperationException("Actual acquired Den provider custody is missing.");

    private Task CloseAcquiredProviderAsync(ProviderOriginal acquired)
    {
        if (acquired.Close is not null) return acquired.Close;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        acquired.Close = CloseOriginalProviderAsync(start.Task, acquired);
        start.SetResult();
        return acquired.Close;
    }

    private static async Task CloseOriginalProviderAsync(Task start, ProviderOriginal acquired)
    {
        await start;
        acquired.ActualClose = acquired.Provider.DisposeAsync().AsTask();
        if (acquired.ActualClose is null)
            throw new InvalidOperationException("The original Den provider supplied no close task.");
        await acquired.ActualClose;
    }

    private sealed class ProviderOriginal(HomeDenStoreEvidenceProvider provider)
    {
        internal HomeDenStoreEvidenceProvider Provider { get; } = provider;
        internal Task? Close, ActualClose;
    }
}
