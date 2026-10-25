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
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default) =>
        CaptureNotesSaveAsync(broker, store, profiles, ownership, originalSource, originalIntent, originalCapability,
            originalActor, HomeNotesResourceAdmissionSource.TargetAppId, HomeNotesResourceAdmissionSource.SaveAction, cancellationToken);

    internal static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesSaveAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalSaveIntent originalIntent, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, string targetAppId, string actionId,
        CancellationToken cancellationToken) =>
        CaptureNotesSaveCoreAsync(broker, store, profiles, ownership, originalSource, originalIntent, originalCapability, originalActor, targetAppId, actionId, cancellationToken, null, null);

    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesSaveWithinOriginalSourceAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalSaveIntent originalIntent, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        CaptureNotesSaveWithinOriginalSourceAsync(broker, store, profiles, ownership, originalSource, originalIntent,
            originalCapability, originalActor, scope, retain, HomeNotesResourceAdmissionSource.TargetAppId,
            HomeNotesResourceAdmissionSource.SaveAction, cancellationToken);

    internal static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesSaveWithinOriginalSourceAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalSaveIntent originalIntent, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain,
        string targetAppId, string actionId, CancellationToken cancellationToken) =>
        CaptureNotesSaveCoreAsync(broker, store, profiles, ownership, originalSource, originalIntent, originalCapability, originalActor, targetAppId, actionId, cancellationToken, scope, retain);

    private static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesSaveCoreAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalSaveIntent originalIntent, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, string targetAppId, string actionId,
        CancellationToken cancellationToken, Action<Action>? originalScope, Action<Task>? retain)
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
            !IsNotesAction(targetAppId, actionId, write: true) ||
            admission.Submission.Scope.TargetAppId != targetAppId ||
            admission.Submission.Scope.ActionName != actionId ||
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
        return CaptureOriginalNotesCoreAsync(broker, store, profiles, ownership, identity.StoreId.ToString("D"),
            originalCapability, originalActor, () => originalSource.IsIssuedOriginalSaveIntent(originalIntent),
            originalScope, retain, cancellationToken);
    }

    /// <summary>Exact original metadata-only Notes library read. The actual owner must retain
    /// its canonical transaction and physical descriptors while validating and reading. This
    /// never admits raw List/Load recovery, repairs or a Notes mutation.</summary>
    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesLibraryReadAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalLibraryReadRequest originalRequest, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default) =>
        CaptureNotesLibraryReadAsync(broker, store, profiles, ownership, originalSource, originalRequest, originalCapability,
            originalActor, HomeNotesResourceAdmissionSource.TargetAppId, HomeNotesResourceAdmissionSource.ReadAction, cancellationToken);

    internal static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesLibraryReadAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalLibraryReadRequest originalRequest, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, string targetAppId, string actionId, CancellationToken cancellationToken) =>
        CaptureNotesLibraryReadCoreAsync(broker, store, profiles, ownership, originalSource, originalRequest, originalCapability, originalActor, targetAppId, actionId, cancellationToken, null, null);

    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesLibraryReadWithinOriginalSourceAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalLibraryReadRequest originalRequest, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        CaptureNotesLibraryReadWithinOriginalSourceAsync(broker, store, profiles, ownership, originalSource, originalRequest,
            originalCapability, originalActor, scope, retain, HomeNotesResourceAdmissionSource.TargetAppId,
            HomeNotesResourceAdmissionSource.ReadAction, cancellationToken);

    internal static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesLibraryReadWithinOriginalSourceAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalLibraryReadRequest originalRequest, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain,
        string targetAppId, string actionId, CancellationToken cancellationToken) =>
        CaptureNotesLibraryReadCoreAsync(broker, store, profiles, ownership, originalSource, originalRequest, originalCapability, originalActor, targetAppId, actionId, cancellationToken, scope, retain);

    private static ValueTask<HomeClaimedResourceCommitFence?> CaptureNotesLibraryReadCoreAsync(
        HomeResourceOperationBroker broker, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, INotesOriginalStoreSource originalSource,
        INotesOriginalLibraryReadRequest originalRequest, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, string targetAppId, string actionId,
        CancellationToken cancellationToken, Action<Action>? originalScope, Action<Task>? retain)
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
        var validSelection = originalRequest.Kind switch
        {
            NotesOriginalReadKind.LibrarySnapshot => originalRequest.VersionId is null,
            NotesOriginalReadKind.VersionHistory => originalRequest.DocumentId is not null && originalRequest.VersionId is null,
            NotesOriginalReadKind.Version => originalRequest.DocumentId is not null &&
                NotesOriginalVersionIdentity.IsVersionId(originalRequest.VersionId),
            _ => false
        };
        if (!validSelection) return Deny();
        var admission = broker.CaptureClaimedAttestation(originalCapability);
        var scope = new ResourceScope(HomeNotesResourceAdmissionSource.ScopeKind, identity.StoreId.ToString("D"),
            originalRequest.StoreObservation.Revision, ResourceAccess.Read);
        if (admission is null || admission.Actor != originalActor ||
            !IsNotesAction(targetAppId, actionId, write: false) ||
            admission.Submission.Scope.TargetAppId != targetAppId ||
            admission.Submission.Scope.ActionName != actionId ||
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
            // Existing library requests retain their original four-key wire contract. History
            // selectors add exactly two reviewed fields; a digest alone cannot widen the read.
            JsonElement requestArguments = arguments;
            if (targetAppId == HomeNotesResourceAdmissionSource.WriteTargetAppId)
            {
                if (originalRequest.Kind != NotesOriginalReadKind.LibrarySnapshot || originalRequest.VersionId is not null)
                    return Deny();
                if (actionId == HomeNotesResourceAdmissionSource.WriteLibraryReadAction)
                {
                    if (originalRequest.DocumentId is not null || !names.SequenceEqual(new[] { "documentId", "operationId", "storeId", "storeRevision" }))
                        return Deny();
                }
                else
                {
                    if (actionId != HomeNotesResourceAdmissionSource.WriteReadAction || originalRequest.DocumentId is null ||
                        !names.SequenceEqual(new[] { "expectedVersion", "request" }) ||
                        arguments.GetProperty("expectedVersion").GetInt64() < 0 ||
                        !arguments.GetProperty("request").TryGetProperty("documentId", out var selectedId) ||
                        selectedId.GetGuid() != originalRequest.DocumentId.Value)
                        return Deny();
                    requestArguments = arguments.GetProperty("request");
                    var requestNames = requestArguments.EnumerateObject().Select(value => value.Name)
                        .Order(StringComparer.Ordinal).ToArray();
                    if (!requestNames.SequenceEqual(new[] { "documentId", "operationId", "storeId", "storeRevision" }))
                        return Deny();
                }
            }
            else
            {
                var expectedNames = originalRequest.Kind == NotesOriginalReadKind.LibrarySnapshot
                    ? new[] { "documentId", "operationId", "storeId", "storeRevision" }
                    : new[] { "documentId", "kind", "operationId", "storeId", "storeRevision", "versionId" };
                if (!names.SequenceEqual(expectedNames)) return Deny();
            }
            if (requestArguments.GetProperty("operationId").GetGuid() != originalRequest.OperationId ||
                requestArguments.GetProperty("storeId").GetGuid() != identity.StoreId ||
                requestArguments.GetProperty("storeRevision").GetString() != scope.Revision)
                return Deny();
            var document = requestArguments.GetProperty("documentId");
            if (originalRequest.DocumentId is { } requested ? document.ValueKind == JsonValueKind.Null ||
                document.GetGuid() != requested : document.ValueKind != JsonValueKind.Null)
                return Deny();
            if (originalRequest.Kind != NotesOriginalReadKind.LibrarySnapshot)
            {
                var kind = requestArguments.GetProperty("kind");
                var version = requestArguments.GetProperty("versionId");
                if (kind.ValueKind != JsonValueKind.String ||
                    kind.GetString() != (originalRequest.Kind == NotesOriginalReadKind.VersionHistory ? "history" : "saved-version"))
                    return Deny();
                if (originalRequest.Kind == NotesOriginalReadKind.VersionHistory
                    ? version.ValueKind != JsonValueKind.Null
                    : version.ValueKind != JsonValueKind.String || version.GetString() != originalRequest.VersionId)
                    return Deny();
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return Deny(); }
        return CaptureOriginalNotesCoreAsync(broker, store, profiles, ownership, identity.StoreId.ToString("D"),
            originalCapability, originalActor, () => originalSource.IsIssuedOriginalLibraryReadRequest(originalRequest),
            originalScope, retain, cancellationToken);
    }

    private static bool IsNotesAction(string targetAppId, string actionId, bool write) =>
        targetAppId == HomeNotesResourceAdmissionSource.TargetAppId &&
            actionId == (write ? HomeNotesResourceAdmissionSource.SaveAction : HomeNotesResourceAdmissionSource.ReadAction) ||
        targetAppId == HomeNotesResourceAdmissionSource.WriteTargetAppId &&
            (write ? actionId == HomeNotesResourceAdmissionSource.WriteSaveAction :
                actionId is HomeNotesResourceAdmissionSource.WriteLibraryReadAction or HomeNotesResourceAdmissionSource.WriteReadAction);

    private static ValueTask<HomeClaimedResourceCommitFence?> CaptureOriginalNotesCoreAsync(HomeResourceOperationBroker broker,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        string storeId, HomeResourceExecutionCapability capability, AuthenticatedResourceActor actor, Func<bool> originalLifetime,
        Action<Action>? scope, Action<Task>? retain, CancellationToken token)
    {
        if (scope is null) return CaptureCoreAsync(broker, store, profiles, ownership, HomeNotesResourceAdmissionSource.StoreKind,
            storeId, capability, actor, [], originalLifetime, token);
        ArgumentNullException.ThrowIfNull(retain);
        // Reuse the SAME fence's original capture ledger and leak-safe resource retention.
        // Notes has no unrelated configuration records; the exact issuer/request/claim above
        // supplies its narrower original tuple before the ordinary capture core.
        var original = new OriginalFenceCapture(); original.Sources = new(original, scope, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (OriginalCaptureGate)
        {
            OriginalCaptures.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Sources.IsHealthy &&
                (value.Fence is null || value.Fence.OriginalClose?.IsCompletedSuccessfully == true));
            if (OriginalCaptures.Count >= 128) throw new InvalidOperationException("Actual unresolved Home fence captures remain retained.");
            original.Driver = Drive(); OriginalCaptures.Add(original);
        }
        original.Sources.Publish(original.Driver); begin.SetResult(); return new(original.Driver);

        async Task<HomeClaimedResourceCommitFence?> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var active = CloudflareOriginalExecutionGuard.EnterOriginal(original);
            HomeClaimedResourceCommitFence? result = null;
            try
            {
                original.Sources.DemandHealthy();
                result = await original.Sources.Capture(() => CaptureCoreAsync(broker, store, profiles, ownership,
                    HomeNotesResourceAdmissionSource.StoreKind, storeId, capability, actor, [], originalLifetime, token,
                    original.Sources).AsTask(), actual => original.Fence = actual).ConfigureAwait(false);
            }
            catch (Exception cause) { original.Sources.Add(cause); }
            await original.Sources.Settle().ConfigureAwait(false); return result;
        }
    }
}
