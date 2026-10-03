using System.Globalization;
using System.Collections.Frozen;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell;

/// <summary>Installed owner composition after the administrator client has authenticated
/// its original supervised caller. It never acquires a Home lease or aliases the remote
/// Home actor into the local authenticated actor source.</summary>
public sealed class LinuxOriginalInstalledOwnerRuntime : IHomeNativeWidgetOriginalActorRuntimeEndpoint, IDisposable
{
    private readonly IHomeNativeControlledLaunchOriginalSessionContextSource _sessions;
    private readonly HomeNativeControlledLaunchSessionContext _session;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly AuthenticatedResourceActor _actor;
    private readonly IHomeNativeInstalledPeerOriginalActorVerifier _verifier;
    private readonly HomeNativeObservedPeer _self;
    private readonly HomeNativeInstalledPeer _owner;
    private readonly LinuxInstalledApplicationWidgetBackend _backend;
    private int _retired;
    private LinuxOriginalInstalledOwnerRuntime(IHomeNativeControlledLaunchOriginalSessionContextSource sessions,
        HomeNativeControlledLaunchSessionContext session, IAuthenticatedResourceActorSource actors,
        AuthenticatedResourceActor actor, IHomeNativeInstalledPeerOriginalActorVerifier verifier,
        HomeNativeObservedPeer self, HomeNativeInstalledPeer owner, LinuxInstalledApplicationWidgetBackend backend)
    { _sessions=sessions;_session=session;_actors=actors;_actor=actor;_verifier=verifier;_self=self;_owner=owner;_backend=backend; }

    public static async Task<LinuxOriginalInstalledOwnerRuntime?> CreateAsync(
        IHomeNativeControlledLaunchOriginalSessionContextSource authenticAdministratorClient,
        IAuthenticatedResourceActorSource localActors, ITrustedHostPrincipalSource actualPrincipals,
        IHomeNativeInstalledPeerVerifier installedVerifier, IInstalledApplicationRegistry canonicalRegistry,
        CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(authenticAdministratorClient);ArgumentNullException.ThrowIfNull(localActors);
        ArgumentNullException.ThrowIfNull(actualPrincipals);ArgumentNullException.ThrowIfNull(installedVerifier);
        ArgumentNullException.ThrowIfNull(canonicalRegistry);
        if(!OperatingSystem.IsLinux() || installedVerifier is not IHomeNativeInstalledPeerOriginalActorVerifier originalVerifier
            || canonicalRegistry is not IInstalledApplicationOriginalReadRegistry originalRead)return null;
        var actor=await localActors.GetCurrentAsync(ct).ConfigureAwait(false);if(actor is null)return null;
        var session=await authenticAdministratorClient.GetForActorAsync(actor,ct).ConfigureAwait(false);
        if(session is null || session.ProfileId!=actor.ProfileId || session.OriginalHostPeer.ProcessId==Environment.ProcessId
            || actor!=await localActors.GetCurrentAsync(ct).ConfigureAwait(false)
            || !await authenticAdministratorClient.IsCurrentForActorAsync(session,actor,ct).ConfigureAwait(false))return null;
        var principal=await actualPrincipals.GetPrincipalAsync(ct).ConfigureAwait(false);
        if(principal is null || actor!=await localActors.GetCurrentAsync(ct).ConfigureAwait(false)
            || !await authenticAdministratorClient.IsCurrentForActorAsync(session,actor,ct).ConfigureAwait(false))return null;
        var self=new HomeNativeObservedPeer(Environment.ProcessId,principal);
        var owner=Snapshot(await originalVerifier.VerifyForActorAsync(self,actor,ct).ConfigureAwait(false));
        if(owner is null || string.IsNullOrWhiteSpace(owner.AppId) ||
            owner.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole) ||
            !owner.AllowedServiceIds.Contains("home.widgets",StringComparer.Ordinal)
            || owner.InstalledApplicationId==Guid.Empty || string.IsNullOrWhiteSpace(owner.InstallationRevision)
            || actor!=await localActors.GetCurrentAsync(ct).ConfigureAwait(false)
            || !await authenticAdministratorClient.IsCurrentForActorAsync(session,actor,ct).ConfigureAwait(false))return null;
        // No reconciliation/provider read or initialization on independent owner startup.
        var existing=await originalRead.ReadExistingForActorAsync(actor,ct).ConfigureAwait(false);
        if(existing is null || actor!=await localActors.GetCurrentAsync(ct).ConfigureAwait(false)
            || !await authenticAdministratorClient.IsCurrentForActorAsync(session,actor,ct).ConfigureAwait(false))return null;
        var app=existing.Applications.SingleOrDefault(value=>value.ApplicationId==owner.InstalledApplicationId
            && owner.InstallationRevision.EndsWith(";home:"+value.Revision.ToString(CultureInfo.InvariantCulture),StringComparison.Ordinal) && value.HomeProfileId==actor.ProfileId
            && value.ProviderId=="linux.xdg-desktop" && value.Enabled && value.ProfileAccessible);
        if(app is null)return null;
        var reference=new HomeNativeWidgetReference(owner.AppId,owner.InstalledApplicationId,owner.InstallationRevision,
            LinuxInstalledApplicationWidgetBackend.WidgetId,LinuxInstalledApplicationWidgetBackend.DefinitionRevision);
        var resources=new ResourceAuthorizationService(localActors,[new InstalledApplicationResourceResolver(canonicalRegistry)]);
        var backend=new LinuxInstalledApplicationWidgetBackend(canonicalRegistry,localActors,resources,reference,app.ApplicationId,app.Revision);
        var result=new LinuxOriginalInstalledOwnerRuntime(authenticAdministratorClient,session,localActors,actor,originalVerifier,self,owner,backend);
        if(!await result.CurrentAsync(ct).ConfigureAwait(false)){result.Dispose();return null;}
        return result;
    }
    internal HomeNativeControlledLaunchSessionContext OriginalSession => _session;
    public HomeNativeWidgetDefinition Declaration()=>_backend.Declaration();
    public ValueTask<HomeNativeWidgetSurface?> CaptureAsync(HomeNativeWidgetCaptureRequest request,CancellationToken ct)
        =>ValueTask.FromResult<HomeNativeWidgetSurface?>(null);
    public async ValueTask<HomeNativeWidgetSurface?> CaptureForActorAsync(HomeNativeWidgetCaptureRequest request,
        AuthenticatedResourceActor expectedActor,CancellationToken ct)
    {
        if(expectedActor!=_actor || !await CurrentAsync(ct).ConfigureAwait(false))return null;
        var surface=await _backend.CaptureForActorAsync(request,_actor,ct).ConfigureAwait(false);
        return await CurrentAsync(ct).ConfigureAwait(false)?surface:null;
    }
    private async Task<bool> CurrentAsync(CancellationToken ct)
    {
        if(Volatile.Read(ref _retired)!=0)return false;
        if(_actor!=await _actors.GetCurrentAsync(ct).ConfigureAwait(false)
            || !await _sessions.IsCurrentForActorAsync(_session,_actor,ct).ConfigureAwait(false))return Retire();
        var owner=Snapshot(await _verifier.VerifyForActorAsync(_self,_actor,ct).ConfigureAwait(false));
        if(owner is null || owner.AppId!=_owner.AppId || owner.InstalledApplicationId!=_owner.InstalledApplicationId
            || owner.InstallationRevision!=_owner.InstallationRevision || owner.ExecutableIdentity!=_owner.ExecutableIdentity
            || !owner.AllowedServiceIds.SetEquals(_owner.AllowedServiceIds) || !owner.Roles.SetEquals(_owner.Roles)
            || _actor!=await _actors.GetCurrentAsync(ct).ConfigureAwait(false)
            || !await _sessions.IsCurrentForActorAsync(_session,_actor,ct).ConfigureAwait(false))return Retire();
        return Volatile.Read(ref _retired)==0;
    }
    private static HomeNativeInstalledPeer? Snapshot(HomeNativeInstalledPeer? owner) =>
        owner?.AllowedServiceIds is not null && owner.Roles is not null
            ? owner with {AllowedServiceIds=owner.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal),Roles=owner.Roles.ToFrozenSet(StringComparer.Ordinal)} : null;
    private bool Retire(){Interlocked.Exchange(ref _retired,1);return false;}
    public void Dispose()=>Interlocked.Exchange(ref _retired,1);
}
