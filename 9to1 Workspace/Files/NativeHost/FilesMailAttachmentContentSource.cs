using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Mail;
using HavenOS.Mail.Providers;
using MailDraft = HavenOS.Mail.MailDraft;

namespace HavenOS.Files.NativeHost;

/// <summary>Original Files read observations and canonical materialization proofs only; never a submission grant.</summary>
public sealed class FilesMailAttachmentContentSource : IMailAttachmentContentSource, IDisposable
{
    private sealed class Lifetime(Func<bool> hostCurrent)
    {
        private int _retired;
        public bool Current()
        {
            if (Volatile.Read(ref _retired) != 0) return false;
            bool available;
            try { available = hostCurrent(); }
            catch { available = false; }
            if (!available) Interlocked.Exchange(ref _retired, 1);
            return available && Volatile.Read(ref _retired) == 0;
        }
        public void Retire() => Interlocked.Exchange(ref _retired, 1);
    }
    private sealed record OriginalAttachment(HostedItemId File, IOriginalCanonicalReadContext Read,
        FilesRevisionId Revision, HostedItemMetadata Metadata, FilesMaterializedFile Materialization);
    private readonly NativeFilesWorkspace _workspace;
    private readonly ResourceAuthorizationService _resources;
    private readonly Guid _accountId;
    private readonly Guid _draftId;
    private readonly Lifetime _lifetime;
    private readonly IReadOnlyDictionary<Guid, OriginalAttachment> _attachments;

    private FilesMailAttachmentContentSource(NativeFilesWorkspace workspace, ResourceAuthorizationService resources,
        Guid accountId, Guid draftId, Lifetime lifetime, IReadOnlyDictionary<Guid, OriginalAttachment> attachments)
    { _workspace = workspace; _resources = resources; _accountId = accountId; _draftId = draftId; _lifetime = lifetime; _attachments = attachments; }

    public static Task<FilesMailAttachmentContentSource> CaptureAsync(NativeFilesWorkspace original,
        FilesArtifactResourceResolver registeredOwner, ResourceAuthorizationService resources,
        MailDraft originalDraft, Func<bool> originalLifetime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalDraft);
        return CaptureSelectionAsync(original, registeredOwner, resources, originalDraft.AccountId,
            originalDraft.DraftId, originalDraft.AttachmentIds, originalLifetime, cancellationToken);
    }

    public static Task<FilesMailAttachmentContentSource> CaptureSelectionAsync(NativeFilesWorkspace original,
        FilesArtifactResourceResolver registeredOwner, ResourceAuthorizationService resources,
        Guid originalAccountId, Guid originalDraftId, IReadOnlyList<Guid> originalFileIds,
        Func<bool> originalLifetime, CancellationToken cancellationToken = default)
        => CaptureCoreAsync(original, registeredOwner, resources, originalAccountId, originalDraftId,
            originalFileIds, null, originalLifetime, cancellationToken);

    public static Task<FilesMailAttachmentContentSource> CaptureDisplayedSelectionAsync(NativeFilesWorkspace original,
        FilesArtifactResourceResolver registeredOwner, ResourceAuthorizationService resources,
        Guid originalAccountId, Guid originalDraftId, HostedItemId originalFile, FilesRevisionId originalRevision,
        Func<bool> originalLifetime, CancellationToken cancellationToken = default)
        => CaptureCoreAsync(original, registeredOwner, resources, originalAccountId, originalDraftId,
            [originalFile.Value], new Dictionary<Guid, FilesRevisionId> { [originalFile.Value] = originalRevision }, originalLifetime, cancellationToken);

    private static async Task<FilesMailAttachmentContentSource> CaptureCoreAsync(NativeFilesWorkspace original,
        FilesArtifactResourceResolver registeredOwner, ResourceAuthorizationService resources,
        Guid originalAccountId, Guid originalDraftId, IReadOnlyList<Guid> originalFileIds,
        IReadOnlyDictionary<Guid, FilesRevisionId>? originalRevisions, Func<bool> originalLifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(registeredOwner);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(originalFileIds);
        ArgumentNullException.ThrowIfNull(originalLifetime);
        var ids = originalFileIds.Take(17).ToArray();
        if (originalAccountId == Guid.Empty || originalDraftId == Guid.Empty || ids.Length is 0 or > 16 ||
            ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new UnauthorizedAccessException("Retain a draft with at most 16 distinct canonical Files attachments.");
        var lifetime = new Lifetime(originalLifetime);
        var entries = new Dictionary<Guid, OriginalAttachment>();
        try
        {
            foreach (var id in ids)
            {
                var read = originalRevisions is not null
                    ? await registeredOwner.CaptureOriginalMailAttachmentReadForRevisionAsync(original, new(id), originalRevisions[id], lifetime.Current, cancellationToken).ConfigureAwait(false)
                    : await registeredOwner.CaptureOriginalMailAttachmentReadAsync(original, new(id), lifetime.Current, cancellationToken).ConfigureAwait(false);
                await RequireAsync(original, resources, read, lifetime, cancellationToken).ConfigureAwait(false);
                var metadata = await original.Provider.GetForOriginalStoreAsync(original.Configuration.StoreId, new(id), cancellationToken).ConfigureAwait(false);
                await RequireAsync(original, resources, read, lifetime, cancellationToken).ConfigureAwait(false);
                if (!metadata.IsSuccess || metadata.Value!.CurrentRevisionId is not { } revision || revision.ToString() != read.OriginalScope.Revision)
                    throw new UnauthorizedAccessException("The original attachment revision changed.");
                var materialization = await original.Materializations.GetExistingByItemIdAsync(new(id), cancellationToken).ConfigureAwait(false);
                await RequireAsync(original, resources, read, lifetime, cancellationToken).ConfigureAwait(false);
                ValidateMaterialization(metadata.Value, materialization, revision);
                entries.Add(id, new(new(id), read, revision, metadata.Value, materialization!));
            }
            return new(original, resources, originalAccountId, originalDraftId, lifetime, entries);
        }
        catch { lifetime.Retire(); throw; }
    }

    public async ValueTask<MailAttachmentContent> ReadAsync(Guid accountId, Guid draftId, Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        if (accountId != _accountId || draftId != _draftId || !_attachments.TryGetValue(attachmentId, out var entry))
            throw new UnauthorizedAccessException("Retain the original draft attachment selection.");
        await RequireAsync(_workspace, _resources, entry.Read, _lifetime, cancellationToken).ConfigureAwait(false);
        var metadata = await _workspace.Provider.GetForOriginalStoreAsync(_workspace.Configuration.StoreId, entry.File, cancellationToken).ConfigureAwait(false);
        await RequireAsync(_workspace, _resources, entry.Read, _lifetime, cancellationToken).ConfigureAwait(false);
        if (!metadata.IsSuccess || metadata.Value != entry.Metadata) throw new UnauthorizedAccessException("The original attachment metadata changed.");
        var mapping = await _workspace.Materializations.GetExistingByItemIdAsync(entry.File, cancellationToken).ConfigureAwait(false);
        await RequireAsync(_workspace, _resources, entry.Read, _lifetime, cancellationToken).ConfigureAwait(false);
        if (mapping != entry.Materialization) throw new UnauthorizedAccessException("The original attachment materialization changed.");
        ValidateMaterialization(entry.Metadata, mapping, entry.Revision);
        byte[] bytes;
        await using (var stream = new FileStream(mapping!.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length != mapping.SizeBytes) throw new InvalidDataException("Attachment content size changed.");
            bytes = new byte[checked((int)mapping.SizeBytes)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            await RequireAsync(_workspace, _resources, entry.Read, _lifetime, cancellationToken).ConfigureAwait(false);
            if (stream.ReadByte() != -1 || "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != mapping.ContentHash!.ToLowerInvariant())
                throw new InvalidDataException("Attachment bytes do not match the committed Files content hash.");
        }
        var after = await _workspace.Materializations.GetExistingByItemIdAsync(entry.File, cancellationToken).ConfigureAwait(false);
        await RequireAsync(_workspace, _resources, entry.Read, _lifetime, cancellationToken).ConfigureAwait(false);
        if (after != mapping) throw new UnauthorizedAccessException("The attachment materialization changed while reading.");
        return new(attachmentId, entry.Metadata.Name, entry.Metadata.ContentType ?? "application/octet-stream", bytes);
    }

    private static async Task RequireAsync(NativeFilesWorkspace workspace, ResourceAuthorizationService resources,
        IOriginalCanonicalReadContext read, Lifetime lifetime, CancellationToken token)
    {
        if (!lifetime.Current() || !resources.IsIssuedOriginalReadOwnerBinding(workspace.Actor, read, "mail.attachment.read", read.OriginalScope,
                workspace.Provider, workspace.Directories, workspace.Configuration.StoreId) ||
            await resources.AuthorizeOriginalReadForActorAsync(workspace.Actor, read, "mail.attachment.read", [read.OriginalScope], token).ConfigureAwait(false) != workspace.Actor ||
            !lifetime.Current()) throw new UnauthorizedAccessException("The original attachment read is unavailable.");
    }

    private static void ValidateMaterialization(HostedItemMetadata metadata, FilesMaterializedFile? mapping, FilesRevisionId revision)
    {
        if (metadata.Kind != HostedItemKind.File || mapping is null || mapping.ItemId != metadata.Id ||
            mapping.CurrentRemoteRevisionId != revision || mapping.BaseRemoteRevisionId != revision || mapping.LocalRevisionId is not null ||
            mapping.State is not (SyncAvailability.AvailableOffline or SyncAvailability.AlwaysAvailable) ||
            mapping.SizeBytes is < 0 or > 32L * 1024 * 1024 || metadata.SizeBytes != mapping.SizeBytes ||
            string.IsNullOrWhiteSpace(metadata.ContentHash) || !string.Equals(metadata.ContentHash, mapping.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("A validated original offline Files materialization is required.");
    }
    public void Dispose() => _lifetime.Retire();
}
