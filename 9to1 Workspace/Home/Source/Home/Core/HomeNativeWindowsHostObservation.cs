using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Home.Core;

/// <summary>Actual kernel observation of THIS connected Windows Home server. No configured principal is accepted as evidence.</summary>
internal static class HomeNativeWindowsHostObservation
{
    internal static HomeNativeObservedPeer? FromConnectedPipe(NamedPipeClientStream original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!OperatingSystem.IsWindows() || !original.IsConnected) return null;
        if (!GetNamedPipeServerProcessId(original.SafePipeHandle, out var processId) ||
            processId is 0 or > int.MaxValue) return null;
        using var process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid) return null;
        var opened = OpenProcessToken(process, 0x0008, out var token); // TOKEN_QUERY
        using (token)
        {
            if (!opened || token.IsInvalid) return null;
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            var sid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(sid) || !original.IsConnected ||
                !GetNamedPipeServerProcessId(original.SafePipeHandle, out var current) || current != processId) return null;
            // The installed-host verifier must independently bind the current controlled process,
            // executable/receipt/launch lifetime and PID reuse. PID/SID metadata alone grants nothing.
            return new((int)processId, "windows-sid:" + sid);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
