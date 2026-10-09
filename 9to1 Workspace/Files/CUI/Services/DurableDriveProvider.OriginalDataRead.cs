using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Files;

public sealed record FilesOriginalDataMetadata(HostedItemMetadata Row, FilesStoreEvidence OriginalStore);

public sealed partial class DurableDriveProvider
{
    /// <summary>The selected row and durable store identity are observed in the
    /// SAME existing snapshot. This performs no adoption, seed or content read.</summary>
    public async Task<FilesResult<FilesOriginalDataMetadata>> GetOriginalDataMetadataWithinSourceAsync(
        Guid expectedStoreId, HostedItemId fileId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var state = await FilesOriginalDeveloperTaskSource.ObserveAsync(
            () => _store.ReadExistingWithinOriginalSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        FilesResult<FilesOriginalDataMetadata>? result = null;
        FilesOriginalDeveloperTaskSource.Invoke(() =>
        {
            token.ThrowIfCancellationRequested(); RequireOriginalReadStore(state, expectedStoreId);
            if (state.StoreCreatedAtUtc is not { } created || state.Operations is null || state.Events is null ||
                state.Items.Any(item => item is null || item.Metadata is null))
                throw new InvalidDataException("The existing Files identity is incomplete; preserve it for recovery.");
            if (state.Items.Any(item => item.Metadata.OwnerPrincipalId != _owner || item.Metadata.LocationId != Location.Id))
                throw new UnauthorizedAccessException("Existing Files items belong to another principal or location.");
            var item = state.Items.SingleOrDefault(entry => entry.Metadata.Id == fileId);
            if (item is null || !IsVisible(state, item))
            {
                result = Fail<FilesOriginalDataMetadata>(FilesErrorCode.ItemNotFound, "The selected Data file is unavailable.", "DataRead", fileId);
                return;
            }
            RequireOriginalReadItem(item.Metadata);
            var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state)));
            var evidence = new FilesStoreEvidence(1, expectedStoreId, created,
                state.CreationSessionId == _creationSessionId,
                state.Items.Count == 0 && state.Operations.Count == 0 && state.Events.Count == 0 && state.Artifacts.Count == 0 &&
                    state.Revisions.Count == 0 && state.RevisionContentReferences.Count == 0 && state.UploadedContents.Count == 0,
                revision, _owner, Location.Id);
            result = FilesResult<FilesOriginalDataMetadata>.Success(new(item.Metadata, evidence));
        }, scope);
        return result ?? throw new InvalidOperationException("The original Data metadata source produced no result.");
    }

    // The configured owner may close this shared store cohort only after its
    // borrowers retire. A Data selection closes only its own service receipts.
    public Task? OriginalDataReadsClose => _store.OriginalReadsClose;
    public void ThrowIfOriginalDataReadJoinWouldCycle() => _store.ThrowIfOriginalReadJoinWouldCycle();
    public Task CloseOriginalDataReadsAndDrainAsync() => _store.CloseOriginalReadsAndDrainAsync();
}
