using System.Security.Cryptography;

namespace HavenOS.Files;

public sealed partial class FilesMaterializationRegistry
{
    /// <summary>Trusted original Dev import create-only registration. The owning native producer
    /// must supply its SAME retained, readable/seekable materialized handle and positively bind it
    /// to localPath before entry and at its final host fence. This method never opens a string path,
    /// follows a filesystem link, disposes the borrowed handle, or establishes that path identity.
    /// The actual Home entry stays held through this returned original and handle cleanup.</summary>
    public Task<FilesMaterializationProof> RegisterOriginalDeveloperMaterializationAsync(
        string localPath, FileStream originalMaterializedContent, FilesMaterializationProof proof,
        SyncAvailability availability, FilesCommitAuthorityGuard originalAuthority, CancellationToken cancellationToken)
        => RegisterOriginalDeveloperMaterializationCoreAsync(localPath, originalMaterializedContent, proof,
            availability, originalAuthority, null, null, cancellationToken);

    public Task<FilesMaterializationProof> RegisterOriginalDeveloperMaterializationAsync(
        string localPath, FileStream originalMaterializedContent, FilesMaterializationProof proof,
        SyncAvailability availability, FilesCommitAuthorityGuard originalAuthority,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        return RegisterOriginalDeveloperMaterializationCoreAsync(localPath, originalMaterializedContent, proof,
            availability, originalAuthority, originalSynchronousScope, retainOriginalTask, cancellationToken);
    }

    private async Task<FilesMaterializationProof> RegisterOriginalDeveloperMaterializationCoreAsync(
        string localPath, FileStream originalMaterializedContent, FilesMaterializationProof proof,
        SyncAvailability availability, FilesCommitAuthorityGuard originalAuthority,
        Action<Action>? originalSynchronousScope, Action<Task>? retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalMaterializedContent);
        ArgumentNullException.ThrowIfNull(proof); ArgumentNullException.ThrowIfNull(originalAuthority);
        if (originalMaterializedContent.GetType() != typeof(FileStream) ||
            !originalMaterializedContent.CanRead || !originalMaterializedContent.CanSeek ||
            proof.ItemId.Value == Guid.Empty || proof.RemoteRevisionId.Value == Guid.Empty || proof.SizeBytes < 0 ||
            availability is not (SyncAvailability.AvailableOffline or SyncAvailability.AlwaysAvailable))
            throw new InvalidDataException("Retain the original supported handle and once-created item/revision proof.");
        var path = ValidateLocalPath(localPath); var key = PathKey(path);
        if (originalMaterializedContent.Length != proof.SizeBytes)
            throw new InvalidDataException("The original materialized handle size differs from the captured source.");
        originalMaterializedContent.Position = 0;
        Task<byte[]> hashTask;
        try
        {
            Task<byte[]> BeginOriginalHash() => SHA256.HashDataAsync(originalMaterializedContent, cancellationToken).AsTask();
            hashTask = originalSynchronousScope is null ? BeginOriginalHash() :
                FilesOriginalDeveloperTaskSource.ObserveAsync(BeginOriginalHash, originalSynchronousScope, retainOriginalTask!);
        }
        catch (OperationCanceledException original) { throw new AggregateException("Direct materialized-handle callback returned no canceled original hash Task.", original); }
        byte[] hash;
        try { hash = await hashTask.ConfigureAwait(false); }
        catch when (hashTask.IsFaulted) { throw hashTask.Exception!; }
        if (!string.Equals("sha256:" + Convert.ToHexString(hash).ToLowerInvariant(), proof.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original materialized handle hash differs from the captured source.");
        originalMaterializedContent.Position = 0;
        Task<FilesMaterializationRegistryState> BeginOriginalUpdate() => _store.UpdateAsync(state =>
        {
            if (state.ItemsByPath.ContainsKey(key) || state.ItemsByPath.Values.Any(value => value.ItemId == proof.ItemId))
                throw new IOException("The original setup item/path already has a mapping; inspect its retained outcome without repeating registration.");
            var registered = new FilesMaterializedFile(path, proof.ItemId, proof.RemoteRevisionId, proof.RemoteRevisionId,
                null, proof.ContentHash, proof.SizeBytes, availability, proof.VerifiedAt, availability == SyncAvailability.AlwaysAvailable);
            return new FilesMaterializationRegistryState
            { ItemsByPath = new(state.ItemsByPath, StringComparer.Ordinal) { [key] = registered } };
        }, originalAuthority.ValidateAsync, cancellationToken);
        var actual = originalSynchronousScope is null ? BeginOriginalUpdate() :
            FilesOriginalDeveloperTaskSource.ObserveAsync(BeginOriginalUpdate, originalSynchronousScope, retainOriginalTask!);
        try { await actual.ConfigureAwait(false); }
        catch when (actual.IsFaulted) { throw actual.Exception!; }
        return proof;
    }
}
