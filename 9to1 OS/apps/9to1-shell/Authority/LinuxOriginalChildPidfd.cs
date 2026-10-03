using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Globalization;
using Microsoft.Win32.SafeHandles;
using Haven.Application;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Private kernel handle to the admitted original child. No signal is sent by PID,
/// process name, public context fields or a reconnecting socket.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxOriginalChildPidfd : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly HomeNativeObservedPeer _peer;
    private readonly LinuxProcessIdentity _process;
    private LinuxOriginalChildPidfd(SafeFileHandle handle, HomeNativeObservedPeer peer, LinuxProcessIdentity process)
    { _handle = handle; _peer = peer; _process = process; }
    [DllImport("libc", EntryPoint = "pidfd_open", SetLastError = true)]
    private static extern int Open(int pid, uint flags);
    [DllImport("libc", EntryPoint = "pidfd_send_signal", SetLastError = true)]
    private static extern int SendSignal(SafeFileHandle handle, int signal, IntPtr info, uint flags);

    internal static async Task<LinuxOriginalChildPidfd?> ObserveAsync(HomeNativeObservedPeer peer,
        LinuxProcessIdentity originalProcess, CancellationToken ct)
    {
        if (originalProcess != await LinuxProcessIdentity.ReadAsync(peer, ct)) return null;
        int descriptor;
        try { descriptor = Open(peer.ProcessId, 0); }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException) { return null; }
        if (descriptor < 0) return null;
        var handle = new SafeFileHandle((IntPtr)descriptor, true);
        var result = new LinuxOriginalChildPidfd(handle, peer, originalProcess);
        try { if (await result.IsOriginalCurrentAsync(ct)) return result; }
        catch { result.Dispose(); throw; }
        result.Dispose(); return null;
    }
    internal async Task<bool> IsOriginalCurrentAsync(CancellationToken ct)
    {
        var held = false;
        try
        {
            if (_handle.IsClosed) return false;
            _handle.DangerousAddRef(ref held);
            if (_process != await LinuxProcessIdentity.ReadAsync(_peer, ct)) return false;
            var descriptor = _handle.DangerousGetHandle().ToInt32();
            var info = await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/fdinfo/{descriptor}", 65536, ct);
            if (info is null) return false;
            var pidLines = info.Split('\n').Where(line => line.StartsWith("Pid:", StringComparison.Ordinal)).ToArray();
            if (pidLines.Length != 1) return false;
            var fields = pidLines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return fields.Length == 2 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) &&
                pid == _peer.ProcessId && _process == await LinuxProcessIdentity.ReadAsync(_peer, ct);
        }
        catch (ObjectDisposedException) { return false; }
        finally { if (held) _handle.DangerousRelease(); }
    }
    internal async Task<bool> TerminateOriginalAsync(CancellationToken ct)
        => await SignalOriginalAsync(15, ct);
    internal async Task<bool> KillOriginalAsync(CancellationToken ct)
        => await SignalOriginalAsync(9, ct);
    private async Task<bool> SignalOriginalAsync(int signal, CancellationToken ct)
    {
        if (!await IsOriginalCurrentAsync(ct)) return false;
        try { return SendSignal(_handle, signal, IntPtr.Zero, 0) == 0; }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException or ObjectDisposedException) { return false; }
    }
    public void Dispose() => _handle.Dispose();
}
