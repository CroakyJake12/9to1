using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootServiceRuntime
{
    // A locator only. The actual server SCM, kernel process, publisher and
    // activated payload must authenticate independently at the client boundary.
    public const string OriginalControlPipeName = "9to1.root.control.v1";
    private readonly CancellationTokenSource _controlStop = new();
    private readonly List<ControlConnection> _controlConnections = [];
    private readonly List<Exception> _controlFailures = [];
    private readonly CloudflareOriginalTaskLedger _controlCleanup = new();
    private readonly TaskCompletionSource _controlReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _controlDriver, _controlStopOriginal, _controlClose;
    private bool _controlSealed;
    private sealed class ControlConnection
    {
        internal readonly CloudflareOriginalTaskLedger Sources = new();
        internal readonly Dictionary<Guid, NativeWindowsHomeInstalledRootAdmission.OriginalActivationRead> Reads = [];
        internal readonly List<ControlCommand> Commands = [];
        internal NamedPipeServerStream? Pipe;
        internal Task? Wait, Driver, PipeClose;
        internal bool WaitJoined, OwnStopAcknowledged, Accepted;
        internal NativeWindowsHomeRootWire.Listening? Listening;
        internal readonly List<ControlResource> Resources = [];
    }
    private sealed class ControlCommand(CloudflareOriginalTaskLedger source)
    {
        internal readonly CloudflareOriginalTaskLedger Sources = source;
        internal Task<bool> Driver = null!;
        internal bool Healthy => Driver.IsCompletedSuccessfully && Sources.OriginalErrors.Count == 0 &&
            Sources.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
    }
    private sealed class ControlResource(IDisposable actual, bool pipeHandle = false, CloudflareOriginalTaskLedger? source = null)
    { internal readonly IDisposable Actual = actual; internal readonly bool PipeHandle = pipeHandle;
      internal readonly CloudflareOriginalTaskLedger? Source = source; internal Task? Close; }
    private sealed class ControlDescriptor(IntPtr original) : IDisposable
    {
        internal readonly IntPtr Value = original;
        public void Dispose() { if (Value != IntPtr.Zero && LocalFree(Value) != IntPtr.Zero) throw NativeControl("LocalFree original pipe descriptor"); }
    }
    public Task StartOriginalControlWithinSourceAsync(StartupObservation sameStartup, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        if (!IsIssuedOriginalStartup(sameStartup) || !sameStartup.HasAcceptedOriginalHomeLaunch)
            throw new UnauthorizedAccessException("The SAME actual successful controlled Home startup is required.");
        lock (_gate)
        {
            if (_controlDriver is null)
                throw new InvalidOperationException("Only actual cold Root startup may acquire the Home control listener before its child resumes.");
        }
        // This is the exact finite readiness source already owned by startup.
        // It is never the encompassing accept/session lifetime task.
        var receipt = _controlReady.Task;
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        { RunControlCallback(_controlCleanup, scope, () => retain(receipt)); return true; });
        return receipt;
    }
    private Task PrepareOriginalControlForStartup()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root listener required.");
        TaskCompletionSource? begin = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _controlSealed, this);
            if (_home is null || !_home.HasOriginalProcess || _home.IsResumed || _user is null || _installed is null)
                throw new UnauthorizedAccessException("The SAME admitted still-suspended Home process/user is required before control startup.");
            if (_controlDriver is null)
            {
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _controlDriver = ControlPublished(begin.Task);
            }
        }
        begin?.SetResult(); return _controlReady.Task;
    }
    private async Task ControlPublished(Task begin)
    {
        await begin.ConfigureAwait(false);
        using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try
        {
            while (true)
            {
                ControlConnection connection;
                lock (_gate)
                {
                    if (_controlSealed) break;
                    if (_controlConnections.Count >= 128)
                        throw new InvalidOperationException("Unconfirmed actual Root control connections remain retained.");
                    connection = new(); connection.Sources.BindOriginalOwner(this);
                    _controlConnections.Add(connection);
                    // The full finite connection is rooted before pipe/token callbacks.
                    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    connection.Driver = ServePublished(connection, start.Task);
                    start.SetResult();
                }
                try { await connection.Driver.ConfigureAwait(false); }
                catch (Exception cause) { lock (_gate) _controlFailures.Add(connection.Driver.Exception ?? cause); }
                if (connection.Sources.OriginalErrors.Count != 0) break;
                lock (_gate)
                {
                    if (connection.Driver.IsCompletedSuccessfully && connection.PipeClose?.IsCompletedSuccessfully == true &&
                        connection.Reads.Count == 0 && connection.WaitJoined)
                        _controlConnections.Remove(connection);
                    if (_controlSealed || connection.Accepted) break; // One controlled Home lifetime; no session adoption.
                }
            }
        }
        catch (Exception cause)
        {
            lock (_gate) _controlFailures.Add(cause);
            _controlReady.TrySetException(cause);
        }
        Exception[] errors; lock (_gate) errors = _controlFailures.ToArray();
        if (errors.Length != 0) throw new AggregateException("Actual Root control/session sources remain unconfirmed.", errors);
    }
    private async Task ServePublished(ControlConnection connection, Task begin)
    {
        await begin.ConfigureAwait(false);
        using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var sources = connection.Sources;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root control required.");
            sources.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                connection.Pipe = CreateOriginalControlPipe(connection, _user!.OperatingSystemPrincipalId);
                if (sources.OriginalErrors.Count != 0) throw new AggregateException("Original pipe acquisition cleanup failed before publication.", sources.OriginalErrors);
                // No child resume is permitted before the original listener exists.
                _controlReady.TrySetResult();
                connection.Wait = connection.Pipe.WaitForConnectionAsync(_controlStop.Token);
                return true;
            });
            if (connection.Wait is null) throw new InvalidOperationException("The actual Root pipe returned no original accept task.");
            try { await connection.Wait.ConfigureAwait(false); connection.WaitJoined = true; }
            catch (Exception cause)
            {
                connection.WaitJoined = true;
                // This exact raw accept was issued with the owner's private stop token.
                // It has no durable effect or accepted client. Faulted OCE/mixed payloads
                // are never promoted, and a failed Cancel callback cannot acknowledge it.
                Task? stop; bool sealedNow; lock (_gate) { stop = _controlStopOriginal; sealedNow = _controlSealed; }
                if (sealedNow && stop is not null)
                    try { await sources.AwaitAsync(stop).ConfigureAwait(false); }
                    catch (Exception sibling) { sources.Capture(stop, sibling); }
                if (sealedNow && connection.Wait.IsCanceled && stop?.IsCompletedSuccessfully == true &&
                    !connection.Pipe!.IsConnected && sources.OriginalErrors.Count == 0)
                    connection.OwnStopAcknowledged = true;
                else sources.Capture(connection.Wait, cause);
            }
            if (!connection.OwnStopAcknowledged)
            {
                connection.Accepted = sources.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    return AuthenticateOriginalHomeClient(connection);
                });
                if (connection.Accepted)
                {
                    while (true)
                    {
                        // Each actual finite command has its own bounded source
                        // cohort; completed healthy commands do not impose a lifetime
                        // request quota or retain every transport buffer forever.
                        var commandSource = new CloudflareOriginalTaskLedger(); commandSource.BindOriginalOwner(this);
                        var command = new ControlCommand(commandSource);
                        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        lock (_gate)
                        {
                            connection.Commands.RemoveAll(old => old.Healthy);
                            if (connection.Commands.Count >= 128) throw new InvalidOperationException("Unconfirmed original Root commands remain retained.");
                            command.Driver = RunCommandPublished(connection, command, start.Task);
                            connection.Commands.Add(command); // BEFORE transport/provider callbacks.
                        }
                        start.SetResult();
                        if (!await command.Driver.ConfigureAwait(false)) break;
                    }
                }
            }
        }
        catch (Exception cause) { sources.Retain(cause); _controlReady.TrySetException(cause); }
        finally
        {
            ControlCommand[] commands; lock (_gate) commands = connection.Commands.ToArray();
            foreach (var command in commands)
            {
                try { await command.Driver.ConfigureAwait(false); }
                catch (Exception cause) { command.Sources.Capture(command.Driver, cause); }
                await command.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in command.Sources.OriginalErrors) sources.Retain(cause);
            }
            // Every source-issued activation lease is joined independently. Unknown
            // close remains rooted on this connection; no native pin is reacquired.
            foreach (var read in connection.Reads.Values.ToArray())
            {
                Task? close = null;
                try { sources.Invoke(() => { close = read.CloseAndDrainOriginalAsync(); _ = sources.Track(close); return true; }); }
                catch (Exception cause) { sources.Retain(cause); }
                if (close is not null)
                    try { await sources.AwaitAsync(close).ConfigureAwait(false); }
                    catch (Exception cause) { sources.Capture(close, cause); }
            }
            if (connection.Reads.Values.All(read => read.OriginalClose?.IsCompletedSuccessfully == true)) connection.Reads.Clear();
            if (connection.Pipe is not null)
            {
                try { sources.Invoke(() => { connection.PipeClose ??= connection.Pipe.DisposeAsync().AsTask(); _ = sources.Track(connection.PipeClose); return true; }); }
                catch (Exception cause) { sources.Retain(cause); }
                if (connection.PipeClose is not null)
                    try { await sources.AwaitAsync(connection.PipeClose).ConfigureAwait(false); }
                    catch (Exception cause) { sources.Capture(connection.PipeClose, cause); }
            }
            CloseOriginalControlResources(connection, includePipe: connection.Pipe is null || connection.PipeClose?.IsCompletedSuccessfully == true);
        }
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (sources.OriginalErrors.Count != 0) throw new AggregateException("Original Root Home connection/read/cleanup failed.", sources.OriginalErrors);
    }
    private async Task<bool> RunCommandPublished(ControlConnection connection, ControlCommand command, Task begin)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var sources = command.Sources; var continued = false;
        try
        {
            var request = await NativeWindowsHomeRootWire.ReadAsync<NativeWindowsHomeRootWire.Request>(
                connection.Pipe!, sources, CancellationToken.None).ConfigureAwait(false);
            if (request is not null)
            {
                var result = await DispatchOriginalControl(connection, request, sources).ConfigureAwait(false);
                await NativeWindowsHomeRootWire.WriteAsync(connection.Pipe!, result, sources, CancellationToken.None).ConfigureAwait(false);
                continued = true;
            }
        }
        catch (Exception cause) { sources.Retain(cause); }
        await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (sources.OriginalErrors.Count != 0)
            throw new AggregateException("The actual finite Root command/raw sources failed and remain retained.", sources.OriginalErrors);
        return continued;
    }
    private async Task<NativeWindowsHomeRootWire.Response> DispatchOriginalControl(ControlConnection connection,
        NativeWindowsHomeRootWire.Request request, CloudflareOriginalTaskLedger sources)
    {
        sources.Invoke(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (!AuthenticateOriginalHomeClient(connection, sources) || request.SchemaVersion != 1 || request.CorrelationId == Guid.Empty)
                throw new UnauthorizedAccessException("The SAME controlled Home/kernel channel and bounded original protocol are required.");
            return true;
        });
        var response = new NativeWindowsHomeRootWire.Response(1, request.CorrelationId, false, null);
        if (request.Command == "close-activation")
        {
            if (request.ReadId == Guid.Empty || !connection.Reads.TryGetValue(request.ReadId, out var read))
                return response with { Reason = "OriginalActivationReadRequired" };
            Task? raw = null; sources.Invoke(() => { raw = read.CloseAndDrainOriginalAsync(); _ = sources.Track(raw); return true; });
            await sources.AwaitAsync(raw!).ConfigureAwait(false);
            connection.Reads.Remove(request.ReadId);
            return response with { Accepted = true, ReadId = request.ReadId };
        }
        if (request.Command is "prepare-selected-application" or "launch-selected-application" or
            "release-selected-application" or "observe-selected-application")
            return await DispatchOriginalSelectedApplication(connection, request, sources).ConfigureAwait(false);
        lock (_gate) if (_retiring || _controlSealed) return response with { Reason = "OriginalRootRetiring" };
        if (request.Command == "observe-current")
        {
            if (_installed is null) return response with { Reason = "ActualInstalledRootRequired" };
            var raw = sources.Invoke(() => _admission.DemandOriginalCurrentWithinSourceAsync(_installed,
                body => sources.Invoke(() => { body(); return true; }), task => { _ = sources.Track(task); }, CancellationToken.None));
            _ = sources.Track(raw); await sources.AwaitAsync(raw).ConfigureAwait(false);
            return response with { Accepted = true };
        }
        if (request.Command == "open-activation")
        {
            if (request.PackageId is null || request.PackageId.Length > 256 || _installed is null ||
                !_installed.Enrollment.SelectedPackageIds.Contains(request.PackageId, StringComparer.Ordinal))
                return response with { Reason = "OriginalInstallerSelectionRequired" };
            if (connection.Reads.Count >= 128) throw new InvalidOperationException("Unclosed actual activation reads remain retained.");
            NativeWindowsHomeInstalledRootAdmission.OriginalActivationRead? acquired = null; Guid id = Guid.Empty;
            await sources.CaptureOriginalAcquisitionAsync(() => sources.Invoke(() =>
                _admission.AcquireOriginalActivationReadWithinSourceAsync(_installed, request.PackageId,
                    body => sources.Invoke(() => { body(); return true; }), raw => { _ = sources.Track(raw); }, CancellationToken.None)),
                value =>
                {
                    acquired = value;
                    // Root this SAME late accepted read before any wrapper failure.
                    // The GUID correlates a private read, never an installed app ID.
                    id = Guid.NewGuid(); connection.Reads.Add(id, value);
                }).ConfigureAwait(false);
            var actual = acquired ?? throw new UnauthorizedAccessException("The actual Root did not issue its selected protected activation read.");
            return response with { Accepted = true, ReadId = id, PackageId = actual.Descriptor.PackageId,
                SignedDescriptor = actual.OriginalArtifact.SignedDescriptorBytes.ToArray(),
                DescriptorPayload = actual.OriginalArtifact.DescriptorPayloadBytes.ToArray(),
                CatalogueRevision = actual.OriginalArtifact.CatalogueRevision,
                ActivationOperationId = actual.Activation.OriginalRootOperationId,
                OriginalPackage = actual.OriginalPackage, Activation = actual.Activation };
        }
        if (request.Command == "validate-activation")
        {
            if (!connection.Reads.TryGetValue(request.ReadId, out var read)) return response with { Reason = "OriginalActivationReadRequired" };
            var raw = sources.Invoke(() => _admission.DemandOriginalActivationReadCurrentWithinSourceAsync(read,
                body => sources.Invoke(() => { body(); return true; }), task => { _ = sources.Track(task); }, CancellationToken.None));
            _ = sources.Track(raw); await sources.AwaitAsync(raw).ConfigureAwait(false);
            return response with { Accepted = true, ReadId = request.ReadId, PackageId = read.Descriptor.PackageId,
                ActivationOperationId = read.Activation.OriginalRootOperationId };
        }
        if (request.Command == "publish-listening")
        {
            var listening = request.Listening;
            if (listening is null || listening.InstalledApplicationId == Guid.Empty || listening.LeaseIdentity == Guid.Empty ||
                listening.Actor.ProfileId != listening.ProfileId || listening.Actor.OrganisationId is not null ||
                string.IsNullOrWhiteSpace(listening.PipeName) || listening.PipeName.Length > 240 ||
                string.IsNullOrWhiteSpace(listening.InstallationRevision) || listening.InstallationRevision.Length > 1024 ||
                string.IsNullOrWhiteSpace(listening.OriginalSessionId) || listening.OriginalSessionId.Length > 1024)
                throw new UnauthorizedAccessException("The controlled signed Home must publish its actual independently issued index/listening observation.");
            if (connection.Listening is { } prior && prior != listening)
                throw new UnauthorizedAccessException("The original Home listening/session tuple cannot be replaced on a live control connection.");
            connection.Listening = listening;
            return response with { Accepted = true, Listening = listening };
        }
        if (request.Command == "observe-listening")
            return connection.Listening is { } current ? response with { Accepted = true, Listening = current }
                : response with { Reason = "ActualHomeListeningObservationRequired" };
        return response with { Reason = "OriginalRootCommandUnavailable" };
    }
    [SupportedOSPlatform("windows")]
    private bool AuthenticateOriginalHomeClient(ControlConnection connection, CloudflareOriginalTaskLedger? commandSource = null)
    {
        var sources = commandSource ?? connection.Sources;
        var actual = connection.Pipe!;
        if (!actual.IsConnected || !GetNamedPipeClientProcessId(actual.SafePipeHandle, out var pid) || pid == 0)
            throw NativeControl("GetNamedPipeClientProcessId original Home");
        if (_home is null || _homeExit is null || !_home.HasOriginalProcess || !_home.IsResumed ||
            _homeExit.IsCompleted || pid != _home.ProcessId) return false;
        bool authenticated = false;
        try
        {
            var process = OpenControlProcess(0x1000, false, pid); connection.Resources.Add(new(process, source: sources));
            if (process.IsInvalid) throw NativeControl("OpenProcess original Home control peer");
            var opened = OpenControlToken(process, 0x0008, out var token); connection.Resources.Add(new(token, source: sources));
            if (!opened || token.IsInvalid) throw NativeControl("OpenProcessToken original Home control peer");
            var identity = new WindowsIdentity(token.DangerousGetHandle()); connection.Resources.Add(new(identity, source: sources));
            authenticated = "windows-sid:" + identity.User?.Value == _user!.OperatingSystemPrincipalId;
            if (!GetNamedPipeClientProcessId(actual.SafePipeHandle, out var again) || again != pid || _homeExit.IsCompleted)
                throw new UnauthorizedAccessException("The actual controlled Home transport/lifetime changed.");
        }
        finally { CloseOriginalControlResources(connection, includePipe: false); }
        if (sources.OriginalErrors.Count != 0)
            throw new AggregateException("Actual control peer/native query cleanup failed.", sources.OriginalErrors);
        return authenticated;
    }
    private void CloseOriginalControlResources(ControlConnection connection, bool includePipe)
    {
        foreach (var resource in connection.Resources.AsEnumerable().Reverse())
        {
            if (resource.PipeHandle && !includePipe) continue;
            var sources = resource.Source ?? connection.Sources;
            if (resource.Close is null)
            {
                var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                resource.Close = receipt.Task; _ = sources.Track(receipt.Task);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { resource.Actual.Dispose(); return true; }); receipt.SetResult(); }
                catch (Exception cause) { sources.Retain(cause); receipt.SetException(cause); }
            }
        }
        connection.Resources.RemoveAll(resource => resource.Close?.IsCompletedSuccessfully == true);
    }
    [SupportedOSPlatform("windows")]
    private NamedPipeServerStream CreateOriginalControlPipe(ControlConnection connection, string actualKernelPrincipal,
        string actualOriginalPipeName = OriginalControlPipeName)
    {
        if (actualOriginalPipeName != OriginalControlPipeName && actualOriginalPipeName != OriginalHostAttestationPipeName)
            throw new UnauthorizedAccessException("Only the source-owned fixed Root protocol locators are supported.");
        if (!actualKernelPrincipal.StartsWith("windows-sid:", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Actual Windows principal required.");
        var sid = new SecurityIdentifier(actualKernelPrincipal["windows-sid:".Length..]);
        var sddl = "O:SYG:SYD:P(A;;GA;;;SY)(A;;GRGW;;;" + sid.Value + ")";
        var accepted = ConvertStringSecurityDescriptor(sddl, 1, out var pointer, out _);
        var descriptor = new ControlDescriptor(pointer); connection.Resources.Add(new(descriptor));
        if (!accepted || descriptor.Value == IntPtr.Zero) throw NativeControl("Original Root control DACL");
        var attributes = new ControlSecurityAttributes { Length = Marshal.SizeOf<ControlSecurityAttributes>(), Descriptor = descriptor.Value };
        var native = CreateNamedPipe("\\\\.\\pipe\\" + actualOriginalPipeName,
            0x00000003 | 0x40000000 | 0x00080000, 0x00000008, 1, 65536, 65536, 0, ref attributes);
        connection.Resources.Add(new(native, pipeHandle: true));
        if (native.IsInvalid) throw NativeControl("CreateNamedPipe original Root control");
        var result = new NamedPipeServerStream(PipeDirection.InOut, true, false, native);
        // Constructor native objects were captured before publication. The stream
        // owns its actual SafePipeHandle; unknown stream close retains that handle.
        connection.Pipe = result;
        CloseOriginalControlResources(connection, includePipe: false);
        return result;
    }
    private void RequestOriginalControlStop()
    {
        TaskCompletionSource? completed = null;
        lock (_gate)
        {
            _controlSealed = true;
            if (_controlStopOriginal is not null) return;
            completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _controlStopOriginal = completed.Task; _ = _controlCleanup.Track(completed.Task);
        }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _controlStop.Cancel(); return true; }); completed.SetResult(); }
        catch (Exception cause) { completed.SetException(cause); }
    }
    private Task CloseOriginalControlAsync()
    {
        TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            if (_controlClose is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _controlClose = ClosePublished(begin.Task); }
            actual = _controlClose;
        }
        try { RequestOriginalControlStop(); } finally { begin?.SetResult(); }
        return actual;
        async Task ClosePublished(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (_controlStopOriginal is not null)
                try { await _controlCleanup.AwaitAsync(_controlStopOriginal).ConfigureAwait(false); }
                catch (Exception cause) { _controlCleanup.Capture(_controlStopOriginal, cause); }
            if (_controlDriver is not null)
                try { await _controlCleanup.AwaitAsync(_controlDriver).ConfigureAwait(false); }
                catch (Exception cause) { _controlCleanup.Capture(_controlDriver, cause); }
            var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = _controlCleanup.Track(receipt.Task);
            try { _controlStop.Dispose(); receipt.SetResult(); } catch (Exception cause) { receipt.SetException(cause); }
            await _controlCleanup.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_controlCleanup.OriginalErrors.Count != 0) throw new AggregateException("Original Root control close remains unconfirmed.", _controlCleanup.OriginalErrors);
        }
    }
    private static void RunControlCallback(CloudflareOriginalTaskLedger sources, Action<Action> scope, Action body)
    {
        var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
        var errors = new List<Exception>();
        void Keep(Exception cause) { lock (errors) errors.Add(cause); sources.Retain(cause); }
        try
        {
            try
            {
                scope(() =>
                {
                    if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                    { var refusal = new InvalidOperationException("Original Root callback is inactive, repeated or foreign-thread."); Keep(refusal); throw refusal; }
                    try { body(); } catch (Exception cause) { Keep(cause); throw; }
                });
                if (used != 1) Keep(new InvalidOperationException("Original Root callback was omitted."));
            }
            catch (Exception cause) { Keep(cause); }
        }
        finally { Volatile.Write(ref active, 0); }
        Exception[] all; lock (errors) all = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (all.Length != 0) throw new AggregateException("Original Root callback/body/protocol failed.", all);
    }
    private static Win32Exception NativeControl(string stage) => new(Marshal.GetLastWin32Error(), stage);
    [StructLayout(LayoutKind.Sequential)] private struct ControlSecurityAttributes { internal int Length; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint open, uint mode, uint instances, uint outbound, uint inbound, uint timeout, ref ControlSecurityAttributes security);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenControlProcess(uint rights, [MarshalAs(UnmanagedType.Bool)] bool inherited, uint pid);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenControlToken(SafeProcessHandle process, uint rights, out SafeAccessTokenHandle token);
}

// Strict bounded internal framing. Detached fields correlate source-owned objects;
// they cannot authenticate a process, enroll a publisher or authorize installation.
internal static class NativeWindowsHomeRootWire
{
    internal sealed record Listening(AuthenticatedResourceActor Actor, string ProfileId, Guid LeaseIdentity,
        string PipeName, Guid InstalledApplicationId, string InstallationRevision, string OriginalSessionId);
    internal sealed record SelectedChoice(Guid OperationId, string AppId, string PackageId,
        Guid InstalledApplicationId, long InstalledApplicationRevision, string ActivationSha256,
        string DescriptorSha256, Listening OriginalListening);
    internal sealed record SelectedWitness(SelectedChoice OriginalChoice, int ProcessId,
        string OperatingSystemPrincipalId, string ProcessStartIdentity, string ExecutableIdentity);
    internal sealed record Request(int SchemaVersion, Guid CorrelationId, string Command)
    { public string? PackageId { get; init; } public Guid ReadId { get; init; } public Listening? Listening { get; init; }
      public SelectedChoice? SelectedChoice { get; init; } public int ProcessId { get; init; } }
    internal sealed record Response(int SchemaVersion, Guid CorrelationId, bool Accepted, string? Reason)
    {
        public Guid ReadId { get; init; } public string? PackageId { get; init; } public string? ActivationOperationId { get; init; }
        public byte[]? SignedDescriptor { get; init; } public byte[]? DescriptorPayload { get; init; }
        public string? CatalogueRevision { get; init; } public HomePackageDatabaseEntry? OriginalPackage { get; init; }
        public HomePackageOriginalInstalledActivationRecord? Activation { get; init; } public Listening? Listening { get; init; }
        public SelectedChoice? SelectedChoice { get; init; } public SelectedWitness? SelectedWitness { get; init; }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    internal static async Task<T?> ReadAsync<T>(Stream actual, CloudflareOriginalTaskLedger sources, CancellationToken token,
        Action<T>? retainActualResult = null) where T : class
    {
        var header = new byte[4]; var offset = 0;
        while (offset != 4)
        {
            var read = await TakeRaw(sources, () => actual.ReadAsync(header.AsMemory(offset), token).AsTask()).ConfigureAwait(false);
            if (read == 0) { if (offset == 0) return null; throw new EndOfStreamException("Original Root frame header was truncated."); }
            offset += read;
        }
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > 2 * 1024 * 1024) throw new InvalidDataException("Original Root frame exceeds its bound.");
        var payload = new byte[length]; offset = 0;
        while (offset != length)
        {
            var read = await TakeRaw(sources, () => actual.ReadAsync(payload.AsMemory(offset), token).AsTask()).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Original Root frame body was truncated."); offset += read;
        }
        return sources.Invoke(() =>
        {
            var value = JsonSerializer.Deserialize<T>(payload, Json) ?? throw new InvalidDataException("Original Root frame contains no typed command.");
            // This private assignment-only receiver roots accepted resources before
            // a borrowed callback can reject publication of the decoded response.
            retainActualResult?.Invoke(value);
            return value;
        });
    }
    private static async Task<T> TakeRaw<T>(CloudflareOriginalTaskLedger sources, Func<Task<T>> factory)
    {
        Task<T>? raw = null; Exception? callbackFailure = null;
        try { sources.Invoke(() => { raw = factory(); _ = sources.Track(raw); return true; }); }
        catch (Exception cause) { sources.Retain(cause); callbackFailure = cause; }
        T value = default!;
        if (raw is not null)
            try { value = await sources.AwaitAsync(raw).ConfigureAwait(false); }
            catch (Exception cause) { sources.Capture(raw, cause); }
        if (callbackFailure is not null || sources.OriginalErrors.Count != 0)
            throw new AggregateException("Original Root transport/callback raw stages failed.", sources.OriginalErrors);
        return raw is null ? throw new InvalidOperationException("Original Root transport returned no raw task.") : value;
    }
    private static async Task TakeRaw(CloudflareOriginalTaskLedger sources, Func<Task> factory)
    {
        Task? raw = null; Exception? callbackFailure = null;
        try { sources.Invoke(() => { raw = factory(); _ = sources.Track(raw); return true; }); }
        catch (Exception cause) { sources.Retain(cause); callbackFailure = cause; }
        if (raw is not null)
            try { await sources.AwaitAsync(raw).ConfigureAwait(false); }
            catch (Exception cause) { sources.Capture(raw, cause); }
        if (callbackFailure is not null || sources.OriginalErrors.Count != 0)
            throw new AggregateException("Original Root transport/callback raw stages failed.", sources.OriginalErrors);
        if (raw is null) throw new InvalidOperationException("Original Root transport returned no raw task.");
    }
    internal static async Task WriteAsync<T>(Stream actual, T value, CloudflareOriginalTaskLedger sources, CancellationToken token)
    {
        var bytes = sources.Invoke(() => JsonSerializer.SerializeToUtf8Bytes(value, Json));
        if (bytes.Length is 0 or > 2 * 1024 * 1024) throw new InvalidDataException("Original Root response exceeds its bound.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await TakeRaw(sources, () => actual.WriteAsync(header, token).AsTask()).ConfigureAwait(false);
        await TakeRaw(sources, () => actual.WriteAsync(bytes, token).AsTask()).ConfigureAwait(false);
        await TakeRaw(sources, () => actual.FlushAsync(token)).ConfigureAwait(false);
    }
}
