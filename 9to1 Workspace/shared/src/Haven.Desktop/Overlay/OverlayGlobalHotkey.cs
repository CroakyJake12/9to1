#if !ANDROID
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Haven.Desktop.Overlay;

/// <summary>Registers one explicit system-wide Overlay shortcut without keyboard hooks or unrelated input capture.</summary>
internal sealed class OverlayGlobalHotkey : IAsyncDisposable
{
    // Win32 application hotkey identifiers must be in the 0x0000-0xBFFF range.
    private const int HotkeyId = 0x4841;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyH = 0x48;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly TaskCompletionSource _threadReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _threadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private uint _threadId;
    private bool _started;
    private bool _disposed;
    private Task? _close;

    public event EventHandler? Pressed;
    public bool IsRegistered { get; private set; }
    public string ShortcutLabel => "Ctrl+Shift+H";
    public string? UnavailableReason { get; private set; }
    internal Task OriginalThreadCompletion => _threadCompletion.Task;

    public bool Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return IsRegistered;
            _started = true;
            if (!OperatingSystem.IsWindows())
            {
                UnavailableReason = "The desktop Overlay global shortcut requires Windows.";
                _ready.Set();
                _threadReady.TrySetResult();
                _threadCompletion.TrySetResult(); // No thread was created on this platform.
                return false;
            }
            _thread = new Thread(MessageLoop) { IsBackground = true, Name = "9to1 Overlay hotkey" };
            try { _thread.Start(); }
            catch (Exception cause)
            {
                _threadReady.TrySetResult();
                _threadCompletion.TrySetException(cause);
                throw;
            }
            if (!_ready.Wait(TimeSpan.FromSeconds(2)))
            {
                UnavailableReason = "Windows did not initialise the Overlay shortcut in time.";
                return false;
            }
            return IsRegistered;
        }
    }

    private void MessageLoop()
    {
        Exception? failure = null;
        var registered = false;
        try
        {
            _threadId = Native.GetCurrentThreadId();
            // Create this thread's queue before publishing readiness: a stop issued
            // during registration can then deliver WM_QUIT to the SAME real queue.
            Native.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _threadReady.TrySetResult();
            if (!Native.RegisterHotKey(IntPtr.Zero, HotkeyId, ModControl | ModShift | ModNoRepeat, VirtualKeyH))
            {
                UnavailableReason = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return;
            }
            registered = IsRegistered = true;
            _ready.Set();
            while (true)
            {
                var result = Native.GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (message.Message == WmHotkey && message.WParam == (nint)HotkeyId)
                    Pressed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception cause) { failure = cause; UnavailableReason = cause.Message; }
        finally
        {
            if (registered && !Native.UnregisterHotKey(IntPtr.Zero, HotkeyId))
            {
                var cleanup = new Win32Exception(Marshal.GetLastWin32Error());
                failure = failure is null ? cleanup : new AggregateException(failure, cleanup);
            }
            IsRegistered = false;
            _ready.Set();
            _threadReady.TrySetResult();
            // Publish only after the actual native finally has finished. Close also
            // joins the physical thread, so this signal is not a timeout receipt.
            if (failure is null) _threadCompletion.TrySetResult();
            else _threadCompletion.TrySetException(failure);
        }
    }

    internal void DemandExternalOriginalRetirementJoin()
    {
        if (ReferenceEquals(Thread.CurrentThread, _thread))
            throw new InvalidOperationException("The actual hotkey source must return before its thread is joined.");
    }

    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalRetirementJoin();
        Task actual;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            _disposed = true;
            if (!_started) { _threadReady.TrySetResult(); _threadCompletion.TrySetResult(); }
            _close = actual = CloseCoreAsync(start.Task);
        }
        start.SetResult();
        return new(actual);
    }

    private async Task CloseCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        await _threadReady.Task.ConfigureAwait(false);
        var failures = new List<Exception>();
        if (!_threadCompletion.Task.IsCompleted && _threadId != 0
            && !Native.PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero))
            failures.Add(new Win32Exception(Marshal.GetLastWin32Error()));
        try { await _threadCompletion.Task.ConfigureAwait(false); }
        catch (Exception cause)
        {
            if (_threadCompletion.Task.Exception is { } group) failures.AddRange(group.InnerExceptions);
            else failures.Add(cause);
        }
        // Completion describes the native finally; the real physical join proves
        // the original thread has returned. Never substitute a one-second timeout.
        if (_thread is { } thread && (thread.ThreadState & System.Threading.ThreadState.Unstarted) == 0) thread.Join();
        _ready.Dispose();
        if (failures.Count > 0) throw new AggregateException("The actual Overlay hotkey source failed to retire.", failures);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
#endif
