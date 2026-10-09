using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Actual Windows SCM cold-start provenance. A protected registered
/// LocalSystem service is launched by SCM, without an interactive caller's CLR
/// hooks/environment. Catalogue/enrollment/full activation admission is separate.
/// The interactive user's SID comes from the actual kernel session/token, never
/// service arguments, an enrollment row or a requested principal string.</summary>
public sealed class NativeWindowsHomeRegisteredRootServiceContext : ITrustedHostPrincipalSource, IAsyncDisposable
{
    public const string OriginalServiceName = "9to1.Root";
    private readonly string _machineFile;
    private readonly object _gate = new();
    private readonly List<Original> _calls = [];
    private readonly ConditionalWeakTable<HomeLocalProfileIdentity, object> _profiles = new();
    private readonly ConditionalWeakTable<InteractiveUser, Original> _users = new();
    private bool _retiring; private Task? _close;
    internal sealed class Resource(IDisposable actual) { internal readonly IDisposable Actual = actual; internal Task? Close; }
    internal sealed class Original(CloudflareOriginalTaskLedger source)
    {
        internal readonly CloudflareOriginalTaskLedger Source = source;
        internal Task<InteractiveUser?> Driver = null!;
        internal readonly List<Resource> Resources = [];
        internal InteractiveUser? User; internal Task? Close;
        internal bool Healthy => Driver.IsCompletedSuccessfully && Source.OriginalErrors.Count == 0 && Source.OriginalTasks.All(task => task.IsCompletedSuccessfully);
    }
    public sealed class InteractiveUser
    {
        internal readonly NativeWindowsHomeRegisteredRootServiceContext Issuer;
        internal readonly SafeAccessTokenHandle Token;
        internal readonly WindowsIdentity Identity;
        internal readonly Original Original;
        internal InteractiveUser(NativeWindowsHomeRegisteredRootServiceContext issuer, Original original,
            SafeAccessTokenHandle token, WindowsIdentity identity, uint session)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows interactive user required.");
            Issuer = issuer; Original = original; Token = token; Identity = identity; SessionId = session; OperatingSystemPrincipalId = "windows-sid:" + identity.User!.Value;
        }
        public string OperatingSystemPrincipalId { get; }
        public uint SessionId { get; }
    }
    public NativeWindowsHomeRegisteredRootServiceContext(string actualConfiguredMachineStateFile)
    { _machineFile = Path.GetFullPath(actualConfiguredMachineStateFile); }
    public HomeLocalProfileIdentity CreateOriginalMachineProfiles(FileHomeCoreStateStore sameStore)
    {
        if (!sameStore.IsOriginalConfiguredFile(_machineFile)) throw new UnauthorizedAccessException("The SAME configured canonical machine state is required.");
        var actual = new HomeLocalProfileIdentity(sameStore, this); _profiles.Add(actual, this); return actual;
    }
    public bool HasOriginalProfiles(HomeLocalProfileIdentity sameProfiles) => _profiles.TryGetValue(sameProfiles, out var issuer) && ReferenceEquals(issuer, this);
    public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => new(ReadPrincipal(token));
    private async Task<string?> ReadPrincipal(CancellationToken token)
    {
        // This complete original is retained by the context itself. The scoped
        // Home profile caller additionally retains this SAME facade Task.
        var original = AcquireOriginalInteractiveUserWithinSourceAsync(body => body(), _ => { }, token);
        var user = await original.ConfigureAwait(false);
        if (user is null) return null;
        var sid = user.OperatingSystemPrincipalId;
        await CloseOriginalUserAsync(user).ConfigureAwait(false);
        return sid;
    }
    public Task<InteractiveUser?> AcquireOriginalInteractiveUserWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        source.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
            () => { RunOriginalCallback(source, scope, body); return true; }));
        var work = new Original(source); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _calls.RemoveAll(old => old.Healthy && old.Resources.All(resource => resource.Close?.IsCompletedSuccessfully == true));
            if (_calls.Count >= 128) throw new InvalidOperationException("Unresolved original Root service resources remain retained.");
            work.Driver = Read(start.Task); _calls.Add(work);
        }
        try { source.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { source.Retain(cause); }
        finally { start.SetResult(); }
        return work.Driver;
        async Task<InteractiveUser?> Read(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try
            {
                token.ThrowIfCancellationRequested();
                if (OperatingSystem.IsWindows()) source.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root service required.");
                    DemandOriginalServicePeer(Environment.ProcessId, null, _machineFile, actual => work.Resources.Add(new(actual)));
                    var current = WindowsIdentity.GetCurrent(); work.Resources.Add(new(current));
                    if (current.User?.Value != "S-1-5-18") throw new UnauthorizedAccessException("The registered Root service must have its actual LocalSystem primary token.");
                    var session = WTSGetActiveConsoleSessionId();
                    if (session == uint.MaxValue) return true; // Genuine absence; never invent an interactive user.
                    var opened = WTSQueryUserToken(session, out var actualToken); work.Resources.Add(new(actualToken));
                    if (!opened || actualToken.IsInvalid) throw Native("WTSQueryUserToken original active session");
                    var identity = new WindowsIdentity(actualToken.DangerousGetHandle()); work.Resources.Add(new(identity));
                    if (identity.User is null || identity.User.Value is "S-1-5-18" or "S-1-5-19" or "S-1-5-20")
                        throw new UnauthorizedAccessException("The kernel interactive session does not identify a real local user.");
                    if (WTSGetActiveConsoleSessionId() != session) throw new UnauthorizedAccessException("The original active user session changed.");
                    work.User = new(this, work, actualToken, identity, session); return true;
                });
            }
            catch (Exception cause) { source.Retain(cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (work.User is null || source.OriginalErrors.Count != 0) await CloseResources(work).ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0) throw new AggregateException("Actual Root service/kernel user admission failed.", source.OriginalErrors);
            if (work.User is not null) _users.Add(work.User, work);
            return work.User;
        }
    }
    internal bool IsIssuedOriginalUser(InteractiveUser same) => OperatingSystem.IsWindows() && same is not null && ReferenceEquals(same.Issuer, this) &&
        _users.TryGetValue(same, out var original) && ReferenceEquals(original.User, same) && original.Healthy &&
        original.Resources.All(resource => resource.Close is null) && !same.Token.IsClosed && !same.Token.IsInvalid;
    [SupportedOSPlatform("windows")]
    internal SafeAccessTokenHandle DemandOriginalUserToken(InteractiveUser same, string expectedPrincipal)
    {
        if (!IsIssuedOriginalUser(same) || same.OperatingSystemPrincipalId != expectedPrincipal || WTSGetActiveConsoleSessionId() != same.SessionId)
            throw new UnauthorizedAccessException("The SAME still-held kernel user token/session is required.");
        return CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            DemandOriginalServicePeer(Environment.ProcessId, null, _machineFile, actual => same.Original.Resources.Add(new(actual)));
            return same.Token;
        });
    }
    public Task CloseOriginalUserAsync(InteractiveUser same)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        if (!_users.TryGetValue(same, out var original) || !ReferenceEquals(original.User, same))
            throw new UnauthorizedAccessException("The actual service-issued kernel user is required.");
        TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            if (original.Close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); original.Close = Close(begin.Task); }
            actual = original.Close;
        }
        begin?.SetResult(); return actual;
        async Task Close(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try { await original.Driver.ConfigureAwait(false); } catch (Exception cause) { original.Source.Capture(original.Driver, cause); }
            await CloseResources(original).ConfigureAwait(false);
        }
    }
    private async Task CloseResources(Original work)
    {
        foreach (var resource in work.Resources.AsEnumerable().Reverse())
        {
            if (resource.Close is null)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                resource.Close = completion.Task; _ = work.Source.Track(resource.Close);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { resource.Actual.Dispose(); return true; }); completion.SetResult(); }
                catch (Exception cause) { completion.SetException(cause); }
            }
            try { await work.Source.AwaitAsync(resource.Close).ConfigureAwait(false); }
            catch (Exception cause) { work.Source.Capture(resource.Close, cause); }
        }
        if (work.Source.OriginalErrors.Count != 0) throw new AggregateException("Original Root kernel resources remain retained after cleanup failure.", work.Source.OriginalErrors);
    }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task, _calls.ToArray()); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task Drain(Task start, Original[] calls)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        foreach (var work in calls)
        {
            try { await work.Driver.ConfigureAwait(false); } catch (Exception cause) { work.Source.Capture(work.Driver, cause); }
            try
            {
                if (work.User is not null && _users.TryGetValue(work.User, out _)) await CloseOriginalUserAsync(work.User).ConfigureAwait(false);
                else await CloseResources(work).ConfigureAwait(false);
            }
            catch (Exception cause) { work.Source.Retain(cause); }
            failures.AddRange(work.Source.OriginalErrors);
        }
        if (failures.Count != 0) throw new AggregateException("Actual Root service context retirement failed.", failures);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private static void RunOriginalCallback(CloudflareOriginalTaskLedger source, Action<Action> caller, Action body)
    {
        var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId; var errors = new List<Exception>();
        void Keep(Exception cause) { lock (errors) errors.Add(cause); source.Retain(cause); }
        try
        {
            try
            {
                caller(() =>
                {
                    if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                    { var failure = new InvalidOperationException("The actual Root service callback is inactive, foreign-thread or repeated."); Keep(failure); throw failure; }
                    try { body(); } catch (Exception cause) { Keep(cause); throw; }
                });
                if (used != 1) Keep(new InvalidOperationException("The actual Root service callback was omitted."));
            }
            catch (Exception cause) { Keep(cause); }
        }
        finally { Volatile.Write(ref active, 0); }
        Exception[] all; lock (errors) all = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (all.Length != 0) throw new AggregateException("Actual Root service caller/body/protocol failed.", all);
    }

    // Kernel/SCM observation is also usable by a connected limited Home client.
    // No source-issued Root runtime proof exists until full signed activation is
    // independently checked and this actual PID/image/configuration match it.
    [SupportedOSPlatform("windows")]
    internal static void DemandOriginalServicePeer(int actualKernelPid, string? expectedProtectedRootImage,
        string machineFile, Action<IDisposable> capture)
    {
        var scm = new ServiceHandle(OpenSCManager(null, null, 0x1)); capture(scm);
        if (scm.IsInvalid) throw Native("OpenSCManager original Root");
        var service = new ServiceHandle(OpenService(scm.Value, OriginalServiceName, 0x1 | 0x4 | 0x00020000)); capture(service);
        if (service.IsInvalid) throw Native("OpenService original Root");
        var statusBuffer = new NativeBuffer(Marshal.SizeOf<ServiceStatus>()); capture(statusBuffer);
        if (!QueryServiceStatusEx(service.Value, 0, statusBuffer.Value, statusBuffer.Bytes, out _)) throw Native("QueryServiceStatusEx original Root");
        var status = Marshal.PtrToStructure<ServiceStatus>(statusBuffer.Value);
        if (status.Type != 0x10 || status.State != 4 || status.ProcessId != actualKernelPid)
            throw new UnauthorizedAccessException("The actual connected process is not the currently running registered original Root service.");
        _ = QueryServiceConfig(service.Value, IntPtr.Zero, 0, out var configBytes);
        if (Marshal.GetLastWin32Error() != 122 || configBytes < Marshal.SizeOf<ServiceConfig>() || configBytes > 65536)
            throw Native("QueryServiceConfigW original Root bound");
        var configBuffer = new NativeBuffer(configBytes); capture(configBuffer);
        if (!QueryServiceConfig(service.Value, configBuffer.Value, configBuffer.Bytes, out _)) throw Native("QueryServiceConfigW original Root");
        var config = Marshal.PtrToStructure<ServiceConfig>(configBuffer.Value);
        var command = Marshal.PtrToStringUni(config.BinaryPath) ?? throw new InvalidDataException("Original Root service image is absent.");
        var account = Marshal.PtrToStringUni(config.Account);
        if (config.Type != 0x10 || config.Start is not (2 or 3) || account is not ("LocalSystem" or "NT AUTHORITY\\SYSTEM"))
            throw new UnauthorizedAccessException("The actual Root service startup/account is unsupported.");
        var expectedArguments = " --root-service --home-machine-state \"" + Path.GetFullPath(machineFile) + "\"";
        if (!command.StartsWith('"') || !command.EndsWith(expectedArguments, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Root's registered startup must use its exact fixed original service entry and canonical machine-state locator.");
        var imageEnd = command.IndexOf('"', 1);
        if (imageEnd < 2 || command[imageEnd..] != "\"" + expectedArguments)
            throw new UnauthorizedAccessException("The actual Root service command contains unsupported arguments.");
        var image = Path.GetFullPath(command[1..imageEnd]);
        if (expectedProtectedRootImage is not null && !StringComparer.OrdinalIgnoreCase.Equals(image, expectedProtectedRootImage))
            throw new UnauthorizedAccessException("The protected service image is not the exact signed activated Root executable.");
        _ = QueryServiceObjectSecurity(service.Value, 0x1 | 0x4, IntPtr.Zero, 0, out var securityBytes);
        if (Marshal.GetLastWin32Error() != 122 || securityBytes is < 20 or > 65536) throw Native("QueryServiceObjectSecurity original Root bound");
        var securityBuffer = new NativeBuffer(securityBytes); capture(securityBuffer);
        if (!QueryServiceObjectSecurity(service.Value, 0x1 | 0x4, securityBuffer.Value, securityBuffer.Bytes, out _))
            throw Native("QueryServiceObjectSecurity original Root");
        var securityBytesActual = new byte[securityBytes]; Marshal.Copy(securityBuffer.Value, securityBytesActual, 0, securityBytes);
        var security = new RawSecurityDescriptor(securityBytesActual, 0);
        if (security.Owner?.Value is not ("S-1-5-18" or "S-1-5-32-544") || security.DiscretionaryAcl is null)
            throw new UnauthorizedAccessException("Root service registration has no protected machine owner/DACL.");
        const int forbidden = 0x2 | 0x20 | 0x10000 | 0x40000 | 0x80000;
        foreach (GenericAce entry in security.DiscretionaryAcl)
        {
            if (entry is not QualifiedAce ace || ace.AceQualifier != AceQualifier.AccessAllowed) continue;
            if ((ace.AccessMask & (forbidden | unchecked((int)0x10000000) | 0x40000000)) != 0 &&
                ace.SecurityIdentifier.Value is not ("S-1-5-18" or "S-1-5-32-544"))
                throw new UnauthorizedAccessException("A foreign principal can alter or retire Root's protected startup registration.");
        }
        if (!QueryServiceStatusEx(service.Value, 0, statusBuffer.Value, statusBuffer.Bytes, out _)) throw Native("QueryServiceStatusEx original Root currentness");
        if (Marshal.PtrToStructure<ServiceStatus>(statusBuffer.Value).ProcessId != actualKernelPid)
            throw new UnauthorizedAccessException("The actual registered Root service changed during observation.");
    }
    private sealed class NativeBuffer(int bytes) : IDisposable
    { internal readonly int Bytes = bytes; internal readonly IntPtr Value = Marshal.AllocHGlobal(bytes); private bool _closed; public void Dispose() { if (!_closed) { Marshal.FreeHGlobal(Value); _closed = true; } } }
    private sealed class ServiceHandle(IntPtr actual) : IDisposable
    { internal readonly IntPtr Value = actual; internal bool IsInvalid => Value == IntPtr.Zero; private bool _closed; public void Dispose() { if (_closed || IsInvalid) return; if (!CloseServiceHandle(Value)) throw Native("CloseServiceHandle original Root"); _closed = true; } }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { internal int Type, State, Accepted, Win32Exit, SpecificExit, Checkpoint, WaitHint, ProcessId, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceConfig { internal int Type, Start, Error; internal IntPtr BinaryPath, Group; internal int Tag; internal IntPtr Dependencies, Account, DisplayName; }
    private static Win32Exception Native(string operation) => new(Marshal.GetLastWin32Error(), operation + " did not return acknowledged kernel provenance.");
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint rights);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr scm, string name, uint rights);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, IntPtr buffer, int bytes, out int required);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceConfig(IntPtr service, IntPtr buffer, int bytes, out int required);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceObjectSecurity(IntPtr service, uint info, IntPtr buffer, int bytes, out int required);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr service);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSQueryUserToken(uint session, out SafeAccessTokenHandle actual);
}
