using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

// Physical observations, never Home/Files authority. Linux does not prevent a
// same-UID external writer from renaming/writing held objects. Current path,
// inode and read-version checks refuse observed diversion; no external-writer
// CAS or Windows share-delete exclusion is promised.
internal readonly record struct LinuxOriginalDownloadIdentity(ulong Inode, uint DeviceMajor, uint DeviceMinor,
    ulong MountId, ushort Mode, uint Uid, uint Gid, uint Links, ulong Size,
    long ModifiedSeconds, uint ModifiedNanos, long ChangedSeconds, uint ChangedNanos)
{
    internal bool IsDirectory => (Mode & 0xf000) == 0x4000;
    internal bool IsRegular => (Mode & 0xf000) == 0x8000;
    internal bool SameFile(LinuxOriginalDownloadIdentity other) => Inode == other.Inode &&
        DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor && MountId == other.MountId;
    internal bool SamePermissions(LinuxOriginalDownloadIdentity other) => SameFile(other) && Mode == other.Mode && Uid == other.Uid && Gid == other.Gid;
    internal bool SamePayloadVersion(LinuxOriginalDownloadIdentity other) => SamePermissions(other) && Links == other.Links &&
        Size == other.Size && ModifiedSeconds == other.ModifiedSeconds && ModifiedNanos == other.ModifiedNanos;
    internal bool SameReadVersion(LinuxOriginalDownloadIdentity other) => SamePayloadVersion(other) &&
        ChangedSeconds == other.ChangedSeconds && ChangedNanos == other.ChangedNanos;
}

internal sealed class LinuxOriginalDownloadRoot : IDisposable
{
    private readonly string _path;
    private readonly SafeFileHandle _root;
    private readonly LinuxOriginalDownloadIdentity _identity;
    private readonly uint _uid;
    private readonly bool _private;
    private bool _closed;
    internal LinuxOriginalDownloadRoot(string path, bool requirePrivate)
        : this(path, requirePrivate, null, null) { }
    internal LinuxOriginalDownloadRoot(string path, bool requirePrivate, SafeFileHandle? heldRoot, uint? expectedUid)
    {
        LinuxOriginalDownloadPaths.RequireCapabilities();
        _path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (_path == "/") throw new UnauthorizedAccessException("A download owner needs an existing bounded directory.");
        _uid = expectedUid ?? LinuxOriginalDownloadPaths.CurrentUid(); _private = requirePrivate;
        LinuxOriginalDownloadPaths.DemandCurrentUid(_uid);
        _root = heldRoot ?? LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(_path, mutable: false);
        try
        {
            LinuxOriginalDownloadPaths.DemandPath(_root, _path, directory: true);
            LinuxOriginalDownloadPaths.DemandOwner(_root, _uid, requirePrivate);
            _identity = LinuxOriginalDownloadPaths.ReadIdentity(_root);
        }
        catch { _root.Dispose(); throw; }
    }
    internal void DemandCurrent()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        LinuxOriginalDownloadPaths.DemandCurrentUid(_uid);
        LinuxOriginalDownloadPaths.DemandPath(_root, _path, directory: true);
        LinuxOriginalDownloadPaths.DemandOwner(_root, _uid, _private);
        if (!_identity.SamePermissions(LinuxOriginalDownloadPaths.ReadIdentity(_root)))
            throw new UnauthorizedAccessException("The original Linux root identity or permissions changed.");
    }
    internal SafeFileHandle OpenDirectory(string path, bool mutable)
    {
        DemandCurrent();
        if (!StringComparer.Ordinal.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), _path))
            throw new UnauthorizedAccessException("This owner retains only its exact original directory.");
        var handle = LinuxOriginalDownloadPaths.OpenAt(_root, ".", LinuxOriginalDownloadPaths.DirectoryFlags(mutable));
        try { LinuxOriginalDownloadPaths.DemandPath(handle, _path, true); return handle; }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() { if (_closed) return; _closed = true; _root.Dispose(); }
}

internal sealed class LinuxOriginalDownloadCollisionException : IOException
{ internal LinuxOriginalDownloadCollisionException() : base("The original Linux destination already exists; no replacement occurred.") { } }

internal static class LinuxOriginalDownloadPaths
{
    private const ulong PathFlag = 0x200000, CloseOnExec = 0x80000, NonBlocking = 0x800;
    internal static ulong DirectoryFlagFor(Architecture architecture) => architecture switch
    {
        Architecture.X64 => 0x10000UL,
        Architecture.Arm64 => 0x4000UL,
        _ => throw new PlatformNotSupportedException("Original Linux downloads require the x64 or ARM64 native ABI.")
    };
    internal static ulong DirectoryFlags(bool mutable) => DirectoryFlagFor(RuntimeInformation.ProcessArchitecture) | CloseOnExec | (mutable ? 0 : PathFlag);
    internal static void RequireCapabilities()
    {
        if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Original Linux downloads require supported little-endian Linux x64/ARM64.");
        if (!NativeLibrary.TryLoad("libc.so.6", out var library))
            throw new PlatformNotSupportedException("Original Linux downloads require the supported C library.");
        try
        {
            foreach (var entry in new[] { "open", "syscall", "statx", "readlink", "renameat2", "mkdirat", "fsync", "geteuid" })
                if (!NativeLibrary.TryGetExport(library, entry, out _))
                    throw new PlatformNotSupportedException("Required original Linux entrypoint is unavailable: " + entry);
        }
        finally { NativeLibrary.Free(library); }
    }
    internal static uint CurrentUid() { RequireCapabilities(); return GetEuid(); }
    internal static void DemandCurrentUid(uint expected)
    { if (CurrentUid() != expected) throw new UnauthorizedAccessException("The original Linux effective user changed."); }
    internal static LinuxOriginalDownloadRoot RetainRoot(string path, bool requirePrivate = false) => new(path, requirePrivate);
    /// <summary>Configuration permits only the designated missing child under this exact
    /// existing parent. Call only inside the validated original transport-plan source.
    /// Neither this configuration nor directory creation grants Home/Files authority.</summary>
    internal static LinuxOriginalDownloadRoot RetainOrCreatePrivateRoot(string path, string configuredParent, uint principal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DemandCurrentUid(principal);
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        configuredParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredParent));
        if (!StringComparer.Ordinal.Equals(Path.GetDirectoryName(path), configuredParent) || !IsSafeLeaf(Path.GetFileName(path)))
            throw new UnauthorizedAccessException("Only the exact configured single-child download root may be created.");
        try { return new LinuxOriginalDownloadRoot(path, requirePrivate: true, heldRoot: null, expectedUid: principal); }
        catch (FileNotFoundException)
        {
            // Missing ancestors are never recursively created. Symlinks, foreign ownership,
            // nonprivate roots and unsupported native custody are not 'missing' fallbacks.
            using var parent = OpenAbsoluteDirectory(configuredParent, mutable: true);
            return CreatePrivateChildRoot(parent, configuredParent, Path.GetFileName(path), principal, cancellationToken);
        }
    }

    internal static LinuxOriginalDownloadRoot CreatePrivateChildRoot(SafeFileHandle parent, string parentPath, string leaf, uint principal, CancellationToken cancellationToken = default)
    {
        if (!IsSafeLeaf(leaf)) throw new UnauthorizedAccessException("The configured download root leaf is invalid.");
        parentPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
        DemandCurrentUid(principal); DemandPath(parent, parentPath, directory: true); DemandOwner(parent, principal);
        var originalParent = ReadIdentity(parent);
        if (!originalParent.IsDirectory) throw new UnauthorizedAccessException("The configured download parent is not a real directory.");
        DemandCurrentUid(principal);
        cancellationToken.ThrowIfCancellationRequested();
        if (MkdirAt(Fd(parent), leaf, 0x1c0) != 0) // exact child, mode0700 subject only to a narrowing umask
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 17) throw new LinuxOriginalDownloadCollisionException(); // no race adoption/retry
            ThrowNative("create the configured private Linux download root", error);
        }
        SafeFileHandle? child = null;
        try
        {
            child = OpenAt(parent, leaf, DirectoryFlags(mutable: true)); // BENEATH|NO_XDEV|NO_SYMLINKS|NO_MAGICLINKS
            var path = Path.Combine(parentPath, leaf);
            DemandCurrentUid(principal); DemandPath(parent, parentPath, directory: true); DemandOwner(parent, principal);
            if (!originalParent.SamePermissions(ReadIdentity(parent)))
                throw new UnauthorizedAccessException("The original download parent changed during root creation; preserve the outcome.");
            DemandPath(child, path, directory: true); DemandOwner(child, principal, privateDirectory: true);
            Flush(child); Flush(parent);
            DemandCurrentUid(principal); DemandPath(parent, parentPath, directory: true); DemandOwner(parent, principal);
            if (!originalParent.SamePermissions(ReadIdentity(parent)))
                throw new UnauthorizedAccessException("The original download parent changed before root retention; preserve the outcome.");
            var retained = new LinuxOriginalDownloadRoot(path, requirePrivate: true, heldRoot: child, expectedUid: principal);
            child = null; // transfer the SAME held descriptor; do not reopen/adopt another inode
            return retained;
        }
        finally
        {
            child?.Dispose();
            // No chmod/chown, path cleanup or deletion of an uncertain created outcome.
            // As elsewhere in this owner, same-UID external-writer exclusion is not promised.
        }
    }

    internal static bool IsSafeLeaf(string leaf) => !string.IsNullOrWhiteSpace(leaf) && leaf is not ("." or "..") &&
        !leaf.Any(character => character is '/' or '\\' || char.IsControl(character)) && Encoding.UTF8.GetByteCount(leaf) <= 255;
    internal static SafeFileHandle OpenAbsoluteDirectory(string path, bool mutable)
    {
        using var volume = Wrap(Open("/", checked((int)DirectoryFlags(mutable: false)), 0), "retain Linux volume");
        var handle = OpenAt(volume, Path.GetFullPath(path).TrimStart('/'), DirectoryFlags(mutable), noMountCrossing: false);
        try { DemandPath(handle, path, true); return handle; }
        catch { handle.Dispose(); throw; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct OpenHow { public ulong Flags, Mode, Resolve; }
    internal static SafeFileHandle OpenAt(SafeFileHandle directory, string relative, ulong flags, ulong mode = 0, bool noMountCrossing = true)
    {
        var how = new OpenHow { Flags = flags, Mode = mode, Resolve = 0xeUL | (noMountCrossing ? 1UL : 0) };
        // openat2 is syscall437 on BOTH supported ABIs. Resolve flags are BENEATH,
        // NO_SYMLINKS, NO_MAGICLINKS, optionally NO_XDEV. No openat fallback.
        var actual = OpenAt2(437, Fd(directory), relative, ref how, (ulong)Marshal.SizeOf<OpenHow>());
        if (actual >= 0 && actual <= int.MaxValue) return new((IntPtr)actual, ownsHandle: true);
        ThrowNative("open original Linux descriptor", Marshal.GetLastPInvokeError());
        throw new InvalidOperationException("No original Linux descriptor was acquired.");
    }
    internal static SafeFileHandle OpenRead(SafeFileHandle parent, string leaf)
    {
        if (!IsSafeLeaf(leaf)) throw new UnauthorizedAccessException("The original Linux file leaf is invalid.");
        return OpenAt(parent, leaf, CloseOnExec | NonBlocking);
    }
    internal static SafeFileHandle CreateStage(SafeFileHandle parent, string leaf)
    {
        if (!IsSafeLeaf(leaf)) throw new UnauthorizedAccessException("The original Linux stage leaf is invalid.");
        return OpenAt(parent, leaf, 2UL | 0x40UL | 0x80UL | CloseOnExec | NonBlocking, 0x180); // RDWR|CREAT|EXCL,0600
    }
    internal static LinuxOriginalDownloadIdentity ReadIdentity(SafeFileHandle handle)
    {
        var bytes = new byte[256];
        if (Statx(Fd(handle), "", 0x1000, 0x17ff, bytes) != 0) ThrowNative("observe original Linux descriptor identity", Marshal.GetLastPInvokeError());
        return ParseIdentity(bytes);
    }
    internal static LinuxOriginalDownloadIdentity ParseIdentity(byte[] bytes)
    {
        if (bytes.Length != 256) throw new InvalidDataException("The actual statx observation has an unsupported layout.");
        const uint consumed = 1 | 2 | 4 | 8 | 16 | 0x40 | 0x80 | 0x100 | 0x200 | 0x1000;
        if ((BitConverter.ToUInt32(bytes, 0) & consumed) != consumed)
            throw new PlatformNotSupportedException("The kernel lacks required original kind/mode/uid/gid/link/inode/size/mtime/ctime/mount observations.");
        var identity = new LinuxOriginalDownloadIdentity(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136),
            BitConverter.ToUInt32(bytes, 140), BitConverter.ToUInt64(bytes, 144), BitConverter.ToUInt16(bytes, 28),
            BitConverter.ToUInt32(bytes, 20), BitConverter.ToUInt32(bytes, 24), BitConverter.ToUInt32(bytes, 16),
            BitConverter.ToUInt64(bytes, 40), BitConverter.ToInt64(bytes, 112), BitConverter.ToUInt32(bytes, 120),
            BitConverter.ToInt64(bytes, 96), BitConverter.ToUInt32(bytes, 104));
        if (identity.ModifiedNanos >= 1_000_000_000 || identity.ChangedNanos >= 1_000_000_000)
            throw new InvalidDataException("The original statx timestamps are invalid.");
        return identity;
    }
    internal static void DemandPath(SafeFileHandle held, string expected, bool directory)
    {
        expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected));
        var buffer = new byte[32769]; var length = ReadLink($"/proc/self/fd/{Fd(held)}", buffer, (ulong)buffer.Length);
        if (length < 0) ThrowNative("observe original Linux descriptor path", Marshal.GetLastPInvokeError());
        if (length >= buffer.Length || !StringComparer.Ordinal.Equals(Encoding.UTF8.GetString(buffer, 0, checked((int)length)), expected))
            throw new UnauthorizedAccessException("The held original Linux descriptor has another or removed path.");
        // Also reopen from the real volume with NO_SYMLINKS: a textual /proc path
        // alone cannot reject an ancestor replaced by a symlink to the same inode.
        using var volume = Wrap(Open("/", checked((int)DirectoryFlags(false)), 0), "retain Linux volume for observation");
        using var observed = OpenAt(volume, expected.TrimStart('/'), PathFlag | CloseOnExec | (directory ? DirectoryFlagFor(RuntimeInformation.ProcessArchitecture) : 0), noMountCrossing: false);
        var before = ReadIdentity(held); var actual = ReadIdentity(observed);
        if (!before.SameFile(actual) || (directory ? !actual.IsDirectory : !actual.IsRegular))
            throw new UnauthorizedAccessException("The current original Linux name no longer identifies its held inode/kind.");
    }
    internal static void DemandOwner(SafeFileHandle handle, uint uid, bool privateDirectory = false)
    {
        DemandCurrentUid(uid); var actual = ReadIdentity(handle);
        if (actual.Uid != uid || (actual.Mode & 0xe00) != 0 || (actual.Mode & (privateDirectory ? 0x3f : 0x12)) != 0 ||
            actual.IsDirectory && (actual.Mode & 0x1c0) != 0x1c0)
            throw new UnauthorizedAccessException("The original Linux object has foreign ownership or unsupported write/private permissions.");
    }
    internal static void DemandPrivateStage(SafeFileHandle handle, uint uid)
    {
        DemandOwner(handle, uid); var actual = ReadIdentity(handle);
        if (!actual.IsRegular || actual.Links != 1 || (actual.Mode & 0x1ff) != 0x180)
            throw new UnauthorizedAccessException("The original Linux stage must be an exclusive regular inode with mode0600.");
    }
    internal static LinuxOriginalDownloadIdentity PublishCreateOnly(SafeFileHandle held, SafeFileHandle parent,
        string directory, string oldLeaf, string newLeaf, LinuxOriginalDownloadIdentity expected)
    {
        if (!IsSafeLeaf(oldLeaf) || !IsSafeLeaf(newLeaf)) throw new UnauthorizedAccessException("The original Linux publication leaf is invalid.");
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        DemandPath(parent, directory, true); DemandPath(held, Path.Combine(directory, oldLeaf), false);
        if (!expected.SameReadVersion(ReadIdentity(held))) throw new IOException("The original Linux stage changed before publication.");
        if (RenameAt2(Fd(parent), oldLeaf, Fd(parent), newLeaf, 1) != 0) // RENAME_NOREPLACE; never overwrite/fallback
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 17) throw new LinuxOriginalDownloadCollisionException();
            ThrowNative("publish original Linux file without replacement", error);
        }
        var after = ReadIdentity(held);
        if (!expected.SamePayloadVersion(after) || !after.IsRegular || after.Links != 1)
            throw new IOException("Preserve the unknown original Linux publication outcome.");
        DemandPath(held, Path.Combine(directory, newLeaf), false);
        return after; // rename may change ctime; caller rehashes the promoted bytes.
    }
    internal static void Flush(SafeFileHandle handle)
    { if (Fsync(Fd(handle)) != 0) ThrowNative("flush original Linux descriptor", Marshal.GetLastPInvokeError()); }
    internal static bool IsNameCollision(Exception error) => error is LinuxOriginalDownloadCollisionException;
    private static int Fd(SafeFileHandle handle)
    { if (handle.IsClosed || handle.IsInvalid) throw new ObjectDisposedException("original Linux descriptor"); return handle.DangerousGetHandle().ToInt32(); }
    private static SafeFileHandle Wrap(int fd, string operation)
    { if (fd < 0) ThrowNative(operation, Marshal.GetLastPInvokeError()); return new((IntPtr)fd, ownsHandle: true); }
    private static void ThrowNative(string operation, int error)
    {
        if (error is 38 or 22 or 95) throw new PlatformNotSupportedException("The kernel/filesystem lacks required original Linux custody: " + operation);
        if (error == 2) throw new FileNotFoundException("The original Linux component is absent: " + operation);
        if (error is 18 or 40) throw new UnauthorizedAccessException("The original Linux path crossed a mount/symlink boundary: " + operation);
        throw new Win32Exception(error, operation);
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)] private static extern long OpenAt2(long number, int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref OpenHow how, ulong size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, [Out] byte[] result);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern long ReadLink([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] result, ulong size);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)] private static extern int RenameAt2(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldName, int destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string newName, uint flags);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)] private static extern int MkdirAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
