using System.Net.Sockets;
using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Reads actual accepted Unix socket peer credentials. Unsupported platforms fail closed.</summary>
public static class HomeNativePeerObservation
{
    public static HomeNativeObservedPeer? FromAcceptedUnixSocket(Socket accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        if (!OperatingSystem.IsLinux() || accepted.AddressFamily != AddressFamily.Unix || !accepted.Connected) return null;
        var handle = accepted.SafeHandle;
        var held = false;
        try
        {
            handle.DangerousAddRef(ref held);
            var credentials = new LinuxPeerCredentials();
            uint length = (uint)Marshal.SizeOf<LinuxPeerCredentials>();
            if (GetSocketOption(handle.DangerousGetHandle().ToInt32(), 1, 17, ref credentials, ref length) != 0 ||
                length != Marshal.SizeOf<LinuxPeerCredentials>() || credentials.ProcessId <= 0) return null;
            return new(credentials.ProcessId, "unix-euid:" + credentials.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { if (held) handle.DangerousRelease(); }
    }
    public static HomeNativeObservedPeer? FromConnectedWindowsPipe(NamedPipeServerStream accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        if (!OperatingSystem.IsWindows() || !accepted.IsConnected) return null;
        if (!GetNamedPipeClientProcessId(accepted.SafePipeHandle, out var processId) || processId is 0 or > int.MaxValue) return null;
        string? principal = null;
        accepted.RunAsClient(() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            using var identity = WindowsIdentity.GetCurrent(ifImpersonating: true);
            principal = identity?.User?.Value;
        });
        return string.IsNullOrWhiteSpace(principal) ? null : new((int)processId, "windows-sid:" + principal);
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxPeerCredentials { public int ProcessId; public uint UserId; public uint GroupId; }
    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetSocketOption(int socket, int level, int option, ref LinuxPeerCredentials value, ref uint length);
}
