using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Early administrator-launched child transition, before profile or backend IO.
/// This is not launch attestation: only the protected supervisor can issue the original
/// process/installation/session evidence. Callers must exit on failure, never open the GUI.</summary>
[SupportedOSPlatform("linux")]
internal static class LinuxControlledChildPrivilegeBoundary
{
    private const int SetDumpable = 4, GetDumpable = 3, GetKeepCaps = 7, SetKeepCaps = 8, GetSecureBits = 27;
    private const int SetNoNewPrivileges = 38, GetNoNewPrivileges = 39;
    private const int CapabilityBoundingDrop = 24, CapabilityAmbient = 47, AmbientClearAll = 4;
    private const uint CapabilityVersion3 = 0x20080522;
    [StructLayout(LayoutKind.Sequential)] private struct CapabilityHeader { public uint Version; public int ProcessId; }
    [StructLayout(LayoutKind.Sequential)] private struct CapabilityWords { public uint Effective, Permitted, Inheritable; }
    [StructLayout(LayoutKind.Sequential)] private struct CapabilityData { public CapabilityWords Low, High; }
    [DllImport("libc", EntryPoint = "getuid")] private static extern uint GetUid();
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEffectiveUid();
    [DllImport("libc", EntryPoint = "getgid")] private static extern uint GetGid();
    [DllImport("libc", EntryPoint = "getegid")] private static extern uint GetEffectiveGid();
    [DllImport("libc", EntryPoint = "getresuid", SetLastError = true)] private static extern int GetUserIds(out uint real, out uint effective, out uint saved);
    [DllImport("libc", EntryPoint = "getresgid", SetLastError = true)] private static extern int GetGroupIds(out uint real, out uint effective, out uint saved);
    [DllImport("libc", EntryPoint = "getgroups", SetLastError = true)] private static extern int GetGroups(int count, IntPtr groups);
    [DllImport("libc", EntryPoint = "setgroups", SetLastError = true)] private static extern int SetGroups(nuint count, IntPtr groups);
    [DllImport("libc", EntryPoint = "setresuid", SetLastError = true)] private static extern int SetUserIds(uint real, uint effective, uint saved);
    [DllImport("libc", EntryPoint = "setresgid", SetLastError = true)] private static extern int SetGroupIds(uint real, uint effective, uint saved);
    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)] private static extern int ProcessControl(int option, nuint first, nuint second, nuint third, nuint fourth);
    [DllImport("libc", EntryPoint = "capset", SetLastError = true)] private static extern int SetCapabilities(ref CapabilityHeader header, ref CapabilityData data);
    [DllImport("libc", EntryPoint = "capget", SetLastError = true)] private static extern int GetCapabilities(ref CapabilityHeader header, ref CapabilityData data);

    // Target IDs are selected by the actual administrator supervisor, not enrollment JSON,
    // a received actor, or an application request. This function issues no authority receipt.
    public static void ApplyBeforeProfileIo(uint targetUid, uint targetGid)
    {
        if (!OperatingSystem.IsLinux() || targetUid is 0 or uint.MaxValue || targetGid is 0 or uint.MaxValue ||
            GetUid() != 0 || GetEffectiveUid() != 0 ||
            ProcessControl(GetNoNewPrivileges, 0, 0, 0, 0) != 1 ||
            ProcessControl(GetKeepCaps, 0, 0, 0, 0) != 0 || ProcessControl(GetSecureBits, 0, 0, 0, 0) != 0)
            throw new UnauthorizedAccessException("A genuine administrator child with safe pre-exec inherited controls and non-root target IDs is required.");
        // Credential changes consult this actual kernel policy. Require non-dumpable
        // transitions so the later explicit setting is not preceded by a same-UID
        // ptrace window while the libc wrapper coordinates runtime threads.
        using (var policy = new FileStream("/proc/sys/fs/suid_dumpable", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Span<byte> bytes = stackalloc byte[4]; var count = policy.Read(bytes);
            if (count != 2 || bytes[0] != (byte)'0' || bytes[1] != (byte)'\n' || policy.ReadByte() != -1)
                throw new UnauthorizedAccessException("Actual non-dumpable credential-transition policy is required.");
        }
        // NNP and bounding sets are per-thread. NNP must already have been established
        // by the actual supervisor BEFORE exec, so every CLR thread inherits it. This
        // main-thread call cannot retroactively secure pre-existing runtime threads.
        Require(ProcessControl(SetDumpable, 0, 0, 0, 0));
        Require(ProcessControl(SetKeepCaps, 0, 0, 0, 0));
        Require(ProcessControl(SetNoNewPrivileges, 1, 0, 0, 0));
        // Drop this calling thread's supported bounding set before losing CAP_SETPCAP. Unsupported
        // capability numbers (EINVAL) are not evidence for a supported capability.
        for (var capability = 0; capability < 64; capability++)
        {
            var result = ProcessControl(CapabilityBoundingDrop, (nuint)capability, 0, 0, 0);
            if (result != 0 && (capability == 0 || Marshal.GetLastPInvokeError() != 22)) Require(result);
        }
        Require(ProcessControl(CapabilityAmbient, AmbientClearAll, 0, 0, 0));
        Require(SetGroups(0, IntPtr.Zero));
        Require(SetGroupIds(targetGid, targetGid, targetGid));
        Require(SetUserIds(targetUid, targetUid, targetUid));
        var header = new CapabilityHeader { Version = CapabilityVersion3, ProcessId = 0 };
        var empty = new CapabilityData();
        Require(SetCapabilities(ref header, ref empty));
        // Linux changes dumpability on credential transitions; enforce it again AFTER
        // dropping IDs rather than relying on the pre-transition setting.
        Require(ProcessControl(SetDumpable, 0, 0, 0, 0));
        var observed = new CapabilityData(); Require(GetCapabilities(ref header, ref observed));
        Require(GetUserIds(out var realUid, out var effectiveUid, out var savedUid));
        Require(GetGroupIds(out var realGid, out var effectiveGid, out var savedGid));
        if (GetUid() != targetUid || GetEffectiveUid() != targetUid || GetGid() != targetGid || GetEffectiveGid() != targetGid ||
            realUid != targetUid || effectiveUid != targetUid || savedUid != targetUid ||
            realGid != targetGid || effectiveGid != targetGid || savedGid != targetGid ||
            GetGroups(0, IntPtr.Zero) != 0 || ProcessControl(GetDumpable, 0, 0, 0, 0) != 0 ||
            ProcessControl(GetNoNewPrivileges, 0, 0, 0, 0) != 1 ||
            observed.Low.Effective != 0 || observed.Low.Permitted != 0 || observed.Low.Inheritable != 0 ||
            observed.High.Effective != 0 || observed.High.Permitted != 0 || observed.High.Inheritable != 0)
            throw new UnauthorizedAccessException("The child privilege transition could not be confirmed.");
        VerifyActualThreadCredentials(targetUid, targetGid);
    }
    private static void VerifyActualThreadCredentials(uint uid, uint gid)
    {
        var before = Directory.EnumerateDirectories("/proc/self/task").Take(4097).ToArray();
        if (before.Length is < 1 or > 4096) throw new UnauthorizedAccessException("Bounded actual runtime thread observations required.");
        foreach (var directory in before)
        {
            if (!int.TryParse(Path.GetFileName(directory), out var tid) || tid <= 0)
                throw new UnauthorizedAccessException("Actual kernel thread identity required.");
            using var stream = new FileStream(Path.Combine(directory, "status"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[65537]; var used = 0;
            while (used < bytes.Length)
            {
                var count = stream.Read(bytes, used, bytes.Length - used); if (count == 0) break; used += count;
            }
            if (used == bytes.Length) throw new UnauthorizedAccessException("Kernel thread status exceeded its bound.");
            var fields = System.Text.Encoding.ASCII.GetString(bytes, 0, used).Split('\n')
                .Where(line => line.Contains(':')).ToDictionary(line => line[..line.IndexOf(':')], line => line[(line.IndexOf(':') + 1)..].Trim(), StringComparer.Ordinal);
            static bool Ids(string? value, uint expected) => value is not null &&
                value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is { Length: 4 } values &&
                values.All(item => uint.TryParse(item, out var actual) && actual == expected);
            if (!fields.TryGetValue("Uid", out var uids) || !Ids(uids, uid) ||
                !fields.TryGetValue("Gid", out var gids) || !Ids(gids, gid) ||
                !fields.TryGetValue("Groups", out var groups) || groups.Length != 0 ||
                !fields.TryGetValue("NoNewPrivs", out var nnp) || nnp != "1" ||
                new[] { "CapEff", "CapPrm", "CapInh", "CapAmb" }.Any(name =>
                    !fields.TryGetValue(name, out var value) || value.Length != 16 || value.Any(character => character != '0')))
                throw new UnauthorizedAccessException("An actual runtime thread retained privileges or mismatched credentials.");
        }
        // A changing runtime thread cohort needs a fresh transition observation; it is
        // not accepted by assuming the unobserved thread inherited a safe state.
        if (!before.Order(StringComparer.Ordinal).SequenceEqual(Directory.EnumerateDirectories("/proc/self/task").Take(4097).Order(StringComparer.Ordinal)))
            throw new UnauthorizedAccessException("The runtime thread cohort changed during privilege observation.");
    }
    private static void Require(int result)
    {
        if (result != 0) throw new UnauthorizedAccessException("The required early child privilege transition failed.");
    }
}
