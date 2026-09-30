using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Services;

/// <summary>A displayed setup session over the actual Home/settings graph. Inspection never binds ownership.</summary>
internal sealed class SpacesStorageSetupSession(IResourceStoreIdentitySource identities,
    IAuthenticatedResourceActorSource actors, HomeLocalStoreOwnership ownership,
    SpacesLocalStoreEvidenceProvider evidence)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthenticatedResourceActor? _actor;
    private Guid? _storeId;
    private string? _requestId;

    internal sealed record Snapshot(Guid StoreId, bool IsOwned, bool CanBindEmpty, string? PendingRequestId);

    public async Task<Snapshot> InspectAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Recover the local profile in Home before configuring Spaces.");
            if (actor.AccountId is not null || actor.OrganisationId is not null)
                throw new UnauthorizedAccessException("Local Spaces requires the current operating-system profile.");
            var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
            if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty)
                throw new UnauthorizedAccessException("The canonical Spaces store identity is unavailable.");
            if (_actor is null) { _actor = actor; _storeId = identity.StoreId; }
            await RequireCurrentAsync(token).ConfigureAwait(false);
            var storeId = identity.StoreId.ToString("D");
            var binding = await ownership.GetVerifiedAsync("spaces", storeId, token).ConfigureAwait(false);
            var observed = await evidence.ReadAsync(storeId, token).ConfigureAwait(false);
            await RequireCurrentAsync(token).ConfigureAwait(false);
            return new(identity.StoreId, binding is not null,
                binding is null && observed is { NewlyCreated: true, IsEmpty: true }, _requestId);
        }
        finally { _gate.Release(); }
    }

    // Called only by the explicit empty-store setup action, never by inspection/navigation.
    public async Task BindEmptyAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await RequireCurrentAsync(token).ConfigureAwait(false);
            await ownership.BindNewEmptyAsync(_actor!, "spaces", _storeId!.Value.ToString("D"), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<HomePermissionAuthorization> RequestImportAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await RequireCurrentAsync(token).ConfigureAwait(false);
            if (_requestId is not null)
                throw new InvalidOperationException("Review the pending Spaces ownership request before making another request.");
            var request = await ownership.RequestImportAsync(_actor!, "spaces", _storeId!.Value.ToString("D"),
                _actor!.AuthenticationRevision, token).ConfigureAwait(false);
            if (request.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
                _requestId = request.RequestId;
            return request;
        }
        finally { _gate.Release(); }
    }

    public async Task CompleteImportAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await RequireCurrentAsync(token).ConfigureAwait(false);
            var requestId = _requestId ?? throw new InvalidOperationException("Request and review Spaces ownership in Home first.");
            await ownership.CompleteImportAsync(requestId, token).ConfigureAwait(false);
            _requestId = null;
        }
        finally { _gate.Release(); }
    }

    private async Task RequireCurrentAsync(CancellationToken token)
    {
        if (_actor is null || _storeId is null || await actors.GetCurrentAsync(token).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("The Home profile changed. Reopen Spaces setup.");
        var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != _storeId ||
            await actors.GetCurrentAsync(token).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("The Spaces store or Home profile changed. Reopen setup.");
    }
}
