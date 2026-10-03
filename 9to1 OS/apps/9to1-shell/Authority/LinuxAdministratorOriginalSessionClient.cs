using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace NineToOne.Os.Shell.Authority;

// Explicit protected administrator trust boundary. Root assertions are NOT claimed
// owner-side kernel observations of an inaccessible hardened root/Home executable.
[SupportedOSPlatform("linux")]
internal sealed class LinuxAdministratorOriginalSessionClient : ILinuxOriginalInstalledOwnerAdministratorClient,
    IHomeNativeControlledLaunchAuthority
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly AuthenticatedResourceActor _localActor;
    private readonly HomeNativeObservedPeer _rootPeer,_ownerPeer;
    private readonly LinuxProcessIdentity _ownerProcess;
    private readonly HomeNativeControlledLaunchSessionContext _issuedContext;
    private readonly AuthenticatedResourceActor _homeActor;
    private readonly LinuxRootInstalledTupleObservation _homeTuple,_ownerTuple;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly CancellationTokenSource _lifetime=new();
    private int _retired;
    private readonly object _disposeGate=new();
    private Task? _disposeTask;
    private LinuxAdministratorOriginalSessionClient(Socket socket,IAuthenticatedResourceActorSource actors,
        AuthenticatedResourceActor actor,HomeNativeObservedPeer rootPeer,HomeNativeObservedPeer ownerPeer,
        LinuxProcessIdentity ownerProcess,LinuxAdministratorOwnerReply reply)
    {
        _socket=socket;_stream=new(socket,ownsSocket:false);_actors=actors;_localActor=actor;
        _rootPeer=rootPeer;_ownerPeer=ownerPeer;_ownerProcess=ownerProcess;
        // Fresh PRIVATE local issuance follows authenticated root enrollment. The public
        // deserialized record itself is never accepted as a session-context issuer.
        var wire=reply.OriginalContext;
        _issuedContext=new(wire.ProfileId,wire.SessionLeaseIdentity,wire.OriginalHostPeer,
            wire.OriginalHostProcessStartIdentity,wire.OriginalHostExecutableIdentity);
        _homeActor=reply.OriginalHomeActor;_homeTuple=reply.OriginalHomeInstallation;_ownerTuple=reply.OriginalOwnerInstallation;
    }
    public CancellationToken OriginalLifetime=>_lifetime.Token;
    internal static async Task<ILinuxOriginalInstalledOwnerAdministratorClient?> ConnectForOwnerAsync(
        string protectedRootSocketPath,IAuthenticatedResourceActorSource localActors,CancellationToken ct)
    {
        if(!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(protectedRootSocketPath) ||
            Path.GetFullPath(protectedRootSocketPath)!=protectedRootSocketPath ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(protectedRootSocketPath)!)) return null;
        var actor=await localActors.GetCurrentAsync(ct);
        if(actor is null || actor.OrganisationId is not null || string.IsNullOrWhiteSpace(actor.AuthenticationRevision)) return null;
        var principal=await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct);
        var ownerPeer=new HomeNativeObservedPeer(Environment.ProcessId,principal);
        var ownerProcess=await LinuxProcessIdentity.ReadAsync(ownerPeer,ct);
        if(ownerProcess is null || actor!=await localActors.GetCurrentAsync(ct)) return null;
        var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        var accepted=false;
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(protectedRootSocketPath),deadline.Token);
            var rootPeer=HomeNativePeerObservation.FromAcceptedUnixSocket(socket);
            if(rootPeer is null || rootPeer.OperatingSystemPrincipalId!="unix-euid:0" || actor!=await localActors.GetCurrentAsync(deadline.Token)) return null;
            using var stream=new NetworkStream(socket,ownsSocket:false);var correlation=Guid.NewGuid();
            await LinuxHomeChildLeaseChannel.WriteAsync(stream,new LinuxAdministratorOwnerRequest(2,correlation,"enroll",actor),deadline.Token);
            var reply=await LinuxHomeChildLeaseChannel.ReadAsync<LinuxAdministratorOwnerReply>(stream,deadline.Token);
            var context=reply.OriginalContext;var homeActor=reply.OriginalHomeActor;
            if(reply.Schema!=2 || reply.Correlation!=correlation || context is null || homeActor is null ||
               context.ProfileId!=actor.ProfileId || homeActor.ProfileId!=actor.ProfileId || homeActor.ActorId!=actor.ActorId ||
               homeActor.AccountId!=actor.AccountId || homeActor.OrganisationId is not null ||
               string.IsNullOrWhiteSpace(homeActor.AuthenticationRevision) ||
               !Guid.TryParseExact(context.SessionLeaseIdentity,"N",out var lease) || lease==Guid.Empty ||
               context.OriginalHostPeer is null || context.OriginalHostPeer.ProcessId<=0 ||
               context.OriginalHostPeer.OperatingSystemPrincipalId!=principal ||
               string.IsNullOrWhiteSpace(context.OriginalHostProcessStartIdentity) ||
               reply.OriginalHomeInstallation is null || reply.OriginalOwnerInstallation is null ||
               reply.OriginalHomeInstallation.AppId!="os.shell" ||
               reply.OriginalHomeInstallation.ExecutableIdentity!=context.OriginalHostExecutableIdentity ||
               reply.OriginalOwnerInstallation.ExecutableIdentity!=ownerProcess.ExecutableIdentity(ownerPeer.ProcessId) ||
               !ValidTuple(reply.OriginalHomeInstallation) || !ValidTuple(reply.OriginalOwnerInstallation) ||
               reply.OriginalOwnerInstallation.AppId=="os.shell" || reply.LaunchCurrent ||
               rootPeer!=HomeNativePeerObservation.FromAcceptedUnixSocket(socket) ||
               ownerProcess!=await LinuxProcessIdentity.ReadAsync(ownerPeer,deadline.Token) || actor!=await localActors.GetCurrentAsync(deadline.Token)) return null;
            var result=new LinuxAdministratorOriginalSessionClient(socket,localActors,actor,rootPeer,ownerPeer,ownerProcess,reply);
            accepted=true;
            bool current;
            try { current=await result.IsCurrentForActorAsync(result._issuedContext,actor,deadline.Token); }
            catch
            {
                try { await result.DisposeAsync(); } catch { /* Preserve first enrollment observation failure after drain attempt. */ }
                throw;
            }
            if(!current) {await result.DisposeAsync();return null;}
            return result;
        }
        finally {if(!accepted)socket.Dispose();}
    }
    private static bool ValidTuple(LinuxRootInstalledTupleObservation tuple)=>tuple is not null &&
        !string.IsNullOrWhiteSpace(tuple.AppId) && tuple.AppId.Length<=4096 &&
        tuple.ApplicationId!=Guid.Empty && tuple.ApplicationRevision>0 && tuple.ReceiptRevision>0 &&
        !string.IsNullOrWhiteSpace(tuple.DesktopIdentity) && tuple.DesktopIdentity.StartsWith("desktop:",StringComparison.Ordinal) && tuple.DesktopIdentity.Length<=4096 &&
        !string.IsNullOrEmpty(tuple.DesktopEntryDigest) && tuple.DesktopEntryDigest.Length==64 && tuple.DesktopEntryDigest.All(char.IsAsciiHexDigit) &&
        !string.IsNullOrEmpty(tuple.ExecutableIdentity) && tuple.ExecutableIdentity.Length<=16384;
    private async ValueTask<bool> LocalActorCurrentOrRetireAsync(AuthenticatedResourceActor actor, CancellationToken ct)
    {
        // A foreign caller cannot consume this authentic original connection.
        if (actor != _localActor || Volatile.Read(ref _retired) != 0) return false;
        if (_localActor != await _actors.GetCurrentAsync(ct).ConfigureAwait(false))
        { Retire(); return false; }
        return Volatile.Read(ref _retired) == 0;
    }
    private async Task<LinuxAdministratorOwnerReply?> ExchangeAsync(string command,
        AuthenticatedResourceActor actor,HomeNativeControlledLaunchObservation? launch,CancellationToken ct)
    {
        if(!await LocalActorCurrentOrRetireAsync(actor,ct))return null;
        await _gate.WaitAsync(ct);
        try
        {
            if(Volatile.Read(ref _retired)!=0 || !await LocalActorCurrentOrRetireAsync(actor,ct) ||
                _rootPeer!=HomeNativePeerObservation.FromAcceptedUnixSocket(_socket) ||
                _ownerProcess!=await LinuxProcessIdentity.ReadAsync(_ownerPeer,ct)) return Retire();
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct,_lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var correlation=Guid.NewGuid();
            await LinuxHomeChildLeaseChannel.WriteAsync(_stream,new LinuxAdministratorOwnerRequest(2,correlation,command,actor,launch),deadline.Token);
            var reply=await LinuxHomeChildLeaseChannel.ReadAsync<LinuxAdministratorOwnerReply>(_stream,deadline.Token);
            if(reply.Schema!=2 || reply.Correlation!=correlation || reply.OriginalContext!=_issuedContext ||
                reply.OriginalHomeActor!=_homeActor || reply.OriginalHomeInstallation!=_homeTuple ||
                reply.OriginalOwnerInstallation!=_ownerTuple || (command!="launch" && reply.LaunchCurrent) ||
                !await LocalActorCurrentOrRetireAsync(actor,deadline.Token) ||
                _rootPeer!=HomeNativePeerObservation.FromAcceptedUnixSocket(_socket) ||
                _ownerProcess!=await LinuxProcessIdentity.ReadAsync(_ownerPeer,deadline.Token) || Volatile.Read(ref _retired)!=0) return Retire();
            return reply;
        }
        catch(Exception error) when(error is IOException or SocketException or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException)
        {Retire();if(ct.IsCancellationRequested)throw;return null;}
        finally {_gate.Release();}
    }
    private LinuxAdministratorOwnerReply? Retire()
    {
        if(Interlocked.Exchange(ref _retired,1)==0)
        {try {_lifetime.Cancel();} finally {_socket.Dispose();}}
        return null;
    }
    public async ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(AuthenticatedResourceActor expectedLocalActor,CancellationToken ct)
        =>await ExchangeAsync("current",expectedLocalActor,null,ct) is null?null:_issuedContext;
    public async ValueTask<bool> IsCurrentForActorAsync(HomeNativeControlledLaunchSessionContext originalContext,AuthenticatedResourceActor expectedLocalActor,CancellationToken ct)
        =>ReferenceEquals(originalContext,_issuedContext) && await ExchangeAsync("current",expectedLocalActor,null,ct) is not null;
    public async ValueTask<AuthenticatedResourceActor?> ReadOriginalHomeActorForActorAsync(HomeNativeControlledLaunchSessionContext originalContext,AuthenticatedResourceActor expectedLocalActor,CancellationToken ct)
        =>await IsCurrentForActorAsync(originalContext,expectedLocalActor,ct)?_homeActor:null;
    public async ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation,CancellationToken ct)
    {
        if(observation.ProcessId!=_ownerPeer.ProcessId || observation.OperatingSystemPrincipalId!=_ownerPeer.OperatingSystemPrincipalId ||
            observation.ProcessStartIdentity!=_ownerProcess.StartTime || observation.ExecutableIdentity!=_ownerTuple.ExecutableIdentity ||
            observation.AppId!=_ownerTuple.AppId || observation.RequiredRole!="" || observation.ProfileId!=_issuedContext.ProfileId ||
            observation.SessionLeaseIdentity!=_issuedContext.SessionLeaseIdentity) return false;
        return (await ExchangeAsync("launch",_localActor,observation,ct))?.LaunchCurrent==true;
    }
    public IHomeNativeInstalledPeerOriginalActorVerifier CreateInstalledOwnerVerifier(IInstalledApplicationRegistry registry)
        =>new LinuxInstallationPeerVerifier(_actors,new OperatingSystemPrincipalSource(),registry,new LinuxControlledLaunchGate(_actors,this,this));
    public IHomeNativeSessionHostOriginalActorVerifier CreateOriginalHomeHostVerifier()=>new OriginalHostVerifier(this);
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
        ExceptionDispatchInfo? failure=null;
        try
        {
            try {Retire();} catch(Exception error){failure=ExceptionDispatchInfo.Capture(error);}
            // Retire closes the owned transport even when cancellation callbacks throw.
            // Wait for the exact admitted exchange to leave before stream/CTS teardown.
            await _gate.WaitAsync();
            try
            {
                try {_stream.Dispose();} catch(Exception error){failure??=ExceptionDispatchInfo.Capture(error);}
                try {_lifetime.Dispose();} catch(Exception error){failure??=ExceptionDispatchInfo.Capture(error);}
            }
            finally {_gate.Release();}
            failure?.Throw();completion.TrySetResult();
        }
        catch(Exception error){completion.TrySetException(error);}
    }
    private sealed class OriginalHostVerifier(LinuxAdministratorOriginalSessionClient client)
        :IHomeNativeSessionHostOriginalActorVerifier
    {
        public async ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed,
            HomeNativeSessionHostRequirement requirement,CancellationToken ct)
        {
            if (!await client.LocalActorCurrentOrRetireAsync(client._localActor, ct)) return null;
            return await VerifyHostForActorAsync(observed, requirement, client._localActor, ct);
        }
        public async ValueTask<HomeNativeInstalledPeer?> VerifyHostForActorAsync(HomeNativeObservedPeer observed,
            HomeNativeSessionHostRequirement requirement,AuthenticatedResourceActor actor,CancellationToken ct)
        {
            var context=client._issuedContext;var tuple=client._homeTuple;
            if(observed!=context.OriginalHostPeer || requirement.AppId!=tuple.AppId ||
                requirement.OperatingSystemApplicationId!=tuple.DesktopIdentity ||
                !await client.IsCurrentForActorAsync(context,actor,ct))return null;
            const string trustPath="/etc/9to1/identity/publishers.json";
            const string receiptsPath="/var/lib/9to1/home/installed-receipts";
            try
            {
                if(!LinuxRootOwnedFiles.DirectoryImmutable(receiptsPath))return null;
                var trustBytes=await LinuxRootOwnedFiles.ReadAsync(trustPath,1024*1024,ct);
                if(trustBytes is null || !await client.LocalActorCurrentOrRetireAsync(actor,ct))return null;
                var trust=InstallationReceiptSignature.DecodeTrust(trustBytes);
                InstallationReceipt? selected=null;byte[]? selectedEnvelope=null;string? selectedPath=null;var count=0;
                foreach(var path in Directory.EnumerateFiles(receiptsPath,"*.9to1-install",SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();if(++count>1024)return null;
                    var envelope=await LinuxRootOwnedFiles.ReadAsync(path,8*1024*1024,ct);
                    if(envelope is null || !await client.LocalActorCurrentOrRetireAsync(actor,ct))return null;
                    var receipt=InstallationReceiptSignature.Verify(envelope,trust);
                    if(receipt is null || receipt.AppId!=tuple.AppId || receipt.ReceiptRevision!=tuple.ReceiptRevision ||
                        receipt.ProviderId!="linux.xdg-desktop" || receipt.OsApplicationId!=tuple.DesktopIdentity ||
                        receipt.Entrypoint!=tuple.DesktopIdentity || !string.Equals(receipt.DesktopEntrySha256,tuple.DesktopEntryDigest,StringComparison.OrdinalIgnoreCase) ||
                        !receipt.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole,StringComparer.Ordinal) ||
                        !receipt.AllowedServiceIds.Contains("home.widgets",StringComparer.Ordinal))continue;
                    if(selected is not null)return null;
                    selected=receipt;selectedPath=path;selectedEnvelope=envelope;
                }
                if(selected is null || !await LinuxInstallationPeerVerifier.PayloadMatchesAsync(selected,ct) ||
                    !await client.LocalActorCurrentOrRetireAsync(actor,ct))return null;
                var executableFile=selected.Files.Single(file=>Path.Combine(selected.InstallRoot,file.Path)==selected.ExecutablePath);
                var signedExecutableIdentity=$"{selected.ExecutablePath};sha256:{executableFile.Sha256.ToUpperInvariant()};pid:{observed.ProcessId};start:{context.OriginalHostProcessStartIdentity}";
                if(signedExecutableIdentity!=context.OriginalHostExecutableIdentity)return null;
                var desktop=await LinuxRootOwnedFiles.ReadAsync(selected.DesktopEntryPath,1024*1024,ct);
                if(desktop is null || !string.Equals(Convert.ToHexString(SHA256.HashData(desktop)),tuple.DesktopEntryDigest,StringComparison.OrdinalIgnoreCase) ||
                    !await client.LocalActorCurrentOrRetireAsync(actor,ct))return null;
                var finalEnvelope=await LinuxRootOwnedFiles.ReadAsync(selectedPath!,8*1024*1024,ct);
                var finalTrust=await LinuxRootOwnedFiles.ReadAsync(trustPath,1024*1024,ct);
                if(finalEnvelope is null || finalTrust is null || !finalEnvelope.AsSpan().SequenceEqual(selectedEnvelope) ||
                    !finalTrust.AsSpan().SequenceEqual(trustBytes) || !await client.IsCurrentForActorAsync(context,actor,ct))return null;
                // Hardened Home process/runtime/held-lease evidence is rechecked by the
                // actual root issuer, not inferred from inaccessible owner-side /proc.
                return new(tuple.AppId,tuple.ApplicationId,
                    $"receipt:{tuple.ReceiptRevision};home:{tuple.ApplicationRevision}",tuple.ExecutableIdentity,
                    selected.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal))
                    {Roles=selected.Roles.ToFrozenSet(StringComparer.Ordinal)};
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or
                CryptographicException or InvalidOperationException or ArgumentException or FormatException)
            {return null;}
        }
    }
}
