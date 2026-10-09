using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>One original suspended native launch. Its PID/path are observations,
/// never installation, publisher enrollment or a held Home-session grant.</summary>
[SupportedOSPlatform("windows")]
internal sealed class NativeWindowsHomeOriginalControlledProcess(object owner)
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _handles = [];
    private readonly Dictionary<IDisposable, Task> _closes = new(ReferenceEqualityComparer.Instance);
    private SafeProcessHandle? _process;
    private SafeWaitHandle? _thread;
    private Task? _exit, _rawExit, _close;
    private bool _resumeAttempted, _resumed;
    internal int ProcessId { get; private set; }
    internal string? OriginalExecutable { get; private set; }
    internal string? OriginalPrincipal { get; private set; }
    internal bool HasOriginalProcess => _process is not null;
    internal bool IsResumed => _resumed;
    internal Task? OriginalExit => _exit;

    internal void CreateSuspendedOriginal(string protectedExecutable, string protectedWorkingDirectory,
        string originalHomePipe, string expectedOriginalOsPrincipal, CloudflareOriginalTaskLedger sources,
        NativeWindowsHomeRegisteredRootServiceContext.InteractiveUser? actualRegisteredRootUser = null,
        string? actualMachineStateFile = null, bool actualSelectedApplication = false)
    {
        if (_process is not null) throw new InvalidOperationException("The actual controlled child was already acquired.");
        OriginalExecutable = Path.GetFullPath(protectedExecutable); OriginalPrincipal = expectedOriginalOsPrincipal;
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        OriginalSecurityDescriptor? processSecurity = null, threadSecurity = null;
        if (actualRegisteredRootUser is not null)
        {
            var desktop = new OriginalEnvironment("winsta0\\default"); _handles.Add(desktop); startup.Desktop = desktop.Pointer;
            // The actual SCM Root owns the suspended objects. Same-user processes
            // may query/synchronize but cannot inject, replace thread context or
            // alter these original process/thread security descriptors before resume.
            var sid = new SecurityIdentifier(expectedOriginalOsPrincipal["windows-sid:".Length..]);
            processSecurity = CaptureSecurity("O:SYG:SYD:P(A;;GA;;;SY)(A;;0x00121000;;;" + sid.Value + ")");
            threadSecurity = CaptureSecurity("O:SYG:SYD:P(A;;GA;;;SY)(A;;0x00120800;;;" + sid.Value + ")");
        }
        SafeAccessTokenHandle? limited = null;
        sources.Invoke(() => { limited = AcquireOriginalLimitedToken(expectedOriginalOsPrincipal, actualRegisteredRootUser); return true; });
        // No inherited caller environment, shell, PATH lookup, startup-hook,
        // debugger/profiler configuration or arbitrary caller arguments.
        var windows = new StringBuilder(32768);
        var length = GetWindowsDirectory(windows, windows.Capacity);
        if (length == 0 || length >= windows.Capacity) throw Native("GetWindowsDirectoryW");
        var environment = "DOTNET_ADDITIONAL_DEPS=\0DOTNET_EnableDiagnostics=0\0DOTNET_MULTILEVEL_LOOKUP=0\0" +
            "DOTNET_ROLL_FORWARD=Disable\0DOTNET_STARTUP_HOOKS=\0SystemRoot=" + windows + "\0WINDIR=" + windows + "\0\0";
        var block = new OriginalEnvironment(environment);
        _handles.Add(block);
        {
            sources.Invoke(() =>
            {
                var command = new StringBuilder(Quote(protectedExecutable) +
                    (actualMachineStateFile is null
                        ? " --home-installed-endpoint " + Quote(originalHomePipe)
                        : (actualSelectedApplication ? " --home-root-host " : " --home-root-control ") + Quote(originalHomePipe) +
                            " --home-machine-state " + Quote(Path.GetFullPath(actualMachineStateFile))));
                ProcessInfo result; bool accepted;
                if (actualRegisteredRootUser is not null)
                {
                    // CreateProcessWithTokenW uses the caller's service session.
                    // CreateProcessAsUserW honors the actual WTS primary token's
                    // independently checked interactive session, without a requested
                    // username/password, shell, PATH search or inherited handles.
                    var processAttributes = new OriginalSecurityAttributes { Length = Marshal.SizeOf<OriginalSecurityAttributes>(), Descriptor = processSecurity!.Pointer };
                    var threadAttributes = new OriginalSecurityAttributes { Length = Marshal.SizeOf<OriginalSecurityAttributes>(), Descriptor = threadSecurity!.Pointer };
                    accepted = CreateProcessAsUser(limited!, protectedExecutable, command, ref processAttributes,
                        ref threadAttributes, false, 0x00000004 | 0x00000400, block.Pointer,
                        protectedWorkingDirectory, ref startup, out result);
                }
                else accepted = CreateProcessWithToken(limited!, 0, protectedExecutable, command,
                    0x00000004 | 0x00000400, block.Pointer, protectedWorkingDirectory, ref startup, out result);
                // Native handles are rooted before checking status or returning
                // through any borrowed post-callback guard.
                if (result.Process != IntPtr.Zero) { _process = new(result.Process, true); _handles.Add(_process); }
                if (result.Thread != IntPtr.Zero) { _thread = new(result.Thread, true); _handles.Add(_thread); }
                ProcessId = checked((int)result.ProcessId);
                if (!accepted || _process is null || _process.IsInvalid || _thread is null || _thread.IsInvalid || ProcessId <= 0)
                    throw Native("CreateProcessWithTokenW original limited-user suspended image");
                return true;
            });
        }
    }
    internal void ResumeOriginal(CloudflareOriginalTaskLedger sources)
    {
        sources.Invoke(() =>
        {
            if (_resumeAttempted || _thread is null || _process is null)
                throw new InvalidOperationException("One original controlled resume is required.");
            // An attempted native effect remains unknown if it does not return
            // its exact known result. Never terminate it as an unstarted child.
            _resumeAttempted = true;
            var prior = ResumeThread(_thread);
            if (prior != 1) throw new IOException("The actual controlled thread resume was refused or unknown: " + prior);
            _resumed = true; return true;
        });
    }
    internal Task AcquireOriginalExit(CloudflareOriginalTaskLedger sources)
    {
        TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (_exit is null)
            {
                if (_process is null || _process.IsInvalid) throw new InvalidOperationException("The SAME original native child is unavailable.");
                start = new(TaskCreationOptions.RunContinuationsAsynchronously); _exit = Wait(start.Task); _ = sources.Track(_exit);
            }
            actual = _exit;
        }
        start?.SetResult(); return actual;
        async Task Wait(Task gate)
        {
            await gate.ConfigureAwait(false);
            sources.Invoke(() =>
            {
                _rawExit = Task.Run(() => CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                    CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                    {
                        var observed = WaitForSingleObject(_process, uint.MaxValue);
                        if (observed != 0) throw new IOException("Original child exit is not acknowledged: " + observed);
                        return true;
                    })));
                _ = sources.Track(_rawExit); return true;
            });
            await sources.AwaitAsync(_rawExit!).ConfigureAwait(false);
        }
    }
    /// <summary>Only the newly created, never-resume-attempted child can be
    /// terminated during failed acquisition. Accepted business is never killed.</summary>
    internal void StopUnstartedOriginal()
    {
        if (_process is null || _process.IsInvalid || _resumeAttempted) return;
        CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                if (!TerminateProcess(_process, 0x000004c7)) throw Native("TerminateProcess original unstarted child");
                return true;
            }));
    }
    internal Task CloseAndDrainOriginalAsync(CloudflareOriginalTaskLedger sources)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return actual;
        async Task Close(Task gate)
        {
            await gate.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (_process is not null && !_process.IsInvalid)
            {
                try { StopUnstartedOriginal(); } catch (Exception cause) { sources.Retain(cause); }
                Task? exit = null;
                try { exit = AcquireOriginalExit(sources); } catch (Exception cause) { sources.Retain(cause); }
                if (exit is not null) try { await sources.AwaitAsync(exit).ConfigureAwait(false); }
                    catch (Exception cause) { sources.Capture(exit, cause); }
                if (exit?.IsCompletedSuccessfully != true)
                    throw new AggregateException("The actual child's exit remains unknown; retain its SAME process/thread/token/native handles.", sources.OriginalErrors);
            }
            foreach (var handle in _handles.AsEnumerable().Reverse())
            {
                if (!_closes.TryGetValue(handle, out var close))
                {
                    var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    close = Dispose(begin.Task, handle); _closes.Add(handle, close); _ = sources.Track(close); begin.SetResult();
                }
                try { await sources.AwaitAsync(close).ConfigureAwait(false); }
                catch (Exception cause) { sources.Capture(close, cause); }
            }
            if (sources.OriginalErrors.Count != 0)
                throw new AggregateException("Original controlled process custody remains failed or unknown.", sources.OriginalErrors);
        }
        async Task Dispose(Task gate, IDisposable actual)
        {
            await gate.ConfigureAwait(false);
            CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { actual.Dispose(); return true; }));
        }
    }
    /// <summary>The Root process may be elevated. Home is always launched with
    /// its SAME original local user's medium-integrity, non-elevated primary token.
    /// A service/SYSTEM principal is not silently translated to an interactive user.</summary>
    private SafeAccessTokenHandle AcquireOriginalLimitedToken(string expectedPrincipal, NativeWindowsHomeRegisteredRootServiceContext.InteractiveUser? actualRegisteredRootUser)
    {
        if (!expectedPrincipal.StartsWith("windows-sid:", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The actual original local Windows principal is required.");
        SafeAccessTokenHandle current;
        if (actualRegisteredRootUser is not null)
            current = actualRegisteredRootUser.Issuer.DemandOriginalUserToken(actualRegisteredRootUser, expectedPrincipal);
        else
        {
            var opened = OpenProcessToken(GetCurrentProcess(), 0x0008 | 0x0002, out current);
            _handles.Add(current); // Capture even an unacknowledged returned native object before status.
            if (!opened || current.IsInvalid) throw Native("OpenProcessToken original Root");
        }
        var currentIdentity = new WindowsIdentity(current.DangerousGetHandle()); _handles.Add(currentIdentity);
        if ("windows-sid:" + currentIdentity.User?.Value != expectedPrincipal ||
            currentIdentity.User?.Value is "S-1-5-18" or "S-1-5-19" or "S-1-5-20")
            throw new UnauthorizedAccessException("Root requires the SAME original local user; a service token needs its genuine interactive-user supplier.");
        var original = current;
        var elevation = ReadTokenUInt(current, 18);
        if (elevation == 2) // TokenElevationTypeFull
        {
            var linked = ReadTokenBuffer(current, 19, IntPtr.Size);
            var handle = Marshal.ReadIntPtr(linked.Pointer);
            var actual = new SafeAccessTokenHandle(handle); _handles.Add(actual);
            if (actual.IsInvalid) throw new UnauthorizedAccessException("The actual elevated Root has no acknowledged linked limited user token.");
            original = actual;
        }
        else if (elevation is not (1 or 3))
            throw new UnauthorizedAccessException("The original Root token elevation is unsupported.");
        var duplicated = DuplicateTokenEx(original, 0x0001 | 0x0002 | 0x0008 | 0x0080 | 0x0100,
            IntPtr.Zero, 2, 1, out var primary);
        _handles.Add(primary);
        if (!duplicated || primary.IsInvalid) throw Native("DuplicateTokenEx original limited primary");
        var identity = new WindowsIdentity(primary.DangerousGetHandle()); _handles.Add(identity);
        if ("windows-sid:" + identity.User?.Value != expectedPrincipal || ReadTokenUInt(primary, 20) != 0 ||
            ReadTokenUInt(primary, 8) != 1)
            throw new UnauthorizedAccessException("Home's original primary token is not the SAME non-elevated local user.");
        if (actualRegisteredRootUser is not null && ReadTokenUInt(primary, 12) != actualRegisteredRootUser.SessionId)
            throw new UnauthorizedAccessException("The actual limited primary token changed interactive sessions.");
        var integrity = ReadTokenBuffer(primary, 25, IntPtr.Size);
        var sid = Marshal.ReadIntPtr(integrity.Pointer);
        var countPointer = GetSidSubAuthorityCount(sid);
        if (countPointer == IntPtr.Zero || Marshal.ReadByte(countPointer) == 0)
            throw new UnauthorizedAccessException("The original limited token has no acknowledged integrity SID.");
        var value = GetSidSubAuthority(sid, (uint)(Marshal.ReadByte(countPointer) - 1));
        if (value == IntPtr.Zero || unchecked((uint)Marshal.ReadInt32(value)) != 0x00002000)
            throw new UnauthorizedAccessException("Home requires the original medium-integrity user token; elevated or foreign integrity is refused.");
        return primary;
    }
    private OriginalSecurityDescriptor CaptureSecurity(string originalSddl)
    {
        var accepted = ConvertOriginalSecurityDescriptor(originalSddl, 1, out var pointer, out _);
        var original = new OriginalSecurityDescriptor(pointer); _handles.Add(original);
        if (!accepted || pointer == IntPtr.Zero) throw Native("Original controlled process/thread security descriptor");
        return original;
    }
    private sealed class OriginalSecurityDescriptor(IntPtr original) : IDisposable
    {
        internal readonly IntPtr Pointer = original;
        public void Dispose() { if (Pointer != IntPtr.Zero && FreeOriginalSecurityDescriptor(Pointer) != IntPtr.Zero) throw Native("LocalFree original controlled process security"); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct OriginalSecurityAttributes
    { internal int Length; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertOriginalSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint bytes);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)] private static extern IntPtr FreeOriginalSecurityDescriptor(IntPtr descriptor);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessAsUser(SafeAccessTokenHandle token,
        string image, StringBuilder command, ref OriginalSecurityAttributes processSecurity,
        ref OriginalSecurityAttributes threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags,
        IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    private sealed class OriginalEnvironment(string value) : IDisposable
    {
        internal readonly IntPtr Pointer = Marshal.StringToHGlobalUni(value);
        private int _closed;
        public void Dispose() { if (Interlocked.Exchange(ref _closed, 1) == 0) Marshal.FreeHGlobal(Pointer); }
    }
    private sealed class OriginalTokenBuffer(int bytes) : IDisposable
    {
        internal readonly IntPtr Pointer = Marshal.AllocHGlobal(bytes);
        private int _closed;
        public void Dispose() { if (Interlocked.Exchange(ref _closed, 1) == 0) Marshal.FreeHGlobal(Pointer); }
    }
    private OriginalTokenBuffer ReadTokenBuffer(SafeAccessTokenHandle token, int informationClass, int minimumBytes = 4)
    {
        _ = GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out var bytes);
        var error = Marshal.GetLastWin32Error();
        if (bytes < minimumBytes || bytes > 65536 || error != 122)
            throw new Win32Exception(error, "The bounded actual original token query is unavailable.");
        var buffer = new OriginalTokenBuffer(bytes); _handles.Add(buffer);
        if (!GetTokenInformation(token, informationClass, buffer.Pointer, bytes, out var actualBytes) || actualBytes < minimumBytes || actualBytes > bytes)
            throw Native("GetTokenInformation original token");
        return buffer;
    }
    private uint ReadTokenUInt(SafeAccessTokenHandle token, int informationClass) =>
        unchecked((uint)Marshal.ReadInt32(ReadTokenBuffer(token, informationClass).Pointer));
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr security,
        int impersonationLevel, int tokenType, out SafeAccessTokenHandle primary);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        IntPtr information, int bytes, out int returnedBytes);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    private static string Quote(string value)
    {
        if (value.Any(ch => ch == '"' || ch == '\0' || char.IsControl(ch)) || value.EndsWith('\\'))
            throw new ArgumentException("One fixed scalar native argument is required.");
        return '"' + value + '"';
    }
    private static Win32Exception Native(string operation) => new(Marshal.GetLastWin32Error(), operation + " did not return known native evidence.");
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        internal int Size; internal IntPtr Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        internal ushort Show, ReservedCount; internal IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo
    { internal IntPtr Process, Thread; internal uint ProcessId, ThreadId; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "CreateProcessWithTokenW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessWithToken(SafeAccessTokenHandle token,
        uint logonFlags, string image, StringBuilder command, uint flags, IntPtr environment,
        string directory, ref StartupInfo startup, out ProcessInfo process);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeWaitHandle thread);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exit);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetWindowsDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetWindowsDirectory(StringBuilder value, int capacity);
}
