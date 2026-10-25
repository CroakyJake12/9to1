using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Actual systemd/logind evidence for the registered Root process and one
/// current active local graphical login. Neither arguments nor environment identify
/// the user. This context creates no profile, enrollment, package or permission.</summary>
public sealed class LinuxHomeRegisteredRootServiceContext
{
    public const string ServiceUnit = "9to1-root.service";
    private readonly ConditionalWeakTable<InteractiveUser, object> _issued = new();
    public sealed class InteractiveUser
    {
        internal readonly LinuxHomeRegisteredRootServiceContext Owner;
        internal InteractiveUser(LinuxHomeRegisteredRootServiceContext owner, string session, uint uid, string type, string? display)
        { Owner = owner; SessionId = session; UserId = uid; SessionType = type; Display = display; }
        public string SessionId { get; }
        public uint UserId { get; }
        public string OperatingSystemPrincipalId => "unix-euid:" + UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public string SessionType { get; }
        public string? Display { get; }
    }
    public sealed record Observation(InteractiveUser? OriginalUser, string? UnavailableReason);

    internal static bool IsOriginalRegisteredProcess(int pid) => pid > 0 && OperatingSystem.IsLinux() &&
        ReadString((out IntPtr pointer) => SdPidGetUnit(pid, out pointer)) == ServiceUnit;

    public Observation ObserveOriginalActiveUser()
    {
        if (!OperatingSystem.IsLinux() || LinuxOriginalDownloadPaths.CurrentUid() != 0)
            return new(null, "ActualLinuxRootEffectiveUidRequired");
        try
        {
            if (ReadString((out IntPtr pointer) => SdPidGetUnit(0, out pointer)) != ServiceUnit)
                return new(null, "ActualRegisteredLinuxRootServiceRequired");
            var users = ReadActiveUsers();
            if (users.Count != 1) return new(null, users.Count == 0 ? "ActiveLocalGraphicalLoginRequired" : "AmbiguousActiveLocalGraphicalLogins");
            var selected = users[0];
            _issued.Add(selected, this);
            DemandOriginalCurrent(selected);
            return new(selected, null);
        }
        catch (DllNotFoundException) { return new(null, "SystemdLogindRuntimeUnavailable"); }
        catch (EntryPointNotFoundException) { return new(null, "SystemdLogindRuntimeUnsupported"); }
    }
    public void DemandOriginalCurrent(InteractiveUser user)
    {
        if (!OperatingSystem.IsLinux() || LinuxOriginalDownloadPaths.CurrentUid() != 0 || user is null ||
            !ReferenceEquals(user.Owner, this) || !_issued.TryGetValue(user, out _) ||
            ReadString((out IntPtr pointer) => SdPidGetUnit(0, out pointer)) != ServiceUnit)
            throw new UnauthorizedAccessException("The SAME actual registered Root/local-login observation is required.");
        var current = ReadActiveUsers();
        if (current.Count != 1 || current[0].SessionId != user.SessionId || current[0].UserId != user.UserId ||
            current[0].SessionType != user.SessionType || current[0].Display != user.Display)
            throw new UnauthorizedAccessException("The original active local graphical login changed or became ambiguous.");
    }
    private List<InteractiveUser> ReadActiveUsers()
    {
        var count = SdGetSessions(out var sessions);
        if (count < 0) throw new IOException("The actual logind session query failed: " + count);
        if (count > 4096) { FreeSessions(sessions, count); throw new IOException("The actual logind session set exceeds the supported bound."); }
        var result = new List<InteractiveUser>();
        try
        {
            for (var index = 0; index < count; index++)
            {
                var session = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(sessions, checked(index * IntPtr.Size)));
                if (session is not { Length: > 0 and <= 256 }) throw new IOException("The actual logind session identifier is invalid.");
                var active = SdSessionIsActive(session); var remote = SdSessionIsRemote(session);
                if (active < 0 || remote < 0) continue; // a session can retire during enumeration
                if (active != 1 || remote != 0 || ReadString((out IntPtr pointer) => SdSessionGetClass(session, out pointer)) != "user") continue;
                var type = ReadString((out IntPtr pointer) => SdSessionGetType(session, out pointer));
                if (type is not ("x11" or "wayland")) continue;
                if (SdSessionGetUid(session, out var uid) < 0 || uid == 0) continue;
                var display = ReadString((out IntPtr pointer) => SdSessionGetDisplay(session, out pointer));
                if (type == "x11" && (display is not { Length: > 1 and <= 128 } || display[0] != ':' ||
                    display.Skip(1).Any(value => !char.IsAsciiDigit(value) && value != '.'))) continue;
                result.Add(new(this, session, uid, type, display));
            }
            return result;
        }
        finally { FreeSessions(sessions, count); }
    }
    private delegate int StringQuery(out IntPtr value);
    private static string? ReadString(StringQuery query)
    {
        var result = query(out var pointer);
        try
        {
            if (result < 0) return null;
            var value = Marshal.PtrToStringUTF8(pointer);
            return value is { Length: <= 4096 } ? value : throw new IOException("The actual systemd string exceeds its supported bound.");
        }
        finally { if (pointer != IntPtr.Zero) Free(pointer); }
    }
    private static void FreeSessions(IntPtr sessions, int count)
    {
        if (sessions == IntPtr.Zero) return;
        try { for (var index = 0; index < count; index++) { var entry = Marshal.ReadIntPtr(sessions, checked(index * IntPtr.Size)); if (entry != IntPtr.Zero) Free(entry); } }
        finally { Free(sessions); }
    }
    [DllImport("libsystemd.so.0", EntryPoint = "sd_pid_get_unit")] private static extern int SdPidGetUnit(int pid, out IntPtr unit);
    [DllImport("libsystemd.so.0", EntryPoint = "sd_get_sessions")] private static extern int SdGetSessions(out IntPtr sessions);
    [DllImport("libsystemd.so.0", EntryPoint = "sd_session_is_active")] private static extern int SdSessionIsActive([MarshalAs(UnmanagedType.LPUTF8Str)] string session);
    [DllImport("libsystemd.so.0", EntryPoint = "sd_session_is_remote")] private static extern int SdSessionIsRemote([MarshalAs(UnmanagedType.LPUTF8Str)] string session);
    [DllImport("libsystemd.so.0", EntryPoint = "sd_session_get_uid")] private static extern int SdSessionGetUid([MarshalAs(UnmanagedType.LPUTF8Str)] string session, out uint uid);
    [DllImport("libsystemd.so.0", EntryPoint = "sd_session_get_type")] private static extern int SdSessionGetType([MarshalAs(UnmanagedType.LPUTF8Str)] string session, out IntPtr type);
    [DllImport("libsystemd.so.0", EntryPoint = "sd_session_get_class")] private static extern int SdSessionGetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string session, out IntPtr type);
    [DllImport("libsystemd.so.0", EntryPoint = "sd_session_get_display")] private static extern int SdSessionGetDisplay([MarshalAs(UnmanagedType.LPUTF8Str)] string session, out IntPtr display);
    [DllImport("libc", EntryPoint = "free")] private static extern void Free(IntPtr value);
}
