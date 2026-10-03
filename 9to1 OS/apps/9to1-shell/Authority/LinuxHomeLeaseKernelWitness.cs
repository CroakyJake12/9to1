using Haven.Application;
using System.Globalization;
using System.Runtime.InteropServices;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Actual descriptor/lock observation, never a launch or session grant. A trusted root
/// supervisor must separately bind this private observation to its own originally spawned child
/// and bootstrap epoch. Reused fd numbers plus the same inode cannot distinguish a closed
/// and reopened open-file description; private epoch/current held-lease checks remain mandatory.
/// A caller-supplied PID/path/GUID is not that independent issuance.</summary>
public sealed class LinuxHomeLeaseKernelWitness
{
    private readonly HomeNativeObservedPeer _peer;
    private readonly LinuxProcessIdentity _process;
    private readonly string _path;
    private readonly string _descriptor;
    private readonly FileIdentity _file;
    private LinuxHomeLeaseKernelWitness(HomeNativeObservedPeer peer, LinuxProcessIdentity process,
        string path, string descriptor, FileIdentity file)
    { _peer = peer; _process = process; _path = path; _descriptor = descriptor; _file = file; }

    public static async ValueTask<LinuxHomeLeaseKernelWitness?> ObserveAsync(HomeNativeObservedPeer peer,
        string canonicalLeasePath, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(canonicalLeasePath) ||
            Path.GetFullPath(canonicalLeasePath) != canonicalLeasePath) return null;
        try
        {
            var process = await LinuxProcessIdentity.ReadAsync(peer, ct).ConfigureAwait(false);
            if (process is null || !ReadIdentity(canonicalLeasePath, true, out var file) ||
                !uint.TryParse(peer.OperatingSystemPrincipalId.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var ownerUid) ||
                file.Uid != ownerUid) return null;
            var root = $"/proc/{peer.ProcessId}/fdinfo";
            var descriptors = Directory.EnumerateFiles(root).Take(4097).ToArray();
            if (descriptors.Length > 4096) return null;
            LinuxHomeLeaseKernelWitness? found = null;
            foreach (var item in descriptors)
            {
                ct.ThrowIfCancellationRequested(); var descriptor = Path.GetFileName(item);
                if (!int.TryParse(descriptor, NumberStyles.None, CultureInfo.InvariantCulture, out var fd) || fd < 0) continue;
                var candidate = new LinuxHomeLeaseKernelWitness(peer, process, canonicalLeasePath, descriptor, file);
                if (!await candidate.HasOriginalDescriptorLockAsync(ct).ConfigureAwait(false)) continue;
                if (found is not null) return null; // Ambiguous duplicate admission is not a unique original witness.
                found = candidate;
            }
            return found is not null && await found.IsCurrentAsync(ct).ConfigureAwait(false) ? found : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException) { return null; }
    }
    public async ValueTask<bool> IsCurrentAsync(CancellationToken ct)
    {
        try
        {
            if (_process != await LinuxProcessIdentity.ReadAsync(_peer, ct).ConfigureAwait(false) ||
                !await HasOriginalDescriptorLockAsync(ct).ConfigureAwait(false)) return false;
            return _process == await LinuxProcessIdentity.ReadAsync(_peer, ct).ConfigureAwait(false) &&
                await HasOriginalDescriptorLockAsync(ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException) { return false; }
    }
    private async ValueTask<bool> HasOriginalDescriptorLockAsync(CancellationToken ct)
    {
        var root = $"/proc/{_peer.ProcessId}"; var fdPath = root + "/fd/" + _descriptor;
        if (new FileInfo(fdPath).LinkTarget != _path || !ReadIdentity(_path, true, out var current) || current != _file ||
            !ReadIdentity(fdPath, false, out var descriptorFile) || descriptorFile != _file) return false;
        var info = await LinuxProcBoundedObservation.ReadAsync(root + "/fdinfo/" + _descriptor, 65536, ct).ConfigureAwait(false);
        if (info is null) return false;
        var matching = 0;
        foreach (var line in info.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 9 || fields[0] != "lock:" || !fields[1].EndsWith(':') ||
                fields[2] != "FLOCK" || fields[3] != "ADVISORY" || fields[4] != "WRITE" ||
                fields[5] != _peer.ProcessId.ToString(CultureInfo.InvariantCulture) || fields[7] != "0" || fields[8] != "EOF") continue;
            var device = fields[6].Split(':');
            if (device.Length == 3 && uint.TryParse(device[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var major) &&
                uint.TryParse(device[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var minor) &&
                ulong.TryParse(device[2], NumberStyles.None, CultureInfo.InvariantCulture, out var inode) &&
                major == _file.Major && minor == _file.Minor && inode == _file.Inode) matching++;
        }
        return matching == 1 && new FileInfo(fdPath).LinkTarget == _path &&
            ReadIdentity(_path, true, out current) && current == _file &&
            ReadIdentity(fdPath, false, out descriptorFile) && descriptorFile == _file;
    }
    private readonly record struct FileIdentity(uint Uid, ulong Inode, uint Major, uint Minor);
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Stat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint Major;
        [FieldOffset(140)] public uint Minor;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat stat);
    private static bool ReadIdentity(string path, bool noFollow, out FileIdentity file)
    {
        file = default;
        if (Statx(-100, path, noFollow ? 0x100 : 0, 0x7ff, out var stat) != 0 ||
            (stat.Mask & 0x10b) != 0x10b || (stat.Mode & 0xf000) != 0x8000) return false;
        file = new(stat.Uid, stat.Inode, stat.Major, stat.Minor); return true;
    }
}
