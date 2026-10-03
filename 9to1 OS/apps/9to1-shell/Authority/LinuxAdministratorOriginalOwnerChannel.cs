using System.Globalization;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Haven.Application;

namespace NineToOne.Os.Shell.Authority;

// All wire fields are observational. Only the private actual root spawn/context below
// can answer these requests; a public tuple cannot enroll a process or create an actor.
internal sealed record LinuxAdministratorOwnerRequest(int Schema, Guid Correlation, string Command,
    AuthenticatedResourceActor OriginalLocalActor, HomeNativeControlledLaunchObservation? Launch = null);
internal sealed record LinuxRootInstalledTupleObservation(string AppId, Guid ApplicationId,
    long ApplicationRevision, long ReceiptRevision, string ExecutableIdentity,
    string DesktopIdentity, string DesktopEntryDigest);
internal sealed record LinuxAdministratorOwnerReply(int Schema, Guid Correlation,
    HomeNativeControlledLaunchSessionContext OriginalContext, AuthenticatedResourceActor OriginalHomeActor,
    LinuxRootInstalledTupleObservation OriginalHomeInstallation,
    LinuxRootInstalledTupleObservation OriginalOwnerInstallation, bool LaunchCurrent);

[SupportedOSPlatform("linux")]
internal static class LinuxAdministratorOriginalOwnerChannel
{
    internal static async Task ServeAsync(LinuxRootSupervisedInstalledWidgetOwner originalOwner,
        LinuxRootSupervisedHome originalHome, Socket actualCanonicalHomeSocket,
        LinuxRootHomeStartPreparation homePreparation,
        LinuxRootInstalledWidgetOwnerStartPreparation ownerPreparation, CancellationToken ct)
    {
        if(await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct)!="unix-euid:0" ||
           !ReferenceEquals(originalOwner.OriginalHomeContext,originalHome.ObserveOriginalContext()) ||
           !await originalOwner.IsOriginalCurrentAsync(ct))
            throw new UnauthorizedAccessException("Actual original administrator owner admission required.");
        // These canonical observations originate from the SAME admitted Home child and
        // independently signed protected desktop identities, never owner labels/JSON.
        var homeCanonical=await LinuxRootOriginalHomeCanonicalReader.ReadAsync(actualCanonicalHomeSocket,
            originalHome,homePreparation,ct) ?? throw new UnauthorizedAccessException("Original canonical Home tuple unavailable.");
        var ownerCanonical=await LinuxRootOriginalHomeCanonicalReader.ReadOwnerAsync(actualCanonicalHomeSocket,
            originalHome,ownerPreparation,ct) ?? throw new UnauthorizedAccessException("Original canonical owner tuple unavailable.");
        var context=originalOwner.OriginalHomeContext;
        var homeActor=originalOwner.OriginalHomeActorObservation;
        var homeTuple=new LinuxRootInstalledTupleObservation("os.shell",homeCanonical.ApplicationId,
            homeCanonical.ApplicationRevision,homePreparation.SignedReceiptRevision,
            context.OriginalHostExecutableIdentity,homeCanonical.DesktopIdentity,homeCanonical.DesktopEntryDigest);
        var ownerTuple=new LinuxRootInstalledTupleObservation(ownerPreparation.SignedAppId,ownerCanonical.ApplicationId,
            ownerCanonical.ApplicationRevision,ownerPreparation.SignedReceiptRevision,
            originalOwner.OriginalOwnerExecutable,ownerCanonical.DesktopIdentity,ownerCanonical.DesktopEntryDigest);
        using var stream=new NetworkStream(originalOwner.OriginalAdministratorSocket,ownsSocket:false);
        AuthenticatedResourceActor? originalLocalActor=null;
        async Task<bool> OriginalCurrent(CancellationToken token)
        {
            if(!await originalOwner.IsOriginalCurrentAsync(token) ||
                originalOwner.OriginalHomeActorObservation!=homeActor ||
                !ReferenceEquals(originalOwner.OriginalHomeContext,context)) return false;
            var currentHome=await LinuxRootOriginalHomeCanonicalReader.ReadAsync(actualCanonicalHomeSocket,
                originalHome,homePreparation,token);
            var currentOwner=await LinuxRootOriginalHomeCanonicalReader.ReadOwnerAsync(actualCanonicalHomeSocket,
                originalHome,ownerPreparation,token);
            return currentHome==homeCanonical && currentOwner==ownerCanonical &&
                await originalOwner.IsOriginalCurrentAsync(token);
        }
        while(true)
        {
            var request=await LinuxOriginalControlFrameReader.ReadAsync<LinuxAdministratorOwnerRequest>(stream,ct);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            
            if(request.Schema!=2 || request.Correlation==Guid.Empty || request.OriginalLocalActor is null ||
                request.Command is not ("enroll" or "current" or "launch") || !await OriginalCurrent(deadline.Token))
                throw new UnauthorizedAccessException("Original owner request unavailable.");
            if(originalLocalActor is null)
            {
                if(request.Command!="enroll" || !SamePersonalSubject(request.OriginalLocalActor,homeActor))
                    throw new UnauthorizedAccessException("Original local owner subject does not match the admitted Home.");
                // This snapshot is request context, never ResourceAuthorization input.
                originalLocalActor=request.OriginalLocalActor;
            }
            else if(request.Command=="enroll" || request.OriginalLocalActor!=originalLocalActor)
                throw new UnauthorizedAccessException("Original owner request context retired.");
            var launchCurrent=false;
            if(request.Command=="launch")
            {
                var observation=request.Launch;
                if(observation is null) throw new UnauthorizedAccessException("Actual launch observation required.");
                launchCurrent=observation.AppId==ownerTuple.AppId && observation.RequiredRole=="" &&
                    observation.ProcessId==originalOwner.OriginalOwnerPeer.ProcessId &&
                    observation.OperatingSystemPrincipalId==originalOwner.OriginalOwnerPeer.OperatingSystemPrincipalId &&
                    observation.ProcessStartIdentity==originalOwner.OriginalOwnerProcessStart &&
                    observation.ExecutableIdentity==ownerTuple.ExecutableIdentity && observation.ProfileId==context.ProfileId &&
                    observation.SessionLeaseIdentity==context.SessionLeaseIdentity;
            }
            else if(request.Launch is not null) throw new UnauthorizedAccessException("Unexpected launch fields refused.");
            if(!await OriginalCurrent(deadline.Token)) throw new UnauthorizedAccessException("Original bootstrap retired before reply.");
            await LinuxHomeChildLeaseChannel.WriteAsync(stream,new LinuxAdministratorOwnerReply(2,request.Correlation,
                context,homeActor,homeTuple,ownerTuple,launchCurrent),deadline.Token);
            if(!await OriginalCurrent(deadline.Token)) throw new UnauthorizedAccessException("Original bootstrap retired after reply.");
        }
    }
    private static bool SamePersonalSubject(AuthenticatedResourceActor local,AuthenticatedResourceActor home)
        => local.OrganisationId is null && home.OrganisationId is null && local.ActorId==home.ActorId &&
           local.ProfileId==home.ProfileId && local.AccountId==home.AccountId &&
           !string.IsNullOrWhiteSpace(local.AuthenticationRevision) && local.AuthenticationRevision.Length<=4096;
}
