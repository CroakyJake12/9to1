using System.ComponentModel;
using System.Runtime.InteropServices;
using Haven.Application;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>SCM dispatch for the original registered service. Tools do not install,
/// register or start it. Service-running status alone is not Home readiness.</summary>
public static class NativeWindowsHomeRootScmEntry
{
    private static readonly object Gate = new();
    private static OriginalService? Current;
    // Failed/unacknowledged original owners remain rooted for the entire process.
    private static readonly List<OriginalService> Retained = [];
    public static int RunOriginalService(string actualConfiguredMachineStateFile)
    {
        if (!OperatingSystem.IsWindows()) return 50;
        OriginalService actual;
        lock (Gate)
        {
            if (Current is not null) throw new InvalidOperationException("One original SCM entry is permitted.");
            actual = Current = new(Path.GetFullPath(actualConfiguredMachineStateFile)); Retained.Add(actual);
        }
        var table = new[]
        {
            new ServiceTable { Name = NativeWindowsHomeRegisteredRootServiceContext.OriginalServiceName, Main = actual.Main },
            new ServiceTable()
        };
        // SCM validates its registered process. Signature/enrollment/native policy
        // verification is independently performed by the retained actual runtime.
        if (!StartServiceCtrlDispatcher(table)) return Marshal.GetLastWin32Error();
        return actual.HealthyStopped ? 0 : 1066;
    }
    private sealed class OriginalService
    {
        private readonly string _machine;
        private readonly object _gate = new();
        private readonly CloudflareOriginalTaskLedger _sources = new();
        private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _unconfirmedLifetime = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ServiceMain Main;
        private readonly ServiceHandler _handler;
        private IntPtr _statusHandle;
        private NativeWindowsHomeRootServiceRuntime? _runtime;
        private Task? _originalBody, _originalClose;
        private bool _healthyStopped;
        internal bool HealthyStopped { get { lock (_gate) return _healthyStopped; } }
        internal OriginalService(string machine)
        {
            _machine = machine; Main = Run; _handler = Control; _sources.BindOriginalOwner(this);
        }
        private void Run(uint argumentCount, IntPtr arguments)
        {
            TaskCompletionSource? begin = null;
            try
            {
                _statusHandle = RegisterServiceCtrlHandlerEx(NativeWindowsHomeRegisteredRootServiceContext.OriginalServiceName, _handler, IntPtr.Zero);
                if (_statusHandle == IntPtr.Zero) throw Native("RegisterServiceCtrlHandlerExW original Root");
                SetStatus(2, 0); // Start pending, no app/host readiness assertion.
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    _runtime = new(_machine); // Retain all actual owners before callbacks/start.
                    _originalBody = RunBody(begin.Task); _ = _sources.Track(_originalBody);
                }
                // Actual SCM/PID query needs a running service. This status states
                // only that its retained service body now runs, never installed Ready.
                SetStatus(4, 0); begin.SetResult();
                _originalBody.GetAwaiter().GetResult();
                lock (_gate) _healthyStopped = true;
                SetStatus(1, 0);
            }
            catch (Exception cause)
            {
                _sources.Retain(cause);
                // Even a status-publication failure releases the already published
                // body and requests its ordinary stop, then joins its actual close.
                _stop.TrySetResult(); begin?.TrySetResult();
                if (_originalBody is not null)
                    try { _originalBody.GetAwaiter().GetResult(); }
                    catch (Exception sibling) { _sources.Capture(_originalBody, sibling); }
                // A failed original close is never replaced by a healthy service-stop
                // receipt. Keep this owner/process alive with its actual evidence.
                if (_originalClose is not null && !_originalClose.IsCompletedSuccessfully)
                {
                    try { SetStatus(3, 1066); } catch (Exception sibling) { _sources.Retain(sibling); }
                    _unconfirmedLifetime.Task.GetAwaiter().GetResult();
                }
                else
                    try { SetStatus(1, 1066); } catch (Exception sibling) { _sources.Retain(sibling); }
            }
        }
        private async Task RunBody(Task begin)
        {
            await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try
            {
                var actual = _runtime!;
                var start = actual.StartOriginalWithinSourceAsync(RunScope, Keep, CancellationToken.None);
                var observation = await _sources.AwaitAsync(start).ConfigureAwait(false);
                if (!actual.IsIssuedOriginalStartup(observation))
                    throw new UnauthorizedAccessException("The original Root did not issue this settled startup observation.");
                // A precise missing official publisher/enrollment/activation/user
                // stays unavailable; it never authorizes a child or a Ready result.
                if (observation.HasAcceptedOriginalHomeLaunch)
                {
                    var control = actual.StartOriginalControlWithinSourceAsync(observation, RunScope, Keep, CancellationToken.None);
                    await _sources.AwaitAsync(control).ConfigureAwait(false);
                }
                await _stop.Task.ConfigureAwait(false);
            }
            catch (Exception cause) { _sources.Retain(cause); }
            finally
            {
                Task? close = null;
                try
                {
                    _sources.Invoke(() =>
                    {
                        _runtime!.RequestOriginalRetirement();
                        close = _runtime.CloseAndDrainOriginalAsync(); _originalClose = close; _ = _sources.Track(close); return true;
                    });
                }
                catch (Exception cause) { _sources.Retain(cause); }
                if (close is not null)
                    try { await _sources.AwaitAsync(close).ConfigureAwait(false); } catch (Exception cause) { _sources.Capture(close, cause); }
            }
            // _originalBody is tracked by the SCM owner, never joined by its own
            // inner body. Independently settled children and local causes suffice.
            if (_sources.OriginalErrors.Count != 0)
                throw new AggregateException("Original Root service/channel/cleanup failed and remains retained.", _sources.OriginalErrors);
        }
        private uint Control(uint control, uint eventType, IntPtr eventData, IntPtr context)
        {
            try
            {
                if (control is 1 or 5) // STOP or SHUTDOWN, no VM/app kill operation.
                {
                    SetStatus(3, 0); _stop.TrySetResult();
                }
                else if (control == 4) SetStatus(4, 0);
            }
            catch (Exception cause) { _sources.Retain(cause); _stop.TrySetResult(); }
            return 0; // Unmanaged callback never leaks a managed exception.
        }
        private void RunScope(Action body) => _sources.Invoke(() => { body(); return true; });
        private void Keep(Task raw) { _ = _sources.Track(raw); }
        private void SetStatus(uint state, uint error)
        {
            if (_statusHandle == IntPtr.Zero) return;
            var status = new ServiceStatus { Type = 0x10, State = state, Accepted = state == 4 ? 1u | 4u : 0,
                Win32Exit = error == 0 ? 0u : 1066u, ServiceExit = error, WaitHint = state is 2 or 3 ? 30000u : 0 };
            if (!SetServiceStatus(_statusHandle, ref status)) throw Native("SetServiceStatus original Root");
        }
    }
    private static Win32Exception Native(string action) => new(Marshal.GetLastWin32Error(), action);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ServiceMain(uint count, IntPtr arguments);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint ServiceHandler(uint control, uint type, IntPtr data, IntPtr context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ServiceTable
    { [MarshalAs(UnmanagedType.LPWStr)] internal string? Name; internal ServiceMain? Main; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus
    { internal uint Type, State, Accepted, Win32Exit, ServiceExit, Checkpoint, WaitHint; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartServiceCtrlDispatcher([In] ServiceTable[] table);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string name, ServiceHandler handler, IntPtr context);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);
}
