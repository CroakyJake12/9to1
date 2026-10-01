using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>A displayed setup session over the actual Home/settings graph. Inspection never binds ownership.</summary>
public class HomeLocalStoreSetupSession(string resourceKind, IResourceStoreIdentitySource identities,
    IAuthenticatedResourceActorSource actors, HomeLocalStoreOwnership ownership,
    IHomeLocalStoreEvidenceProvider evidence)
{
    public string ResourceKind { get; } = ValidateKind(resourceKind, evidence);

    private static string ValidateKind(string kind, IHomeLocalStoreEvidenceProvider source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(kind, source.ResourceKind, StringComparison.Ordinal))
            throw new ArgumentException("The setup resource kind must match its actual owning evidence provider.", nameof(kind));
        return kind;
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthenticatedResourceActor? _actor;
    private Guid? _storeId;
    private string? _requestId;
    private string? _auditRequestId;

    public sealed record Snapshot(Guid StoreId, bool IsOwned, bool CanBindEmpty, string? PendingRequestId, string? PendingAuditRequestId);

    public async Task<Snapshot> InspectAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Recover the local profile in Home before configuring local storage.");
            if (actor.AccountId is not null || actor.OrganisationId is not null)
                throw new UnauthorizedAccessException("Local storage requires the current operating-system profile.");
            var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
            if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty)
                throw new UnauthorizedAccessException("The canonical store identity is unavailable.");
            if (_actor is null) { _actor = actor; _storeId = identity.StoreId; }
            await RequireCurrentAsync(token).ConfigureAwait(false);
            var storeId = identity.StoreId.ToString("D");
            var binding = await ownership.GetVerifiedAsync(ResourceKind, storeId, token).ConfigureAwait(false);
            var observed = await evidence.ReadAsync(storeId, token).ConfigureAwait(false);
            await RequireCurrentAsync(token).ConfigureAwait(false);
            return new(identity.StoreId, binding is not null,
                binding is null && observed is { NewlyCreated: true, IsEmpty: true, AccessibleToCurrentOsPrincipal: true } &&
                observed.ResourceKind == ResourceKind && observed.StoreId == storeId &&
                !string.IsNullOrWhiteSpace(observed.Revision), _requestId, _auditRequestId);
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
            await ownership.BindNewEmptyAsync(_actor!, ResourceKind, _storeId!.Value.ToString("D"), token).ConfigureAwait(false);
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
                throw new InvalidOperationException("Review the pending local store ownership request before making another request.");
            var request = await ownership.RequestImportAsync(_actor!, ResourceKind, _storeId!.Value.ToString("D"),
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
            var requestId = _requestId ?? throw new InvalidOperationException("Request and review local store ownership in Home first.");
            try
            {
                await ownership.CompleteImportAsync(requestId, token).ConfigureAwait(false);
                _requestId = null;
            }
            catch (HomeStoreImportAuditPendingException pending) when (pending.RequestId == requestId)
            {
                // Ownership was acknowledged. Retain only the audit retry, never repeat the import.
                _requestId = null;
                _auditRequestId = pending.RequestId;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task RetryAuditAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await RequireCurrentAsync(token).ConfigureAwait(false);
            var requestId = _auditRequestId ?? throw new InvalidOperationException("No local store import audit needs recovery.");
            await ownership.RetryImportAuditAsync(requestId, token).ConfigureAwait(false);
            _auditRequestId = null;
        }
        finally { _gate.Release(); }
    }

    private async Task RequireCurrentAsync(CancellationToken token)
    {
        if (_actor is null || _storeId is null || await actors.GetCurrentAsync(token).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("The Home profile changed. Reopen local store setup.");
        var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != _storeId ||
            await actors.GetCurrentAsync(token).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("The local store or Home profile changed. Reopen setup.");
    }
}
