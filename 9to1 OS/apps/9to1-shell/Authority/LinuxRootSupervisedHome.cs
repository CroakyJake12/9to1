using System.Runtime.Versioning;
using System.Diagnostics;
using System.Net.Sockets;
using System.Globalization;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Administrator-owned original child startup. No PID, actor, lease or executable
/// supplied by a client grants issuance. The fixed root socket is an explicit trusted
/// administrator boundary; its assertions are not owner-side kernel executable observations.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxRootSupervisedHome : IDisposable
{
    private readonly Process _spawned;
    private readonly LinuxNativeAtomicSupervisedSession? _native;
    private readonly uint _groupId;
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly LinuxRootHomeStartPreparation _prepared;
    private readonly HomeNativeObservedPeer _peer;
    private readonly LinuxProcessIdentity _process;
    private readonly LinuxHomeLeaseReply _original;
    private readonly LinuxHomeLeaseKernelWitness _witness;
    private readonly LinuxOriginalChildPidfd _pidfd;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HomeNativeControlledLaunchSessionContext _issuedContext;
    private readonly Task _exitWatcher;
    private int _retired;
    private LinuxRootSupervisedHome(Process spawned, Socket socket, LinuxRootHomeStartPreparation prepared,
        HomeNativeObservedPeer peer, LinuxProcessIdentity process, LinuxHomeLeaseReply original,
        LinuxHomeLeaseKernelWitness witness, LinuxOriginalChildPidfd pidfd, uint groupId, LinuxNativeAtomicSupervisedSession? native = null)
    {
        _native = native; _spawned = spawned; _groupId = groupId; _socket = socket; _stream = new NetworkStream(socket, ownsSocket: false);
        _prepared = prepared; _peer = peer; _process = process; _original = original; _witness = witness; _pidfd = pidfd;
        _issuedContext = new(original.OriginalHomeActor.ProfileId, original.LeaseIdentity, peer,
            process.StartTime, process.ExecutableIdentity(peer.ProcessId));
        _exitWatcher = WatchOriginalExitAsync();
    }
    internal AuthenticatedResourceActor OriginalHomeActorObservation => _original.OriginalHomeActor;
    internal HomeNativeControlledLaunchSessionContext ObserveOriginalContext() => _issuedContext;
    internal ValueTask<bool> IsOriginalIssuedContextCurrentAsync(HomeNativeControlledLaunchSessionContext context, CancellationToken ct) =>
        ReferenceEquals(context, _issuedContext) ? IsOriginalCurrentAsync(ct) : ValueTask.FromResult(false);
    private async Task WatchOriginalExitAsync()
    {
        try { if (_native is not null) await _native.OriginalExitObservedTask.ConfigureAwait(false);
              else await _spawned.WaitForExitAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { Retire(); _pidfd.Dispose(); _spawned.Dispose(); }
    }

    private static async Task<bool> HasActualSupervisorCapabilitiesAsync(CancellationToken ct)
    {
        var status = await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/status", 65536, ct);
        if (status is null) return false;
        var rows = status.Split('\n').Where(line => line.StartsWith("CapEff:", StringComparison.Ordinal)).ToArray();
        if (rows.Length != 1) return false;
        var values = rows[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        const ulong required = (1ul << 1) | (1ul << 5) | (1ul << 6) | (1ul << 7) | (1ul << 8) | (1ul << 19);
        return values.Length == 2 && ulong.TryParse(values[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var actual) &&
            (actual & required) == required;
    }

    private static async Task<bool> HasActualChildCredentialBoundaryAsync(HomeNativeObservedPeer peer, uint groupId, CancellationToken ct)
    {
        if (!uint.TryParse(peer.OperatingSystemPrincipalId.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var uid)) return false;
        var status = await LinuxProcBoundedObservation.ReadAsync($"/proc/{peer.ProcessId}/status", 65536, ct);
        if (status is null) return false;
        var fields = status.Split('\n').Where(line => line.Contains(':'))
            .ToDictionary(line => line[..line.IndexOf(':')], line => line[(line.IndexOf(':') + 1)..].Trim(), StringComparer.Ordinal);
        static bool Ids(string? input, uint expected) => input is not null &&
            input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is { Length: 4 } values &&
            values.All(value => uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var actual) && actual == expected);
        return fields.TryGetValue("Uid", out var uids) && Ids(uids, uid) &&
            fields.TryGetValue("Gid", out var gids) && Ids(gids, groupId) &&
            fields.TryGetValue("Groups", out var groups) && groups.Length == 0 &&
            fields.TryGetValue("NoNewPrivs", out var nnp) && nnp == "1" &&
            new[] { "CapEff", "CapPrm", "CapInh", "CapAmb", "CapBnd" }.All(name =>
                fields.TryGetValue(name, out var value) && value.Length == 16 && value.All(character => character == '0'));
    }

    // The caller owns an ACTUAL bound root socket in an immutable root directory and keeps
    // it alive for this original bootstrap only. Child admission is exact kernel PID+UID.
    internal static Task<LinuxRootSupervisedHome?> StartPreparedAsync(
        LinuxRootHomeStartPreparation prepared, Socket actualListeningSocket,
        string actualSocketPath, uint userId, uint groupId, string actualUserHome,
        CancellationToken ct)
 => StartCoreAsync(prepared, actualListeningSocket, actualSocketPath, userId, groupId, actualUserHome, null, null, ct);
    internal static Task<LinuxRootSupervisedHome?> StartWithCanonicalPreparedAsync(
        LinuxRootHomeStartPreparation prepared, Socket actualListeningSocket,
        string actualSocketPath, uint userId, uint groupId, string actualUserHome,
        string actualCanonicalSocketPath, CancellationToken ct) =>
        StartCoreAsync(prepared, actualListeningSocket, actualSocketPath, userId, groupId, actualUserHome, actualCanonicalSocketPath, null, ct);
    internal static Task<LinuxRootSupervisedHome?> StartWithOriginalObservationChannelsPreparedAsync(
        LinuxRootHomeStartPreparation prepared, Socket actualListeningSocket, string actualSocketPath,
        uint userId, uint groupId, string actualUserHome, string actualCanonicalSocketPath,
        string actualWidgetObservationSocketPath, CancellationToken ct) =>
        StartCoreAsync(prepared, actualListeningSocket, actualSocketPath, userId, groupId, actualUserHome,
            actualCanonicalSocketPath, actualWidgetObservationSocketPath, ct);
    private static async Task<LinuxRootSupervisedHome?> StartCoreAsync(
        LinuxRootHomeStartPreparation prepared, Socket actualListeningSocket,
        string actualSocketPath, uint userId, uint groupId, string actualUserHome,
        string? actualCanonicalSocketPath, string? actualWidgetObservationSocketPath, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || userId is 0 or uint.MaxValue || groupId is 0 or uint.MaxValue ||
            await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !await HasActualSupervisorCapabilitiesAsync(ct) ||
            !await prepared.IsCurrentForAdministratorAsync(ct) ||
            actualListeningSocket.LocalEndPoint is not UnixDomainSocketEndPoint bound ||
            bound.ToString() != actualSocketPath || !Path.IsPathFullyQualified(actualSocketPath) ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualSocketPath)!) ||
            !Path.IsPathFullyQualified(actualUserHome) || Path.GetFullPath(actualUserHome) != actualUserHome ||
            !Directory.Exists(actualUserHome) || (File.GetAttributes(actualUserHome) & FileAttributes.ReparsePoint) != 0 ||
            (actualCanonicalSocketPath is not null && (!Path.IsPathFullyQualified(actualCanonicalSocketPath) ||
                Path.GetFullPath(actualCanonicalSocketPath) != actualCanonicalSocketPath ||
                !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualCanonicalSocketPath)!))) ||
            (actualWidgetObservationSocketPath is not null && (actualCanonicalSocketPath is null ||
                actualWidgetObservationSocketPath == actualCanonicalSocketPath ||
                !Path.IsPathFullyQualified(actualWidgetObservationSocketPath) ||
                Path.GetFullPath(actualWidgetObservationSocketPath) != actualWidgetObservationSocketPath ||
                !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualWidgetObservationSocketPath)!))) ||
            !LinuxRootOwnedFiles.RegularFileImmutable("/usr/bin/setpriv") ||
            !LinuxOriginalSpawnPidfd.PlatformHandleAvailable()) return null;
        // Maintained util-linux establishes inherited NNP BEFORE CLR threads are created.
        // The real protected apphost still begins as root and drops all credentials in Main.
        var start = new ProcessStartInfo("/usr/bin/setpriv") { UseShellExecute = false,
            WorkingDirectory = prepared.InstallRoot };
        start.ArgumentList.Add("--no-new-privs"); start.ArgumentList.Add("--");
        start.ArgumentList.Add(prepared.ExecutablePath); start.ArgumentList.Add("--native-controlled-home-child");
        start.ArgumentList.Add(userId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(groupId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(actualSocketPath); start.ArgumentList.Add("--service-only");
        if (actualCanonicalSocketPath is not null)
        { start.ArgumentList.Add("--canonical-socket"); start.ArgumentList.Add(actualCanonicalSocketPath); }
        if (actualWidgetObservationSocketPath is not null)
        { start.ArgumentList.Add("--widget-observation-socket"); start.ArgumentList.Add(actualWidgetObservationSocketPath); }
        start.Environment.Clear(); start.Environment["HOME"] = actualUserHome;
        start.Environment["PATH"] = "/usr/bin:/bin"; start.Environment["DOTNET_EnableDiagnostics"] = "0";
        var spawned = Process.Start(start) ?? throw new IOException("Actual maintained child could not start.");
        Socket? accepted = null; var published = false;
        LinuxOriginalSpawnPidfd? startupHandle = null;
        try
        {
            startupHandle = await LinuxOriginalSpawnPidfd.CaptureAsync(spawned, CancellationToken.None);
            if (startupHandle is null) return null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30)); var attempts = 0;
            while (true)
            {
                if (spawned.HasExited || ++attempts > 64) return null;
                accepted = await actualListeningSocket.AcceptAsync(deadline.Token).ConfigureAwait(false);
                var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
                if (observed == new HomeNativeObservedPeer(spawned.Id, "unix-euid:" + userId.ToString(CultureInfo.InvariantCulture))) break;
                accepted.Dispose(); accepted = null;
            }
            var peer = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
            if (peer is null || spawned.HasExited) return null;
            var process = await LinuxProcessIdentity.ReadAsync(peer, deadline.Token).ConfigureAwait(false);
            if (process is null || process.ExecutablePath != prepared.ExecutablePath ||
                !await HasActualChildCredentialBoundaryAsync(peer, groupId, deadline.Token) ||
                !await prepared.IsCurrentForAdministratorAsync(deadline.Token) ||
                !await prepared.MatchesActualRuntimeAsync(peer, deadline.Token)) return null;
            using var stream = new NetworkStream(accepted, ownsSocket: false);
            var correlation = Guid.NewGuid();
            await LinuxHomeChildLeaseChannel.WriteAsync(stream, new LinuxHomeLeaseChallenge(1, correlation), deadline.Token);
            var reply = await LinuxHomeChildLeaseChannel.ReadAsync<LinuxHomeLeaseReply>(stream, deadline.Token);
            if (reply.Schema != 1 || reply.Correlation != correlation ||
                !Guid.TryParseExact(reply.LeaseIdentity, "N", out var lease) || lease == Guid.Empty ||
                reply.OriginalHomeActor is null || reply.OriginalHomeActor.OrganisationId is not null ||
                string.IsNullOrWhiteSpace(reply.OriginalHomeActor.ProfileId) ||
                string.IsNullOrWhiteSpace(reply.OriginalHomeActor.AuthenticationRevision) ||
                !Path.IsPathFullyQualified(reply.LeasePath)) return null;
            var witness = await LinuxHomeLeaseKernelWitness.ObserveAsync(peer, reply.LeasePath, deadline.Token);
            if (witness is null || spawned.HasExited || process != await LinuxProcessIdentity.ReadAsync(peer, deadline.Token) ||
                !await prepared.IsCurrentForAdministratorAsync(deadline.Token) ||
                !await prepared.MatchesActualRuntimeAsync(peer, deadline.Token)) return null;
            var pidfd = await LinuxOriginalChildPidfd.ObserveAsync(peer, process, deadline.Token);
            if (pidfd is null || spawned.HasExited) { pidfd?.Dispose(); return null; }
            var result = new LinuxRootSupervisedHome(spawned, accepted, prepared, peer, process, reply, witness, pidfd, groupId);
            accepted = null;
            if (!await result.IsOriginalCurrentAsync(deadline.Token)) { result.Dispose(); return null; }
            published = true; return result;
        }
        finally
        {
            accepted?.Dispose();
            if (!published)
            {
                actualListeningSocket.Dispose();
                // No generic Kill(PID) or child-tree sweep. Only this retained original pidfd.
                if (startupHandle is not null && !await startupHandle.TerminateAndDrainSameActualSpawnAsync())
                    Console.Error.WriteLine("The original spawned Home did not acknowledge bounded shutdown.");
                spawned.Dispose();
            }
            startupHandle?.Dispose();
        }
    }

    internal static async Task<LinuxRootSupervisedHome?> StartNativeWithOriginalObservationChannelsPreparedAsync(
        LinuxRootAtomicSpawnHelperPreparation actualPreparedHelper, string privateNativeControlPath,
        LinuxRootHomeStartPreparation prepared, Socket actualListeningSocket,
        string actualSocketPath, uint userId, uint groupId, string actualUserHome,
        string? actualCanonicalSocketPath, string? actualWidgetObservationSocketPath, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || userId is 0 or uint.MaxValue || groupId is 0 or uint.MaxValue ||
            await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !await HasActualSupervisorCapabilitiesAsync(ct) ||
            !await prepared.IsCurrentForAdministratorAsync(ct) ||
            actualListeningSocket.LocalEndPoint is not UnixDomainSocketEndPoint bound ||
            bound.ToString() != actualSocketPath || !Path.IsPathFullyQualified(actualSocketPath) ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualSocketPath)!) ||
            !Path.IsPathFullyQualified(actualUserHome) || Path.GetFullPath(actualUserHome) != actualUserHome ||
            !Directory.Exists(actualUserHome) || (File.GetAttributes(actualUserHome) & FileAttributes.ReparsePoint) != 0 ||
            (actualCanonicalSocketPath is not null && (!Path.IsPathFullyQualified(actualCanonicalSocketPath) ||
                Path.GetFullPath(actualCanonicalSocketPath) != actualCanonicalSocketPath ||
                !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualCanonicalSocketPath)!))) ||
            (actualWidgetObservationSocketPath is not null && (actualCanonicalSocketPath is null ||
                actualWidgetObservationSocketPath == actualCanonicalSocketPath ||
                !Path.IsPathFullyQualified(actualWidgetObservationSocketPath) ||
                Path.GetFullPath(actualWidgetObservationSocketPath) != actualWidgetObservationSocketPath ||
                !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualWidgetObservationSocketPath)!))) ||
            !LinuxRootOwnedFiles.RegularFileImmutable("/usr/bin/setpriv") ||
            !LinuxOriginalSpawnPidfd.PlatformHandleAvailable()) return null;
        // Maintained util-linux establishes inherited NNP BEFORE CLR threads are created.
        // The real protected apphost still begins as root and drops all credentials in Main.
        var fixedArguments = new List<string>();
        fixedArguments.Add("--no-new-privs"); fixedArguments.Add("--");
        fixedArguments.Add(prepared.ExecutablePath); fixedArguments.Add("--native-controlled-home-child");
        fixedArguments.Add(userId.ToString(CultureInfo.InvariantCulture));
        fixedArguments.Add(groupId.ToString(CultureInfo.InvariantCulture));
        fixedArguments.Add(actualSocketPath); fixedArguments.Add("--service-only");
        if (actualCanonicalSocketPath is not null)
        { fixedArguments.Add("--canonical-socket"); fixedArguments.Add(actualCanonicalSocketPath); }
        if (actualWidgetObservationSocketPath is not null)
        { fixedArguments.Add("--widget-observation-socket"); fixedArguments.Add(actualWidgetObservationSocketPath); }
        var native = await LinuxNativeAtomicSupervisedSession.StartForAdministratorAsync(actualPreparedHelper,
            privateNativeControlPath, prepared.InstallRoot, actualUserHome, fixedArguments,
            token => prepared.IsCurrentForAdministratorAsync(token), ct).ConfigureAwait(false);
        if (native is null) return null;
        Process spawned;
        try { spawned = Process.GetProcessById(native.OriginalProcessId); }
        catch { await native.DisposeAsync(); throw; }
        Socket? accepted = null; var published = false; Exception? primary = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30)); var attempts = 0;
            while (true)
            {
                if (!await native.IsOriginalLiveAsync(deadline.Token) || ++attempts > 64) return null;
                accepted = await actualListeningSocket.AcceptAsync(deadline.Token).ConfigureAwait(false);
                var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
                if (observed == new HomeNativeObservedPeer(spawned.Id, "unix-euid:" + userId.ToString(CultureInfo.InvariantCulture))) break;
                accepted.Dispose(); accepted = null;
            }
            var peer = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
            if (peer is null || !await native.IsOriginalLiveAsync(deadline.Token)) return null;
            var process = await LinuxProcessIdentity.ReadAsync(peer, deadline.Token).ConfigureAwait(false);
            if (process is null || process.StartTime != native.OriginalStartTicks.ToString(CultureInfo.InvariantCulture) ||
                !await native.IsOriginalLiveAsync(deadline.Token) || process.ExecutablePath != prepared.ExecutablePath ||
                !await HasActualChildCredentialBoundaryAsync(peer, groupId, deadline.Token) ||
                !await prepared.IsCurrentForAdministratorAsync(deadline.Token) ||
                !await prepared.MatchesActualRuntimeAsync(peer, deadline.Token)) return null;
            using var stream = new NetworkStream(accepted, ownsSocket: false);
            var correlation = Guid.NewGuid();
            await LinuxHomeChildLeaseChannel.WriteAsync(stream, new LinuxHomeLeaseChallenge(1, correlation), deadline.Token);
            var reply = await LinuxHomeChildLeaseChannel.ReadAsync<LinuxHomeLeaseReply>(stream, deadline.Token);
            if (reply.Schema != 1 || reply.Correlation != correlation ||
                !Guid.TryParseExact(reply.LeaseIdentity, "N", out var lease) || lease == Guid.Empty ||
                reply.OriginalHomeActor is null || reply.OriginalHomeActor.OrganisationId is not null ||
                string.IsNullOrWhiteSpace(reply.OriginalHomeActor.ProfileId) ||
                string.IsNullOrWhiteSpace(reply.OriginalHomeActor.AuthenticationRevision) ||
                !Path.IsPathFullyQualified(reply.LeasePath)) return null;
            var witness = await LinuxHomeLeaseKernelWitness.ObserveAsync(peer, reply.LeasePath, deadline.Token);
            if (witness is null || !await native.IsOriginalLiveAsync(deadline.Token) || process != await LinuxProcessIdentity.ReadAsync(peer, deadline.Token) ||
                !await prepared.IsCurrentForAdministratorAsync(deadline.Token) ||
                !await prepared.MatchesActualRuntimeAsync(peer, deadline.Token)) return null;
            var pidfd = await LinuxOriginalChildPidfd.ObserveAsync(peer, process, deadline.Token);
            if (pidfd is null || !await native.IsOriginalLiveAsync(deadline.Token)) { pidfd?.Dispose(); return null; }
            var result = new LinuxRootSupervisedHome(spawned, accepted, prepared, peer, process, reply, witness, pidfd, groupId, native);
            accepted = null; published = true; // result now owns socket, pidfd and session
            bool current;
            try { current = await result.IsOriginalCurrentAsync(deadline.Token); }
            catch (Exception observation)
            {
                Exception? cleanup = null;
                try { if (!await result.ShutdownOriginalAndDrainForAdministratorAsync()) throw new IOException("Original native Home shutdown was not acknowledged."); }
                catch (Exception error) { cleanup = error; }
                try { await native.DisposeAsync(); await result.OriginalExitObserved; }
                catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                try { result.Dispose(); } catch (Exception error) { cleanup ??= error; }
                if (cleanup is not null) throw new AggregateException(observation, cleanup);
                throw;
            }
            if (!current)
            {
                Exception? cleanup = null;
                try { if (!await result.ShutdownOriginalAndDrainForAdministratorAsync()) throw new IOException("Original native Home shutdown was not acknowledged."); }
                catch (Exception error) { cleanup = error; }
                try { await native.DisposeAsync(); await result.OriginalExitObserved; }
                catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                try { result.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null) throw cleanup;
                return null;
            }
            return result;
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Exception? cleanup = null;
            void Attempt(Action action) { try { action(); } catch (Exception error) { cleanup ??= error; } }
            Attempt(() => accepted?.Dispose());
            if (!published)
            {
                try { await native.DisposeAsync(); } catch (Exception error) { cleanup ??= error; }
                Attempt(spawned.Dispose);
            }
            if (cleanup is not null) throw primary is null ? cleanup : new AggregateException(primary, cleanup);
        }
    }

    internal async ValueTask<bool> IsOriginalCurrentAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _retired) != 0) return false;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _retired) != 0 || (_native is null ? _spawned.HasExited : !await _native.IsOriginalLiveAsync(ct)) ||
                await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
                _process != await LinuxProcessIdentity.ReadAsync(_peer, ct) ||
                !await HasActualChildCredentialBoundaryAsync(_peer, _groupId, ct) ||
                !await _prepared.IsCurrentForAdministratorAsync(ct) ||
                !await _prepared.MatchesActualRuntimeAsync(_peer, ct) ||
                !await _witness.IsCurrentAsync(ct) || !await _pidfd.IsOriginalCurrentAsync(ct)) return Retire();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5)); var correlation = Guid.NewGuid();
            await LinuxHomeChildLeaseChannel.WriteAsync(_stream, new LinuxHomeLeaseChallenge(1, correlation), deadline.Token);
            var reply = await LinuxHomeChildLeaseChannel.ReadAsync<LinuxHomeLeaseReply>(_stream, deadline.Token);
            if (reply.Schema != 1 || reply.Correlation != correlation || reply.LeaseIdentity != _original.LeaseIdentity ||
                reply.LeasePath != _original.LeasePath || reply.OriginalHomeActor != _original.OriginalHomeActor ||
                (_native is null ? _spawned.HasExited : !await _native.IsOriginalLiveAsync(deadline.Token)) || _process != await LinuxProcessIdentity.ReadAsync(_peer, deadline.Token) ||
                !await HasActualChildCredentialBoundaryAsync(_peer, _groupId, deadline.Token) ||
                !await _witness.IsCurrentAsync(deadline.Token) ||
                !await _prepared.IsCurrentForAdministratorAsync(deadline.Token) ||
                !await _prepared.MatchesActualRuntimeAsync(_peer, deadline.Token) ||
                !await _pidfd.IsOriginalCurrentAsync(deadline.Token)) return Retire();
            return (_native is null || await _native.IsOriginalLiveAsync(ct)) && Volatile.Read(ref _retired) == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SocketException or
            OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        { Retire(); if (ct.IsCancellationRequested) throw; return false; }
        finally { _gate.Release(); }
    }
    private bool Retire() { Interlocked.Exchange(ref _retired, 1); _socket.Dispose(); return false; }
    internal async Task<bool> TerminateOriginalForAdministratorAsync(CancellationToken ct)
    {
        if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0") return false;
        var signaled = await _pidfd.TerminateOriginalAsync(ct);
        Retire(); return signaled;
    }
    public void Dispose() { Retire(); _stream.Dispose(); }
    internal async Task<bool> ShutdownOriginalAndDrainForAdministratorAsync()
    {
        if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(default) != "unix-euid:0") return false;
        Retire();
        if (_native is not null) { await _native.DisposeAsync(); await _exitWatcher; return true; }
        if (_exitWatcher.IsCompleted) { await _exitWatcher; return true; }
        await _pidfd.TerminateOriginalAsync(default);
        try { await _exitWatcher.WaitAsync(TimeSpan.FromSeconds(10)); return true; }
        catch (TimeoutException) { }
        if (!await _pidfd.KillOriginalAsync(default)) return _exitWatcher.IsCompleted;
        try { await _exitWatcher.WaitAsync(TimeSpan.FromSeconds(10)); return true; }
        catch (TimeoutException) { return false; }
    }
    internal Task OriginalExitObserved => _exitWatcher;
}
