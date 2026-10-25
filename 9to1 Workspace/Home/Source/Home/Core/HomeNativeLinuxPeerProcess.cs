using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Physical process custody only. A peer-bound pidfd and running executable
/// inode prevent exit/PID reuse or exec from silently changing an accepted Unix peer.
/// These observations establish no installed package, publisher, controlled launch or grant.</summary>
internal sealed class HomeNativeLinuxPeerProcess : IDisposable
{
    private readonly Socket _socket;
    private readonly HomeNativeObservedPeer _peer;
    private readonly SafeFileHandle _pid;
    private readonly SafeFileHandle? _executable;
    private readonly ImageIdentity _image;
    private readonly object _gate = new();
    private bool _closed;
    private HomeNativeLinuxPeerProcess(Socket socket, HomeNativeObservedPeer peer,
        SafeFileHandle pid, SafeFileHandle? executable, ImageIdentity image)
    { _socket = socket; _peer = peer; _pid = pid; _executable = executable; _image = image; }

    internal static HomeNativeLinuxPeerProcess? Capture(Socket socket) => Capture(socket, requireImage: true);
    internal static HomeNativeLinuxPeerProcess? CaptureKernelServiceLifetime(Socket socket) => Capture(socket, requireImage: false);
    private static HomeNativeLinuxPeerProcess? Capture(Socket socket, bool requireImage)
    {
        var peer = HomeNativePeerObservation.FromAcceptedUnixSocket(socket);
        if (peer is null || !requireImage && peer.OperatingSystemPrincipalId != "unix-euid:0") return null;
        // SO_PEERPIDFD binds to the process that actually established THIS socket,
        // unlike pidfd_open(observed numeric PID) after an asynchronous gap.
        uint size = 4;
        if (GetPeerPidFd(socket.SafeHandle, 1, 77, out var descriptor, ref size) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 3) return null; // ESRCH: the original connected peer has exited
            if (error is 22 or 92 or 95)
                throw new HomeNativePeerProcessUnavailableException();
            throw new Win32Exception(error, "Retain the actual connected Home peer process.");
        }
        if (descriptor < 0) throw new IOException("The connected Home peer did not supply an actual pidfd.");
        var pid = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        SafeFileHandle? executable = null;
        try
        {
            if (size != 4) throw new IOException("The connected Home peer pidfd has an unsupported kernel shape.");
            if (!Alive(pid)) return null;
            if (!requireImage)
            {
                var serviceLifetime = new HomeNativeLinuxPeerProcess(socket, peer, pid, null, default);
                if (serviceLifetime.Observe() is null) return null;
                pid = null!; return serviceLifetime;
            }
            try { executable = File.OpenHandle($"/proc/{peer.ProcessId}/exe", FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (Exception cause) when (cause is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (IOException) when (!Alive(pid)) { return null; }
            var identity = ReadImage(executable);
            if ((identity.Mode & 0xf000) != 0x8000) throw new IOException("The running Home peer image is not a regular executable inode.");
            var held = new HomeNativeLinuxPeerProcess(socket, peer, pid, executable, identity);
            if (held.Observe() is null) return null;
            executable = null; pid = null!; // exact descriptors transfer only after full capture/currentness
            return held;
        }
        finally { executable?.Dispose(); pid?.Dispose(); }
    }

    internal HomeNativeObservedPeer? Observe()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!Alive(_pid) || HomeNativePeerObservation.FromAcceptedUnixSocket(_socket) != _peer) return null;
            if (_executable is null) return _peer; // lifetime/credentials only, never an executable/installed observation
            SafeFileHandle actual;
            try { actual = File.OpenHandle($"/proc/{_peer.ProcessId}/exe", FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (Exception cause) when (cause is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (IOException) when (!Alive(_pid)) { return null; }
            using (actual)
                return _image == ReadImage(_executable) && _image == ReadImage(actual) && Alive(_pid) ? _peer : null;
        }
    }

    private readonly record struct ImageIdentity(ulong Inode, uint Major, uint Minor, ushort Mode, uint Uid,
        ulong Size, long ModifiedSeconds, uint ModifiedNanos, long ChangedSeconds, uint ChangedNanos);
    private static ImageIdentity ReadImage(SafeFileHandle descriptor)
    {
        if (!BitConverter.IsLittleEndian || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Home peer process custody requires supported Linux64 statx.");
        var bytes = new byte[256]; const uint required = 1 | 2 | 8 | 0x40 | 0x80 | 0x100 | 0x200;
        if (Statx(descriptor, "", 0x1000, required, bytes) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Observe the SAME running Home peer executable inode.");
        if ((BitConverter.ToUInt32(bytes, 0) & required) != required)
            throw new PlatformNotSupportedException("The kernel omitted actual Home peer executable identity fields.");
        return new(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140),
            BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 20), BitConverter.ToUInt64(bytes, 40),
            BitConverter.ToInt64(bytes, 112), BitConverter.ToUInt32(bytes, 120), BitConverter.ToInt64(bytes, 96), BitConverter.ToUInt32(bytes, 104));
    }
    [StructLayout(LayoutKind.Sequential)] private struct PollEntry { internal int Descriptor; internal short Events, Returned; }
    private static bool Alive(SafeFileHandle descriptor)
    {
        var entry = new PollEntry { Descriptor = descriptor.DangerousGetHandle().ToInt32(), Events = 1 };
        var result = Poll(ref entry, 1, 0);
        if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Observe the original connected Home peer lifetime.");
        if ((entry.Returned & 0x20) != 0) throw new IOException("The original Home peer pidfd became invalid.");
        return result == 0 && entry.Returned == 0;
    }
    public void Dispose()
    { lock (_gate) { if (_closed) return; _closed = true; _executable?.Dispose(); _pid.Dispose(); } }

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetPeerPidFd(SafeSocketHandle socket, int level, int option, out int descriptor, ref uint size);
    [DllImport("libc", EntryPoint = "poll", SetLastError = true)] private static extern int Poll(ref PollEntry entry, nuint count, int timeout);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(SafeFileHandle descriptor, string path, int flags, uint mask, [Out] byte[] bytes);
}

/// <summary>Older Linux kernels retain local Home functionality but cannot authenticate
/// protected native IPC with the required peer-bound process lifetime.</summary>
internal sealed class HomeNativePeerProcessUnavailableException : PlatformNotSupportedException
{
    internal const string Code = "HomePeerProcessPinUnavailable";
    internal HomeNativePeerProcessUnavailableException() : base(
        "This Linux kernel lacks SO_PEERPIDFD support (Linux6.5 or a supported backport). Protected native Home IPC is unavailable; process-local Home remains usable.") { }
}
