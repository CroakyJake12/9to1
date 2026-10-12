using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>Browser adapter over the authenticated CAKE account preference owner; local data is an exact-account offline cache.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserHomeThemePreferences : IBrowserHomeThemePreferences
{
    private Backend? _backend;
    private Action<bool>? _accountChanged;
    private int _accountSubscription;
    private HomeThemePreferenceService? _service;
    private bool _disposed, _refreshQueued, _refreshRunning, _canChange;
    private Task _refreshWork = Task.CompletedTask;
    private readonly object _operationGate=new();
    private readonly HashSet<Task> _operations=[];
    private Task? _disposal;
    private readonly SemaphoreSlim _refreshGate=new(1,1);
    public HomeThemeScope? Scope { get; private set; }
    public bool CanChange => !_disposed && _service is not null && _canChange;
    public CuiThemePreferenceSnapshot Current { get; private set; }
    public string? Diagnostic { get; private set; }
    public string ScopeDescription => Scope is null ? "Sign in to save account-wide theme preferences." :
        Current.FromCache ? "Account-wide preferences · using this account’s offline cache." : "Account-wide theme preferences.";
    public string SavedDescription => "Appearance saved to your account.";
    public event EventHandler? Changed;
    public CuiThemeDefinitionCatalog Catalog { get; }

    private BrowserHomeThemePreferences(CuiThemeDefinitionCatalog catalog)
    {
        Catalog=catalog;
        // This is explicitly a presentation-only fallback, never a durable
        // device/account owner and never a writable Home settings scope.
        Current=new("Professional",0,"presentation-only",false,new() { ReduceMotion=Program.ReduceMotion() });
    }

    public static async Task<BrowserHomeThemePreferences> CreateAsync(CuiThemeDefinitionCatalog? catalog=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var owner=new BrowserHomeThemePreferences(catalog??new());
        try { owner._accountChanged=owner.AccountChanged; owner._accountSubscription=Subscribe(owner._accountChanged); await owner.RefreshAsync(token); return owner; }
        catch(Exception primary)
        {
            try { await owner.DisposeAsync(); }
            catch(Exception cleanup) {throw new AggregateException("Home theme acquisition and cleanup failed.",primary,cleanup);}
            throw;
        }
    }

    public Task<HomeThemeOperationResult> SetAsync(string name,CuiAccessibilitySettings? accessibility=null,CancellationToken token=default,CuiAppearance? appearance=null,CuiAccentPreference? accent=null)
    {
        var start=new TaskCompletionSource();Task<HomeThemeOperationResult> original;
        lock(_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(!CanChange||_service is null||Scope is not { } scope) return Task.FromResult(new HomeThemeOperationResult(false,"ThemePreferenceUnavailable",Diagnostic:Diagnostic??"Theme changes cannot be saved in this session."));
            var settings=(accessibility??Current.Accessibility).Validate();
            var mode=appearance??Current.Appearance;
            if(mode is { } selected && !Enum.IsDefined(selected))throw new ArgumentOutOfRangeException(nameof(appearance));
            _operations.RemoveWhere(task=>task.IsCompleted);
            original=SetCoreAsync(start.Task,_service,scope,name,settings,Current.Revision,token,mode,accent ?? Current.EffectiveAccent,null);_operations.Add(original);
        }
        start.SetResult();return original;
    }
    public Task<HomeThemeOperationResult> SaveCustomThemeAsync(CuiCustomTheme theme,CuiThemePreferenceSnapshot observed,CancellationToken token=default)
    {
        var start=new TaskCompletionSource();Task<HomeThemeOperationResult> original;
        lock(_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(!CanChange||_service is null||Scope is not { } scope||observed.ScopeId!=scope.OwnerId||!CanAccess(scope.OwnerId))
                return Task.FromResult(new HomeThemeOperationResult(false,"ThemeScopeMismatch",Diagnostic:"The signed-in account changed. Reopen the theme editor before saving."));
            if(!ReferenceEquals(observed,Current))
                return Task.FromResult(new HomeThemeOperationResult(false,"RevisionConflict",Diagnostic:"Account appearance changed while this draft was open. Refresh before saving."));
            string library;
            try
            {
                theme=CuiCustomThemeContract.Validate(theme);
                var existing=observed.CustomThemesJson is null?Array.Empty<CuiCustomTheme>():CuiCustomThemeContract.DeserializeLibrary(observed.CustomThemesJson);
                library=CuiCustomThemeContract.SerializeLibrary(existing.Where(item=>item.Id!=theme.Id).Append(theme));
            }
            catch(ArgumentException error)
            { return Task.FromResult(new HomeThemeOperationResult(false,"InvalidCustomTheme",Diagnostic:error.Message)); }
            _operations.RemoveWhere(task=>task.IsCompleted);
            original=SetCoreAsync(start.Task,_service,scope,theme.Name,observed.Accessibility,observed.Revision,token,observed.Appearance,observed.EffectiveAccent,library);
            _operations.Add(original);
        }
        start.SetResult();return original;
    }
    private async Task<HomeThemeOperationResult> SetCoreAsync(Task start,HomeThemePreferenceService service,HomeThemeScope scope,string name,CuiAccessibilitySettings settings,long revision,CancellationToken token,CuiAppearance? appearance,CuiAccentPreference accent,string? customThemesJson)
    {
        await start;
        var result=await service.SetAsync(scope,name,new(settings.ReduceMotion,settings.HighContrast,settings.DisplayScale,
            settings.InterfaceFontFamilyOverride,settings.CodeFontFamilyOverride),revision,token,appearance?.ToString(),
            new HomeThemeAccentPreference((HomeThemeAccentOverrideMode)accent.Mode,accent.AllAppsColour,accent.IndividualApps),customThemesJson);
        if(result.Succeeded && result.Value is { } value) Observe(value);
        else if(Scope==scope)Diagnostic=result.Diagnostic;
        return result;
    }

    public Task RefreshAsync(CancellationToken token=default)
    {
        var start=new TaskCompletionSource();Task original;
        lock(_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);_operations.RemoveWhere(task=>task.IsCompleted);
            original=RefreshCoreAsync(start.Task,token);_operations.Add(original);
        }
        start.SetResult();return original;
    }
    private async Task RefreshCoreAsync(Task start,CancellationToken token)
    {
        await start;
        await _refreshGate.WaitAsync(token);
        try { await RefreshOwnerAsync(token); }
        finally { _refreshGate.Release(); }
    }
    private async Task RefreshOwnerAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string account;
        try { account=await Request(token,ThemeContext); }
        catch(Exception error) when(error is JSException or IOException or InvalidOperationException)
        { _canChange=false;Diagnostic="Account preferences are unavailable. Sign in or reconnect to save appearance changes.";Publish();return; }
        if(string.IsNullOrEmpty(account))
        { ResetAccountPresentation();Diagnostic="Sign in to save account-wide theme preferences.";Publish();return; }
        var scope=new HomeThemeScope(HomeDataScope.AccountSynchronized,account);
        if(Scope!=scope) { ResetAccountPresentation();Scope=scope; }
        if(_service is null)
        {
            _backend=new();_service=new(_backend,new AccountAuthority(),new CatalogAdapter());
            _service.Changed+=Invalidated;
        }
        HomeThemeOperationResult result;
        try { result=await _service.GetAsync(Scope!,token); }
        catch(Exception error) when(error is JSException or IOException or JsonException or NotSupportedException or InvalidOperationException)
        {
            _canChange=false;
            Diagnostic=Current.Revision>0 ? "Home preferences could not be read. The last valid appearance is retained; saving is unavailable." :
                "Home preferences could not be read. Professional is being used for this session; saving is unavailable.";
            Publish();return;
        }
        if(_disposed||Scope!=scope)return;
        var previousCanChange=_canChange;
        _canChange=result.Succeeded && result.Value is { } resolved && !resolved.FromCache && !(resolved.UsedFallback&&resolved.Preference.Revision==0);
        if(result.Succeeded && result.Value is { } value) {Observe(value);if(previousCanChange!=_canChange)Publish();}
        else
        {
            Diagnostic=result.Diagnostic??"Home theme preferences are unavailable. Professional is being used.";
            Current=new("Professional",Current.Revision,Scope!.OwnerId,false,Current.Accessibility,Current.Appearance,Current.Accent); Publish();
        }
    }
    private void Observe(HomeThemeResolution value)
    {
        if(_disposed||value.Preference.Scope!=Scope)return;
        var stored=value.Preference; var a=stored.Accessibility;
        if(stored.Revision<Current.Revision) { if(value.UsedFallback)Diagnostic=value.Diagnostic;return; }
        var next=new CuiThemePreferenceSnapshot(stored.ThemeName,stored.Revision,Scope!.OwnerId,value.FromCache,
            new() {ReduceMotion=a.ReduceMotion,HighContrast=a.HighContrast,DisplayScale=a.DisplayScale,
                InterfaceFontFamilyOverride=a.InterfaceFontFamily,CodeFontFamilyOverride=a.CodeFontFamily},
            stored.Appearance is null ? null : Enum.Parse<CuiAppearance>(stored.Appearance),
            stored.Accent is { } accent ? new CuiAccentPreference((CuiAccentOverrideMode)accent.Mode,accent.AllAppsColour,accent.IndividualApps) : null,stored.CustomThemesJson);
        Diagnostic=value.Diagnostic;
        if(next==Current)return;
        Current=next; Publish();
    }
    private void Publish()
    {
        if(_disposed)return;
        foreach(var observer in Changed?.GetInvocationList()??[])
            try { ((EventHandler)observer)(this,EventArgs.Empty); }
            catch(Exception) { Diagnostic="Theme saved; a presentation observer needs to refresh."; }
    }
    private void ResetAccountPresentation()
    {
        _canChange=false;Scope=null;
        Current=new("Professional",0,"presentation-only",false,new() { ReduceMotion=Program.ReduceMotion() });
    }
    private void AccountChanged(bool revoked)
    {
        if(_disposed)return;
        if(revoked) { ResetAccountPresentation();Diagnostic="Sign in to save account-wide theme preferences.";Publish(); }
        QueueRefresh();
    }
    private void Invalidated(object? sender,HomeThemeScope scope)
    {
        if(_disposed||scope!=Scope)return;
        QueueRefresh();
    }
    private void QueueRefresh()
    {
        _refreshQueued=true;
        if(!_refreshRunning) { _refreshRunning=true; _refreshWork=DrainRefreshAsync(); }
    }
    private async Task DrainRefreshAsync()
    {
        try { while(_refreshQueued&&!_disposed) { _refreshQueued=false; await RefreshAsync(); } }
        catch(Exception) { Diagnostic="The Home theme could not be refreshed; the current presentation is retained."; }
        finally { _refreshRunning=false; }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource start;Task disposal;
        lock(_operationGate)
        {
            if(_disposal is not null)return new(_disposal);
            _disposed=true;Unsubscribe(_accountSubscription);_accountChanged=null;if(_service is not null)_service.Changed-=Invalidated;
            start=new();disposal=CloseAsync(start.Task,_operations.Append(_refreshWork).ToArray());_disposal=disposal;
        }
        start.SetResult();return new(disposal);
    }
    private async Task CloseAsync(Task start,Task[] operations)
    {
        await start;
        var errors=new List<Exception>();
        var joined=Task.WhenAll(operations);
        try { await joined; }
        catch(Exception error)
        { if(joined.Exception is { InnerExceptions.Count:>0 } aggregate)errors.AddRange(aggregate.InnerExceptions);else errors.Add(error); }
        try { if(_service is not null)await _service.DisposeAsync(); } catch(Exception error) {if(!errors.Any(previous=>ReferenceEquals(previous,error)))errors.Add(error);}
        try { _backend?.Dispose(); } catch(Exception error) {errors.Add(error);}
        Changed=null;
        if(errors.Count==1)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if(errors.Count>1)throw new AggregateException("Home appearance operations settled with errors.",errors);
    }

    private sealed class AccountAuthority:IHomeThemeScopeAuthority
    {
        public ValueTask<bool> CanAccessAsync(HomeThemeScope scope,bool write,CancellationToken cancellationToken=default)
        { cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(scope.Scope==HomeDataScope.AccountSynchronized&&scope.OrganizationId is null&&CanAccess(scope.OwnerId)); }
    }
    private sealed class CatalogAdapter:IHomeCustomThemeCatalog
    {
        public bool TryResolve(string requestedName,out string canonicalName)
        { var theme=CuiThemeCatalog.All.FirstOrDefault(theme=>string.Equals(theme.DisplayName,requestedName,StringComparison.OrdinalIgnoreCase)); canonicalName=theme?.DisplayName??"Professional";return theme is not null; }
        public bool TryResolve(string requestedName,string customThemesJson,out string canonicalName)
        {
            canonicalName="Professional";
            try
            {
                var library=CuiCustomThemeContract.DeserializeLibrary(customThemesJson);
                var selected=library.FirstOrDefault(theme=>string.Equals(theme.Name,requestedName,StringComparison.OrdinalIgnoreCase));
                if(selected is not null) { canonicalName=selected.Name;return true; }
                // Custom names from a previous account never qualify a new scope.
                var builtin=CuiThemeCatalog.All.FirstOrDefault(theme=>string.Equals(theme.DisplayName,requestedName,StringComparison.OrdinalIgnoreCase));
                if(builtin is not null) {canonicalName=builtin.DisplayName;return true;}
            }
            catch(ArgumentException) { }
            return false;
        }
    }
    private sealed class Backend:IHomeThemePreferenceBackend,IDisposable
    {
        public event EventHandler<HomeThemeScope>? Changed { add { } remove { } }
        public bool Supports(HomeThemeScope scope)=>scope.Scope==HomeDataScope.AccountSynchronized&&scope.OrganizationId is null;
        public async Task<HomeThemeReadResult> ReadAsync(HomeThemeScope scope,CancellationToken cancellationToken=default)
        {
            if(!Supports(scope))return new(HomeThemeReadState.Unsupported);
            return JsonSerializer.Deserialize(await Request(cancellationToken,id=>ReadTheme(id,scope.OwnerId)),BrowserHomeThemeJsonContext.Default.HomeThemeReadResult)??new(HomeThemeReadState.InvalidData);
        }
        public Task<HomeThemeReadResult> ReadCacheAsync(HomeThemeScope scope,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Supports(scope)?JsonSerializer.Deserialize(ReadCache(scope.OwnerId),BrowserHomeThemeJsonContext.Default.HomeThemeReadResult)??new(HomeThemeReadState.InvalidData):new(HomeThemeReadState.Unsupported));
        }
        public async Task<HomeThemeWriteResult> CompareExchangeAsync(HomeThemeScope scope,long expectedRevision,HomeThemePreference candidate,CancellationToken cancellationToken=default)
        {
            if(!Supports(scope))return new(false,"ThemeScopeUnsupported");
            return JsonSerializer.Deserialize(await Request(cancellationToken,id=>CompareTheme(id,scope.OwnerId,expectedRevision,
                JsonSerializer.Serialize(candidate,BrowserHomeThemeJsonContext.Default.HomeThemePreference))),BrowserHomeThemeJsonContext.Default.HomeThemeWriteResult)??new(false,"InvalidThemeReceipt");
        }
        public Task WriteCacheAsync(HomeThemePreference preference,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!Supports(preference.Scope))throw new UnauthorizedAccessException();
            try { WriteCache(preference.Scope.OwnerId,JsonSerializer.Serialize(preference,BrowserHomeThemeJsonContext.Default.HomeThemePreference)); }
            catch(JSException error) { throw new IOException("The account preference cache could not be refreshed.",error); }
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
    private static async Task<string> Request(CancellationToken token,Func<string,Task<string>> operation)
    {
        token.ThrowIfCancellationRequested();var id=Guid.NewGuid().ToString("N");
        using var registration=token.Register(()=>Cancel(id));
        token.ThrowIfCancellationRequested();var result=await operation(id);token.ThrowIfCancellationRequested();return result;
    }
    [JSImport("themeContext","nineToOneHomeAccountTheme")] private static partial Task<string> ThemeContext(string requestId);
    [JSImport("canAccess","nineToOneHomeAccountTheme")] private static partial bool CanAccess(string scope);
    [JSImport("readTheme","nineToOneHomeAccountTheme")] private static partial Task<string> ReadTheme(string requestId,string scope);
    [JSImport("readCache","nineToOneHomeAccountTheme")] private static partial string ReadCache(string scope);
    [JSImport("compareTheme","nineToOneHomeAccountTheme")] private static partial Task<string> CompareTheme(string requestId,string scope,double expectedRevision,string candidate);
    [JSImport("writeCache","nineToOneHomeAccountTheme")] private static partial void WriteCache(string scope,string preference);
    [JSImport("cancel","nineToOneHomeAccountTheme")] private static partial void Cancel(string requestId);
    [JSImport("subscribe","nineToOneHomeAccountTheme")] private static partial int Subscribe([JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> callback);
    [JSImport("unsubscribe","nineToOneHomeAccountTheme")] private static partial void Unsubscribe(int subscription);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true)]
[JsonSerializable(typeof(HomeThemeReadResult))]
[JsonSerializable(typeof(HomeThemeWriteResult))]
[JsonSerializable(typeof(HomeThemePreference))]
internal sealed partial class BrowserHomeThemeJsonContext : JsonSerializerContext;
