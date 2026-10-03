using System.Net.Sockets;
using System.Runtime.Versioning;
using Haven.Application;

namespace NineToOne.Os.Shell.Authority;

internal sealed record LinuxAdministratorHomeLaunchRequest(int Schema,Guid Correlation,string LeaseIdentity,
    AuthenticatedResourceActor OriginalHomeActor,HomeNativeControlledLaunchObservation Launch);
internal sealed record LinuxAdministratorHomeLaunchReply(int Schema,Guid Correlation,bool Current);

// Separate authority connection for the actual original Home child. This never lends
// an owner actor to Home, adopts a remote lease, or verifies roles from a wire string.
[SupportedOSPlatform("linux")]
internal static class LinuxAdministratorOriginalHomeAuthorityChannel
{
    internal static async Task ServeAsync(Socket actualAcceptedHomeSocket,LinuxRootSupervisedHome originalHome,
        LinuxRootSupervisedInstalledWidgetOwner originalOwner,CancellationToken ct)
    {
        var context=originalHome.ObserveOriginalContext();var actor=originalHome.OriginalHomeActorObservation;
        if(await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct)!="unix-euid:0" ||
            !ReferenceEquals(originalOwner.OriginalHomeContext,context) ||
            HomeNativePeerObservation.FromAcceptedUnixSocket(actualAcceptedHomeSocket)!=context.OriginalHostPeer ||
            !await originalHome.IsOriginalIssuedContextCurrentAsync(context,ct))
            throw new UnauthorizedAccessException("Same actual original Home authority connection required.");
        using var stream=new NetworkStream(actualAcceptedHomeSocket,ownsSocket:false);
        while(true)
        {
            var request=await LinuxOriginalControlFrameReader.ReadAsync<LinuxAdministratorHomeLaunchRequest>(stream,ct);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(60));
            
            if(request.Schema!=3 || request.Correlation==Guid.Empty || request.LeaseIdentity!=context.SessionLeaseIdentity ||
                request.OriginalHomeActor!=actor || request.Launch is null ||
                HomeNativePeerObservation.FromAcceptedUnixSocket(actualAcceptedHomeSocket)!=context.OriginalHostPeer ||
                !await originalHome.IsOriginalIssuedContextCurrentAsync(context,deadline.Token))
                throw new UnauthorizedAccessException("Original Home authority context retired.");
            var observation=request.Launch;
            var current=observation.ProfileId==context.ProfileId && observation.SessionLeaseIdentity==context.SessionLeaseIdentity;
            if(current && observation.AppId==originalOwner.OriginalOwnerAppId)
                current=observation.RequiredRole=="" && observation.ProcessId==originalOwner.OriginalOwnerPeer.ProcessId &&
                    observation.OperatingSystemPrincipalId==originalOwner.OriginalOwnerPeer.OperatingSystemPrincipalId &&
                    observation.ProcessStartIdentity==originalOwner.OriginalOwnerProcessStart &&
                    observation.ExecutableIdentity==originalOwner.OriginalOwnerExecutable &&
                    await originalOwner.IsOriginalCurrentAsync(deadline.Token);
            else if(current && observation.AppId=="os.shell")
                current=(observation.RequiredRole is "" or "home.session-host") &&
                    observation.ProcessId==context.OriginalHostPeer.ProcessId &&
                    observation.OperatingSystemPrincipalId==context.OriginalHostPeer.OperatingSystemPrincipalId &&
                    observation.ProcessStartIdentity==context.OriginalHostProcessStartIdentity &&
                    observation.ExecutableIdentity==context.OriginalHostExecutableIdentity;
            else current=false;
            if(!await originalHome.IsOriginalIssuedContextCurrentAsync(context,deadline.Token))
                throw new UnauthorizedAccessException("Original Home retired before authority response.");
            await LinuxHomeChildLeaseChannel.WriteAsync(stream,new LinuxAdministratorHomeLaunchReply(3,request.Correlation,current),deadline.Token);
            if(!await originalHome.IsOriginalIssuedContextCurrentAsync(context,deadline.Token))
                throw new UnauthorizedAccessException("Original Home retired after authority response.");
        }
    }
}
