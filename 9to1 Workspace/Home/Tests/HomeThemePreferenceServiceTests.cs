using HavenOS.Home.Core;
using Xunit;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Home.Tests;

public sealed class HomeThemePreferenceServiceTests
{
    private static readonly HomeThemeScope Device=new(HomeDataScope.DeviceLocal,"fixture-device");
    [Fact]
    public async Task Optional_appearance_round_trips_in_the_existing_revisioned_owner_and_invalid_values_preserve_saved_data()
    {
        var legacy = HomeThemePreference.Default(Device);
        Assert.Null(legacy.Appearance);
        var backend = new Backend { Value = legacy };
        using var service = new HomeThemePreferenceService(backend, new Authority(), new Catalog());
        Assert.Null((await service.GetAsync(Device)).Value!.Preference.Appearance);
        foreach (var mode in new[] { "SuperBright", "Bright", "Dark", "SuperDark" })
        {
            var result = await service.SetAsync(Device, "Glow", new(DisplayScale: 1.5), backend.Value!.Revision, appearance: mode);
            Assert.True(result.Succeeded);
            Assert.Equal(mode, result.Value!.Preference.Appearance);
            Assert.Equal(result.Value.Preference, backend.Value);
            Assert.Equal(Device, backend.Value!.Scope);
            Assert.Equal(1.5, backend.Value.Accessibility.DisplayScale);
        }
        var saved = backend.Value;
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Device, "Glow", new(), saved!.Revision, appearance: "Unrecognised"));
        Assert.Equal(saved, backend.Value);
        Assert.Equal("RevisionConflict", (await service.SetAsync(Device, "Glow", new(), 0, appearance: "Bright")).Code);
        backend.OverrideRead = new(HomeThemeReadState.Available, saved! with { Appearance = "FutureMode" });
        Assert.True((await service.GetAsync(Device)).Value!.UsedFallback);
        Assert.Equal(saved, backend.Value);
    }
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
    public async Task Cache_permission_failure_does_not_discard_an_authorized_primary_theme_read()
    {
        var stored = HomeThemePreference.Default(Device) with { ThemeName = "Glow", Revision = 4 };
        var backend = new Backend { Value = stored, CacheFailure = new UnauthorizedAccessException("offline cache is read-only") };
        using var service = new HomeThemePreferenceService(backend, new Authority(), new Catalog());

        var result = await service.GetAsync(Device);

        Assert.True(result.Succeeded);
        Assert.Equal("Succeeded", result.Code);
        Assert.Equal(stored, result.Value!.Preference);
        Assert.Equal("Glow", result.Value.EffectiveThemeName);
        Assert.Contains("offline cache could not be refreshed", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stored, backend.Value);
    }
    [Fact]
    public async Task Cache_failure_does_not_bypass_the_final_authority_check()
    {
        var stored = HomeThemePreference.Default(Device) with { ThemeName = "Glow", Revision = 4 };
        var authority = new Authority();
        var backend = new Backend
        {
            Value = stored,
            CacheFailure = new UnauthorizedAccessException("cache became read-only"),
            CacheWriteAction = () => authority.Allowed = false
        };
        using var service = new HomeThemePreferenceService(backend, authority, new Catalog());

        var result = await service.GetAsync(Device);

        Assert.False(result.Succeeded);
        Assert.Equal("PermissionDenied", result.Code);
        Assert.Null(result.Value);
        Assert.Equal(stored, backend.Value);
        Assert.Null(backend.Cache);
    }
    [Fact]
    public async Task Cache_write_cancellation_is_not_swallowed_after_a_primary_read()
    {
        var stored = HomeThemePreference.Default(Device) with { ThemeName = "Glow", Revision = 4 };
        var backend = new Backend { Value = stored, CacheFailure = new OperationCanceledException("cache refresh cancelled") };
        using var service = new HomeThemePreferenceService(backend, new Authority(), new Catalog());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(Device));
        Assert.Equal(stored, backend.Value);
        Assert.Null(backend.Cache);
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
    [Fact]
    public async Task Accent_choices_use_structural_JSON_receipts_and_theme_only_writes_preserve_them()
    {
        var choices = new Dictionary<string,string> { ["app.present"]="#db3545", ["app.write"]="#123abc" };
        var accent = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.IndividualApps,"#6366f1",choices);
        choices["app.present"]="#FFFFFF";
        Assert.Equal("#DB3545",accent.ResolveColour("app.present"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string,string>)accent.IndividualApps).Add("app.wave","#000000"));
        var backend = new Backend { CloneReceipt=true };
        using var service = new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        var saved = await service.SetAsync(Device,"Glow",new(),0,appearance:"Dark",accent:accent);
        Assert.True(saved.Succeeded); Assert.Equal(accent,saved.Value!.Preference.Accent);
        Assert.NotSame(accent,backend.Value!.Accent);
        Assert.Equal(saved.Value.Preference,backend.Value);
        var themeOnly = await service.SetAsync(Device,"Bubble",new(),1);
        Assert.True(themeOnly.Succeeded); Assert.Equal(accent,themeOnly.Value!.Preference.Accent);
        Assert.Equal("Dark",themeOnly.Value.Preference.Appearance);
        var reloaded = await service.GetAsync(Device);
        Assert.Equal(accent,reloaded.Value!.Preference.Accent);
        Assert.Equal("#2563EB",reloaded.Value.Preference.Accent!.ResolveColour("app.canvas"));
        Assert.Equal("#DB3545",reloaded.Value.Preference.Accent.ResolveColour("app.present"));
        var none = await service.SetAsync(Device,"Professional",new(),2,accent:accent.WithMode(HomeThemeAccentOverrideMode.None));
        Assert.True(none.Succeeded); Assert.Null(none.Value!.Preference.Accent!.ResolveColour("app.present"));
        Assert.Equal("#DB3545",none.Value.Preference.Accent.IndividualApps["app.present"]);
    }
    [Fact]
    public async Task Persisted_accent_mode_transitions_preserve_individual_choices_and_restore_effective_fallbacks()
    {
        var initial = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.IndividualApps,
            individualApps: new Dictionary<string,string>
            { ["app.present"] = "#DB3545", ["app.write"] = "#123ABC" });
        var backend = new Backend { CloneReceipt = true };
        using var service = new HomeThemePreferenceService(backend, new Authority(), new Catalog());

        var individual = await service.SetAsync(Device, "Professional", new(), 0, accent: initial);
        Assert.True(individual.Succeeded);
        Assert.Equal("#DB3545", individual.Value!.Preference.Accent!.ResolveColour("app.present"));
        Assert.Equal("#2563EB", individual.Value.Preference.Accent.ResolveColour("app.canvas"));

        var allAppsPreference = initial.WithAllAppsColour("#7A2BBC").WithMode(HomeThemeAccentOverrideMode.AllApps);
        var allApps = await service.SetAsync(Device, "Professional", new(), 1, accent: allAppsPreference);
        Assert.True(allApps.Succeeded);
        Assert.Equal("#7A2BBC", allApps.Value!.Preference.Accent!.ResolveColour("app.canvas"));
        Assert.NotEqual(HomeThemeAccentPreference.InitialIndividualAppColour,
            allApps.Value.Preference.Accent.ResolveColour("app.canvas"));
        Assert.Equal("#DB3545", allApps.Value.Preference.Accent.IndividualApps["app.present"]);
        Assert.Equal("#123ABC", allApps.Value.Preference.Accent.IndividualApps["app.write"]);
        var restoredAllApps = (await service.GetAsync(Device)).Value!.Preference.Accent!;
        Assert.Equal(HomeThemeAccentOverrideMode.AllApps, restoredAllApps.Mode);
        Assert.Equal("#7A2BBC", restoredAllApps.AllAppsColour);
        Assert.Equal("#7A2BBC", restoredAllApps.ResolveColour("app.canvas"));
        Assert.Equal("#DB3545", restoredAllApps.IndividualApps["app.present"]);

        var nonePreference = allAppsPreference.WithMode(HomeThemeAccentOverrideMode.None);
        var none = await service.SetAsync(Device, "Professional", new(), 2, accent: nonePreference);
        Assert.True(none.Succeeded);
        Assert.Null(none.Value!.Preference.Accent!.ResolveColour("app.canvas"));
        Assert.Equal("#DB3545", none.Value.Preference.Accent.IndividualApps["app.present"]);
        var restoredNone = (await service.GetAsync(Device)).Value!.Preference.Accent!;
        Assert.Equal(HomeThemeAccentOverrideMode.None, restoredNone.Mode);
        Assert.Equal("#7A2BBC", restoredNone.AllAppsColour);
        Assert.Null(restoredNone.ResolveColour("app.canvas"));
        Assert.Equal("#123ABC", restoredNone.IndividualApps["app.write"]);

        var restored = await service.SetAsync(Device, "Professional", new(), 3,
            accent: nonePreference.WithMode(HomeThemeAccentOverrideMode.IndividualApps));
        Assert.True(restored.Succeeded);
        var reloaded = await service.GetAsync(Device);
        var savedAccent = reloaded.Value!.Preference.Accent!;
        Assert.Equal(HomeThemeAccentOverrideMode.IndividualApps, savedAccent.Mode);
        Assert.Equal("#7A2BBC", savedAccent.AllAppsColour);
        Assert.Equal("#DB3545", savedAccent.ResolveColour("app.present"));
        Assert.Equal("#123ABC", savedAccent.ResolveColour("app.write"));
        Assert.Equal("#2563EB", savedAccent.ResolveColour("app.canvas"));
    }
    [Fact]
    public async Task Full_individual_accent_capacity_preserves_every_choice_and_keeps_blue_fallback()
    {
        var choices = Enumerable.Range(1, HomeThemeAccentPreference.MaximumIndividualAppChoices)
            .ToDictionary(index => $"app.custom-{index:D2}", index => "#" + index.ToString("X6"));
        var backend = new Backend { CloneReceipt = true };
        using var service = new HomeThemePreferenceService(backend, new Authority(), new Catalog());

        var saved = await service.SetAsync(Device, "Professional", new(), 0,
            accent: new(HomeThemeAccentOverrideMode.IndividualApps, individualApps: choices));
        Assert.True(saved.Succeeded);
        var restored = (await service.GetAsync(Device)).Value!.Preference.Accent!;

        Assert.Equal(HomeThemeAccentPreference.MaximumIndividualAppChoices, restored.IndividualApps.Count);
        foreach (var (appId, colour) in choices) Assert.Equal(colour, restored.ResolveColour(appId));
        Assert.Equal("#2563EB", restored.ResolveColour("app.not-configured"));
    }
    [Fact]
    public void Legacy_v1_preferences_default_to_no_override_and_invalid_new_values_fail_without_mutation()
    {
        var legacy = JsonSerializer.Deserialize("{\"schemaVersion\":1,\"scope\":{\"scope\":0,\"ownerId\":\"fixture-device\"},\"themeName\":\"Professional\",\"revision\":0,\"accessibility\":{}}",AccentJsonContext.Default.HomeThemePreference)!;
        Assert.Null(legacy.Accent); legacy.Validate();
        Assert.Null(HomeThemeAccentPreference.Default.ResolveColour("app.present"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HomeThemeAccentPreference((HomeThemeAccentOverrideMode)19));
        Assert.Throws<ArgumentException>(() => new HomeThemeAccentPreference(allAppsColour:"red"));
        Assert.Throws<ArgumentException>(() => new HomeThemeAccentPreference(individualApps:new Dictionary<string,string>{{"../present","#123456"}}));
        Assert.Throws<ArgumentException>(() => new HomeThemeAccentPreference(individualApps:Enumerable.Range(0,33).ToDictionary(x=>"app.a"+x,_=>"#123456")));
        Assert.Equal("#6366F1",new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.AllApps).ResolveColour("app.wave"));
        var first = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.IndividualApps,individualApps:new Dictionary<string,string>{{"app.wave","#112233"},{"app.present","#445566"}});
        var second = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.IndividualApps,individualApps:new Dictionary<string,string>{{"app.present","#445566"},{"app.wave","#112233"}});
        Assert.Equal(first,second); Assert.Equal(first.GetHashCode(),second.GetHashCode());
    }
    [Fact]
    public async Task Concurrent_accent_and_theme_only_writes_do_not_erase_the_winner()
    {
        var backend = new Backend { HoldCommit=new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var first = new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        using var second = new HomeThemePreferenceService(backend,new Authority(),new Catalog());
        var accent = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.AllApps,"#168B4F");
        var write = first.SetAsync(Device,"Glow",new(),0,accent:accent);
        await backend.CommitEntered.Task;
        var oldThemeWrite = second.SetAsync(Device,"Bubble",new(),0);
        backend.HoldCommit.SetResult();
        var results = await Task.WhenAll(write,oldThemeWrite);
        Assert.Single(results,result=>result.Succeeded);
        Assert.Single(results,result=>result.Code=="RevisionConflict");
        Assert.Equal(1,backend.Value!.Revision);
        if (results[0].Succeeded) Assert.Equal(accent,backend.Value.Accent);
        else Assert.Null(backend.Value.Accent);
    }
    [Fact]
    public async Task Custom_library_uses_the_same_revision_and_survives_theme_and_accent_only_updates()
    {
        const string library="[{\"id\":\"fixture\",\"name\":\"My theme\"}]";
        var backend=new Backend {CloneReceipt=true};
        using var service=new HomeThemePreferenceService(backend,new Authority(),new CustomCatalog(library));
        var saved=await service.SetAsync(Device,"My theme",new(),0,customThemesJson:library);
        Assert.True(saved.Succeeded); Assert.Equal(library,backend.Value!.CustomThemesJson);
        var themeOnly=await service.SetAsync(Device,"Glow",new(),1);
        Assert.True(themeOnly.Succeeded); Assert.Equal(library,themeOnly.Value!.Preference.CustomThemesJson);
        var accentOnly=await service.SetAsync(Device,"Glow",new(),2,accent:new(HomeThemeAccentOverrideMode.AllApps,"#123456"));
        Assert.True(accentOnly.Succeeded); Assert.Equal(library,accentOnly.Value!.Preference.CustomThemesJson);
        Assert.Equal("RevisionConflict",(await service.SetAsync(Device,"Professional",new(),1,customThemesJson:"[]")).Code);
        Assert.Equal(library,backend.Value!.CustomThemesJson);
        var clear=await service.SetAsync(Device,"Professional",new(),3,customThemesJson:"[]");
        Assert.True(clear.Succeeded); Assert.Equal("[]",clear.Value!.Preference.CustomThemesJson);
        Assert.Equal("#123456",clear.Value.Preference.Accent!.AllAppsColour);
    }
    [Fact]
    public async Task Unsupported_custom_libraries_and_denied_preservation_reads_do_not_change_the_owner()
    {
        const string library="[{\"id\":\"fixture\",\"name\":\"My theme\"}]";
        var backend=new Backend {Value=HomeThemePreference.Default(Device) with {Revision=5,CustomThemesJson=library}};
        using var service=new HomeThemePreferenceService(backend,new Authority(),new CustomCatalog(library));
        var before=backend.Value;
        Assert.Equal("ThemeUnavailable",(await service.SetAsync(Device,"Professional",new(),5,customThemesJson:"[{\"unknown\":true}]")).Code);
        Assert.Equal("ThemeUnavailable",(await service.SetAsync(Device,"Missing theme",new(),5,customThemesJson:library)).Code);
        Assert.Equal(before,backend.Value);
        backend.ReadState=HomeThemeReadState.PermissionDenied;
        Assert.Equal("PermissionDenied",(await service.SetAsync(Device,"Glow",new(),5)).Code);
        Assert.Equal(before,backend.Value);
    }
    private sealed class CustomCatalog(string validLibrary):IHomeCustomThemeCatalog
    {
        public bool TryResolve(string name,out string canonicalName) {canonicalName=name;return name is "Professional" or "Glow";}
        public bool TryResolve(string name,string library,out string canonicalName)
        {canonicalName=name;return (library==validLibrary||library=="[]") && (TryResolve(name,out canonicalName)||library==validLibrary&&name=="My theme");}
    }
    private sealed class Authority:IHomeThemeScopeAuthority
    { public bool Allowed=true;public ValueTask<bool> CanAccessAsync(HomeThemeScope scope,bool write,CancellationToken token=default)=>ValueTask.FromResult(Allowed&&scope==Device); }
    private sealed class Catalog:IHomeThemeCatalog
    { public bool TryResolve(string name,out string canonicalName) {canonicalName=name;return name is "Professional" or "Glow" or "Bubble";} }
    private sealed class Backend:IHomeThemePreferenceBackend
    {
        public HomeThemePreference? Value,Cache;public bool CloneReceipt;public int CacheReads;public HomeThemeReadState ReadState=HomeThemeReadState.Available;
        public HomeThemeReadResult? OverrideRead;public Exception? CacheFailure;public Action? ReadAction;public Action? CacheWriteAction;
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
            lock(this) {if((Value?.Revision??0)!=expectedRevision)return new(false,"RevisionConflict");Value=CloneReceipt ? JsonSerializer.Deserialize(JsonSerializer.Serialize(candidate,AccentJsonContext.Default.HomeThemePreference),AccentJsonContext.Default.HomeThemePreference) : candidate;return new(true,"Succeeded",Value);}
        }
        public Task WriteCacheAsync(HomeThemePreference preference,CancellationToken token=default)
        {CacheWriteAction?.Invoke();if(CacheFailure is not null)throw CacheFailure;Cache=preference;return Task.CompletedTask;}
        public void Invalidate(HomeThemeScope scope)=>Changed?.Invoke(this,scope);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HomeThemePreference))]
internal sealed partial class AccentJsonContext : JsonSerializerContext;
