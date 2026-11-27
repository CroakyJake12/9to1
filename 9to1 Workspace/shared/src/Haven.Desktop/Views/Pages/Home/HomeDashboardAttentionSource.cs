using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>A detached observation of an existing request. It cannot approve or execute it.</summary>
internal sealed record HomeDashboardAttentionRequest(
    string RequestId, string CallerName, string ActionName, DateTimeOffset RequestedAt, string Fingerprint);

internal sealed record HomeDashboardAttentionSnapshot(
    DateTimeOffset ObservedAt, IReadOnlyList<HomeDashboardAttentionRequest> Pending);

/// <summary>Borrows the original Home permission journal and profile. There is no attention store,
/// task-state inference or permission mutation here. Home's normal snapshot owns grant expiry.</summary>
internal sealed class HomeDashboardAttentionSource(
    HomePermissionTrustService permissions,
    IAuthenticatedResourceActorSource actors,
    AuthenticatedResourceActor originalActor,
    Func<string, AuthenticatedResourceActor, Func<bool>, CancellationToken, Task<bool>> review)
{
    private readonly HomePermissionTrustService _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
    private readonly IAuthenticatedResourceActorSource _actors = actors ?? throw new ArgumentNullException(nameof(actors));
    private readonly AuthenticatedResourceActor _actor = originalActor ?? throw new ArgumentNullException(nameof(originalActor));
    private readonly Func<string, AuthenticatedResourceActor, Func<bool>, CancellationToken, Task<bool>> _review =
        review ?? throw new ArgumentNullException(nameof(review));

    internal async Task<HomeDashboardAttentionSnapshot> ReadAsync(CancellationToken token)
    {
        await RequireActorAsync(token).ConfigureAwait(false);
        var snapshot = await _permissions.GetSnapshotAsync(cancellationToken: token).ConfigureAwait(false);
        await RequireActorAsync(token).ConfigureAwait(false);
        var pending = snapshot.PendingRequests.Where(IsCurrentActorPending)
            .OrderBy(request => request.RequestedAt).ThenBy(request => request.RequestId, StringComparer.Ordinal)
            .Select(Observe).ToArray();
        return new(DateTimeOffset.UtcNow, Array.AsReadOnly(pending));
    }

    internal async Task<bool> ReviewAsync(HomeDashboardAttentionRequest captured,
        Func<bool> isPublicationCurrent, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(isPublicationCurrent);
        await RequireActorAsync(token).ConfigureAwait(false);
        var current = await _permissions.ReadRequestObservationAsync(captured.RequestId, token).ConfigureAwait(false);
        await RequireActorAsync(token).ConfigureAwait(false);
        if (current is null || !IsCurrentActorPending(current) || Observe(current) != captured) return false;
        // The App owns UI-thread admission and the exact existing review-window original.
        // It checks this page's publication callback immediately before acquiring that original.
        return await _review(captured.RequestId, _actor, isPublicationCurrent, token).ConfigureAwait(false);
    }

    private bool IsCurrentActorPending(HomePermissionRequest request) =>
        request.State == HomePermissionRequestState.PendingApproval && request.Impact.ResourceBinding?.OriginalActor == _actor;

    private static HomeDashboardAttentionRequest Observe(HomePermissionRequest request) =>
        new(request.RequestId, request.Caller.DisplayName, request.Scope.ActionName, request.RequestedAt,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request))));

    private async Task RequireActorAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("Reopen Home to review requests for the current profile.");
        token.ThrowIfCancellationRequested();
    }
}
