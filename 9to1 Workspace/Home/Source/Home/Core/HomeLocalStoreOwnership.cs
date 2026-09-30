using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Observed by the owning store adapter from its real durable identity and local OS access boundary.</summary>
public sealed record HomeLocalStoreEvidence(string ResourceKind, string StoreId, string Revision,
    bool NewlyCreated, bool IsEmpty, bool AccessibleToCurrentOsPrincipal);
public interface IHomeLocalStoreEvidenceSource
{
    ValueTask<HomeLocalStoreEvidence?> ReadAsync(string resourceKind, string storeId, CancellationToken cancellationToken);
}
public interface IHomeLocalStoreEvidenceProvider
{
    string ResourceKind { get; }
    ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken);
}
public sealed class HomeLocalStoreEvidenceRegistry(IEnumerable<IHomeLocalStoreEvidenceProvider> providers) : IHomeLocalStoreEvidenceSource
{
    private readonly IHomeLocalStoreEvidenceProvider[] _providers = providers.ToArray();
    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string kind, string storeId, CancellationToken cancellationToken)
    {
        var owners = _providers.Where(provider => provider.ResourceKind == kind).ToArray();
        return owners.Length == 1 ? owners[0].ReadAsync(storeId, cancellationToken) : ValueTask.FromResult<HomeLocalStoreEvidence?>(null);
    }
}
public sealed record HomeLocalStoreBinding(string ResourceKind, string StoreId, string ProfileId,
    string ObservedStoreRevision, string? ImportApprovalId);

/// <summary>Explicit personal-store ownership registry. Store IDs come from durable owning repository metadata, never mutable paths.</summary>
public sealed class HomeLocalStoreOwnership(IHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
    IHomeLocalStoreEvidenceSource evidence, HomePermissionTrustService permissions)
{
    private sealed record Import(HomeLocalStoreEvidence Evidence, AuthenticatedResourceActor Actor);
    private readonly ConcurrentDictionary<string, Import> _imports = new();

    public async Task<HomeLocalStoreBinding?> GetVerifiedAsync(string kind, string storeId, CancellationToken ct = default)
    {
        var actor = await profiles.GetCurrentAsync(ct).ConfigureAwait(false);
        var observed = await evidence.ReadAsync(kind, storeId, ct).ConfigureAwait(false);
        if (actor is null || !Valid(observed, kind, storeId)) return null;
        var read = await store.ReadAsync(ct).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidDataException("Store ownership state needs recovery; it was not reset.");
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == Id(kind, storeId));
        if (record is null) return null;
        if (record.SchemaVersion != 1 || record.RecordType != "home.local-store-ownership" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("Unsupported store ownership schema; preserve it for recovery.");
        HomeLocalStoreBinding binding;
        try { binding = record.Payload.Deserialize<HomeLocalStoreBinding>() ?? throw new JsonException(); }
        catch (JsonException exception) { throw new InvalidDataException("Corrupt store ownership; preserve it for recovery.", exception); }
        return binding.ResourceKind == kind && binding.StoreId == storeId && binding.ProfileId == actor.ProfileId ? binding : null;
    }

    public async Task<HomeLocalStoreBinding> BindNewEmptyAsync(string kind, string storeId, CancellationToken ct = default)
    {
        var actor = await profiles.GetCurrentAsync(ct).ConfigureAwait(false) ?? throw new UnauthorizedAccessException();
        var observed = await evidence.ReadAsync(kind, storeId, ct).ConfigureAwait(false);
        if (!Valid(observed, kind, storeId) || !observed!.NewlyCreated || !observed.IsEmpty)
            throw new UnauthorizedAccessException("An existing or unknown store requires explicit ownership import.");
        return await BindAsync(observed, actor, null, ct).ConfigureAwait(false);
    }

    public async Task<HomePermissionAuthorization> RequestImportAsync(string kind, string storeId, string sessionId, CancellationToken ct = default)
    {
        var actor = await profiles.GetCurrentAsync(ct).ConfigureAwait(false) ?? throw new UnauthorizedAccessException();
        var observed = await evidence.ReadAsync(kind, storeId, ct).ConfigureAwait(false);
        if (!Valid(observed, kind, storeId)) throw new UnauthorizedAccessException("Unknown or inaccessible local store cannot be imported.");
        if (_imports.Count >= 256) throw new InvalidOperationException("Too many pending ownership imports.");
        var reference = new HomeObjectReference("local-resource-store", kind + ":" + storeId);
        var authorization = await permissions.AuthorizeAsync(new(null,
            new(actor.ActorId, actor.ActorId, actor.ProfileId, actor.AuthenticationRevision, true), sessionId,
            new("9to1.home.local-profile", "home.profile.importStore", [reference]),
            new([reference.ObjectType], 1, [reference], false,
                "Import ownership of existing " + kind + " store " + storeId + " into local profile " + actor.ProfileId,
                null, Digest(observed!))), ct).ConfigureAwait(false);
        if (authorization.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
            _imports[authorization.RequestId] = new(observed!, actor);
        return authorization;
    }

    public async Task<HomeLocalStoreBinding> CompleteImportAsync(string requestId, CancellationToken ct = default)
    {
        if (!_imports.TryGetValue(requestId, out var import)) throw new UnauthorizedAccessException("The import is not bound to this host request.");
        var actor = await profiles.GetCurrentAsync(ct).ConfigureAwait(false);
        var current = await evidence.ReadAsync(import.Evidence.ResourceKind, import.Evidence.StoreId, ct).ConfigureAwait(false);
        if (actor != import.Actor || current != import.Evidence) throw new UnauthorizedAccessException("The profile or store changed; review the import again.");
        if (!(await permissions.GetAuthorizationAsync(requestId, ct).ConfigureAwait(false)).IsAllowed ||
            !_imports.TryRemove(requestId, out _) || !(await permissions.BeginExecutionAsync(requestId, ct).ConfigureAwait(false)).IsAllowed)
            throw new UnauthorizedAccessException("Explicit Home ownership import approval is required.");
        try
        {
            var binding = await BindAsync(current!, actor!, requestId, ct).ConfigureAwait(false);
            await permissions.RecordExecutionAsync(requestId, new(HomePermissionRequestState.Succeeded, "HOME_STORE_IMPORTED",
                "Local store ownership import completed.", [new("local-resource-store", current!.ResourceKind + ":" + current.StoreId)]), CancellationToken.None).ConfigureAwait(false);
            return binding;
        }
        catch
        {
            await permissions.RecordExecutionAsync(requestId, new(HomePermissionRequestState.Failed, "HOME_STORE_IMPORT_FAILED",
                "Local store ownership import failed; existing data was preserved.", []), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<HomeLocalStoreBinding> BindAsync(HomeLocalStoreEvidence observed, AuthenticatedResourceActor actor, string? approval, CancellationToken ct)
    {
        if (actor.OrganisationId is not null || actor.AccountId is not null ||
            await profiles.GetCurrentAsync(ct).ConfigureAwait(false) != actor ||
            await evidence.ReadAsync(observed.ResourceKind, observed.StoreId, ct).ConfigureAwait(false) != observed)
            throw new UnauthorizedAccessException("The local personal ownership boundary changed.");
        var existing = await GetVerifiedAsync(observed.ResourceKind, observed.StoreId, ct).ConfigureAwait(false);
        if (existing is not null) return existing;
        var binding = new HomeLocalStoreBinding(observed.ResourceKind, observed.StoreId, actor.ProfileId, observed.Revision, approval);
        var write = await store.WriteAsync(new(Id(observed.ResourceKind, observed.StoreId), "home.local-store-ownership", 1,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(binding)), 0, ct).ConfigureAwait(false);
        if (!write.IsSuccess) throw new InvalidOperationException("Store ownership conflicts with an existing binding; explicit recovery is required.");
        return binding;
    }
    private static bool Valid(HomeLocalStoreEvidence? observed, string kind, string id) => observed is not null &&
        observed.ResourceKind == kind && observed.StoreId == id && !string.IsNullOrWhiteSpace(kind) &&
        !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(observed.Revision) && observed.AccessibleToCurrentOsPrincipal;
    private static string Id(string kind, string id) => "home.local-store:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + id)));
    private static string Digest(HomeLocalStoreEvidence observed) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(observed)));
}
