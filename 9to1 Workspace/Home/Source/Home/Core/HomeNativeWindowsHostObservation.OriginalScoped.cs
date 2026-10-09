using System.IO.Pipes;
using System.Security.Principal;
using Haven.Application;

namespace HavenOS.Home.Core;

internal static partial class HomeNativeWindowsHostObservation
{
    internal static Task<HomeNativeObservedPeer?> FromConnectedPipeWithinOriginalSourceAsync(
        NamedPipeClientStream sameConnectedPipe, HomeNativeOriginalStartupScope source) =>
        source.BeginChild(child => Task.FromResult(child.Invoke(() =>
        {
            ArgumentNullException.ThrowIfNull(sameConnectedPipe);
            if (!OperatingSystem.IsWindows() || !sameConnectedPipe.IsConnected) return null;
            if (!GetNamedPipeServerProcessId(sameConnectedPipe.SafePipeHandle, out var processId) ||
                processId is 0 or > int.MaxValue) return null;
            var process = child.CaptureNative(OpenProcess(0x1000, false, processId));
            if (process.IsInvalid) return null;
            var opened = OpenProcessToken(process, 0x0008, out var token);
            child.CaptureNative(token); // Even a failed native open returns its actual wrapper.
            if (!opened || token.IsInvalid) return null;
            var identity = child.CaptureNative(new WindowsIdentity(token.DangerousGetHandle()));
            var sid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(sid) || !sameConnectedPipe.IsConnected ||
                !GetNamedPipeServerProcessId(sameConnectedPipe.SafePipeHandle, out var current) || current != processId) return null;
            return new HomeNativeObservedPeer((int)processId, "windows-sid:" + sid);
        })));
}
