using System.Security.Cryptography;
using HavenOS.Home.Apps;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>
/// Physical Linux package staging/publication for an authenticated installer owner. This
/// object authenticates neither publishers nor callers and is deliberately not registered
/// as a Home root, installed-peer verifier or package database. The owning root operation
/// must verify its enrolled publisher policy, dependencies, approval and canonical journal
/// before calling Publish. No installer command or machine path is assumed here.
/// </summary>
/// <remarks>
/// Uses an existing private effective-UID-owned directory and the maintained openat2/statx
/// custody. flock coordinates participating publishers; it cannot exclude an external
/// same-UID writer. Name/inode/version checks refuse observed interference. A publication
/// is one renameat2: an update exchanges directories and retains the previous package at
/// the original stage name. Recovery/journal settlement belongs to Home's existing owner.
/// </remarks>
public sealed partial class LinuxHomePackageArtifactStore : IDisposable
{
    private readonly string _path;
    private readonly uint _uid;
    private readonly LinuxOriginalDownloadRoot _root;
    private readonly SafeFileHandle _directory;
    private readonly object _gate = new();
    private bool _closed;

    public LinuxHomePackageArtifactStore(string existingPrivatePackageDirectory)
    {
        _path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(existingPrivatePackageDirectory));
        _uid = LinuxOriginalDownloadPaths.CurrentUid();
        _root = LinuxOriginalDownloadPaths.RetainRoot(_path, requirePrivate: true);
        try { _directory = _root.OpenDirectory(_path, mutable: true); }
        catch { _root.Dispose(); throw; }
    }

    /// <summary>Physical expected-name observation, never an installation/launch grant.</summary>
    public sealed class ActiveDirectory
    {
        internal readonly LinuxHomePackageArtifactStore Owner;
        internal readonly LinuxOriginalDownloadIdentity Identity;
        public string Path { get; }
        internal ActiveDirectory(LinuxHomePackageArtifactStore owner, LinuxOriginalDownloadIdentity identity)
        { Owner = owner; Identity = identity; Path = System.IO.Path.Combine(owner._path, "active"); }
    }

    /// <summary>Same-owner retained stage and complete file cohort. Dispose after publication
    /// or refusal. Disposal closes descriptors and never deletes uncertain/recovery artifacts.</summary>
    public sealed class ValidatedStage : IDisposable
    {
        internal readonly LinuxHomePackageArtifactStore Owner;
        internal readonly string Leaf;
        internal readonly SafeFileHandle Directory;
        internal readonly LinuxOriginalDownloadIdentity Identity;
        internal readonly HomePackageActivatedFile[] Files;
        internal readonly string Entrypoint;
        internal readonly Dictionary<string, LinuxOriginalDownloadIdentity> FileIdentities;
        internal bool Closed, Published;
        public string StagePath => System.IO.Path.Combine(Owner._path, Leaf);
        internal ValidatedStage(LinuxHomePackageArtifactStore owner, string leaf, SafeFileHandle directory,
            LinuxOriginalDownloadIdentity identity, HomePackageActivatedFile[] files, string entrypoint,
            Dictionary<string, LinuxOriginalDownloadIdentity> identities)
        { Owner = owner; Leaf = leaf; Directory = directory; Identity = identity; Files = files;
          Entrypoint = entrypoint; FileIdentities = identities; }
        public void Dispose() { lock (Owner._gate) { if (Closed) return; Closed = true; Directory.Dispose(); } }
    }

    public sealed record Publication(string ActivePath, string? RetainedPreviousPath);

    /// <summary>Failure after the namespace changed must be retained by the original root
    /// journal as an uncertain commit; retrying as if nothing happened is unsafe.</summary>
    public sealed class PublicationUncertainException : IOException
    {
        public Publication ObservedPublication { get; }
        internal PublicationUncertainException(Publication publication, Exception cause)
            : base("The package directory changed but publication durability/currentness could not be confirmed; retain the original operation for recovery.", cause)
        { ObservedPublication = publication; }
    }

    public ActiveDirectory? ObserveActive()
    {
        lock (_gate)
        {
            DemandCurrent();
            using var publicationLock = LockDirectory(_directory);
            return ObserveActiveCore();
        }
    }

    /// <summary>Validate actual extracted bytes, including receipt/payload files supplied
    /// in the complete cohort by the root's signed-manifest verifier. Extra entries, links,
    /// writable foreign files and a non-executable entrypoint are refused. Hashes alone
    /// never establish publisher trust.</summary>
    public ValidatedStage ValidateStage(string stageLeaf, IReadOnlyList<HomePackageActivatedFile> completeFiles,
        string entrypointRelativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completeFiles);
        if (!LinuxOriginalDownloadPaths.IsSafeLeaf(stageLeaf) || stageLeaf == "active")
            throw new ArgumentException("A distinct existing stage directory is required.", nameof(stageLeaf));
        var files = completeFiles.Take(4097).Select(value => value with { }).ToArray();
        if (files.Length is < 1 or > 4096 || files.Any(value =>
                !HomePackageOriginalInstalledActivationRecord.SafeRelative(value.RelativePath) ||
                value.Bytes is < 0 or > 8L * 1024 * 1024 * 1024 || !IsDigest(value.Sha256)) ||
            files.Select(value => value.RelativePath).Distinct(StringComparer.Ordinal).Count() != files.Length ||
            files.Sum(value => value.Bytes) > 8L * 1024 * 1024 * 1024 ||
            !files.Any(value => value.RelativePath == entrypointRelativePath))
            throw new InvalidDataException("A bounded complete package cohort and its entrypoint are required.");
        lock (_gate)
        {
            DemandCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            using var publicationLock = LockDirectory(_directory);
            var directory = OpenPrivateDirectory(stageLeaf);
            try
            {
                var identity = LinuxOriginalDownloadPaths.ReadIdentity(directory);
                var identities = ValidateFiles(directory, stageLeaf, files, entrypointRelativePath, null, cancellationToken);
                if (!identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(directory)))
                    throw new IOException("The original package stage changed during validation.");
                LinuxOriginalDownloadPaths.Flush(directory);
                DemandCurrent();
                // Release before transferring descriptor ownership to the result, so
                // an unlock failure cannot strand a stage that was never returned.
                publicationLock.Dispose();
                return new(this, stageLeaf, directory, identity, files, entrypointRelativePath, identities);
            }
            catch { directory.Dispose(); throw; }
        }
    }

    /// <summary>Publish only the SAME validated stage and expected active observation.
    /// Null requires no active directory. No cancellation is raised after the atomic
    /// rename; any later native failure explicitly reports an uncertain publication.</summary>
    public Publication Publish(ValidatedStage stage, ActiveDirectory? expectedActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        lock (_gate)
        {
            DemandCurrent();
            if (!ReferenceEquals(stage.Owner, this) || stage.Closed || stage.Published ||
                expectedActive is not null && !ReferenceEquals(expectedActive.Owner, this))
                throw new UnauthorizedAccessException("The same live original stage/active owner is required.");
            Publication? outcome = null;
            try
            {
                using var publicationLock = LockDirectory(_directory);
                LinuxOriginalDownloadPaths.DemandPath(stage.Directory, stage.StagePath, directory: true);
                if (!stage.Identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(stage.Directory)))
                    throw new IOException("The original validated package directory changed.");
                _ = ValidateFiles(stage.Directory, stage.Leaf, stage.Files, stage.Entrypoint, stage.FileIdentities, cancellationToken);
                if (!stage.Identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(stage.Directory)))
                    throw new IOException("The original package directory changed during publication validation.");
                LinuxOriginalDownloadPaths.Flush(stage.Directory);
                var current = ObserveActiveCore();
                if (expectedActive is null ? current is not null :
                    current is null || !expectedActive.Identity.SameReadVersion(current.Identity))
                    throw new IOException("The active package changed; refresh the original root operation before publication.");
                DemandCurrent();
                cancellationToken.ThrowIfCancellationRequested();
                var result = new Publication(Path.Combine(_path, "active"), expectedActive is null ? null : stage.StagePath);
                RenameDirectory(_directory, stage.Leaf, "active", exchange: expectedActive is not null);
                stage.Published = true;
                outcome = result;
                // rename changes ctime. Compare inode/permissions and inspect the actual
                // new namespace rather than reusing the pre-rename read-version.
                LinuxOriginalDownloadPaths.Flush(_directory);
                LinuxOriginalDownloadPaths.DemandPath(stage.Directory, result.ActivePath, directory: true);
                if (!stage.Identity.SamePermissions(LinuxOriginalDownloadPaths.ReadIdentity(stage.Directory)))
                    throw new IOException("The published package no longer identifies its original stage.");
                if (expectedActive is not null)
                {
                    using var retained = OpenPrivateDirectory(stage.Leaf);
                    if (!expectedActive.Identity.SamePermissions(LinuxOriginalDownloadPaths.ReadIdentity(retained)))
                        throw new IOException("The previous package was not retained at the original stage name.");
                }
                DemandCurrent();
                return result;
            }
            // Includes a failed publication-lock release after rename. Such cleanup
            // errors must not erase the original namespace-changing outcome.
            catch (Exception cause) when (outcome is not null) { throw new PublicationUncertainException(outcome, cause); }
        }
    }

    private ActiveDirectory? ObserveActiveCore()
    {
        try
        {
            using var active = OpenPrivateDirectory("active");
            return new(this, LinuxOriginalDownloadPaths.ReadIdentity(active));
        }
        catch (FileNotFoundException) { return null; }
    }

    private SafeFileHandle OpenPrivateDirectory(string leaf)
    {
        var held = LinuxOriginalDownloadPaths.OpenAt(_directory, leaf, LinuxOriginalDownloadPaths.DirectoryFlags(mutable: true));
        try
        {
            LinuxOriginalDownloadPaths.DemandPath(held, Path.Combine(_path, leaf), directory: true);
            LinuxOriginalDownloadPaths.DemandOwner(held, _uid, privateDirectory: true);
            return held;
        }
        catch { held.Dispose(); throw; }
    }

    private Dictionary<string, LinuxOriginalDownloadIdentity> ValidateFiles(SafeFileHandle directory, string leaf,
        HomePackageActivatedFile[] files, string entrypoint, Dictionary<string, LinuxOriginalDownloadIdentity>? originals,
        CancellationToken token)
    {
        var path = Path.Combine(_path, leaf);
        var directories = files.SelectMany(value => ParentDirectories(value.RelativePath)).ToHashSet(StringComparer.Ordinal);
        var expected = files.Select(value => value.RelativePath).Concat(directories).ToHashSet(StringComparer.Ordinal);
        // Enumeration supplies names only. Every directory/file is separately opened
        // beneath the SAME retained descriptor with NO_SYMLINKS/NO_MAGICLINKS/NO_XDEV.
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var heldDirectories = new List<(SafeFileHandle Handle, string Path, LinuxOriginalDownloadIdentity Identity)>();
        try
        {
            var pending = new Stack<string>(); pending.Push("");
            while (pending.TryPop(out var parent))
            {
                token.ThrowIfCancellationRequested();
                foreach (var entry in Directory.EnumerateFileSystemEntries(Path.Combine(path, parent)))
                {
                    var relative = Path.GetRelativePath(path, entry);
                    if (observed.Count >= expected.Count || !expected.Contains(relative) || !observed.Add(relative))
                        throw new InvalidDataException("The staged package contains an extra or redirected entry.");
                    if (!directories.Contains(relative)) continue;
                    var child = LinuxOriginalDownloadPaths.OpenAt(directory, relative, LinuxOriginalDownloadPaths.DirectoryFlags(mutable: true));
                    try
                    {
                        LinuxOriginalDownloadPaths.DemandPath(child, entry, directory: true);
                        LinuxOriginalDownloadPaths.DemandOwner(child, _uid, privateDirectory: true);
                        heldDirectories.Add((child, entry, LinuxOriginalDownloadPaths.ReadIdentity(child)));
                    }
                    catch { child.Dispose(); throw; }
                    pending.Push(relative);
                }
            }
            if (!expected.SetEquals(observed)) throw new InvalidDataException("The complete staged package cohort is missing entries.");
            var identities = new Dictionary<string, LinuxOriginalDownloadIdentity>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                using var held = LinuxOriginalDownloadPaths.OpenAt(directory, file.RelativePath, 0x80000UL | 0x800UL);
                var before = LinuxOriginalDownloadPaths.ReadIdentity(held);
                if (!before.IsRegular || before.Links != 1 || before.Size != (ulong)file.Bytes ||
                    file.RelativePath == entrypoint && (before.Mode & 0x40) == 0 ||
                    originals is not null && !originals[file.RelativePath].SameReadVersion(before))
                    throw new InvalidDataException("The staged package file, size, executable mode or retained identity differs.");
                LinuxOriginalDownloadPaths.DemandOwner(held, _uid);
                LinuxOriginalDownloadPaths.DemandPath(held, Path.Combine(path, file.RelativePath), directory: false);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65536]; long offset = 0;
                while (offset < file.Bytes)
                {
                    token.ThrowIfCancellationRequested();
                    var read = RandomAccess.Read(held, buffer.AsSpan(0, (int)Math.Min(buffer.Length, file.Bytes - offset)), offset);
                    if (read == 0) throw new EndOfStreamException("The staged package file was truncated.");
                    hash.AppendData(buffer, 0, read); offset += read;
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    !before.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(held)))
                    throw new InvalidDataException("The staged package file failed integrity or changed during its read.");
                LinuxOriginalDownloadPaths.DemandPath(held, Path.Combine(path, file.RelativePath), directory: false);
                LinuxOriginalDownloadPaths.Flush(held);
                identities.Add(file.RelativePath, before);
            }
            // Persist nested namespace entries after their payloads, then let the owner
            // flush the stage and publication parent. Retain their original identities
            // until this final pass rather than trusting an earlier pathname traversal.
            foreach (var child in heldDirectories.AsEnumerable().Reverse())
            {
                token.ThrowIfCancellationRequested();
                LinuxOriginalDownloadPaths.DemandPath(child.Handle, child.Path, directory: true);
                if (!child.Identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(child.Handle)))
                    throw new IOException("The original nested package directory changed during validation.");
                LinuxOriginalDownloadPaths.Flush(child.Handle);
            }
            LinuxOriginalDownloadPaths.DemandPath(directory, path, directory: true);
            return identities;
        }
        finally { foreach (var child in heldDirectories.AsEnumerable().Reverse()) child.Handle.Dispose(); }
    }

    private static IEnumerable<string> ParentDirectories(string relative)
    {
        var parent = Path.GetDirectoryName(relative);
        while (!string.IsNullOrEmpty(parent)) { yield return parent; parent = Path.GetDirectoryName(parent); }
    }
    private static bool IsDigest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private void DemandCurrent() { ObjectDisposedException.ThrowIf(_closed, this); _root.DemandCurrent(); }
    public void Dispose()
    {
        lock (_gate) { if (_closed) return; _closed = true; _directory.Dispose(); _root.Dispose(); }
    }
}
