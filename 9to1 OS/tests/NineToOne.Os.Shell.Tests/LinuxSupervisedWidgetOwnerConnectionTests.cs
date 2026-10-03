using System.Net.Sockets;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

// Actual Unix sockets/FileHome/kernel profile; controlled administrator negatives only.
// No fabricated context is accepted as a real supervised startup or installed owner.
public sealed class LinuxSupervisedWidgetOwnerConnectionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_observation_port_or_copied_context_denies_before_registration_and_backend(bool copiedContext)
    {
        var root=Path.Combine(Path.GetTempPath(),"astra-supervised-owner-wire-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var homePath=Path.Combine(root,"home.json");var home=new FileHomeCoreStateStore(homePath);
            var principals=new OperatingSystemPrincipalSource();var actors=new HomeLocalProfileIdentity(home,principals);
            var actor=(await actors.GetCurrentAsync(default))!;
            var registry=new HomeInstalledApplicationRegistry(home,actors,[]);
            var resources=new ResourceAuthorizationService(actors,[new InstalledApplicationResourceResolver(registry)]);
            var before=await File.ReadAllBytesAsync(homePath);var backend=new Backend();var verifier=new Verifier();
            var endpoint=new UnixDomainSocketEndPoint(Path.Combine(root,"owner.sock"));
            using var listener=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);listener.Bind(endpoint);listener.Listen(1);
            using var client=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);await client.ConnectAsync(endpoint);
            using var accepted=await listener.AcceptAsync();
            MissingSession session=copiedContext?new CopySession(actor):new MissingSession();
            var result=await LinuxSupervisedWidgetOwnerConnection.ConnectAsync(client,
                new("os.shell","desktop:9to1-os-shell.desktop"),verifier,verifier,principals,actors,resources,session,
                [new("card","1","Card",new(1,1),new(1,1),new(1,1),"card.config","card.surface",HomeNativeWidgetUpdateMode.Manual,null,[],[])],backend,default);
            Assert.Null(result);Assert.Equal(0,accepted.Available);Assert.Equal(0,verifier.Calls);Assert.Equal(0,backend.Calls);
            Assert.Equal(before,await File.ReadAllBytesAsync(homePath));
            Assert.DoesNotContain((await home.ReadAsync()).State!.Records,value=>value.RecordType=="home.installed-apps");
            if(copiedContext){Assert.Equal(1,session.Reads);Assert.Equal(1,((CopySession)session).Checks);}
            else Assert.Equal(0,session.Reads);
        }
        finally{Directory.Delete(root,true);}
    }
    private class MissingSession:IHomeNativeControlledLaunchOriginalSessionContextSource
    {
        public int Reads;
        public virtual ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(AuthenticatedResourceActor actor,CancellationToken ct)
        {++Reads;return ValueTask.FromResult<HomeNativeControlledLaunchSessionContext?>(null);}
        public virtual ValueTask<bool> IsCurrentForActorAsync(HomeNativeControlledLaunchSessionContext context,AuthenticatedResourceActor actor,CancellationToken ct)
            =>ValueTask.FromResult(false);
    }
    private sealed class CopySession(AuthenticatedResourceActor original):MissingSession,IHomeNativeControlledLaunchOriginalHomeActorObservationSource
    {
        private readonly HomeNativeControlledLaunchSessionContext issued=new(original.ProfileId,"controlled-never-admitted",new(1,"controlled"),"controlled","controlled");
        public int Checks;
        public override ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(AuthenticatedResourceActor actor,CancellationToken ct)
        {++Reads;return ValueTask.FromResult<HomeNativeControlledLaunchSessionContext?>(issued with{});}
        public override ValueTask<bool> IsCurrentForActorAsync(HomeNativeControlledLaunchSessionContext context,AuthenticatedResourceActor actor,CancellationToken ct)
        {++Checks;return ValueTask.FromResult(ReferenceEquals(context,issued)&&actor==original);}
        public ValueTask<AuthenticatedResourceActor?> ReadOriginalHomeActorForActorAsync(HomeNativeControlledLaunchSessionContext context,AuthenticatedResourceActor actor,CancellationToken ct)
            =>throw new InvalidOperationException("Copied context must not reach Home observation.");
    }
    private sealed class Backend:IHomeNativeWidgetOriginalActorRuntimeEndpoint
    {
        public int Calls;
        public ValueTask<HomeNativeWidgetSurface?> CaptureAsync(HomeNativeWidgetCaptureRequest request,CancellationToken ct)
        {++Calls;return ValueTask.FromResult<HomeNativeWidgetSurface?>(null);}
        public ValueTask<HomeNativeWidgetSurface?> CaptureForActorAsync(HomeNativeWidgetCaptureRequest request,AuthenticatedResourceActor actor,CancellationToken ct)
        {++Calls;return ValueTask.FromResult<HomeNativeWidgetSurface?>(null);}
    }
    private sealed class Verifier:IHomeNativeInstalledPeerVerifier,IHomeNativeInstalledPeerOriginalActorVerifier,IHomeNativeSessionHostVerifier,IHomeNativeSessionHostOriginalActorVerifier
    {
        public int Calls;
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer,CancellationToken ct){++Calls;return ValueTask.FromResult<HomeNativeInstalledPeer?>(null);}
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer peer,AuthenticatedResourceActor actor,CancellationToken ct){++Calls;return ValueTask.FromResult<HomeNativeInstalledPeer?>(null);}
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer peer,HomeNativeSessionHostRequirement requirement,CancellationToken ct){++Calls;return ValueTask.FromResult<HomeNativeInstalledPeer?>(null);}
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostForActorAsync(HomeNativeObservedPeer peer,HomeNativeSessionHostRequirement requirement,AuthenticatedResourceActor actor,CancellationToken ct){++Calls;return ValueTask.FromResult<HomeNativeInstalledPeer?>(null);}
    }
}
