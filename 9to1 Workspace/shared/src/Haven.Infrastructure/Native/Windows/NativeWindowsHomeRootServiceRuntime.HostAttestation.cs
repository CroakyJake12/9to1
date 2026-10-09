using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.Versioning;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootServiceRuntime
{
    // READ-only host evidence has its own locator and listener. An app can never
    // borrow the private controlled Home command connection or its actor grant.
    public const string OriginalHostAttestationPipeName = "9to1.root.home-host.v1";
    private readonly CancellationTokenSource _hostAttestationStop = new();
    private readonly CloudflareOriginalTaskLedger _hostAttestationCleanup = new();
    private readonly TaskCompletionSource _hostAttestationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ControlConnection> _hostAttestationQueries = [];
    private readonly Dictionary<ControlConnection, ControlConnection> _hostAttestationNativePeers = [];
    private readonly Dictionary<ControlConnection, HostRequestDeadline> _hostAttestationDeadlines = [];
    private sealed class HostRequestDeadline(CancellationTokenSource actual)
    { internal readonly CancellationTokenSource Actual = actual; internal Task? OriginalClose; }
    private Task? _hostAttestationDriver, _hostAttestationStopOriginal, _hostAttestationClose;
    private bool _hostAttestationSealed;

    private Task PrepareOriginalHostAttestationForStartup()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root listener required.");
        TaskCompletionSource? begin = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _hostAttestationSealed, this);
            if (_home is null || !_home.HasOriginalProcess || _home.IsResumed || _user is null || _installed is null)
                throw new UnauthorizedAccessException("The SAME admitted suspended Home/kernel user is required before host evidence startup.");
            if (_hostAttestationDriver is null)
            { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _hostAttestationDriver = RunHostAttestation(begin.Task); }
        }
        begin?.SetResult(); return _hostAttestationReady.Task;
    }

    private async Task RunHostAttestation(Task begin)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        while (true)
        {
            ControlConnection query; TaskCompletionSource start;
            lock (_gate)
            {
                if (_hostAttestationSealed) break;
                // Already independently joined healthy request cohorts may retire;
                // unknown requests, actual handles and failure causes remain rooted.
                foreach (var completed in _hostAttestationQueries.Where(value => value.Driver?.IsCompletedSuccessfully == true &&
                    value.PipeClose?.IsCompletedSuccessfully == true &&
                    (!_hostAttestationDeadlines.TryGetValue(value, out var deadline) || deadline.OriginalClose?.IsCompletedSuccessfully == true) &&
                    value.Sources.OriginalErrors.Count == 0 &&
                    value.Sources.OriginalTasks.All(raw => raw.IsCompletedSuccessfully)).ToArray())
                { completed.Driver!.GetAwaiter().GetResult(); _hostAttestationQueries.Remove(completed); _hostAttestationNativePeers.Remove(completed); _hostAttestationDeadlines.Remove(completed); }
                if (_hostAttestationQueries.Count >= 128)
                    throw new InvalidOperationException("Unconfirmed actual Root host observation originals remain retained.");
                query = new(); query.Sources.BindOriginalOwner(this);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                query.Driver = ObserveHostRequest(query, start.Task); _hostAttestationQueries.Add(query);
            }
            start.SetResult();
            // This actual request driver is observed before any healthy quota pruning.
            try { await query.Driver.ConfigureAwait(false); }
            catch (Exception cause) { _hostAttestationCleanup.Retain(query.Driver.Exception ?? cause); }
            if (query.OwnStopAcknowledged) break;
        }
    }

    private async Task ObserveHostRequest(ControlConnection query, Task begin)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var source = query.Sources;
        NativeWindowsHomeInstalledRootAdmission.OriginalActivationRead? read = null;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root host evidence is required.");
            source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                query.Pipe = CreateOriginalControlPipe(query, _user!.OperatingSystemPrincipalId, OriginalHostAttestationPipeName);
                _hostAttestationReady.TrySetResult();
                query.Wait = query.Pipe.WaitForConnectionAsync(_hostAttestationStop.Token); return true;
            });
            if (query.Wait is null) throw new InvalidOperationException("The actual host pipe returned no accept Task.");
            try { await query.Wait.ConfigureAwait(false); query.WaitJoined = true; }
            catch (Exception cause)
            {
                query.WaitJoined = true;
                Task? stop; bool sealedNow; lock (_gate) { stop = _hostAttestationStopOriginal; sealedNow = _hostAttestationSealed; }
                if (sealedNow && stop is not null)
                    try { await source.AwaitAsync(stop).ConfigureAwait(false); }
                    catch (Exception sibling) { source.Capture(stop, sibling); }
                if (sealedNow && query.Wait.IsCanceled && stop?.IsCompletedSuccessfully == true &&
                    query.Pipe?.IsConnected != true && source.OriginalErrors.Count == 0) query.OwnStopAcknowledged = true;
                else source.Capture(query.Wait, cause);
            }
            if (!query.OwnStopAcknowledged)
            {
                // The caller obtains only Home host evidence. Its SID is an OS peer
                // observation, never an installed app identity or service/action grant.
                var sameUser = source.Invoke(() => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); return AuthenticateOriginalHostObserver(query); });
                if (!sameUser) throw new UnauthorizedAccessException("The actual Root host observer is outside its interactive user.");
                // Root owns the actual request deadline before arming its timer.
                // It is independent of observer callbacks and does not cancel an
                // accepted Home launch or borrow the private Home command lifetime.
                var deadline = source.Invoke(() =>
                {
                    var actual = CancellationTokenSource.CreateLinkedTokenSource(_hostAttestationStop.Token);
                    var retained = new HostRequestDeadline(actual);
                    lock (_gate) _hostAttestationDeadlines.Add(query, retained);
                    actual.CancelAfter(TimeSpan.FromSeconds(30)); return retained;
                });
                var request = await NativeWindowsHomeRootWire.ReadAsync<NativeWindowsHomeRootWire.Request>(query.Pipe!, source, deadline.Actual.Token).ConfigureAwait(false);
                if (request is null) throw new EndOfStreamException("The actual host observer supplied no original request.");
                if (request.SchemaVersion != 1 || request.CorrelationId == Guid.Empty || request.Command != "observe-home-host" ||
                    request.PackageId is not null || request.ReadId != Guid.Empty || request.Listening is not null)
                    throw new InvalidDataException("Only the bounded original READ-only Home host observation is supported.");
                var response = new NativeWindowsHomeRootHostWire.Response(1, request.CorrelationId, null, "ActualHomeListeningRequired");
                ControlConnection? controlled;
                lock (_gate) controlled = _controlConnections.SingleOrDefault(value => value.Accepted && value.Listening is not null && value.PipeClose is null);
                if (!_retiring && controlled?.Listening is { } listening && _homeExit?.IsCompleted == false && _home?.IsResumed == true && _installed is not null)
                {
                    // Query native Home using a separate resource cohort. The real
                    // control connection remains private, and its resource list is not
                    // borrowed concurrently by this observer.
                    var native = new ControlConnection { Pipe = controlled.Pipe }; native.Sources.BindOriginalOwner(this);
                    lock (_gate) _hostAttestationNativePeers.Add(query, native); // Root actual query handles before callbacks.
                    if (!source.Invoke(() => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); return AuthenticateOriginalHomeClient(native, source); }))
                        throw new UnauthorizedAccessException("The actual original controlled Home exited or changed kernel identity.");
                    await source.CaptureOriginalAcquisitionAsync(() => source.Invoke(() =>
                        _admission.AcquireOriginalActivationReadWithinSourceAsync(_installed, _installed.Home.Entry.PackageId,
                            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, deadline.Actual.Token)),
                        actual => read = actual).ConfigureAwait(false);
                    if (read is null) throw new InvalidOperationException("The actual Root did not retain its original Home activation read.");
                    var validate = source.Invoke(() => _admission.DemandOriginalActivationReadCurrentWithinSourceAsync(read,
                        body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, deadline.Actual.Token));
                    _ = source.Track(validate); await source.AwaitAsync(validate).ConfigureAwait(false);
                    response = source.Invoke(() =>
                    {
                        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                        if (_retiring || _homeExit.IsCompleted || _home.ProcessId <= 0 || controlled.Listening != listening || controlled.PipeClose is not null)
                            throw new UnauthorizedAccessException("The SAME controlled Home/listening cohort retired during attestation.");
                        var activation = read.Activation; var entry = read.OriginalPackage;
                        var executable = activation.ActivatedFiles.Single(file => file.RelativePath == activation.EntrypointRelativePath).Sha256;
                        var fingerprint = NativeWindowsHomeRootHostWire.Fingerprint(entry, activation);
                        if (activation.AppId != "home" || entry.PackageId != _installed.Home.Entry.PackageId || fingerprint != listening.InstallationRevision)
                            throw new UnauthorizedAccessException("The original controlled Home no longer matches its published protected installation.");
                        return new NativeWindowsHomeRootHostWire.Response(1, request.CorrelationId,
                            new(_home.ProcessId, _user!.OperatingSystemPrincipalId, listening, entry, activation,
                                read.OriginalArtifact.SignedDescriptorBytes.ToArray(), read.OriginalArtifact.DescriptorPayloadBytes.ToArray(),
                                read.OriginalArtifact.CatalogueRevision, executable), null);
                    });
                }
                // A complete protected read closes independently before publication.
                if (read is not null)
                {
                    var close = source.Invoke(() => read.CloseAndDrainOriginalAsync()); _ = source.Track(close);
                    await source.AwaitAsync(close).ConfigureAwait(false);
                }
                await NativeWindowsHomeRootWire.WriteAsync(query.Pipe!, response, source, deadline.Actual.Token).ConfigureAwait(false);
            }
        }
        catch (Exception cause) { source.Retain(cause); _hostAttestationReady.TrySetException(cause); }
        finally
        {
            if (read is not null)
            {
                Task? close = null;
                try { source.Invoke(() => { close = read.CloseAndDrainOriginalAsync(); _ = source.Track(close); return true; }); }
                catch (Exception cause) { source.Retain(cause); }
                if (close is not null) try { await source.AwaitAsync(close).ConfigureAwait(false); } catch (Exception cause) { source.Capture(close, cause); }
            }
            if (query.Pipe is not null)
            {
                try { source.Invoke(() => { query.PipeClose ??= query.Pipe.DisposeAsync().AsTask(); _ = source.Track(query.PipeClose); return true; }); }
                catch (Exception cause) { source.Retain(cause); }
                if (query.PipeClose is not null) try { await source.AwaitAsync(query.PipeClose).ConfigureAwait(false); } catch (Exception cause) { source.Capture(query.PipeClose, cause); }
            }
            // An accepted wait remains an actual Root-owned raw receipt even
            // if a finite acquisition fails after returning it. Pipe disposal above
            // retires an unaccepted wait; this independent join cannot be skipped.
            if (query.Wait is not null && !query.WaitJoined)
            {
                try { await query.Wait.ConfigureAwait(false); }
                catch (Exception cause) { source.Capture(query.Wait, cause); }
                finally { query.WaitJoined = true; }
            }
            // Wire and activation operations are independently terminal before this
            // SAME actual CTS closes. No sibling close is skipped on failure.
            HostRequestDeadline? deadline; lock (_gate) _hostAttestationDeadlines.TryGetValue(query, out deadline);
            if (deadline is not null)
            {
                if (deadline.OriginalClose is null)
                {
                    var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    deadline.OriginalClose = close.Task; _ = source.Track(close.Task);
                    try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { deadline.Actual.Dispose(); return true; }); close.SetResult(); }
                    catch (Exception cause) { close.SetException(cause); }
                }
                try { await source.AwaitAsync(deadline.OriginalClose).ConfigureAwait(false); }
                catch (Exception cause) { source.Capture(deadline.OriginalClose, cause); }
            }
            ControlConnection? native; lock (_gate) _hostAttestationNativePeers.TryGetValue(query, out native);
            if (native is not null) CloseOriginalControlResources(native, includePipe: false);
            CloseOriginalControlResources(query, query.Pipe is null || query.PipeClose?.IsCompletedSuccessfully == true);
        }
        await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (source.OriginalErrors.Count != 0) throw new AggregateException("Actual Root host observation/raw/native cleanup remains unconfirmed.", source.OriginalErrors);
    }

    [SupportedOSPlatform("windows")]
    private bool AuthenticateOriginalHostObserver(ControlConnection query)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (query.Pipe?.IsConnected != true || !GetNamedPipeClientProcessId(query.Pipe.SafePipeHandle, out var pid) || pid == 0)
            throw NativeControl("GetNamedPipeClientProcessId actual host observer");
        var process = OpenControlProcess(0x1000, false, pid); query.Resources.Add(new(process));
        if (process.IsInvalid) throw NativeControl("OpenProcess actual host observer");
        var opened = OpenControlToken(process, 0x0008, out var token); query.Resources.Add(new(token));
        if (!opened || token.IsInvalid) throw NativeControl("OpenProcessToken actual host observer");
        var identity = new WindowsIdentity(token.DangerousGetHandle()); query.Resources.Add(new(identity));
        var same = "windows-sid:" + identity.User?.Value == _user!.OperatingSystemPrincipalId;
        if (!GetNamedPipeClientProcessId(query.Pipe.SafePipeHandle, out var again) || again != pid)
            throw new UnauthorizedAccessException("The actual host observer transport changed.");
        CloseOriginalControlResources(query, includePipe: false); return same;
    }

    private Task CloseOriginalHostAttestationAsync()
    {
        TaskCompletionSource? begin = null, stop = null; Task actual;
        lock (_gate)
        {
            _hostAttestationSealed = true;
            if (_hostAttestationClose is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _hostAttestationClose = Close(begin.Task); }
            if (_hostAttestationStopOriginal is null) { stop = new(TaskCreationOptions.RunContinuationsAsynchronously); _hostAttestationStopOriginal = stop.Task; _ = _hostAttestationCleanup.Track(stop.Task); }
            actual = _hostAttestationClose;
        }
        try
        {
            if (stop is not null)
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _hostAttestationStop.Cancel(); return true; }); stop.SetResult(); }
                catch (Exception cause) { stop.SetException(cause); }
        }
        finally { begin?.SetResult(); }
        return actual;
        async Task Close(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (_hostAttestationStopOriginal is not null)
                try { await _hostAttestationCleanup.AwaitAsync(_hostAttestationStopOriginal).ConfigureAwait(false); } catch (Exception cause) { _hostAttestationCleanup.Capture(_hostAttestationStopOriginal, cause); }
            if (_hostAttestationDriver is not null)
                try { await _hostAttestationCleanup.AwaitAsync(_hostAttestationDriver).ConfigureAwait(false); } catch (Exception cause) { _hostAttestationCleanup.Capture(_hostAttestationDriver, cause); }
            ControlConnection[] queries; lock (_gate) queries = _hostAttestationQueries.ToArray();
            foreach (var query in queries)
            {
                if (query.Driver is not null) try { await query.Driver.ConfigureAwait(false); } catch (Exception cause) { _hostAttestationCleanup.Retain(query.Driver.Exception ?? cause); }
                await query.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in query.Sources.OriginalErrors) _hostAttestationCleanup.Retain(cause);
            }
            var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _ = _hostAttestationCleanup.Track(disposed.Task);
            try { _hostAttestationStop.Dispose(); disposed.SetResult(); } catch (Exception cause) { disposed.SetException(cause); }
            await _hostAttestationCleanup.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_hostAttestationCleanup.OriginalErrors.Count != 0) throw new AggregateException("Actual Root host listener/observations close failed.", _hostAttestationCleanup.OriginalErrors);
        }
    }
}

internal static class NativeWindowsHomeRootHostWire
{
    internal sealed record Witness(int ProcessId, string OsPrincipal, NativeWindowsHomeRootWire.Listening Listening,
        HomePackageDatabaseEntry Package, HomePackageOriginalInstalledActivationRecord Activation,
        byte[] SignedDescriptor, byte[] DescriptorPayload, string CatalogueRevision, string EntrypointSha256);
    internal sealed record Response(int SchemaVersion, Guid CorrelationId, Witness? OriginalHome, string? UnavailableReason);
    internal static string Fingerprint(HomePackageDatabaseEntry package, HomePackageOriginalInstalledActivationRecord activation) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { package, activation })));
}
