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

/// <summary>The binding was acknowledged durable; only its exact audit outcome remains pending.
/// Retrying the import operation is not permitted. RetryImportAuditAsync never writes a binding.</summary>
public sealed class HomeStoreImportAuditPendingException(string requestId, HomeLocalStoreBinding binding, Exception cause)
    : IOException("Store ownership was imported, but its Home audit needs recovery. Retry only the audit.", cause)
{
    public string RequestId { get; } = requestId;
    public HomeLocalStoreBinding Binding { get; } = binding;
}

/// <summary>Explicit personal-store ownership registry. Store IDs come from durable owning repository metadata, never mutable paths.</summary>
public sealed class HomeLocalStoreOwnership(IHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
    IHomeLocalStoreEvidenceSource evidence, HomePermissionTrustService permissions)
{
    internal bool IsBoundTo(IHomeCoreStateStore candidate, HomeLocalProfileIdentity identity) =>
        ReferenceEquals(store, candidate) && ReferenceEquals(profiles, identity);
    private sealed record Import(HomeLocalStoreEvidence Evidence, AuthenticatedResourceActor Actor);
    private readonly ConcurrentDictionary<string, Import> _imports = new();
    private sealed record ImportCompletion(AuthenticatedResourceActor Actor, HomeLocalStoreBinding Binding, HomeExecutionOutcome Outcome)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
    private readonly ConcurrentDictionary<string, ImportCompletion> _importAudits = new();

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

    internal async ValueTask<ResourceStoreBindingReceipt?> CaptureReceiptAsync(HomeLocalStoreBinding expected,
        CancellationToken ct)
    {
        // Deliberately no owning-store evidence read: callers can hold that store's commit lease.
        var read = await store.ReadAsync(ct).ConfigureAwait(false);
        if (!read.IsSuccess) return null;
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == Id(expected.ResourceKind, expected.StoreId));
        if (record is null || record.SchemaVersion != 1 || record.RecordType != "home.local-store-ownership" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical) return null;
        try
        {
            if (record.Payload.Deserialize<HomeLocalStoreBinding>() != expected) return null;
            return new(record.Revision, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record.Payload))));
        }
        catch (JsonException) { return null; }
    }

    internal async ValueTask<bool> IsReceiptCurrentAsync(VerifiedResourceStoreOwnership captured, CancellationToken ct)
    {
        if (captured.Receipt is null) return false;
        var read = await store.ReadAsync(ct).ConfigureAwait(false);
        if (!read.IsSuccess) return false;
        return IsReceiptCurrentInState(read.State!, captured);
    }

    // A trusted issuer captures ownership BEFORE acquiring Home's commit lease. Final checks
    // inspect that same locked state and must never recursively call ReadAsync or an owning store.
    internal static bool IsReceiptCurrentInState(HomeCoreStoredState state, VerifiedResourceStoreOwnership captured)
    {
        if (captured.Receipt is null) return false;
        var records = state.Records.Where(item => item.RecordId == Id(captured.ResourceKind, captured.StoreId)).ToArray();
        if (records.Length != 1) return false;
        var record = records[0];
        if (record.SchemaVersion != 1 || record.RecordType != "home.local-store-ownership" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical ||
            record.Revision != captured.Receipt.Revision) return false;
        try
        {
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>();
            return binding is not null && binding.ResourceKind == captured.ResourceKind && binding.StoreId == captured.StoreId &&
                binding.ProfileId == captured.ProfileId && binding.ObservedStoreRevision == captured.ObservedStoreRevision &&
                Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record.Payload))) == captured.Receipt.Fingerprint;
        }
        catch (JsonException) { return false; }
    }

    public Task<HomeLocalStoreBinding> BindNewEmptyAsync(string kind, string storeId, CancellationToken ct = default) =>
        BindNewEmptyCoreAsync(null, kind, storeId, ct);
    public Task<HomeLocalStoreBinding> BindNewEmptyAsync(AuthenticatedResourceActor expectedActor, string kind,
        string storeId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return BindNewEmptyCoreAsync(expectedActor, kind, storeId, ct);
    }
    private async Task<HomeLocalStoreBinding> BindNewEmptyCoreAsync(AuthenticatedResourceActor? expectedActor,
        string kind, string storeId, CancellationToken ct)
    {
        var actor = await profiles.GetCurrentAsync(ct).ConfigureAwait(false) ?? throw new UnauthorizedAccessException();
        if (expectedActor is not null && actor != expectedActor)
            throw new UnauthorizedAccessException("The displayed Home setup profile changed; reopen setup.");
        var observed = await evidence.ReadAsync(kind, storeId, ct).ConfigureAwait(false);
        if (!Valid(observed, kind, storeId) || !observed!.NewlyCreated || !observed.IsEmpty)
            throw new UnauthorizedAccessException("An existing or unknown store requires explicit ownership import.");
        return await BindAsync(observed, actor, null, ct).ConfigureAwait(false);
    }

    public Task<HomePermissionAuthorization> RequestImportAsync(string kind, string storeId, string sessionId, CancellationToken ct = default) =>
        RequestImportCoreAsync(null, kind, storeId, sessionId, ct);
    public Task<HomePermissionAuthorization> RequestImportAsync(AuthenticatedResourceActor expectedActor, string kind,
        string storeId, string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return RequestImportCoreAsync(expectedActor, kind, storeId, sessionId, ct);
    }
    private async Task<HomePermissionAuthorization> RequestImportCoreAsync(AuthenticatedResourceActor? expectedActor,
        string kind, string storeId, string sessionId, CancellationToken ct)
    {
        var actor = await profiles.GetCurrentAsync(ct).ConfigureAwait(false) ?? throw new UnauthorizedAccessException();
        if (expectedActor is not null && actor != expectedActor)
            throw new UnauthorizedAccessException("The displayed Home setup profile changed; reopen setup.");
        var observed = await evidence.ReadAsync(kind, storeId, ct).ConfigureAwait(false);
        if (!Valid(observed, kind, storeId)) throw new UnauthorizedAccessException("Unknown or inaccessible local store cannot be imported.");
        if (actor != await profiles.GetCurrentAsync(ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The Home setup profile changed during store observation.");
        if (_imports.Count + _importAudits.Count >= 256) throw new InvalidOperationException("Too many pending ownership imports.");
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
        HomeLocalStoreBinding binding;
        try { binding = await BindAsync(current!, actor!, requestId, ct).ConfigureAwait(false); }
        catch
        {
            // An owning-store exception can occur after publication. Do not claim that no binding
            // exists or turn an uncertain result into permission to repeat the operation.
            try
            {
                await permissions.RecordExecutionAsync(requestId, new(HomePermissionRequestState.PartiallyCompleted,
                    "HOME_STORE_IMPORT_UNCERTAIN", "Ownership import could not confirm its result; inspect the existing binding before recovery.",
                    [new("local-resource-store", current!.ResourceKind + ":" + current.StoreId)]), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception auditError) when (auditError is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            throw;
        }
        var completion = new ImportCompletion(actor!, binding, new(HomePermissionRequestState.Succeeded, "HOME_STORE_IMPORTED",
            "Local store ownership import completed.", Array.AsReadOnly(new[] { new HomeObjectReference("local-resource-store", current!.ResourceKind + ":" + current.StoreId) })));
        _importAudits[requestId] = completion;
        return await FinishImportAuditAsync(requestId, completion, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Retries only the retained audit for an acknowledged import in this host lifetime.
    /// It cannot recreate, alter or regrant ownership, even when the binding or evidence changed later.</summary>
    public async Task<HomeLocalStoreBinding> RetryImportAuditAsync(string requestId, CancellationToken ct = default)
    {
        if (!_importAudits.TryGetValue(requestId, out var completion) ||
            await profiles.GetCurrentAsync(ct).ConfigureAwait(false) != completion.Actor)
            throw new UnauthorizedAccessException("No pending import audit belongs to this authenticated host session.");
        return await FinishImportAuditAsync(requestId, completion, ct).ConfigureAwait(false);
    }

    private async Task<HomeLocalStoreBinding> FinishImportAuditAsync(string requestId, ImportCompletion completion, CancellationToken ct)
    {
        await completion.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                var result = await permissions.RecordExecutionAsync(requestId, completion.Outcome, ct).ConfigureAwait(false);
                if (!result.Succeeded) throw new InvalidOperationException(result.Code + ": " + result.Message);
                _importAudits.TryRemove(requestId, out _);
                return completion.Binding;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
            { throw new HomeStoreImportAuditPendingException(requestId, completion.Binding, error); }
        }
        finally { completion.Gate.Release(); }
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
        var write = await store.WriteGuardedAsync(new(Id(observed.ResourceKind, observed.StoreId), "home.local-store-ownership", 1,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(binding)), 0, actor, profiles, ct).ConfigureAwait(false);
        if (!write.IsSuccess) throw new InvalidOperationException("Store ownership conflicts with an existing binding; explicit recovery is required.");
        return binding;
    }
    private static bool Valid(HomeLocalStoreEvidence? observed, string kind, string id) => observed is not null &&
        observed.ResourceKind == kind && observed.StoreId == id && !string.IsNullOrWhiteSpace(kind) &&
        !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(observed.Revision) && observed.AccessibleToCurrentOsPrincipal;
    private static string Id(string kind, string id) => "home.local-store:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + id)));
    private static string Digest(HomeLocalStoreEvidence observed) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(observed)));
}
