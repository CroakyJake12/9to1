using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Shutdown handle for the private Process returned by THIS actual spawn.
/// Executable/UID can change during controlled setpriv/apphost startup; this primitive
/// grants no launch receipt. Process exit, kernel pidfd and original start are all checked.</summary>
internal sealed class LinuxOriginalSpawnPidfd : IDisposable
{
    private readonly Process _spawned;
    private readonly SafeFileHandle _handle;
    private readonly string _originalStart;
    private LinuxOriginalSpawnPidfd(Process spawned, SafeFileHandle handle, string originalStart)
    { _spawned = spawned; _handle = handle; _originalStart = originalStart; }
    [DllImport("libc", EntryPoint = "pidfd_open", SetLastError = true)] private static extern int Open(int pid, uint flags);
    [DllImport("libc", EntryPoint = "pidfd_send_signal", SetLastError = true)] private static extern int Signal(SafeFileHandle handle, int signal, IntPtr info, uint flags);
    internal static bool PlatformHandleAvailable()
    {
        try { var fd = Open(Environment.ProcessId, 0); if (fd < 0) return false; using var handle = new SafeFileHandle((IntPtr)fd, true); return true; }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException) { return false; }
    }
    internal static async Task<LinuxOriginalSpawnPidfd?> CaptureAsync(Process actuallySpawned, CancellationToken ct)
    {
        if (actuallySpawned.HasExited) return null;
        var fd = Open(actuallySpawned.Id, 0); if (fd < 0) return null;
        var handle = new SafeFileHandle((IntPtr)fd, true);
        try
        {
            var start = await ReadStartAsync(actuallySpawned.Id, ct);
            if (start is null || actuallySpawned.HasExited) { handle.Dispose(); return null; }
            var result = new LinuxOriginalSpawnPidfd(actuallySpawned, handle, start);
            if (await result.IsSameActualSpawnAsync(ct)) return result;
            result.Dispose(); return null;
        }
        catch { handle.Dispose(); throw; }
    }
    private static async Task<string?> ReadStartAsync(int pid, CancellationToken ct)
    {
        var text = await LinuxProcBoundedObservation.ReadAsync($"/proc/{pid}/stat", 65536, ct);
        if (text is null || !text.StartsWith(pid.ToString(CultureInfo.InvariantCulture) + " (", StringComparison.Ordinal)) return null;
        var closing = text.LastIndexOf(')'); if (closing < 0) return null;
        var fields = text[(closing + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 19 && ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var start) && start > 0
            ? fields[19] : null;
    }
    private async Task<bool> IsSameActualSpawnAsync(CancellationToken ct)
    {
        var held = false;
        try
        {
            if (_handle.IsClosed || _spawned.HasExited) return false;
            _handle.DangerousAddRef(ref held);
            var descriptor = _handle.DangerousGetHandle().ToInt32();
            var info = await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/fdinfo/{descriptor}", 65536, ct);
            if (info is null) return false;
            var lines = info.Split('\n').Where(line => line.StartsWith("Pid:", StringComparison.Ordinal)).ToArray();
            if (lines.Length != 1) return false;
            var fields = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return fields.Length == 2 && fields[1] == _spawned.Id.ToString(CultureInfo.InvariantCulture) &&
                _originalStart == await ReadStartAsync(_spawned.Id, ct) && !_spawned.HasExited;
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException) { return false; }
        finally { if (held) _handle.DangerousRelease(); }
    }
    internal async Task<bool> TerminateSameActualSpawnAsync(CancellationToken ct)
    {
        if (!await IsSameActualSpawnAsync(ct)) return false;
        try { return Signal(_handle, 15, IntPtr.Zero, 0) == 0; }
        catch (Exception error) when (error is ObjectDisposedException or EntryPointNotFoundException or DllNotFoundException) { return false; }
    }
    internal async Task<bool> TerminateAndDrainSameActualSpawnAsync()
    {
        if (_spawned.HasExited) return true;
        if (!await TerminateSameActualSpawnAsync(CancellationToken.None)) return _spawned.HasExited;
        try { await _spawned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); return true; }
        catch (TimeoutException) { }
        // Escalation still addresses ONLY the original retained kernel handle.
        if (!await IsSameActualSpawnAsync(CancellationToken.None)) return _spawned.HasExited;
        try { if (Signal(_handle, 9, IntPtr.Zero, 0) != 0) return _spawned.HasExited; }
        catch (Exception error) when (error is ObjectDisposedException or EntryPointNotFoundException or DllNotFoundException) { return false; }
        try { await _spawned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); return true; }
        catch (TimeoutException) { return false; }
    }
    public void Dispose() => _handle.Dispose();
}
