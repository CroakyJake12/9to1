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
    private bool _disposed;
    private string? _pendingImport;
    public string ResourceKind => "den";

    public async Task<string> SelectAsync(string nativeSelectedRoot, bool createNew, HomeLocalStoreOwnership ownership, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSelectedRoot);
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Opening is an observation. Even an existing empty Den is never marked newly created.
            var candidate = createNew
                ? await HomeDenStoreEvidenceProvider.CreateAsync(nativeSelectedRoot, actors, ct)
                : await HomeDenStoreEvidenceProvider.OpenAsync(nativeSelectedRoot, actors, ct);
            try
            {
                // The old bitmap/decoder/agent lease must retire before a replacement is visible.
                await retirePresentation(ct);
                await _evidence.WaitAsync(ct);
                try
                {
                    var previous = _provider;
                    _provider = candidate;
                    _pendingImport = null;
                    candidate = null!;
                    if (previous is not null) await previous.DisposeAsync();
                }
                finally { _evidence.Release(); }
            }
            finally { if (candidate is not null) await candidate.DisposeAsync(); }
            var selected = _provider!;
            if (createNew)
                await ownership.BindNewEmptyAsync(ResourceKind, selected.Store.Manifest.DenId, ct);
            return selected.Store.Manifest.DenId;
        }
        finally { _changes.Release(); }
    }

    public async Task<HomePermissionAuthorization> RequestExistingImportAsync(HomeLocalStoreOwnership ownership, CancellationToken ct = default)
    {
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _provider ?? throw new InvalidOperationException("Select a Den first.");
            var actor = await actors.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("A current Home profile is required.");
            var review = await ownership.RequestImportAsync(ResourceKind, selected.Store.Manifest.DenId, actor.AuthenticationRevision, ct);
            _pendingImport = review.RequestId;
            return review;
        }
        finally { _changes.Release(); }
    }

    public async Task CompleteExistingImportAsync(string displayedRequestId, HomeLocalStoreOwnership ownership, CancellationToken ct = default)
    {
        await _changes.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _provider ?? throw new InvalidOperationException("Select a Den first.");
            if (_pendingImport is null || displayedRequestId != _pendingImport)
                throw new UnauthorizedAccessException("This is not the displayed Den import review.");
            var binding = await ownership.CompleteImportAsync(_pendingImport, ct);
            if (binding.ResourceKind != ResourceKind || binding.StoreId != selected.Store.Manifest.DenId)
                throw new UnauthorizedAccessException("The approved import belongs to a different Den.");
            _pendingImport = null;
        }
        finally { _changes.Release(); }
    }

    public async Task<HomePersonalDenSession> OpenBoundSessionAsync(IResourceStoreOwnershipReceiptAuthority receipts, CancellationToken ct = default)
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

    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken ct)
    {
        await _evidence.WaitAsync(ct);
        try { return _disposed || _provider is null ? null : await _provider.ReadAsync(storeId, ct); }
        finally { _evidence.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _changes.WaitAsync();
        try
        {
            if (_disposed) return;
            await retirePresentation(CancellationToken.None);
            await _evidence.WaitAsync();
            try
            {
                _disposed = true;
                var previous = _provider;
                _provider = null;
                if (previous is not null) await previous.DisposeAsync();
            }
            finally { _evidence.Release(); }
        }
        finally { _changes.Release(); }
    }
}
