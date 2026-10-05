using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Trusted native host-local TASK identity. Reads the effective OS account/token and its
/// actual account profile, not environment usernames, CAKE subjects or a fabricated Home profile.
/// Register this concrete source only as TaskRunPermissionAuthority's local host input. Its identity
/// grants no resource/installed-application/Home rights; those owners keep their own canonical actor.
/// Browser execution must supply its genuine verified session source instead.</summary>
public sealed class HostLocalTaskActorSource : IAuthenticatedResourceActorSource
{
    public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var first = Observe();
        token.ThrowIfCancellationRequested();
        var second = Observe();
        return ValueTask.FromResult(first is not null && first == second ? first : null);
    }
    private static AuthenticatedResourceActor? Observe()
    {
        if (OperatingSystem.IsWindows()) return ObserveWindows();
        if (OperatingSystem.IsLinux()) return ObserveLinux();
        return null; // No username/path/anonymous fallback on unsupported hosts.
    }
    private static AuthenticatedResourceActor Create(string principal, string actualProfile, string authentication)
    {
        var profile = Path.GetFullPath(actualProfile);
        if (!Directory.Exists(profile)) throw new UnauthorizedAccessException("Actual OS account profile is unavailable.");
        if (OperatingSystem.IsWindows()) profile = profile.ToUpperInvariant();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principal + "\n" + profile)));
        return new("host-local-task:" + principal, "host-local-os-profile:" + digest, null, null, authentication);
    }
    [SupportedOSPlatform("windows")]
    private static AuthenticatedResourceActor? ObserveWindows()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsAuthenticated || identity.User?.Value is not { } sid) return null;
        var actualToken = identity.AccessToken;
        if (!GetTokenInformation(actualToken, 10, out TokenStatistics statistics, Marshal.SizeOf<TokenStatistics>(), out var returned) ||
            returned != Marshal.SizeOf<TokenStatistics>())
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Actual Windows token identity could not be read.");
        uint length = 0;
        _ = GetUserProfileDirectory(actualToken, null, ref length);
        if (length is < 2 or > 32768) throw new UnauthorizedAccessException("Token-bound OS profile length is invalid.");
        var profile = new StringBuilder((int)length);
        if (!GetUserProfileDirectory(actualToken, profile, ref length))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Token-bound OS account profile is unavailable.");
        // AuthenticationId is the kernel logon LUID; ModifiedId observes effective-token changes.
        var auth = $"host-local-windows-logon:{statistics.AuthenticationId.High:X8}{statistics.AuthenticationId.Low:X8}:token:{statistics.ModifiedId.High:X8}{statistics.ModifiedId.Low:X8}";
        return Create("windows-sid:" + sid, profile.ToString(), auth);
    }
    private static AuthenticatedResourceActor? ObserveLinux()
    {
        var uid = GetEffectiveUserId();
        var buffer = Marshal.AllocHGlobal(65536);
        try
        {
            var status = GetPasswordEntry(uid, out var entry, buffer, 65536, out var actual);
            if (status != 0 || actual == IntPtr.Zero || entry.Uid != uid)
                throw new UnauthorizedAccessException("Actual effective UID/account profile is unavailable.");
            var profile = ReadBoundedAccountField(entry.Directory, buffer, 65536);
            var session = GetSessionId(0);
            if (session < 0) throw new UnauthorizedAccessException("Actual kernel session is unavailable.");
            using var process = Process.GetCurrentProcess();
            if (GetEffectiveUserId() != uid) return null;
            // Linux has no Windows-style logon LUID here. Label the observed kernel session and
            // actual host process activation accurately; this is not a CAKE/Home authentication revision.
            return Create("unix-euid:" + uid, profile,
                $"host-local-linux-session:{session}:activation:{process.Id}:{process.StartTime.ToUniversalTime().Ticks}");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static string ReadBoundedAccountField(IntPtr value, IntPtr buffer, int size)
    {
        var offset = value.ToInt64() - buffer.ToInt64();
        if (offset < 0 || offset >= size) throw new UnauthorizedAccessException("Actual account profile pointer is outside its owned lookup buffer.");
        var available = Math.Min(32768, size - (int)offset);
        var bytes = new List<byte>();
        for (var i = 0; i < available; i++)
        {
            var next = Marshal.ReadByte(value, i);
            if (next == 0)
            {
                var text = new UTF8Encoding(false, true).GetString(bytes.ToArray());
                if (!Path.IsPathFullyQualified(text)) throw new UnauthorizedAccessException("Actual OS profile is not an absolute path.");
                return text;
            }
            bytes.Add(next);
        }
        throw new UnauthorizedAccessException("Actual OS account profile is unbounded.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenStatistics
    {
        public Luid TokenId; public Luid AuthenticationId; public long ExpirationTime;
        public int TokenType; public int ImpersonationLevel;
        public uint DynamicCharged; public uint DynamicAvailable; public uint GroupCount; public uint PrivilegeCount;
        public Luid ModifiedId;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PasswordEntry
    {
        public IntPtr Name; public IntPtr Password; public uint Uid; public uint Gid;
        public IntPtr Gecos; public IntPtr Directory; public IntPtr Shell;
    }
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token,
        int informationClass, out TokenStatistics statistics, int length, out int returned);
    [DllImport("userenv.dll", EntryPoint = "GetUserProfileDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetUserProfileDirectory(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token,
        StringBuilder? profile, ref uint length);
    [DllImport("libc", EntryPoint = "geteuid", ExactSpelling = true)] private static extern uint GetEffectiveUserId();
    [DllImport("libc", EntryPoint = "getsid", ExactSpelling = true)] private static extern int GetSessionId(int pid);
    [DllImport("libc", EntryPoint = "getpwuid_r", ExactSpelling = true)]
    private static extern int GetPasswordEntry(uint uid, out PasswordEntry entry, IntPtr buffer, nuint size, out IntPtr actual);
}
