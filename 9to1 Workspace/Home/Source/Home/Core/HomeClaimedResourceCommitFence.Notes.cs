using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeClaimedResourceCommitFence
{
    /// <summary>Exact original Notes save claim. This captures the existing Home ownership
    /// receipt and claimed attestation, not Notes mutation authority. Only the actual Notes
    /// transaction may validate and hold the returned fence through durable publication.</summary>
    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesSaveAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalSaveIntent originalIntent, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(originalSource); ArgumentNullException.ThrowIfNull(originalIntent);
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalActor);
        ValueTask<HomeClaimedResourceCommitFence?> Deny() => ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        if (!originalSource.IsIssuedOriginalSaveIntent(originalIntent) ||
            !originalSource.IsIssuedOriginalStoreObservation(originalIntent.StoreObservation) ||
            originalIntent.StoreObservation.StoreIdentity is not { SchemaVersion: 1 } identity || identity.StoreId == Guid.Empty ||
            originalIntent.OperationId == Guid.Empty || originalIntent.DocumentId == Guid.Empty ||
            originalIntent.ExpectedVersion < 0 || originalIntent.ExpectedVersion == long.MaxValue)
            return Deny();
        var admission = broker.CaptureClaimedAttestation(originalCapability);
        var scope = new ResourceScope(HomeNotesResourceAdmissionSource.ScopeKind, identity.StoreId.ToString("D"),
            originalIntent.StoreObservation.Revision, ResourceAccess.Write);
        if (admission is null || admission.Actor != originalActor ||
            admission.Submission.Scope.TargetAppId != HomeNotesResourceAdmissionSource.TargetAppId ||
            admission.Submission.Scope.ActionName != HomeNotesResourceAdmissionSource.SaveAction ||
            admission.Submission.Impact.ResourceBinding is not { SchemaVersion: 1 } binding ||
            binding.Scopes.Count != 1 || binding.Scopes[0] != scope ||
            originalCapability.Scopes.Count != 1 || originalCapability.Scopes[0] != scope)
            return Deny();
        try
        {
            var arguments = originalIntent.Arguments;
            if (arguments.ValueKind != JsonValueKind.Object ||
                admission.Submission.Impact.ArgumentsDigest != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText()))))
                return Deny();
            var names = arguments.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(new[] { "contentSha256", "documentId", "expectedVersion", "operationId", "reason", "storeId", "storeRevision" }) ||
                arguments.GetProperty("operationId").GetGuid() != originalIntent.OperationId ||
                arguments.GetProperty("storeId").GetGuid() != identity.StoreId ||
                arguments.GetProperty("storeRevision").GetString() != scope.Revision ||
                arguments.GetProperty("documentId").GetGuid() != originalIntent.DocumentId ||
                arguments.GetProperty("expectedVersion").GetInt64() != originalIntent.ExpectedVersion ||
                arguments.GetProperty("contentSha256").GetString() != originalIntent.ContentSha256 ||
                originalIntent.ContentSha256 is not { Length: 64 } hash ||
                hash.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')) ||
                arguments.GetProperty("reason").GetString() != originalIntent.Reason ||
                originalIntent.Reason is not { Length: > 0 and <= 160 })
                return Deny();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return Deny(); }
        // Pure original-owner identity only. No async Notes/Home read is invoked while the
        // eventual same Home lease is held; the private Notes transaction enforces custody/CAS.
        return CaptureCoreAsync(broker, store, profiles, ownership, HomeNotesResourceAdmissionSource.StoreKind,
            identity.StoreId.ToString("D"), originalCapability, originalActor, [],
            () => originalSource.IsIssuedOriginalSaveIntent(originalIntent), cancellationToken);
    }

    /// <summary>Exact original metadata-only Notes library read. The actual owner must retain
    /// its canonical transaction and physical descriptors while validating and reading. This
    /// never admits raw List/Load recovery, repairs or a Notes mutation.</summary>
    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesLibraryReadAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalLibraryReadRequest originalRequest, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(originalSource); ArgumentNullException.ThrowIfNull(originalRequest);
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalActor);
        ValueTask<HomeClaimedResourceCommitFence?> Deny() => ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        if (!originalSource.IsIssuedOriginalLibraryReadRequest(originalRequest) ||
            !originalSource.IsIssuedOriginalStoreObservation(originalRequest.StoreObservation) ||
            originalRequest.StoreObservation.StoreIdentity is not { SchemaVersion: 1 } identity || identity.StoreId == Guid.Empty ||
            originalRequest.OperationId == Guid.Empty || originalRequest.DocumentId == Guid.Empty)
            return Deny();
        var admission = broker.CaptureClaimedAttestation(originalCapability);
        var scope = new ResourceScope(HomeNotesResourceAdmissionSource.ScopeKind, identity.StoreId.ToString("D"),
            originalRequest.StoreObservation.Revision, ResourceAccess.Read);
        if (admission is null || admission.Actor != originalActor ||
            admission.Submission.Scope.TargetAppId != HomeNotesResourceAdmissionSource.TargetAppId ||
            admission.Submission.Scope.ActionName != HomeNotesResourceAdmissionSource.ReadAction ||
            admission.Submission.Impact.ResourceBinding is not { SchemaVersion: 1 } binding ||
            binding.Scopes.Count != 1 || binding.Scopes[0] != scope ||
            originalCapability.Scopes.Count != 1 || originalCapability.Scopes[0] != scope)
            return Deny();
        try
        {
            var arguments = originalRequest.Arguments;
            if (arguments.ValueKind != JsonValueKind.Object ||
                admission.Submission.Impact.ArgumentsDigest != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText()))))
                return Deny();
            var names = arguments.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(new[] { "documentId", "operationId", "storeId", "storeRevision" }) ||
                arguments.GetProperty("operationId").GetGuid() != originalRequest.OperationId ||
                arguments.GetProperty("storeId").GetGuid() != identity.StoreId ||
                arguments.GetProperty("storeRevision").GetString() != scope.Revision)
                return Deny();
            var document = arguments.GetProperty("documentId");
            if (originalRequest.DocumentId is { } requested ? document.ValueKind == JsonValueKind.Null ||
                document.GetGuid() != requested : document.ValueKind != JsonValueKind.Null)
                return Deny();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return Deny(); }
        return CaptureCoreAsync(broker, store, profiles, ownership, HomeNotesResourceAdmissionSource.StoreKind,
            identity.StoreId.ToString("D"), originalCapability, originalActor, [],
            () => originalSource.IsIssuedOriginalLibraryReadRequest(originalRequest), cancellationToken);
    }
}
