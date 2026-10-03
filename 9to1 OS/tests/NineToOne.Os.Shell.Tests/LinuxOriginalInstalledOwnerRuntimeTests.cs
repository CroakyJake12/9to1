using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

// Real FileHome/kernel profile. The absent administrator source is a controlled
// negative; these cases do not manufacture or accept a supervised installed owner.
public sealed class LinuxOriginalInstalledOwnerRuntimeTests
{
    [Fact]
    public async Task Absent_administrator_issuance_does_not_observe_installed_owner_or_initialize_inventory()
    {
        using var f=new Fixture();_ = await f.Actors.GetCurrentAsync(default);
        var before=await File.ReadAllBytesAsync(f.Path);
        Assert.Null(await LinuxOriginalInstalledOwnerRuntime.CreateAsync(new MissingSession(),f.Actors,f.Principals,f.Verifier,f.Registry));
        Assert.Equal(0,f.Verifier.Calls);Assert.Equal(before,await File.ReadAllBytesAsync(f.Path));
        Assert.DoesNotContain((await f.Store.ReadAsync()).State!.Records,r=>r.RecordType=="home.installed-apps");
    }
    [Fact]
    public async Task Held_absent_administrator_return_after_actual_profile_replacement_cannot_adopt_owner_or_write()
    {
        using var f=new Fixture();_ = await f.Actors.GetCurrentAsync(default);
        var held=new HeldMissingSession();var pending=LinuxOriginalInstalledOwnerRuntime.CreateAsync(held,f.Actors,f.Principals,f.Verifier,f.Registry);
        try
        {
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var state=await f.Store.ReadAsync();var record=Assert.Single(state.State!.Records,r=>r.RecordId=="home.local-profile");
            var profile=record.Payload.Deserialize<HomeLocalProfile>()!;
            Assert.True((await f.Store.WriteAsync(record with {Revision=record.Revision+1,
                Payload=JsonSerializer.SerializeToElement(profile with {ProfileId=Guid.NewGuid()})},record.Revision)).IsSuccess);
            var replaced=await File.ReadAllBytesAsync(f.Path);held.Release.TrySetResult();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(5)));Assert.Equal(0,f.Verifier.Calls);
            Assert.Equal(replaced,await File.ReadAllBytesAsync(f.Path));
        }
        finally {held.Release.TrySetResult();try{await pending;}catch{}}
    }
    private class MissingSession:IHomeNativeControlledLaunchOriginalSessionContextSource
    {
        public virtual ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(AuthenticatedResourceActor actor,CancellationToken ct)
            =>ValueTask.FromResult<HomeNativeControlledLaunchSessionContext?>(null);
        public ValueTask<bool> IsCurrentForActorAsync(HomeNativeControlledLaunchSessionContext context,AuthenticatedResourceActor actor,CancellationToken ct)
            =>ValueTask.FromResult(false);
    }
    private sealed class HeldMissingSession:MissingSession
    {
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(AuthenticatedResourceActor actor,CancellationToken ct)
        {Entered.TrySetResult();await Release.Task.WaitAsync(ct);return null;}
    }
    private sealed class Verifier:IHomeNativeInstalledPeerVerifier,IHomeNativeInstalledPeerOriginalActorVerifier
    {
        public int Calls;
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer,CancellationToken ct)
        {++Calls;return ValueTask.FromResult<HomeNativeInstalledPeer?>(null);}
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer peer,AuthenticatedResourceActor actor,CancellationToken ct)
        {++Calls;return ValueTask.FromResult<HomeNativeInstalledPeer?>(null);}
    }
    private sealed class Fixture:IDisposable
    {
        private readonly string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"astra-owner-start-"+Guid.NewGuid().ToString("N"));
        public string Path {get;}
        public OperatingSystemPrincipalSource Principals {get;}=new();
        public FileHomeCoreStateStore Store {get;}
        public HomeLocalProfileIdentity Actors {get;}
        public HomeInstalledApplicationRegistry Registry {get;}
        public Verifier Verifier {get;}=new();
        public Fixture(){Directory.CreateDirectory(root);Path=System.IO.Path.Combine(root,"home.json");Store=new(Path);Actors=new(Store,Principals);Registry=new(Store,Actors,[]);}
        public void Dispose()=>Directory.Delete(root,true);
    }
}
