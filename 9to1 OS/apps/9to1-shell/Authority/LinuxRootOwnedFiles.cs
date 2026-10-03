using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Root installation evidence is read from no-follow handles; user-writable paths never supply trust.</summary>
internal static class LinuxRootOwnedFiles
{
    private const int NoFollow = 0x20000, CloseOnExec = 0x80000, NonBlocking = 0x800, DirectoryFlag = 0x10000;
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Stat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanoseconds;
        [FieldOffset(112)] public long ModifySeconds;
        [FieldOffset(120)] public uint ModifyNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat stat);

    public static async Task<byte[]?> ReadAsync(string path, int maximumBytes, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !ParentsImmutable(path)) return null;
        using var handle = Handle(path, false); if (handle is null || !Immutable(handle, false, out var before) || before.Size > (ulong)maximumBytes) return null;
        await using var stream = new FileStream(handle, FileAccess.Read, 65536, false);
        using var bytes = new MemoryStream((int)before.Size);
        var buffer = new byte[65536];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct); if (count == 0) break;
            if (bytes.Length + count > maximumBytes) return null;
            bytes.Write(buffer, 0, count);
        }
        if (!Immutable(handle, false, out var after) || before != after || (ulong)bytes.Length != before.Size || !ParentsImmutable(path)) return null;
        return bytes.ToArray();
    }
    public static async Task<bool> MatchesAsync(string path, long size, string sha256, CancellationToken ct, bool elfExecutable = false)
    {
        if (!OperatingSystem.IsLinux() || size < 0 || !ParentsImmutable(path)) return false;
        using var handle = Handle(path, false); if (handle is null || !Immutable(handle, false, out var before) || before.Size != (ulong)size) return false;
        if (elfExecutable && (before.Mode & 0x49) == 0) return false;
        await using var stream = new FileStream(handle, FileAccess.Read, 65536, false);
        if (elfExecutable)
        {
            var prefix = new byte[4]; if (await stream.ReadAsync(prefix, ct) != 4 || !prefix.AsSpan().SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46 })) return false;
            stream.Position = 0;
        }
        var digest = await SHA256.HashDataAsync(stream, ct);
        return Immutable(handle, false, out var after) && before == after && ParentsImmutable(path) &&
            CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(sha256));
    }
    public static bool RegularFileImmutable(string path) => OperatingSystem.IsLinux() && ParentsImmutable(path) && OwnImmutable(path, false);
    public static bool DirectoryImmutable(string path) => OperatingSystem.IsLinux() && ParentsImmutable(path) && OwnImmutable(path, true);
    private static bool ParentsImmutable(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path) return false;
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            if (!OwnImmutable(parent, true)) return false;
        return true;
    }
    private static bool OwnImmutable(string path, bool directory)
    { using var handle = Handle(path, directory); return handle is not null && Immutable(handle, directory, out _); }
    private static SafeFileHandle? Handle(string path, bool directory)
    {
        var fd = Open(path, NoFollow | CloseOnExec | NonBlocking | (directory ? DirectoryFlag : 0));
        return fd < 0 ? null : new((IntPtr)fd, true);
    }
    private static bool Immutable(SafeFileHandle handle, bool directory, out FileEvidence evidence)
    {
        evidence = default;
        if (Statx((int)handle.DangerousGetHandle(), "", 0x1000 | 0x100, 0x7ff, out var stat) != 0 || (stat.Mask & 0x3cb) != 0x3cb ||
            stat.Uid != 0 || (stat.Mode & 0x12) != 0 || (stat.Mode & 0xf000) != (directory ? 0x4000 : 0x8000)) return false;
        evidence = new(stat.Inode, stat.Size, stat.Mode, stat.DeviceMajor, stat.DeviceMinor, stat.ChangeSeconds, stat.ChangeNanoseconds, stat.ModifySeconds, stat.ModifyNanoseconds);
        return true;
    }
    private readonly record struct FileEvidence(ulong Inode, ulong Size, ushort Mode, uint DeviceMajor, uint DeviceMinor, long ChangeSeconds, uint ChangeNanoseconds, long ModifySeconds, uint ModifyNanoseconds);
}
