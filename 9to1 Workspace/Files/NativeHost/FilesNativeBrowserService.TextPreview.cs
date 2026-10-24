using System.Security.Cryptography;
using System.Text;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed record FilesOriginalTextPreview(bool Succeeded, string Code, string Message,
    Guid FileId, string Name, string Revision, string Text, long TotalBytes, bool IsTruncated = false);

public sealed partial class FilesNativeBrowserService
{
    public const int MaximumTextPreviewBytes = 1024 * 1024;
    public const int MaximumTextPreviewCharacters = 65536;
    public bool HasOriginalTextPreviewOwner => _originalFolders is not null;

    // Descriptive eligibility only, never an authority check or content-type guarantee.
    public static bool IsTextPreviewCandidate(HostedItemMetadata item) => item.Kind == HostedItemKind.File &&
        (item.ContentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true ||
         item.ContentType is "application/json" or "application/xml" or "application/javascript" ||
         Path.GetExtension(item.Name).ToLowerInvariant() is ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".yaml" or ".yml"
             or ".cs" or ".py" or ".js" or ".ts" or ".css" or ".html" or ".cui" or ".sql" or ".sh" or ".log" or ".toml" or ".ini");

    /// <summary>One bounded read from the SAME privately issued current page/row. The native
    /// borrower retains every source Task; no paths, permission grants or mutable file handles escape.</summary>
    public Task<FilesOriginalTextPreview> ReadOriginalTextPreviewWithinSourceAsync(
        FilesNativeBrowserPage page, HostedItemMetadata row, AuthenticatedResourceActor actor,
        Func<bool> lifetime, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var original = new CloudflareOriginalTaskLedger(); original.BindOriginalOwner(this); original.BindOriginalCallerCallback(scope);
        var source = new FilesOriginalReadSourceScope(body => original.Invoke(() => { body(); return true; }),
            actual => { original.Track(actual); retain(actual); });
        return original.RunToOriginalSettlementAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var retained = source.Invoke(() => _originalPages.TryGetValue(page, out var issued) && issued.Actor == actor &&
                !page.IsTrash && page.Items.Any(item => ReferenceEquals(item, row)) && row.Kind == HostedItemKind.File &&
                row.CurrentRevisionId is not null && lifetime()
                    ? issued : throw new UnauthorizedAccessException("Select the SAME current original Files row for preview."));
            var workspace = await source.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(page.StoreID, source, token)).ConfigureAwait(false);
            if (workspace?.Actor != actor || actor.AccountId is not null || actor.OrganisationId is not null ||
                !ReferenceEquals(workspace.Provider, retained.Workspace.Provider) ||
                !ReferenceEquals(workspace.Directories, retained.Workspace.Directories) ||
                !ReferenceEquals(workspace.Materializations, retained.Workspace.Materializations))
                throw new UnauthorizedAccessException("The original Files preview workspace changed.");
            var owner = _originalFolders?.BorrowOriginalItemReadIssuer()
                ?? throw new NotSupportedException("The original Files text-preview owner is unavailable.");
            var read = await source.Observe(() => owner.CaptureOriginalTextPreviewReadAsync(workspace, row, lifetime, source, token)).ConfigureAwait(false);
            async Task Demand(CancellationToken currentToken)
            {
                currentToken.ThrowIfCancellationRequested();
                if (!source.Invoke(lifetime) || !resources.IsIssuedOriginalReadOwnerBinding(actor, read, "files.preview.read",
                    read.OriginalScope, workspace.Provider, workspace.Directories, page.StoreID) ||
                    await source.Observe(() => resources.AuthorizeOriginalReadForActorAsync(actor, read, "files.preview.read",
                        [read.OriginalScope], currentToken).AsTask()).ConfigureAwait(false) != actor || !source.Invoke(lifetime))
                    throw new UnauthorizedAccessException("The original Home READ no longer permits this Files preview.");
                var evidence = await source.Observe(() => workspace.Provider.GetStoreEvidenceAsync(page.StoreID, source.OriginalSynchronousScope, source.RetainOriginalTask, currentToken)).ConfigureAwait(false);
                if (evidence.StoreId != page.StoreID || evidence.Revision != page.StoreRevision)
                    throw new InvalidOperationException("The displayed Files page changed. Refresh before previewing.");
            }
            await Demand(token).ConfigureAwait(false);
            if (!IsTextPreviewCandidate(row)) return Refused("UnsupportedPreview", "This file has no supported plain-text preview.");
            var content = await source.Observe(() => workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
                page.StoreID, row.Id, row.CurrentRevisionId, token)).ConfigureAwait(false);
            await Demand(token).ConfigureAwait(false);
            if (!content.IsSuccess || content.Value is not { } committed || committed.Revision.ItemId != row.Id ||
                committed.Revision.Id != row.CurrentRevisionId || committed.Revision.SizeBytes != row.SizeBytes ||
                NormalizeTextPreviewHash(committed.Revision.ContentHash) is not { } hash ||
                hash != NormalizeTextPreviewHash(row.ContentHash))
                throw new InvalidDataException("The selected file has no matching immutable Files content revision.");
            if (committed.Revision.SizeBytes is not { } size || size < 0)
                throw new InvalidDataException("The original text preview has no bounded content size.");
            if (size > MaximumTextPreviewBytes)
                return Refused("PreviewTooLarge", "Plain-text preview supports files up to 1 MiB. No file bytes were read.", size);
            if (committed.UploadAnchorFolderId is not { } anchor ||
                committed.ProviderContentReference is not { Length: > 0 } relative ||
                !Guid.TryParse(actor.ProfileId, out var profile))
                return Refused("PreviewUnavailable", "This file has no supported existing local content materialisation.", size);
            var roots = workspace.Configuration.AppFolders.Where(pair => pair.Value == anchor).ToArray();
            if (roots.Length != 1)
                return Refused("PreviewUnavailable", "The original file has no unique registered content anchor.", size);
            var mapping = await source.Observe(() => workspace.Directories.ResolveOriginalRegisteredFolderAsync(profile,
                roots[0].Key, anchor, workspace.Provider, page.StoreID, true,
                ct => new ValueTask(Demand(ct)), token)).ConfigureAwait(false);
            await Demand(token).ConfigureAwait(false);
            if (!mapping.IsSuccess || mapping.Value is not { } binding || binding.FolderId != anchor ||
                binding.LocationId != row.LocationId || binding.ProfileId != profile)
                throw new UnauthorizedAccessException("The original Files content anchor changed.");
            var path = source.Invoke(() => OriginalTextPreviewPath(binding.DirectoryPath, relative));
            FileStream? stream = null;
            byte[] bytes = new byte[checked((int)size)];
            try
            {
                source.Invoke(() => { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan); return true; });
                if (stream!.Length != size) throw new InvalidDataException("The original preview materialisation changed size.");
                await source.ObserveVoid(() => stream.ReadExactlyAsync(bytes, token).AsTask()).ConfigureAwait(false);
                var extra = new byte[1];
                if (await source.Observe(() => stream.ReadAsync(extra, token).AsTask()).ConfigureAwait(false) != 0 ||
                    Convert.ToHexString(SHA256.HashData(bytes)) != hash)
                    throw new InvalidDataException("The preview bytes do not match the immutable Files checksum.");
                await Demand(token).ConfigureAwait(false);
            }
            catch (Exception cause) { original.Retain(cause); throw; }
            finally
            {
                if (stream is not null) await source.ObserveVoid(() => stream.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
            var after = await source.Observe(() => workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
                page.StoreID, row.Id, row.CurrentRevisionId, token)).ConfigureAwait(false);
            if (!after.IsSuccess || after.Value != committed)
                throw new InvalidOperationException("The original Files content changed during preview.");
            var finalMapping = await source.Observe(() => workspace.Directories.ResolveOriginalRegisteredFolderAsync(profile,
                roots[0].Key, anchor, workspace.Provider, page.StoreID, true,
                ct => new ValueTask(Demand(ct)), token)).ConfigureAwait(false);
            if (!finalMapping.IsSuccess || finalMapping.Value != binding ||
                source.Invoke(() => OriginalTextPreviewPath(binding.DirectoryPath, relative)) != path)
                throw new UnauthorizedAccessException("The original preview materialisation changed.");
            await Demand(token).ConfigureAwait(false);
            string text;
            try
            {
                text = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf
                    ? new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3)
                    : new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException) { return Refused("UnsupportedEncoding", "This preview supports UTF-8 text only. Binary and other encodings are not displayed.", size); }
            if (text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                return Refused("UnsupportedBinary", "This file contains binary/control data and cannot be shown as plain text.", size);
            var truncated = text.Length > MaximumTextPreviewCharacters;
            if (truncated)
            {
                var length = MaximumTextPreviewCharacters;
                if (char.IsHighSurrogate(text[length - 1])) length--;
                text = text[..length];
            }
            await Demand(token).ConfigureAwait(false);
            return new FilesOriginalTextPreview(true, "PreviewReady", truncated
                ? "Read-only UTF-8 preview · first 65,536 characters at most; remaining text is not displayed."
                : "Read-only UTF-8 preview · no code or links are executed.", row.Id.Value, row.Name,
                row.CurrentRevisionId!.Value.ToString(), text, size, truncated);
            FilesOriginalTextPreview Refused(string code, string message, long total = 0) =>
                new(false, code, message, row.Id.Value, row.Name, row.CurrentRevisionId!.Value.ToString(), "", total);
        });
    }
    private static string? NormalizeTextPreviewHash(string? value)
    {
        if (value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) value = value[7..];
        return value is { Length: 64 } && value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
    }
    private static string OriginalTextPreviewPath(string directory, string relative)
    {
        if (Path.IsPathFullyQualified(relative) || relative.Contains('\0') ||
            relative.Split(['/', '\\']).Any(part => part is "" or "." or ".."))
            throw new UnauthorizedAccessException("The original Files content reference is invalid.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The original preview reference escapes its registered Files anchor.");
        // Include ancestors above the registered root too: a changed parent must not redirect it.
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("The original preview materialisation redirects outside its Files owner.");
        return full;
    }
}
