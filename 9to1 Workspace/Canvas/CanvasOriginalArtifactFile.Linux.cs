using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Apps.Canvas;

internal sealed partial class CanvasOriginalArtifactFile
{
    private const int LinuxMaximumBytes = 512 * 1024 * 1024;
    private const ulong LinuxCloseOnExec = 0x80000, LinuxNonBlocking = 0x800, LinuxPath = 0x200000;
    // O_DIRECTORY has distinct Linux UAPI values on the supported x64/ARM64 ABIs.
    private static readonly ulong LinuxDirectory = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x4000UL : 0x10000UL;
    private readonly List<LinuxDirectoryBinding> _linuxDirectories = [];
    private LinuxFileIdentity? _linuxIdentity;
    private byte[]? _linuxExpectedWriteHash;
    private string? _linuxLeafName;

    /// <summary>Descriptor-relative Linux custody, not a kernel namespace or an external-writer
    /// lock. Linux permits rename/write by other processes: re-observation refuses known changes
    /// before the caller's independent Home/Files commit fence. It cannot promise Windows share
    /// exclusion. A failed write may leave its unpublished, exclusively created candidate behind;
    /// cleanup never unlinks a mutable pathname which could now name someone else's file.</summary>
    private void AcquireLinux(string root, string relative, bool create)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64) || !BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("Canvas requires the supported 64-bit little-endian Linux openat2/statx ABI.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (fullRoot == "/" || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains('\\') || relative.Contains('\0') ||
            relative.Split('/').Any(part => string.IsNullOrEmpty(part) || part is "." or ".."))
            throw new InvalidDataException("Canvas requires a bounded root and exact Files-relative artifact path.");
        var parts = relative.Split('/');
        if (parts.Length + 1 > 128)
            throw new InvalidDataException("Canvas original ancestor custody exceeds its finite handle bound.");
        _path = Path.Combine(fullRoot, relative);
        RequireLinuxEntryPoints();

        var volume = LinuxWrap(LinuxOpen("/", LinuxDirectory | LinuxCloseOnExec, 0), "retain Canvas Linux volume");
        RetainLinuxDirectory(volume, "/", null, ".", allowMountCrossing: true);
        var heldRoot = LinuxOpenRelative(volume, fullRoot.TrimStart('/'), LinuxDirectory | LinuxCloseOnExec, allowMountCrossing: true);
        RetainLinuxDirectory(heldRoot, fullRoot, volume, fullRoot.TrimStart('/'), allowMountCrossing: true);
        var parent = heldRoot;
        var current = fullRoot;
        foreach (var name in parts[..^1])
        {
            DemandLinuxDirectories();
            SafeFileHandle child;
            try { child = LinuxOpenRelative(parent, name, LinuxDirectory | LinuxCloseOnExec); }
            catch (FileNotFoundException) when (create)
            {
                // The effect remains relative to this exact held parent, never a recombined path.
                // A competing creator is re-opened through the same no-follow boundary.
                if (LinuxMkdirAt(LinuxFd(parent), name, 0x1c0) != 0 && Marshal.GetLastPInvokeError() != 17)
                    LinuxThrow(Marshal.GetLastPInvokeError(), "create Canvas candidate directory");
                child = LinuxOpenRelative(parent, name, LinuxDirectory | LinuxCloseOnExec);
            }
            current = Path.Combine(current, name);
            RetainLinuxDirectory(child, current, parent, name, allowMountCrossing: false);
            parent = child;
        }
        DemandLinuxDirectories();
        _linuxLeafName = parts[^1];
        // O_NONBLOCK rejects a FIFO/device admission without blocking. O_EXCL never
        // truncates or follows an existing target; modes are private independent of umask.
        _leaf = LinuxOpenRelative(parent, _linuxLeafName,
            LinuxCloseOnExec | LinuxNonBlocking | (create ? 2UL | 0x40UL | 0x80UL : 0UL), create ? 0x180UL : 0UL);
        LinuxDemandPath(_leaf, _path);
        var identity = LinuxObserve(_leaf);
        DemandLinuxRegular(identity);
        _linuxIdentity = identity;
        _stream = new FileStream(_leaf, create ? FileAccess.ReadWrite : FileAccess.Read, 4096, isAsync: false);
    }

    private void RetainLinuxDirectory(SafeFileHandle handle, string path, SafeFileHandle? parent,
        string name, bool allowMountCrossing)
    {
        // Register ownership before observations which can fail; Acquire's independent
        // cleanup then also closes partially admitted components.
        _parents.Add(handle);
        LinuxDemandPath(handle, path);
        var identity = LinuxObserve(handle);
        if (!identity.IsDirectory) throw new UnauthorizedAccessException("The retained Canvas Linux ancestor is not a directory.");
        _linuxDirectories.Add(new(handle, path, parent, name, allowMountCrossing, identity));
    }

    private void DemandLinuxDirectories()
    {
        foreach (var binding in _linuxDirectories)
        {
            LinuxDemandPath(binding.Handle, binding.Path);
            var held = LinuxObserve(binding.Handle);
            if (!held.IsDirectory || !binding.Identity.SameFile(held))
                throw new UnauthorizedAccessException("The retained Canvas Linux directory changed identity.");
            if (binding.Parent is null) continue;
            using var named = LinuxOpenRelative(binding.Parent, binding.Name, LinuxPath | LinuxDirectory | LinuxCloseOnExec,
                allowMountCrossing: binding.AllowMountCrossing);
            var current = LinuxObserve(named);
            if (!current.IsDirectory || !held.SameFile(current))
                throw new UnauthorizedAccessException("The current Canvas Linux directory name no longer denotes its retained owner.");
        }
    }

    private void DemandLinuxUnchanged(long size)
    {
        var leaf = _leaf ?? throw new ObjectDisposedException(nameof(CanvasOriginalArtifactFile));
        if (size is <= 0 or > LinuxMaximumBytes)
            throw new InvalidDataException("Canvas artifact size is outside its bounded original custody.");
        DemandLinuxDirectories();
        LinuxDemandPath(leaf, _path);
        var actual = LinuxObserve(leaf);
        DemandLinuxRegular(actual);
        if (_linuxIdentity is not { } expected || !expected.SameReadVersion(actual) || actual.Size != (ulong)size)
            throw new InvalidDataException("The retained Canvas Linux artifact identity, size or read version changed.");
        var parent = _linuxDirectories[^1].Handle;
        using var named = LinuxOpenRelative(parent, _linuxLeafName!, LinuxPath | LinuxCloseOnExec);
        var namedIdentity = LinuxObserve(named);
        DemandLinuxRegular(namedIdentity);
        if (!actual.SameReadVersion(namedIdentity))
            throw new UnauthorizedAccessException("The Canvas Linux artifact name no longer denotes its retained revision.");
    }

    private void PrepareLinuxWrite(ReadOnlyMemory<byte> bytes)
    {
        if (_linuxIdentity is null) return;
        _linuxExpectedWriteHash = SHA256.HashData(bytes.Span);
    }

    private void SealLinuxWrite(int size, CancellationToken token)
    {
        if (_linuxIdentity is not { } original) return;
        token.ThrowIfCancellationRequested();
        DemandLinuxDirectories();
        var leaf = _leaf ?? throw new ObjectDisposedException(nameof(CanvasOriginalArtifactFile));
        LinuxDemandPath(leaf, _path);
        var before = LinuxObserve(leaf);
        DemandLinuxRegular(before);
        if (!original.SameFile(before) || before.Size != (ulong)size || _linuxExpectedWriteHash is null)
            throw new InvalidDataException("The Canvas Linux candidate changed identity or size during its write.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[Math.Min(64 * 1024, size)];
        var offset = 0;
        while (offset < size)
        {
            token.ThrowIfCancellationRequested();
            var count = RandomAccess.Read(leaf, buffer.AsSpan(0, Math.Min(buffer.Length, size - offset)), offset);
            if (count == 0) throw new EndOfStreamException("The retained Canvas Linux write ended before its acknowledged size.");
            hash.AppendData(buffer, 0, count); offset += count;
        }
        var after = LinuxObserve(leaf);
        if (!before.SameReadVersion(after) || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), _linuxExpectedWriteHash))
            throw new InvalidDataException("The retained Canvas Linux candidate bytes do not match the owned write snapshot.");
        // Flush directory entries as well as the leaf; missing filesystem support
        // is an explicit refusal, never a durability success claim.
        foreach (var directory in _linuxDirectories.Skip(1).Reverse())
            if (LinuxFsync(LinuxFd(directory.Handle)) != 0)
                LinuxThrow(Marshal.GetLastPInvokeError(), "flush Canvas Linux candidate directory");
        token.ThrowIfCancellationRequested();
        _linuxIdentity = after;
    }

    private static void DemandLinuxRegular(LinuxFileIdentity identity)
    {
        if (!identity.IsRegular || identity.Links != 1)
            throw new UnauthorizedAccessException("Canvas requires a regular single-link retained Linux artifact.");
    }
    private sealed record LinuxDirectoryBinding(SafeFileHandle Handle, string Path, SafeFileHandle? Parent,
        string Name, bool AllowMountCrossing, LinuxFileIdentity Identity);
    private readonly record struct LinuxFileIdentity(ulong Inode, uint DeviceMajor, uint DeviceMinor, ulong MountId,
        ushort Mode, uint Links, ulong Size, long ModifiedSeconds, uint ModifiedNanos, long ChangedSeconds, uint ChangedNanos)
    {
        internal bool IsDirectory => (Mode & 0xf000) == 0x4000;
        internal bool IsRegular => (Mode & 0xf000) == 0x8000;
        internal bool SameFile(LinuxFileIdentity other) => Inode == other.Inode && DeviceMajor == other.DeviceMajor &&
            DeviceMinor == other.DeviceMinor && MountId == other.MountId;
        internal bool SameReadVersion(LinuxFileIdentity other) => SameFile(other) && Size == other.Size &&
            ModifiedSeconds == other.ModifiedSeconds && ModifiedNanos == other.ModifiedNanos &&
            ChangedSeconds == other.ChangedSeconds && ChangedNanos == other.ChangedNanos;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxOpenHow { public ulong Flags, Mode, Resolve; }
    private static SafeFileHandle LinuxOpenRelative(SafeFileHandle parent, string path, ulong flags, ulong mode = 0,
        bool allowMountCrossing = false)
    {
        var how = new LinuxOpenHow { Flags = flags, Mode = mode, Resolve = 0xeUL | (allowMountCrossing ? 0UL : 1UL) };
        var fd = LinuxOpenAt2(437, LinuxFd(parent), path, ref how, (ulong)Marshal.SizeOf<LinuxOpenHow>());
        if (fd >= 0) return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        LinuxThrow(Marshal.GetLastPInvokeError(), "open Canvas Linux component");
        throw new InvalidOperationException("No Canvas Linux descriptor was acquired.");
    }
    private static SafeFileHandle LinuxWrap(int fd, string operation)
    {
        if (fd < 0) LinuxThrow(Marshal.GetLastPInvokeError(), operation);
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }
    private static int LinuxFd(SafeFileHandle handle)
    {
        if (handle.IsClosed || handle.IsInvalid) throw new ObjectDisposedException("retained Canvas Linux descriptor");
        return handle.DangerousGetHandle().ToInt32();
    }
    private static LinuxFileIdentity LinuxObserve(SafeFileHandle handle)
    {
        var bytes = new byte[256];
        if (LinuxStatx(LinuxFd(handle), "", 0x1000, 0x17ff, bytes) != 0)
            LinuxThrow(Marshal.GetLastPInvokeError(), "observe Canvas Linux descriptor");
        const uint needed = 0x1 | 0x4 | 0x40 | 0x80 | 0x100 | 0x200 | 0x1000;
        if ((BitConverter.ToUInt32(bytes, 0) & needed) != needed)
            throw new PlatformNotSupportedException("The Linux kernel did not supply Canvas's consumed statx identity/version fields.");
        return new(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140),
            BitConverter.ToUInt64(bytes, 144), BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 16),
            BitConverter.ToUInt64(bytes, 40), BitConverter.ToInt64(bytes, 112), BitConverter.ToUInt32(bytes, 120),
            BitConverter.ToInt64(bytes, 96), BitConverter.ToUInt32(bytes, 104));
    }
    private static void LinuxDemandPath(SafeFileHandle handle, string expected)
    {
        // Kernel-owned /proc observation only; no caller path is reopened through procfs.
        var buffer = new byte[32769];
        var length = LinuxReadLink($"/proc/self/fd/{LinuxFd(handle)}", buffer, (ulong)buffer.Length);
        if (length < 0) LinuxThrow(Marshal.GetLastPInvokeError(), "observe retained Canvas Linux pathname");
        if (length >= buffer.Length) throw new InvalidDataException("Canvas's retained Linux path exceeds its bounded observation.");
        if (!StringComparer.Ordinal.Equals(Encoding.UTF8.GetString(buffer, 0, checked((int)length)), expected))
            throw new UnauthorizedAccessException("The retained Canvas Linux component was moved, unlinked or rebound.");
    }
    private static void RequireLinuxEntryPoints()
    {
        if (!NativeLibrary.TryLoad("libc.so.6", out var library))
            throw new PlatformNotSupportedException("Canvas's supported Linux C library is unavailable.");
        try
        {
            foreach (var name in new[] { "open", "syscall", "statx", "readlink", "mkdirat", "fsync" })
                if (!NativeLibrary.TryGetExport(library, name, out _))
                    throw new PlatformNotSupportedException("Canvas's retained Linux entry point is unavailable: " + name);
        }
        finally { NativeLibrary.Free(library); }
    }
    private static void LinuxThrow(int error, string operation)
    {
        if (error is 38 or 22 or 95) throw new PlatformNotSupportedException("The filesystem/kernel does not support " + operation + ".");
        if (error == 2) throw new FileNotFoundException("The retained Canvas Linux component is absent.");
        if (error is 18 or 40 or 20) throw new UnauthorizedAccessException("Canvas refuses a redirected, mounted or non-directory Linux component.");
        if (error == 17) throw new IOException("The Canvas Linux candidate already exists; it was not overwritten.");
        throw new Win32Exception(error, operation);
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int LinuxOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ulong flags, uint mode);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long LinuxOpenAt2(long number, int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref LinuxOpenHow how, ulong size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int LinuxStatx(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, [Out] byte[] result);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
    private static extern long LinuxReadLink([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] result, ulong length);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int LinuxMkdirAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int LinuxFsync(int fd);
}
