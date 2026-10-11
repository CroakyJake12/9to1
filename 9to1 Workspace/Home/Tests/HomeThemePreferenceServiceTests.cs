using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeThemePreferenceServiceTests
{
    private static readonly HomeThemeScope Device=new(HomeDataScope.DeviceLocal,"fixture-device");
    [Fact]
    public async Task Authorised_revisioned_set_and_cache_preserve_exact_scope()
    {
        var backend=new Backend(); using var service=new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        var first=await service.SetAsync(Device,"Glow",new(DisplayScale:1.5),0);
        Assert.True(first.Succeeded); Assert.Equal(1,first.Value!.Preference.Revision);
        var stale=await service.SetAsync(Device,"Bubble",new(),0); Assert.False(stale.Succeeded);Assert.Equal("RevisionConflict",stale.Code);
        backend.ReadState=HomeThemeReadState.Offline;
        var cached=await service.GetAsync(Device);Assert.True(cached.Value!.FromCache);Assert.Equal("Glow",cached.Value.EffectiveThemeName);
        backend.OverrideRead=new(HomeThemeReadState.Available,first.Value.Preference with { Scope=new(HomeDataScope.AccountSynchronized,"other") });
        Assert.Equal("ThemeScopeMismatch",(await service.GetAsync(Device)).Code);
    }
    [Fact]
    public async Task Permission_denial_never_falls_back_to_a_cached_account()
    {
        var backend=new Backend { ReadState=HomeThemeReadState.PermissionDenied,Cache=HomeThemePreference.Default(Device) };
        using var service=new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        var result=await service.GetAsync(Device);Assert.Equal("PermissionDenied",result.Code);Assert.Null(result.Value);Assert.Equal(0,backend.CacheReads);
    }
    [Fact]
    public async Task Missing_custom_theme_falls_back_without_overwriting_original_record()
    {
        var saved=HomeThemePreference.Default(Device) with { ThemeName="Uninstalled",Revision=4 };
        var backend=new Backend { Value=saved };using var service=new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        var result=await service.GetAsync(Device);Assert.True(result.Value!.UsedFallback);Assert.Equal("Professional",result.Value.EffectiveThemeName);
        Assert.Equal(saved,backend.Value);Assert.Equal("Uninstalled",result.Value.Preference.ThemeName);
    }
    [Fact]
    public async Task Observer_failure_and_postcommit_cache_cancellation_keep_success_receipt()
    {
        var backend=new Backend { CacheFailure=new OperationCanceledException("after actual fixture CAS") };
        using var service=new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        service.Changed+=(_,_)=>throw new InvalidOperationException("observer fixture");
        var result=await service.SetAsync(Device,"Glow",new(),0);
        Assert.True(result.Succeeded);Assert.Equal("Succeeded",result.Code);Assert.Equal(backend.Value,result.Value!.Preference);
        Assert.NotNull(result.Diagnostic);Assert.NotEmpty(service.ObserverFailures);
        backend.Invalidate(Device);Assert.True(service.ObserverFailures.Count>=2);
    }
    [Fact]
    public async Task Scope_authority_is_rechecked_after_provider_read()
    {
        var authority=new Authority();var backend=new Backend { ReadAction=()=>authority.Allowed=false };
        using var service=new HomeThemePreferenceService(backend,authority,new Catalog());
        Assert.Equal("PermissionDenied",(await service.GetAsync(Device)).Code);
    }
    [Fact]
    public async Task Async_disposal_seals_admission_and_joins_the_original_held_CAS()
    {
        var backend=new Backend { HoldCommit=new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service=new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        var operation=service.SetAsync(Device,"Glow",new(),0);
        await backend.CommitEntered.Task;
        var close=service.DisposeAsync().AsTask();Assert.False(close.IsCompleted);
        Assert.Throws<ObjectDisposedException>(()=> { _=service.GetAsync(Device); });
        backend.HoldCommit.SetResult();
        Assert.True((await operation).Succeeded);await close;Assert.Equal(1,backend.Value!.Revision);
    }
    private sealed class Authority:IHomeThemeScopeAuthority
    { public bool Allowed=true;public ValueTask<bool> CanAccessAsync(HomeThemeScope scope,bool write,CancellationToken token=default)=>ValueTask.FromResult(Allowed&&scope==Device); }
    private sealed class Catalog:IHomeThemeCatalog
    { public bool TryResolve(string name,out string canonicalName) {canonicalName=name;return name is "Professional" or "Glow" or "Bubble";} }
    private sealed class Backend:IHomeThemePreferenceBackend
    {
        public HomeThemePreference? Value,Cache;public int CacheReads;public HomeThemeReadState ReadState=HomeThemeReadState.Available;
        public HomeThemeReadResult? OverrideRead;public Exception? CacheFailure;public Action? ReadAction;
        public TaskCompletionSource? HoldCommit;public TaskCompletionSource CommitEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<HomeThemeScope>? Changed;
        public bool Supports(HomeThemeScope scope)=>scope.Scope==HomeDataScope.DeviceLocal;
        public Task<HomeThemeReadResult> ReadAsync(HomeThemeScope scope,CancellationToken token=default)
        {ReadAction?.Invoke();return Task.FromResult(OverrideRead??new(ReadState,Value));}
        public Task<HomeThemeReadResult> ReadCacheAsync(HomeThemeScope scope,CancellationToken token=default)
        {CacheReads++;return Task.FromResult(new HomeThemeReadResult(HomeThemeReadState.Available,Cache));}
        public async Task<HomeThemeWriteResult> CompareExchangeAsync(HomeThemeScope scope,long expectedRevision,HomeThemePreference candidate,CancellationToken token=default)
        {
            CommitEntered.TrySetResult();if(HoldCommit is not null)await HoldCommit.Task;
            lock(this) {if((Value?.Revision??0)!=expectedRevision)return new(false,"RevisionConflict");Value=candidate;return new(true,"Succeeded",candidate);}
        }
        public Task WriteCacheAsync(HomeThemePreference preference,CancellationToken token=default)
        {if(CacheFailure is not null)throw CacheFailure;Cache=preference;return Task.CompletedTask;}
        public void Invalidate(HomeThemeScope scope)=>Changed?.Invoke(this,scope);
    }
}
