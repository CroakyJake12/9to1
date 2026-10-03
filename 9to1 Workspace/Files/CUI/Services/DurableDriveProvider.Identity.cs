using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Files;

public sealed record FilesStoreEvidence(int SchemaVersion, Guid StoreId, DateTimeOffset CreatedAtUtc,
    bool NewlyCreated, bool IsEmpty, string Revision, string OwnerPrincipalId, FilesLocationId LocationId);

public sealed partial class DurableDriveProvider
{
    /// <summary>Identity is persisted in the actual owning state, never derived from a path.
    /// Legacy identity creation is not evidence of a newly created store. Snapshot and UUID share the state lock.</summary>
    public async Task<FilesStoreEvidence> GetStoreEvidenceAsync(CancellationToken cancellationToken = default)
    {
        FilesStoreEvidence? evidence = null;
        await _store.UpdateAsync(state =>
        {
            if (state.Items is null || state.Operations is null || state.Events is null || state.Artifacts is null ||
                state.Revisions is null || state.RevisionContentReferences is null || state.UploadedContents is null ||
                state.Items.Any(item => item is null || item.Metadata is null))
                throw new InvalidDataException("Files state collections are incomplete; preserve the state for recovery.");
            if (state.StoreOwnerPrincipalId is { } storedOwner && storedOwner != _owner ||
                state.StoreLocationId is { } storedLocation && storedLocation != Location.Id ||
                state.Items.Any(item => item.Metadata.OwnerPrincipalId != _owner || item.Metadata.LocationId != Location.Id))
                throw new UnauthorizedAccessException("The configured Files state belongs to another principal or location.");
            if (state.StoreId is null || state.StoreId == Guid.Empty)
                state = state with { StoreId = Guid.NewGuid(), StoreCreatedAtUtc = DateTimeOffset.UtcNow, CreationSessionId = null };
            state = state with { StoreOwnerPrincipalId = _owner, StoreLocationId = Location.Id };
            var createdAt = state.StoreCreatedAtUtc ?? throw new InvalidDataException("Files store creation metadata is incomplete.");
            var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state)));
            evidence = new(1, state.StoreId.Value, createdAt, state.CreationSessionId == _creationSessionId,
                state.Items.Count == 0 && state.Operations.Count == 0 && state.Events.Count == 0 &&
                state.Artifacts.Count == 0 && state.Revisions.Count == 0 && state.RevisionContentReferences.Count == 0 && state.UploadedContents.Count == 0,
                revision, _owner, Location.Id);
            return state;
        }, cancellationToken);
        return evidence!;
    }
    /// <summary>Reads an existing original store without adoption, migration or serialization. Missing/mismatched
    /// UUID, principal or location is refused under the existing persistent read lease.</summary>
    public async Task<FilesStoreEvidence> GetStoreEvidenceAsync(Guid expectedStoreId,
        CancellationToken cancellationToken = default)
    {
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Select the original Files store identity.", nameof(expectedStoreId));
        var state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false);
        if (state.StoreId != expectedStoreId || state.StoreOwnerPrincipalId != _owner ||
            state.StoreLocationId != Location.Id)
            throw new UnauthorizedAccessException("The existing Files store differs from the original selection.");
        if (state.StoreCreatedAtUtc is not { } createdAt || state.Items is null || state.Operations is null ||
            state.Events is null || state.Artifacts is null || state.Revisions is null ||
            state.RevisionContentReferences is null || state.UploadedContents is null ||
            state.Items.Any(item => item is null || item.Metadata is null))
            throw new InvalidDataException("Existing Files store metadata is incomplete; preserve it for recovery.");
        if (state.Items.Any(item => item.Metadata.OwnerPrincipalId != _owner || item.Metadata.LocationId != Location.Id))
            throw new UnauthorizedAccessException("Existing Files items belong to another principal or location.");
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state)));
        return new(1, expectedStoreId, createdAt, state.CreationSessionId == _creationSessionId,
            state.Items.Count == 0 && state.Operations.Count == 0 && state.Events.Count == 0 &&
            state.Artifacts.Count == 0 && state.Revisions.Count == 0 && state.RevisionContentReferences.Count == 0 && state.UploadedContents.Count == 0,
            revision, _owner, Location.Id);
    }

}
