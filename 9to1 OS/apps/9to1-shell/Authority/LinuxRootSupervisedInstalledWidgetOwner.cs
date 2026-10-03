using System.Diagnostics;
using System.Runtime.Versioning;
using System.Globalization;
using System.Net.Sockets;
using Haven.Application;
namespace NineToOne.Os.Shell.Authority;

// Private admission only for the exact independent signed owner ACTUALLY spawned here.
// Root owns this lifetime together with the original Home context; no public peer/DTO
// constructor, actor alias, Home role, or arbitrary process enrollment is available.
[SupportedOSPlatform("linux")]
internal sealed class LinuxRootSupervisedInstalledWidgetOwner : IAsyncDisposable
{
    private readonly Process _spawned;
    private readonly LinuxNativeAtomicSupervisedSession? _native;
    private readonly Socket _socket;
    private readonly LinuxRootInstalledWidgetOwnerStartPreparation _prepared;
    private readonly LinuxRootSupervisedHome _home;
    private readonly HomeNativeControlledLaunchSessionContext _homeContext;
    private readonly HomeNativeObservedPeer _peer;
    private readonly LinuxProcessIdentity _process;
    private readonly LinuxOriginalChildPidfd _pidfd;
    private readonly uint _groupId;
    private readonly SemaphoreSlim _gate = new(1,1);
    private int _retired;
    private readonly object _disposeGate=new();
    private Task? _disposeTask;
    private LinuxRootSupervisedInstalledWidgetOwner(Process spawned,Socket socket,
        LinuxRootInstalledWidgetOwnerStartPreparation prepared,LinuxRootSupervisedHome home,
        HomeNativeObservedPeer peer,LinuxProcessIdentity process,LinuxOriginalChildPidfd pidfd,uint groupId, LinuxNativeAtomicSupervisedSession? native = null)
    { _spawned=spawned;_socket=socket;_prepared=prepared;_home=home;_homeContext=home.ObserveOriginalContext();
      _peer=peer;_process=process;_pidfd=pidfd;_groupId=groupId;_native=native; }
    internal Socket OriginalAdministratorSocket => _socket;
    internal HomeNativeObservedPeer OriginalOwnerPeer => _peer;
    internal string OriginalOwnerProcessStart => _process.StartTime;
    internal string OriginalOwnerExecutable => _process.ExecutableIdentity(_peer.ProcessId);
    internal string OriginalOwnerAppId => _prepared.SignedAppId;
    internal HomeNativeControlledLaunchSessionContext OriginalHomeContext => _homeContext;
    internal AuthenticatedResourceActor OriginalHomeActorObservation => _home.OriginalHomeActorObservation;
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


    internal static async Task<LinuxRootSupervisedInstalledWidgetOwner?> StartPreparedAsync(
        LinuxRootInstalledWidgetOwnerStartPreparation prepared,LinuxRootSupervisedHome originalHome,
        Socket actualListeningSocket,string actualSocketPath,string actualWidgetSocketPath,
        uint userId,uint groupId,string actualUserHome,CancellationToken ct)
    {
        if(!OperatingSystem.IsLinux() || userId is 0 or uint.MaxValue || groupId is 0 or uint.MaxValue ||
           await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct)!="unix-euid:0" ||
           !await HasActualSupervisorCapabilitiesAsync(ct) || !await prepared.IsCurrentForAdministratorAsync(ct) ||
           !await originalHome.IsOriginalIssuedContextCurrentAsync(originalHome.ObserveOriginalContext(),ct) ||
           actualListeningSocket.LocalEndPoint is not UnixDomainSocketEndPoint bound || bound.ToString()!=actualSocketPath ||
           !Path.IsPathFullyQualified(actualSocketPath) || !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualSocketPath)!) ||
           !Path.IsPathFullyQualified(actualWidgetSocketPath) || Path.GetFullPath(actualWidgetSocketPath)!=actualWidgetSocketPath ||
           !Path.IsPathFullyQualified(actualUserHome) || Path.GetFullPath(actualUserHome)!=actualUserHome ||
           !Directory.Exists(actualUserHome) || (File.GetAttributes(actualUserHome)&FileAttributes.ReparsePoint)!=0 ||
           !LinuxRootOwnedFiles.RegularFileImmutable("/usr/bin/setpriv") || !LinuxOriginalSpawnPidfd.PlatformHandleAvailable()) return null;
        var start = new ProcessStartInfo("/usr/bin/setpriv") { UseShellExecute = false,
            WorkingDirectory = prepared.InstallRoot };
        start.ArgumentList.Add("--no-new-privs"); start.ArgumentList.Add("--");
        start.ArgumentList.Add(prepared.ExecutablePath); start.ArgumentList.Add("--native-controlled-widget-owner");
        start.ArgumentList.Add(userId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(groupId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(actualSocketPath); start.ArgumentList.Add(actualWidgetSocketPath);
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

            var context=originalHome.ObserveOriginalContext();
            var pidfd=await LinuxOriginalChildPidfd.ObserveAsync(peer,process,deadline.Token);
            if(pidfd is null) return null;
            if(spawned.HasExited || !await originalHome.IsOriginalIssuedContextCurrentAsync(context,deadline.Token))
            { pidfd.Dispose(); return null; }
            var result=new LinuxRootSupervisedInstalledWidgetOwner(spawned,accepted,prepared,originalHome,peer,process,pidfd,groupId);
            accepted=null; published=true;
            bool current;
            try { current=await result.IsOriginalCurrentAsync(deadline.Token); }
            catch { await result.DisposeAsync(); throw; }
            if(!current) { await result.DisposeAsync(); return null; }
            return result;
        }
        finally
        {
            accepted?.Dispose();
            if(!published)
            {
                if(startupHandle is not null && !await startupHandle.TerminateAndDrainSameActualSpawnAsync())
                    Console.Error.WriteLine("Original installed owner did not acknowledge bounded shutdown.");
                spawned.Dispose();
            }
            startupHandle?.Dispose();
        }
    }
    internal static async Task<LinuxRootSupervisedInstalledWidgetOwner?> StartNativePreparedAsync(
        LinuxRootAtomicSpawnHelperPreparation actualPreparedHelper,string privateNativeControlPath,
        LinuxRootInstalledWidgetOwnerStartPreparation prepared,LinuxRootSupervisedHome originalHome,
        Socket actualListeningSocket,string actualSocketPath,string actualWidgetSocketPath,
        uint userId,uint groupId,string actualUserHome,CancellationToken ct)
    {
        var originalContext = originalHome.ObserveOriginalContext();
        if(!OperatingSystem.IsLinux() || userId is 0 or uint.MaxValue || groupId is 0 or uint.MaxValue ||
           await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct)!="unix-euid:0" ||
           !await HasActualSupervisorCapabilitiesAsync(ct) || !await prepared.IsCurrentForAdministratorAsync(ct) ||
           !await originalHome.IsOriginalIssuedContextCurrentAsync(originalContext,ct) ||
           actualListeningSocket.LocalEndPoint is not UnixDomainSocketEndPoint bound || bound.ToString()!=actualSocketPath ||
           !Path.IsPathFullyQualified(actualSocketPath) || !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(actualSocketPath)!) ||
           !Path.IsPathFullyQualified(actualWidgetSocketPath) || Path.GetFullPath(actualWidgetSocketPath)!=actualWidgetSocketPath ||
           !Path.IsPathFullyQualified(actualUserHome) || Path.GetFullPath(actualUserHome)!=actualUserHome ||
           !Directory.Exists(actualUserHome) || (File.GetAttributes(actualUserHome)&FileAttributes.ReparsePoint)!=0 ||
           !LinuxRootOwnedFiles.RegularFileImmutable("/usr/bin/setpriv") || !LinuxOriginalSpawnPidfd.PlatformHandleAvailable()) return null;
        var fixedArguments = new[] { "--no-new-privs", "--", prepared.ExecutablePath,
            "--native-controlled-widget-owner", userId.ToString(CultureInfo.InvariantCulture),
            groupId.ToString(CultureInfo.InvariantCulture), actualSocketPath, actualWidgetSocketPath };
        var native = await LinuxNativeAtomicSupervisedSession.StartForAdministratorAsync(actualPreparedHelper,
            privateNativeControlPath, prepared.InstallRoot, actualUserHome, fixedArguments, async token =>
                await prepared.IsCurrentForAdministratorAsync(token) &&
                await originalHome.IsOriginalIssuedContextCurrentAsync(originalContext, token), ct).ConfigureAwait(false);
        if (native is null) return null;
        // Process is observational only; actual original shutdown identity is the
        // private received native pidfd, never this non-parent Process wrapper.
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

            var context=originalContext;
            var pidfd=await LinuxOriginalChildPidfd.ObserveAsync(peer,process,deadline.Token);
            if(pidfd is null) return null;
            if(!await native.IsOriginalLiveAsync(deadline.Token) || !await originalHome.IsOriginalIssuedContextCurrentAsync(context,deadline.Token))
            { pidfd.Dispose(); return null; }
            var result=new LinuxRootSupervisedInstalledWidgetOwner(spawned,accepted,prepared,originalHome,peer,process,pidfd,groupId,native);
            accepted=null; published=true;
            bool current;
            try { current=await result.IsOriginalCurrentAsync(deadline.Token); }
            catch { await result.DisposeAsync(); throw; }
            if(!current) { await result.DisposeAsync(); return null; }
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
        await _gate.WaitAsync(ct);
        try
        {
            if(Volatile.Read(ref _retired)!=0 || (_native is null ? _spawned.HasExited : !await _native.IsOriginalLiveAsync(ct)) ||
               await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct)!="unix-euid:0" ||
               _process!=await LinuxProcessIdentity.ReadAsync(_peer,ct) ||
               !await HasActualChildCredentialBoundaryAsync(_peer,_groupId,ct) ||
               !await _prepared.IsCurrentForAdministratorAsync(ct) || !await _prepared.MatchesActualRuntimeAsync(_peer,ct) ||
               !await _pidfd.IsOriginalCurrentAsync(ct) || !await _home.IsOriginalIssuedContextCurrentAsync(_homeContext,ct))
                return Retire();
            return (_native is null || await _native.IsOriginalLiveAsync(ct)) && Volatile.Read(ref _retired)==0;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidOperationException or SocketException)
        { return Retire(); }
        finally { _gate.Release(); }
    }
    internal async Task RetireOriginalNativeHelperAndDrainForAdministratorAsync(CancellationToken ct)
    {
        if (_native is null || !await IsOriginalCurrentAsync(ct))
            throw new UnauthorizedAccessException("Actual current native installed owner required.");
        // Deny new admissions without closing the original owner socket before
        // the actual helper signal: EOF must not cause an earlier natural exit.
        Interlocked.Exchange(ref _retired, 1);
        Exception? primary = null;
        try { await _native.RetireOriginalHelperAndObserveChildExitForAdministratorAsync(ct); }
        catch (Exception error) { primary = error; }
        try { await DisposeAsync(); }
        catch (Exception error) { primary = primary is null ? error : new AggregateException(primary, error); }
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private bool Retire() { Interlocked.Exchange(ref _retired,1);_socket.Dispose();return false; }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion=null;Task task;
        lock(_disposeGate)
        {
            if(_disposeTask is null)
            {completion=new(TaskCreationOptions.RunContinuationsAsynchronously);_disposeTask=completion.Task;}
            task=_disposeTask;
        }
        if(completion is not null) _=FinishDisposeAsync(completion);
        return new(task);
    }
    private async Task FinishDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            Retire();
            await _gate.WaitAsync();
            try
            {
                if (_native is not null) await _native.DisposeAsync();
                else if(!_spawned.HasExited)
                {
                    await _pidfd.TerminateOriginalAsync(default);
                    try { await _spawned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch(TimeoutException)
                    {
                        if(!await _pidfd.KillOriginalAsync(default)) throw new IOException("Original owner shutdown identity unavailable.");
                        await _spawned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                }
            }
            finally
            {
                try {_pidfd.Dispose();} finally {_spawned.Dispose();_gate.Release();}
            }
            completion.TrySetResult();
        }
        catch(Exception error){completion.TrySetException(error);}
    }
}
