using System.Security.Cryptography;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Files;

namespace HavenOS.Files.NativeHost;

/// <summary>Files-owned package bytes. A verified read lease never grants installation authority.</summary>
public sealed class FilesCompatibilityPackageContentSource(NativeFilesWorkspaceAuthority workspaces,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources) : ICompatibilityPackageContentSource
{
    private const string Action = "os.compatibility.package.read";

    public async ValueTask<ICompatibilityPackageContentLease> ReadAsync(Guid fileId, string expectedContentRevision,
        long maximumBytes, CancellationToken token)
    {
        if (fileId == Guid.Empty || !Guid.TryParse(expectedContentRevision, out var expected) || expected == Guid.Empty || maximumBytes < 1)
            throw new ArgumentException("Select a canonical package and a bounded immutable content revision.");
        var workspace = await workspaces.GetCurrentAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Set up and verify Files storage in Home before opening this package.");
        var actor = workspace.Actor;
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The package profile changed.");
        var id = new HostedItemId(fileId);
        var metadata = await workspace.Provider.GetAsync(id, token).ConfigureAwait(false);
        if (!metadata.IsSuccess || metadata.Value is not { Kind: HostedItemKind.File, CurrentRevisionId: { } cas } item)
            throw new InvalidOperationException("The canonical package file is unavailable.");
        var content = await workspace.Provider.GetCurrentArtifactContentAsync(id, token).ConfigureAwait(false);
        if (!content.IsSuccess || content.Value!.Revision.Id.Value != expected ||
            content.Value.Revision.SizeBytes is not { } size || size < 1 || size > maximumBytes ||
            NormalizeHash(content.Value.Revision.ContentHash) is not { } hash ||
            content.Value.UploadAnchorFolderId is not { } anchor ||
            content.Value.ProviderContentReference is not { } relative || Path.IsPathFullyQualified(relative) ||
            relative.Split(['/', '\\']).Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("The package has no bounded verified immutable Files revision.");
        var captured = content.Value;
        var scope = new ResourceScope("files.item", id.ToString(), cas.ToString(), ResourceAccess.Read);
        await RequireCurrentAsync(token).ConfigureAwait(false);
        var root = await workspace.Directories.ResolveFolderAsync(actor.AccountId ?? Guid.Empty,
            actor.AccountId is null && Guid.TryParse(actor.ProfileId, out var profile) ? profile : null,
            anchor, token).ConfigureAwait(false);
        if (!root.IsSuccess || root.Value!.FolderId != anchor)
            throw new UnauthorizedAccessException("The package source has no current registered Files folder.");
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Value.DirectoryPath));
        var path = Path.GetFullPath(Path.Combine(directory, relative));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The package source escapes its registered Files folder.");
        RequireNoRedirects(path, directory);
        string? leaseDirectory = Path.Combine(directory, ".9to1-package-leases", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(leaseDirectory);
            RequireNoRedirects(leaseDirectory, directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(leaseDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var leasePath = Path.Combine(leaseDirectory, "source.bin");
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            await using (var output = new FileStream(leasePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                if (input.Length != size) throw new InvalidDataException("Package bytes differ from their canonical length.");
                var buffer = new byte[65536];
                long copied = 0;
                while (copied < size)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, size - copied)), token).ConfigureAwait(false);
                    if (count == 0) throw new InvalidDataException("The package materialisation ended before its canonical length.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    copied += count;
                }
                if (await input.ReadAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false) != 0)
                    throw new InvalidDataException("The package materialisation exceeds its canonical length.");
                await output.FlushAsync(token).ConfigureAwait(false); output.Position = 0;
                if (Convert.ToHexString(await SHA256.HashDataAsync(output, token).ConfigureAwait(false)) != hash)
                    throw new InvalidDataException("Package bytes differ from their canonical Files checksum.");
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(leasePath, UnixFileMode.UserRead);
            await RequireCurrentAsync(token).ConfigureAwait(false);
            var source = new CompatibilityPackageSource(fileId, expectedContentRevision, cas.ToString(), item.Name, size, hash, actor);
            var lease = new Lease(source, leasePath, leaseDirectory, RequireCurrentAsync);
            leaseDirectory = null;
            return lease;
        }
        finally { if (leaseDirectory is not null && Directory.Exists(leaseDirectory)) Directory.Delete(leaseDirectory, true); }

        async Task RequireCurrentAsync(CancellationToken cancellationToken)
        {
            var currentWorkspace = await workspaces.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (currentWorkspace?.Actor != actor || currentWorkspace.Configuration.StoreId != workspace.Configuration.StoreId ||
                await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor ||
                await resources.AuthorizeAsync(Action, [scope], cancellationToken).ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("Current Home ownership no longer permits this package source.");
            var current = await currentWorkspace.Provider.GetAsync(id, cancellationToken).ConfigureAwait(false);
            var currentContent = await currentWorkspace.Provider.GetCurrentArtifactContentAsync(id, cancellationToken).ConfigureAwait(false);
            if (!current.IsSuccess || current.Value != item || !currentContent.IsSuccess || currentContent.Value != captured)
                throw new InvalidOperationException("The canonical package changed. Inspect its current revision again.");
            if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor ||
                await resources.AuthorizeAsync(Action, [scope], cancellationToken).ConfigureAwait(false) != actor)
                throw new UnauthorizedAccessException("Package source access changed during revalidation.");
        }
    }

    private static void RequireNoRedirects(string path, string root)
    {
        for (var current = path; ; current = Path.GetDirectoryName(current)
            ?? throw new UnauthorizedAccessException("The Files materialisation escaped its registered folder."))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("The Files materialisation redirects outside its registered folder.");
            if (current == root) return;
        }
    }

    private static string? NormalizeHash(string? value)
    {
        if (value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) value = value[7..];
        return value is { Length: 64 } && value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
    }

    private sealed class Lease(CompatibilityPackageSource source, string path, string directory,
        Func<CancellationToken, Task> current) : ICompatibilityPackageContentLease
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<Stream> _streams = [];
        private bool _disposed;
        private bool _cleanupComplete;
        public CompatibilityPackageSource Source => source;
        public async ValueTask<Stream> OpenReadAsync(CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                await current(token).ConfigureAwait(false);
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                try
                {
                    if (stream.Length != source.Length || Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != source.Sha256)
                        throw new InvalidDataException("The Files operation lease changed.");
                    stream.Position = 0;
                    await current(token).ConfigureAwait(false);
                    _streams.Add(stream);
                    return stream;
                }
                catch { await stream.DisposeAsync().ConfigureAwait(false); throw; }
            }
            finally { _gate.Release(); }
        }
        public async ValueTask RevalidateAsync(CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try { ObjectDisposedException.ThrowIf(_disposed, this); await current(token).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed && _cleanupComplete) return;
                _disposed = true;
                foreach (var stream in _streams) await stream.DisposeAsync().ConfigureAwait(false);
                _streams.Clear();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                _cleanupComplete = true; // a failed cleanup remains retryable while reads stay denied
            }
            finally { _gate.Release(); }
        }
    }
}
