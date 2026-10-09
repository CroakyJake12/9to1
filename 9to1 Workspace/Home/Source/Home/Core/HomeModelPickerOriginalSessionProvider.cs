using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>One native manager lifetime, bound by the trusted host to its first actual actor.
/// It never falls back to the legacy current-actor route mutation port.</summary>
public sealed class HomeModelPickerOriginalSessionProvider(
    IHomeModelPickerFeatureProvider owner, IAuthenticatedResourceActorSource actors)
    : IHomeModelPickerFeatureProvider, IDisposable
{
    private readonly IHomeModelPickerFeatureProvider _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private readonly IAuthenticatedResourceActorSource _actors = actors ?? throw new ArgumentNullException(nameof(actors));
    private readonly SemaphoreSlim _begin = new(1, 1);
    private AuthenticatedResourceActor? _original;
    private bool _begun;
    private volatile bool _disposed;
    public async Task<bool> BeginAsync(CancellationToken ct = default)
    {
        await _begin.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed || _begun) return await CurrentAsync(ct).ConfigureAwait(false) is not null;
            _begun = true;
            var original = await _actors.GetCurrentAsync(ct).ConfigureAwait(false);
            if (original is null || _disposed || await _actors.GetCurrentAsync(ct).ConfigureAwait(false) != original) return false;
            _original = original;
            return true;
        }
        finally { _begin.Release(); }
    }
    public async Task<bool> IsCurrentAsync(CancellationToken ct = default) =>
        await CurrentAsync(ct).ConfigureAwait(false) is not null;
    private async Task<AuthenticatedResourceActor?> CurrentAsync(CancellationToken ct)
    {
        var original = _original;
        return !_disposed && original is not null && await _actors.GetCurrentAsync(ct).ConfigureAwait(false) == original && !_disposed
            ? original : null;
    }
    private static HomeCoreOperationResult<T> Denied<T>() =>
        new(false, "OriginalModelSessionUnavailable", "The originating model manager session is unavailable. Reopen it before a new edit.");
    private async Task<HomeCoreOperationResult<T>> ReadAsync<T>(Func<Task<HomeCoreOperationResult<T>>> read, CancellationToken ct)
    {
        if (await CurrentAsync(ct).ConfigureAwait(false) is null) return Denied<T>();
        var result = await read().ConfigureAwait(false);
        return await CurrentAsync(ct).ConfigureAwait(false) is null ? Denied<T>() : result;
    }
    public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken cancellationToken = default) =>
        ReadAsync(() => _owner.GetCatalogueAsync(query, cancellationToken), cancellationToken);
    public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken cancellationToken = default) =>
        ReadAsync(() => _owner.GetSnapshotAsync(scope, category, cancellationToken), cancellationToken);
    public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken cancellationToken = default) =>
        ReadAsync(() => _owner.PreviewResolutionAsync(request, cancellationToken), cancellationToken);
    public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken cancellationToken = default)
    {
        var original = await CurrentAsync(cancellationToken).ConfigureAwait(false);
        if (original is null || _owner is not IHomeModelPickerOriginalActorFeatureProvider bound) return Denied<HomeModelPickerSnapshot>();
        // The owning port fences actor through review and guarded final CAS. Retain its actual
        // fixed outcome even if this view is superseded after the mutation has already committed.
        return await bound.UpdateRouteForActorAsync(original, edit, cancellationToken).ConfigureAwait(false);
    }
    public Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken cancellationToken = default) =>
        _owner.RetryAuditAsync(requestId, cancellationToken); // Exact owner audit only; never route update or fresh review.
    public void Dispose() { _disposed = true; }
}
