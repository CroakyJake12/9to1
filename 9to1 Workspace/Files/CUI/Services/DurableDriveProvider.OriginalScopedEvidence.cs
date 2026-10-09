using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Files;

public sealed partial class DurableDriveProvider
{
    public async Task<FilesStoreEvidence> GetStoreEvidenceAsync(Guid expectedStoreId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        void Scope(Action callback) => FilesOriginalSourceCallbackScope.Invoke(callback, originalSynchronousScope);
        if (expectedStoreId == Guid.Empty) throw new ArgumentException("Select the original Files store identity.", nameof(expectedStoreId));
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(
            () => _store.ReadExistingAsync(cancellationToken), Scope, retainOriginalTask).ConfigureAwait(false);
        FilesStoreEvidence? observed = null;
        FilesOriginalDeveloperTaskSource.Invoke(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            observed = new(1, expectedStoreId, createdAt, state.CreationSessionId == _creationSessionId,
                state.Items.Count == 0 && state.Operations.Count == 0 && state.Events.Count == 0 &&
                state.Artifacts.Count == 0 && state.Revisions.Count == 0 && state.RevisionContentReferences.Count == 0 && state.UploadedContents.Count == 0,
                revision, _owner, Location.Id);
        }, Scope);
        return observed ?? throw new InvalidOperationException("No actual original Files evidence was observed.");
    }
}
