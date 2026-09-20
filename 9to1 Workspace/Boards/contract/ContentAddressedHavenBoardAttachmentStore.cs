using System.Security.Cryptography;

namespace CakeOS.Apps.Boards.Contract;

public interface IHavenBoardAttachmentStore
{
    Task<HavenBoardAttachment> ImportAsync(
        string boardId,
        string displayName,
        Stream content,
        string? attachmentId = null,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string boardId,
        HavenBoardAttachment attachment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Local-only content-addressed attachment storage.
///
/// Display names and caller paths are never used as filesystem paths. Blob names are SHA-256
/// digests calculated while streaming to an application-owned board directory. Deletion is
/// intentionally omitted until a reference index exists, because content blobs may be deduplicated.
/// </summary>
public sealed class ContentAddressedHavenBoardAttachmentStore : IHavenBoardAttachmentStore
{
    public const long DefaultMaxAttachmentBytes = 100L * 1024 * 1024;
    private const string ReferencePrefix = "sha256:";

    private readonly string _rootDirectory;
    private readonly long _maxAttachmentBytes;

    public ContentAddressedHavenBoardAttachmentStore(
        string rootDirectory,
        long maxAttachmentBytes = DefaultMaxAttachmentBytes)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("An attachment storage directory is required.", nameof(rootDirectory));
        if (maxAttachmentBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAttachmentBytes));

        _rootDirectory = Path.GetFullPath(rootDirectory);
        _maxAttachmentBytes = maxAttachmentBytes;
    }

    public async Task<HavenBoardAttachment> ImportAsync(
        string boardId,
        string displayName,
        Stream content,
        string? attachmentId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSafeId(boardId, "Board ID");
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
            throw new ArgumentException("Attachment content must be readable.", nameof(content));

        attachmentId = string.IsNullOrWhiteSpace(attachmentId)
            ? "att-" + Guid.NewGuid().ToString("N")
            : attachmentId;
        ValidateSafeId(attachmentId, "Attachment ID");

        var safeDisplayName = NormaliseDisplayName(displayName);
        var boardDirectory = EnsureOwnedBoardDirectory(boardId);
        var tempPath = Path.Combine(boardDirectory, ".import-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? cleanupPath = tempPath;

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long totalBytes = 0;
            var buffer = new byte[64 * 1024];

            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: buffer.Length,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                        break;

                    totalBytes = checked(totalBytes + read);
                    if (totalBytes > _maxAttachmentBytes)
                        throw new InvalidDataException($"Attachment exceeds the {_maxAttachmentBytes}-byte local limit.");

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            RestrictUnixPermissions(tempPath);
            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var finalPath = Path.Combine(boardDirectory, digest + ".blob");

            if (File.Exists(finalPath))
            {
                RejectLinkOrReparsePoint(finalPath);
                if (!await FileMatchesDigestAsync(finalPath, digest, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("Existing attachment blob failed its content-address integrity check.");

                File.Delete(tempPath);
                cleanupPath = null;
            }
            else
            {
                File.Move(tempPath, finalPath);
                cleanupPath = null;
                RestrictUnixPermissions(finalPath);
            }

            return new HavenBoardAttachment(
                Id: attachmentId,
                DisplayName: safeDisplayName,
                LocalReference: ReferencePrefix + digest,
                Availability: HavenBoardAttachmentAvailability.Available);
        }
        finally
        {
            if (cleanupPath is not null)
            {
                try
                {
                    if (File.Exists(cleanupPath))
                        File.Delete(cleanupPath);
                }
                catch (IOException)
                {
                    // Preserve the import failure. Stale .import-* files are never considered blobs.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the import failure. Stale .import-* files are never considered blobs.
                }
            }
        }
    }

    public Task<Stream?> OpenReadAsync(
        string boardId,
        HavenBoardAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSafeId(boardId, "Board ID");
        ArgumentNullException.ThrowIfNull(attachment);
        ValidateSafeId(attachment.Id, "Attachment ID");

        var digest = ParseReference(attachment.LocalReference);
        var boardDirectory = BoardDirectory(boardId);
        if (!Directory.Exists(boardDirectory))
            return Task.FromResult<Stream?>(null);

        RejectLinkOrReparsePoint(boardDirectory);
        var path = Path.Combine(boardDirectory, digest + ".blob");
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        RejectLinkOrReparsePoint(path);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    private string EnsureOwnedBoardDirectory(string boardId)
    {
        Directory.CreateDirectory(_rootDirectory);
        RejectLinkOrReparsePoint(_rootDirectory);

        var boardDirectory = BoardDirectory(boardId);
        Directory.CreateDirectory(boardDirectory);
        RejectLinkOrReparsePoint(boardDirectory);
        return boardDirectory;
    }

    private string BoardDirectory(string boardId) => Path.Combine(_rootDirectory, boardId);

    private static string NormaliseDisplayName(string? displayName)
    {
        var name = Path.GetFileName((displayName ?? string.Empty).Trim());
        return string.IsNullOrWhiteSpace(name) ? "attachment" : name;
    }

    private static string ParseReference(string localReference)
    {
        if (string.IsNullOrWhiteSpace(localReference) ||
            !localReference.StartsWith(ReferencePrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Attachment local reference must be a SHA-256 content reference.");

        var digest = localReference[ReferencePrefix.Length..];
        if (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Attachment SHA-256 reference is malformed.");

        return digest.ToLowerInvariant();
    }

    private static void ValidateSafeId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException($"{label} must contain 1 to 128 safe characters.");
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException($"{label} may contain only ASCII letters, digits, '-' and '_'.");
    }

    private static void RejectLinkOrReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new IOException($"Attachment storage path must not be a symbolic link or reparse point: {path}");
    }

    private static async Task<bool> FileMatchesDigestAsync(
        string path,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            hash.AppendData(buffer, 0, read);
        }

        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return string.Equals(digest, expectedDigest, StringComparison.Ordinal);
    }

    private static void RestrictUnixPermissions(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
