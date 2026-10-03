using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Receives one ORIGINAL child pidfd from the privately authenticated native
/// supervisor. This is shutdown identity only, never installed/actor/launch admission.
/// The native supervisor remains the child's parent and owns waitid/reaping; root
/// independently observes pidfd exit, including after supervisor death.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxNativeIssuedChildPidfd : IDisposable
{
    private readonly SafeFileHandle _handle;
    internal int OriginalProcessId { get; }
    internal ulong OriginalStartTicks { get; }
    private LinuxNativeIssuedChildPidfd(SafeFileHandle handle, int pid, ulong ticks)
    { _handle = handle; OriginalProcessId = pid; OriginalStartTicks = ticks; }

    [StructLayout(LayoutKind.Sequential)] private struct IoVector { internal nint Data; internal nuint Length; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        internal nint Name; internal uint NameLength; internal nint Vectors; internal nuint VectorCount;
        internal nint Control; internal nuint ControlLength; internal int Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PollDescriptor
    { internal int Descriptor; internal short Events; internal short Returned; }
    [DllImport("libc", SetLastError = true)] private static extern nint recvmsg(int socket, ref Message message, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int close(int descriptor);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref PollDescriptor descriptor, nuint count, int timeout);
    [DllImport("libc", SetLastError = true)] private static extern int pidfd_send_signal(SafeFileHandle handle, int signal, nint info, uint flags);

    // ABI is explicitly Linux 64-bit native msghdr/cmsghdr; other platforms refuse.
    // Caller must establish the exact signed/helper payload before calling this method.
    // Supplied nonce is private startup correlation, never executable or actor authority.
    internal static async Task<LinuxNativeIssuedChildPidfd?> ReceiveCreatedAsync(
        Socket originalControl, Process originalHelper, byte[] originalNonce, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalNonce);
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8 || originalNonce.Length != 32 ||
            Marshal.SizeOf<Message>() != 56 || Marshal.SizeOf<IoVector>() != 16 ||
            originalControl.SocketType != SocketType.Seqpacket) return null;
        var nonce = originalNonce.ToArray();
        var expected = new HomeNativeObservedPeer(originalHelper.Id, "unix-euid:0");
        if (HomeNativePeerObservation.FromAcceptedUnixSocket(originalControl) != expected || originalHelper.HasExited)
            return null;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            ct.ThrowIfCancellationRequested();
            if (originalHelper.HasExited || HomeNativePeerObservation.FromAcceptedUnixSocket(originalControl) != expected)
                return null;
            if (!originalControl.Poll(0, SelectMode.SelectRead))
            { await Task.Delay(10, ct).ConfigureAwait(false); continue; }
            var received = ReceiveOnce(originalControl, nonce);
            if (received is null) return null;
            try
            {
                if (originalHelper.HasExited || HomeNativePeerObservation.FromAcceptedUnixSocket(originalControl) != expected ||
                    !await received.IsOriginalLiveAsync(ct).ConfigureAwait(false))
                { received.Dispose(); return null; }
                return received;
            }
            catch { received.Dispose(); throw; }
        }
        return null;
    }

    private static LinuxNativeIssuedChildPidfd? ReceiveOnce(Socket socket, byte[] nonce)
    {
        var bytes = Marshal.AllocHGlobal(65); var ancillary = Marshal.AllocHGlobal(4096);
        var vectorMemory = Marshal.AllocHGlobal(16); var descriptors = new List<int>();
        var retained = -1; var socketHeld = false;
        try
        {
            Marshal.StructureToPtr(new IoVector { Data = bytes, Length = 65 }, vectorMemory, false);
            var message = new Message { Vectors = vectorMemory, VectorCount = 1, Control = ancillary, ControlLength = 4096 };
            socket.SafeHandle.DangerousAddRef(ref socketHeld);
            // Nonblocking and CLOEXEC at kernel receipt, not a later flag mutation.
            var count = recvmsg(socket.SafeHandle.DangerousGetHandle().ToInt32(), ref message, 0x40000000 | 0x40);
            // Failed recvmsg did not initialize ancillary storage; never parse/close
            // descriptors from that uninitialized memory.
            if (count < 0) return null;
            var controlLength = checked((int)message.ControlLength);
            if (controlLength < 0 || controlLength > 4096) return null;
            var offset = 0; var malformed = false; var headers = 0;
            while (offset + 16 <= controlLength)
            {
                var length64 = unchecked((ulong)Marshal.ReadInt64(ancillary, offset));
                if (length64 < 16 || length64 > (ulong)(controlLength - offset)) { malformed = true; break; }
                var length = checked((int)length64); var level = Marshal.ReadInt32(ancillary, offset + 8);
                var type = Marshal.ReadInt32(ancillary, offset + 12); headers++;
                if (level == 1 && type == 1)
                {
                    if ((length - 16) % 4 != 0) malformed = true;
                    for (var index = 16; index + 4 <= length; index += 4)
                        descriptors.Add(Marshal.ReadInt32(ancillary, offset + index));
                }
                else malformed = true;
                offset += (length + 7) & ~7;
            }
            if (count != 64 || (message.Flags & (0x20 | 0x8)) != 0 || malformed || headers != 1 || descriptors.Count != 1)
                return null;
            var packet = new byte[64]; Marshal.Copy(bytes, packet, 0, packet.Length);
            if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != 0x31505341 ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)) != 1 ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)) != 1 ||
                BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(8)) != 1 ||
                !packet.AsSpan(16, 32).SequenceEqual(nonce) ||
                BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(52)) != 0) return null;
            var pid = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(48));
            var ticks = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(56));
            // SafeFileHandle regards zero as invalid. Refuse and close a legitimate
            // fd0 rather than silently transferring an invalid managed handle.
            if (pid == 0 || pid > int.MaxValue || ticks == 0 || descriptors[0] <= 0) return null;
            var result = new LinuxNativeIssuedChildPidfd(new SafeFileHandle((nint)descriptors[0], ownsHandle: true), (int)pid, ticks);
            retained = descriptors[0]; return result;
        }
        finally
        {
            if (socketHeld) socket.SafeHandle.DangerousRelease();
            foreach (var descriptor in descriptors) if (descriptor >= 0 && descriptor != retained) close(descriptor);
            Marshal.FreeHGlobal(vectorMemory); Marshal.FreeHGlobal(ancillary); Marshal.FreeHGlobal(bytes);
        }
    }

    internal bool OriginalExitObserved()
    {
        var held = false;
        try
        {
            _handle.DangerousAddRef(ref held);
            var p = new PollDescriptor { Descriptor = _handle.DangerousGetHandle().ToInt32(), Events = 1 };
            var result = poll(ref p, 1, 0);
            // POLLNVAL/error is NOT proof of original child exit.
            return result == 1 && (p.Returned & 1) != 0 && (p.Returned & (8 | 32)) == 0;
        }
        finally { if (held) _handle.DangerousRelease(); }
    }

    internal async Task<bool> IsOriginalLiveAsync(CancellationToken ct)
    {
        if (_handle.IsClosed || OriginalExitObserved()) return false;
        var held = false;
        try
        {
            _handle.DangerousAddRef(ref held);
            var descriptor = _handle.DangerousGetHandle().ToInt32();
            var info = await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/fdinfo/{descriptor}", 65536, ct);
            var rows = info?.Split('\n').Where(line => line.StartsWith("Pid:", StringComparison.Ordinal)).ToArray();
            if (rows is null || rows.Length != 1 ||
                rows[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is not { Length: 2 } values ||
                values[1] != OriginalProcessId.ToString(CultureInfo.InvariantCulture)) return false;
            var stat = await LinuxProcBoundedObservation.ReadAsync($"/proc/{OriginalProcessId}/stat", 65536, ct);
            if (stat is null || !stat.StartsWith(OriginalProcessId.ToString(CultureInfo.InvariantCulture) + " (", StringComparison.Ordinal)) return false;
            var end = stat.LastIndexOf(')'); if (end < 0) return false;
            var fields = stat[(end + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return fields.Length > 19 && ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) &&
                ticks == OriginalStartTicks && !OriginalExitObserved();
        }
        finally { if (held) _handle.DangerousRelease(); }
    }

    internal async Task<bool> TerminateAndObserveOriginalExitAsync()
    {
        if (OriginalExitObserved()) return true;
        if (pidfd_send_signal(_handle, 15, 0, 0) != 0 && Marshal.GetLastPInvokeError() != 3) return false;
        if (await WaitExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)) return true;
        if (pidfd_send_signal(_handle, 9, 0, 0) != 0 && Marshal.GetLastPInvokeError() != 3) return false;
        return await WaitExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }
    private async Task<bool> WaitExitAsync(TimeSpan duration)
    {
        var timer = Stopwatch.StartNew();
        do { if (OriginalExitObserved()) return true; await Task.Delay(10).ConfigureAwait(false); } while (timer.Elapsed < duration);
        return OriginalExitObserved();
    }
    // Trusted composition must drain first. Closing a pidfd alone does not stop a child.
    public void Dispose() => _handle.Dispose();
}
