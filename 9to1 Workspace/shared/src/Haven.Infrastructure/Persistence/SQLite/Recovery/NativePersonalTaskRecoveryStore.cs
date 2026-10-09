using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

/// <summary>Actual configured Linux personal-store kernel identity. This is not Home, Windows,
/// browser/account ownership, a model grant or an inferred owner from environment usernames.</summary>
internal sealed partial class NativePersonalTaskRecoveryStore : IDisposable
{
    private readonly SafeFileHandle _directory;
    private readonly SafeFileHandle _database;
    private readonly string _directoryPath;
    private readonly string _databasePath;
    private readonly Identity _expectedDirectory;
    private readonly Identity _expectedDatabase;
    private readonly uint _uid;
    private bool _disposed;
    private const int NoFollow = 0x20000;
    private const int CloseOnExec = 0x80000;
    private const int DirectoryFlag = 0x10000;
    private const int EmptyPath = 0x1000;
    private const uint StatBasic = 0x7ff;

    private NativePersonalTaskRecoveryStore(string directory, string database)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Cold personal-store authorization is currently Linux-only; no other domain grant is substituted.");
        _directoryPath = Path.GetFullPath(directory); _databasePath = Path.GetFullPath(database);
        if (Path.GetDirectoryName(_databasePath) != _directoryPath)
            throw new UnauthorizedAccessException("The configured database must be a direct child of its actual personal store.");
        _uid = GetEffectiveUserId();
        _directory = OpenOriginal(_directoryPath, DirectoryFlag);
        try
        {
            _expectedDirectory = Observe(_directory, directory: true);
            _database = OpenOriginal(_databasePath, 0);
            try { _expectedDatabase = Observe(_database, directory: false); Validate(); }
            catch { _database.Dispose(); throw; }
        }
        catch { _directory.Dispose(); throw; }
    }

    internal static NativePersonalTaskRecoveryStore Acquire(string directory, string database) =>
        OperatingSystem.IsWindows() ? AcquireOriginalWindows(directory, database) : new(directory, database);
    internal void Validate()
    {
        if (_windows is { } windows) { windows.Validate(); return; }
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (GetEffectiveUserId() != _uid || !Observe(_directory, true).SameObject(_expectedDirectory)
            || !Observe(_database, false).SameObject(_expectedDatabase)
            || !ObservePath(_directoryPath, true).SameObject(_expectedDirectory)
            || !ObservePath(_databasePath, false).SameObject(_expectedDatabase))
            throw new UnauthorizedAccessException("The actual protected personal-store principal/path/inode changed.");
        // Existing SQLite companion files are part of this same private store, never a
        // group-readable/source-replaced route around the protected main file.
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(_databasePath + suffix)) _ = ObservePath(_databasePath + suffix, false);
    }

    internal byte[] ReadOrCreateAuthenticationKey(bool create)
    {
        if (_windows is { } windows) return windows.ReadOrCreateAuthenticationKey(create);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Protected personal-store keys require the actual Linux kernel owner.");
        Validate();
        var path = Path.Combine(_directoryPath, ".task-recovery-auth.v1");
        if (!File.Exists(path))
        {
            if (!create) throw new UnauthorizedAccessException("The original store authentication key is missing; no capsule provenance can be reconstructed.");
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            try
            {
                // Create relative to the SAME already-owned directory handle. Parent-path
                // replacement cannot redirect this original key acquisition.
                var fd = OpenAt(_directory.DangerousGetHandle().ToInt32(), ".task-recovery-auth.v1",
                    1 | 0x40 | 0x80 | NoFollow | CloseOnExec, 0x180); // WRONLY|CREAT|EXCL,0600
                if (fd < 0)
                {
                    var actualError = Marshal.GetLastWin32Error();
                    // Only the kernel's exclusive-create EEXIST result can denote a
                    // concurrent creator. Our own write/flush faults remain real faults.
                    if (actualError != 17) throw new IOException("Original authentication key creation failed.",
                        new System.ComponentModel.Win32Exception(actualError));
                }
                else
                {
                    using var createdHandle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
                    using var stream = new FileStream(createdHandle, FileAccess.Write);
                    stream.Write(bytes); stream.Flush(flushToDisk: true);
                }
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        using var handle = OpenOriginal(path, 0);
        var first = Observe(handle, false);
        if (first.Size != 32) throw new UnauthorizedAccessException("The original authentication key has an invalid size.");
        var actual = new byte[32];
        if (RandomAccess.Read(handle, actual, 0) != actual.Length || Observe(handle, false) != first
            || ObservePath(path, false) != first)
            throw new UnauthorizedAccessException("The original authentication key changed while it was read.");
        Validate(); return actual;
    }

    private static SafeFileHandle OpenOriginal(string path, int flags)
    {
        // Resolve every component with actual openat(O_NOFOLLOW), never just the final
        // filename. A symlinked ancestor cannot supply the protected store identity.
        var parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var fd = Open("/", DirectoryFlag | NoFollow | CloseOnExec);
        if (fd < 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var current = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        try
        {
            for (var index = 0; index < parts.Length; index++)
            {
                var next = OpenAt(current.DangerousGetHandle().ToInt32(), parts[index],
                    (index == parts.Length - 1 ? flags : DirectoryFlag) | NoFollow | CloseOnExec, 0);
                if (next < 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "Actual no-follow personal-store path component could not be acquired.");
                current.Dispose(); current = new SafeFileHandle((IntPtr)next, ownsHandle: true);
            }
            var result = current; current = null!; return result;
        }
        finally { current?.Dispose(); }
    }
    private Identity Observe(SafeFileHandle handle, bool directory)
    {
        if (Statx(handle.DangerousGetHandle().ToInt32(), "", EmptyPath, StatBasic, out var stat) != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Actual personal-store handle identity is unavailable.");
        return Demand(stat, directory);
    }
    private Identity ObservePath(string path, bool directory)
    {
        using var handle = OpenOriginal(path, directory ? DirectoryFlag : 0);
        return Observe(handle, directory);
    }
    private Identity Demand(StatxBuffer stat, bool directory)
    {
        var expectedType = directory ? 0x4000 : 0x8000;
        if ((stat.Mask & StatBasic) != StatBasic || stat.Uid != _uid || (stat.Mode & 0xf000) != expectedType
            || (stat.Mode & 0x3f) != 0 || stat.LinkCount == 0 || !directory && stat.LinkCount != 1)
            throw new UnauthorizedAccessException("Cold recovery requires a private current-principal directory and regular single-link database/key files; no chmod/adoption is inferred.");
        return new(stat.Inode, stat.DeviceMajor, stat.DeviceMinor, stat.Uid, stat.Mode, stat.Size);
    }
    public void Dispose()
    {
        if (_windows is { } windows) { windows.Dispose(); return; }
        if (_disposed) return; _disposed = true;
        try { _database.Dispose(); } finally { _directory.Dispose(); }
    }
    private readonly record struct Identity(ulong Inode, uint DeviceMajor, uint DeviceMinor, uint Uid, ushort Mode, ulong Size)
    {
        internal bool SameObject(Identity other) => Inode == other.Inode && DeviceMajor == other.DeviceMajor
            && DeviceMinor == other.DeviceMinor && Uid == other.Uid && Mode == other.Mode;
    }
    // statx has a fixed UAPI layout (unlike struct stat). Only the kernel-provided fields
    // below are consumed; timestamps/spare fields retain their exact native offsets.
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct StatxBuffer
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(16)] internal uint LinkCount;
        [FieldOffset(20)] internal uint Uid;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(40)] internal ulong Size;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)] private static extern int OpenAt(int directoryFd, string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int directoryFd, string path, int flags, uint mask, out StatxBuffer result);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEffectiveUserId();
}
