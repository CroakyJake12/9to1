using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>Browser adapter over Home's existing local-appearance envelope. Account sync is not fabricated.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserHomeThemePreferences : IBrowserHomeThemePreferences
{
    private Backend? _backend;
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
        try { await owner.RefreshAsync(token); return owner; }
        catch(Exception primary)
        {
            try { await owner.DisposeAsync(); }
            catch(Exception cleanup) {throw new AggregateException("Home theme acquisition and cleanup failed.",primary,cleanup);}
            throw;
        }
    }

    public Task<HomeThemeOperationResult> SetAsync(string name,CuiAccessibilitySettings? accessibility=null,CancellationToken token=default)
    {
        var start=new TaskCompletionSource();Task<HomeThemeOperationResult> original;
        lock(_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(!CanChange||_service is null||Scope is not { } scope) return Task.FromResult(new HomeThemeOperationResult(false,"ThemePreferenceUnavailable",Diagnostic:Diagnostic??"Theme changes cannot be saved in this session."));
            var settings=(accessibility??Current.Accessibility).Validate();
            _operations.RemoveWhere(task=>task.IsCompleted);
            original=SetCoreAsync(start.Task,_service,scope,name,settings,Current.Revision,token);_operations.Add(original);
        }
        start.SetResult();return original;
    }
    private async Task<HomeThemeOperationResult> SetCoreAsync(Task start,HomeThemePreferenceService service,HomeThemeScope scope,string name,CuiAccessibilitySettings settings,long revision,CancellationToken token)
    {
        await start;
        var result=await service.SetAsync(scope,name,new(settings.ReduceMotion,settings.HighContrast,settings.DisplayScale,
            settings.InterfaceFontFamilyOverride,settings.CodeFontFamilyOverride),revision,token);
        if(result.Succeeded && result.Value is { } value) Observe(value);
        else Diagnostic=result.Diagnostic;
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
        if(_service is null)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var scope=new HomeThemeScope(HomeDataScope.DeviceLocal,await ThemeContext());
                _backend=new(scope);_service=new(_backend,new DeviceAuthority(scope),new CatalogAdapter(Catalog));
                Scope=scope;_service.Changed+=Invalidated;
            }
            catch(Exception error) when(error is JSException or InvalidOperationException or IOException or JsonException)
            { Diagnostic="Home theme preferences are unavailable. Professional is being used for this session; theme changes cannot be saved.";Publish();return; }
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
        if(_disposed)return;
        var previousCanChange=_canChange;
        _canChange=result.Succeeded && result.Value is { } resolved && !(resolved.UsedFallback&&resolved.Preference.Revision==0);
        if(result.Succeeded && result.Value is { } value) {Observe(value);if(previousCanChange!=_canChange)Publish();}
        else
        {
            Diagnostic=result.Diagnostic??"Home theme preferences are unavailable. Professional is being used.";
            Current=new("Professional",Current.Revision,Scope!.OwnerId,false,Current.Accessibility); Publish();
        }
    }
    private void Observe(HomeThemeResolution value)
    {
        if(_disposed||value.Preference.Scope!=Scope)return;
        var stored=value.Preference; var a=stored.Accessibility;
        if(stored.Revision<Current.Revision) { if(value.UsedFallback)Diagnostic=value.Diagnostic;return; }
        var next=new CuiThemePreferenceSnapshot(stored.ThemeName,stored.Revision,Scope!.OwnerId,value.FromCache,
            new() {ReduceMotion=a.ReduceMotion,HighContrast=a.HighContrast,DisplayScale=a.DisplayScale,
                InterfaceFontFamilyOverride=a.InterfaceFontFamily,CodeFontFamilyOverride=a.CodeFontFamily});
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
    private void Invalidated(object? sender,HomeThemeScope scope)
    {
        if(_disposed||scope!=Scope)return;
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
            _disposed=true;if(_service is not null)_service.Changed-=Invalidated;
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

    private sealed class DeviceAuthority(HomeThemeScope scope):IHomeThemeScopeAuthority
    {
        public ValueTask<bool> CanAccessAsync(HomeThemeScope requested,bool write,CancellationToken cancellationToken=default)
        { cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(requested==scope&&scope.Scope==HomeDataScope.DeviceLocal); }
    }
    private sealed class CatalogAdapter(CuiThemeDefinitionCatalog catalog):IHomeThemeCatalog
    {
        public bool TryResolve(string requestedName,out string canonicalName)
        { if(catalog.TryResolve(requestedName,out var theme)&&theme is not null) {canonicalName=theme.Name;return true;} canonicalName="Professional";return false; }
    }
    private sealed class Backend: IHomeThemePreferenceBackend,IDisposable
    {
        private readonly HomeThemeScope _scope;
        private readonly Action _notify;
        private readonly int _subscription;
        public event EventHandler<HomeThemeScope>? Changed;
        public Backend(HomeThemeScope scope)
        { _scope=scope;_notify=()=>Changed?.Invoke(this,scope);_subscription=Subscribe(_notify); }
        public bool Supports(HomeThemeScope scope)=>scope==_scope&&scope.Scope==HomeDataScope.DeviceLocal;
        public async Task<HomeThemeReadResult> ReadAsync(HomeThemeScope scope,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!Supports(scope))return new(HomeThemeReadState.Unsupported);
            return JsonSerializer.Deserialize(await ReadTheme(scope.OwnerId),BrowserHomeThemeJsonContext.Default.HomeThemeReadResult)??new(HomeThemeReadState.InvalidData);
        }
        public Task<HomeThemeReadResult> ReadCacheAsync(HomeThemeScope scope,CancellationToken cancellationToken=default)=>ReadAsync(scope,cancellationToken);
        public async Task<HomeThemeWriteResult> CompareExchangeAsync(HomeThemeScope scope,long expectedRevision,HomeThemePreference candidate,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!Supports(scope))return new(false,"ThemeScopeUnsupported");
            return JsonSerializer.Deserialize(await CompareTheme(scope.OwnerId,expectedRevision,
                JsonSerializer.Serialize(candidate,BrowserHomeThemeJsonContext.Default.HomeThemePreference)),BrowserHomeThemeJsonContext.Default.HomeThemeWriteResult)
                ??new(false,"InvalidThemeReceipt");
        }
        // For device-local preferences the original durable envelope is already
        // the offline copy; there is no redundant browser cache/store to overwrite.
        public Task WriteCacheAsync(HomeThemePreference preference,CancellationToken cancellationToken=default)
        { cancellationToken.ThrowIfCancellationRequested();if(!Supports(preference.Scope))throw new UnauthorizedAccessException();return Task.CompletedTask; }
        public void Dispose() { Unsubscribe(_subscription);Changed=null; }
    }

    [JSImport("themeContext","nineToOneHomeLocal")] private static partial Task<string> ThemeContext();
    [JSImport("readTheme","nineToOneHomeLocal")] private static partial Task<string> ReadTheme(string scope);
    [JSImport("compareTheme","nineToOneHomeLocal")] private static partial Task<string> CompareTheme(string scope,double expectedRevision,string candidate);
    [JSImport("subscribe","nineToOneHomeLocal")] private static partial int Subscribe([JSMarshalAs<JSType.Function>] Action callback);
    [JSImport("unsubscribe","nineToOneHomeLocal")] private static partial void Unsubscribe(int subscription);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true)]
[JsonSerializable(typeof(HomeThemeReadResult))]
[JsonSerializable(typeof(HomeThemeWriteResult))]
[JsonSerializable(typeof(HomeThemePreference))]
internal sealed partial class BrowserHomeThemeJsonContext : JsonSerializerContext;
