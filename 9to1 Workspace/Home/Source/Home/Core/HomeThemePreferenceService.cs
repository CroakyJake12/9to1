namespace HavenOS.Home.Core;

public sealed record HomeThemeScope(HomeDataScope Scope, string OwnerId, string? OrganizationId = null)
{
    public HomeThemeScope Validate()
    {
        if (Scope is not (HomeDataScope.DeviceLocal or HomeDataScope.AccountSynchronized) || string.IsNullOrWhiteSpace(OwnerId) ||
            OwnerId.Length > 256 || OrganizationId?.Length > 256 || Scope == HomeDataScope.DeviceLocal && OrganizationId is not null)
            throw new ArgumentException("Theme preference requires an explicit supported device/account scope.");
        return this;
    }
}

public sealed record HomeThemeAccessibility(bool ReduceMotion = false, bool HighContrast = false, double DisplayScale = 1,
    string? InterfaceFontFamily = null, string? CodeFontFamily = null)
{
    public HomeThemeAccessibility Validate()
    {
        if (!double.IsFinite(DisplayScale) || DisplayScale is < .5 or > 5 || InvalidFont(InterfaceFontFamily) || InvalidFont(CodeFontFamily))
            throw new ArgumentException("Theme accessibility settings have an invalid scale or font family.");
        return this;
    }
    private static bool InvalidFont(string? value) => value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains(',') || value.Any(char.IsControl));
}

public sealed record HomeThemePreference(int SchemaVersion, HomeThemeScope Scope, string ThemeName, long Revision, HomeThemeAccessibility Accessibility)
{
    // Optional in the existing v1 envelope: old preferences keep the host default.
    // Home owns the value without referencing a particular UI renderer.
    public string? Appearance { get; init; }
    public const int CurrentSchemaVersion = 1;
    public static HomeThemePreference Default(HomeThemeScope scope) => new(CurrentSchemaVersion,scope.Validate(),"Professional",0,new());
    public HomeThemePreference Validate()
    {
        ArgumentNullException.ThrowIfNull(Scope); ArgumentNullException.ThrowIfNull(Accessibility);
        Scope.Validate(); Accessibility.Validate();
        if (SchemaVersion != CurrentSchemaVersion || Revision < 0 || string.IsNullOrWhiteSpace(ThemeName) || ThemeName.Length > 128 || ThemeName.Any(char.IsControl) ||
            Appearance is not (null or "SuperBright" or "Bright" or "Dark" or "SuperDark"))
            throw new ArgumentException("Theme preference has an unsupported schema or invalid value.");
        return this;
    }
}

public enum HomeThemeReadState { Available, Offline, Unsupported, PermissionDenied, InvalidData }
public sealed record HomeThemeReadResult(HomeThemeReadState State, HomeThemePreference? Preference = null, string? Diagnostic = null);
public sealed record HomeThemeWriteResult(bool Succeeded, string Code, HomeThemePreference? Preference = null, string? Diagnostic = null);
public sealed record HomeThemeResolution(HomeThemePreference Preference, string EffectiveThemeName, bool FromCache, bool UsedFallback, string? Diagnostic = null);
public sealed record HomeThemeOperationResult(bool Succeeded, string Code, HomeThemeResolution? Value = null, string? Diagnostic = null);

/// <summary>Provided by the actual current Home actor/profile authority; absence is not authorisation.</summary>
public interface IHomeThemeScopeAuthority
{
    ValueTask<bool> CanAccessAsync(HomeThemeScope scope, bool write, CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter to the existing native/browser preference owner or authorised settings
/// sync provider. CompareExchange must atomically check revision AND current scope
/// authority at commit; the service's preliminary checks are not a write capability.
/// Cache data belongs to that same owner and is keyed by the complete exact scope.
/// </summary>
public interface IHomeThemePreferenceBackend
{
    bool Supports(HomeThemeScope scope);
    Task<HomeThemeReadResult> ReadAsync(HomeThemeScope scope, CancellationToken cancellationToken = default);
    Task<HomeThemeReadResult> ReadCacheAsync(HomeThemeScope scope, CancellationToken cancellationToken = default);
    Task<HomeThemeWriteResult> CompareExchangeAsync(HomeThemeScope scope, long expectedRevision, HomeThemePreference candidate, CancellationToken cancellationToken = default);
    Task WriteCacheAsync(HomeThemePreference preference, CancellationToken cancellationToken = default);
    /// <summary>Invalidation only; consumers re-read under their current authority before seeing values.</summary>
    event EventHandler<HomeThemeScope>? Changed;
}

/// <summary>Installed compatible theme names, supplied without coupling Home Core to a renderer.</summary>
public interface IHomeThemeCatalog
{
    bool TryResolve(string requestedName, out string canonicalName);
}

/// <summary>Canonical theme get/set/subscribe semantics over existing owning providers; no competing store.</summary>
public sealed class HomeThemePreferenceService : IDisposable,IAsyncDisposable
{
    private readonly IHomeThemePreferenceBackend _backend;
    private readonly IHomeThemeScopeAuthority _authority;
    private readonly IHomeThemeCatalog _catalog;
    private bool _disposed,_closing;
    private readonly object _operationGate=new();
    private readonly HashSet<Task> _operations=[];
    private Task? _disposal;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _observerErrors = new();
    public IReadOnlyList<Exception> ObserverFailures => _observerErrors.ToArray();
    public event EventHandler<HomeThemeScope>? Changed;

    public HomeThemePreferenceService(IHomeThemePreferenceBackend backend, IHomeThemeScopeAuthority authority, IHomeThemeCatalog catalog)
    {
        _backend=backend??throw new ArgumentNullException(nameof(backend)); _authority=authority??throw new ArgumentNullException(nameof(authority));
        _catalog=catalog??throw new ArgumentNullException(nameof(catalog)); backend.Changed+=Invalidated;
    }
    public Task<HomeThemeOperationResult> GetAsync(HomeThemeScope scope,CancellationToken token=default)=>
        Track(()=>GetCoreAsync(scope,token));
    private async Task<HomeThemeOperationResult> GetCoreAsync(HomeThemeScope scope,CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed,this); scope.Validate();
        if (!await _authority.CanAccessAsync(scope,false,token).ConfigureAwait(false)) return Denied();
        if (!_backend.Supports(scope)) return new(false,"ThemeScopeUnsupported",Diagnostic:"No preference provider supports this scope.");
        var read=await _backend.ReadAsync(scope,token).ConfigureAwait(false);
        if (read.State==HomeThemeReadState.PermissionDenied) return Denied();
        var fromCache=false;
        if (read.State==HomeThemeReadState.Offline)
        {
            read=await _backend.ReadCacheAsync(scope,token).ConfigureAwait(false); fromCache=true;
            if (read.State==HomeThemeReadState.PermissionDenied) return Denied();
        }
        if (read.State==HomeThemeReadState.Unsupported && !fromCache) return new(false,"ThemeScopeUnsupported",Diagnostic:read.Diagnostic);
        if (!await _authority.CanAccessAsync(scope,false,token).ConfigureAwait(false)) return Denied();
        if (read.Preference is { } stored && stored.Scope!=scope) return new(false,"ThemeScopeMismatch",Diagnostic:"The preference provider returned a different scope.");
        var value=read.Preference;
        var invalid=read.State!=HomeThemeReadState.Available;
        if(value is not null)
        {
            try { value.Validate(); }
            catch(ArgumentException) { invalid=true; value=null; }
        }
        value??=HomeThemePreference.Default(scope);
        var supported=_catalog.TryResolve(value.ThemeName,out var name);
        var fallback=invalid||!supported;
        var diagnostic=fallback ? "The saved theme is unavailable or incompatible. Professional is being used; the saved preference is preserved." :
            fromCache ? "Using the last valid cached Home theme preference while offline." : null;
        if(!supported||invalid) name="Professional";
        // Invalid reads never replace or delete the owner's existing preference/cache.
        if(!fromCache && !invalid && read.Preference is not null)
        {
            try { await _backend.WriteCacheAsync(value,token).ConfigureAwait(false); }
            catch(IOException) { diagnostic="Theme loaded, but its offline cache could not be refreshed."; }
        }
        if (!await _authority.CanAccessAsync(scope,false,token).ConfigureAwait(false)) return Denied();
        return new(true,fallback?"ThemeFallback":fromCache?"ThemeCached":"Succeeded",new(value,name,fromCache,fallback,diagnostic),diagnostic);
    }

    public Task<HomeThemeOperationResult> SetAsync(HomeThemeScope scope,string themeName,HomeThemeAccessibility accessibility,long expectedRevision,CancellationToken token=default,string? appearance=null)=>
        Track(()=>SetCoreAsync(scope,themeName,accessibility,expectedRevision,token,appearance));
    private async Task<HomeThemeOperationResult> SetCoreAsync(HomeThemeScope scope,string themeName,HomeThemeAccessibility accessibility,long expectedRevision,CancellationToken token,string? appearance)
    {
        ObjectDisposedException.ThrowIf(_disposed,this); scope.Validate(); accessibility.Validate();
        if(expectedRevision<0||string.IsNullOrWhiteSpace(themeName)) return new(false,"InvalidArgument",Diagnostic:"Choose a valid theme and revision.");
        if(!await _authority.CanAccessAsync(scope,true,token).ConfigureAwait(false)) return Denied();
        if(!_backend.Supports(scope)) return new(false,"ThemeScopeUnsupported",Diagnostic:"No preference provider supports this scope.");
        if(!_catalog.TryResolve(themeName,out var name)) return new(false,"ThemeUnavailable",Diagnostic:"This theme is not installed or compatible.");
        var candidate=(new HomeThemePreference(HomeThemePreference.CurrentSchemaVersion,scope,name,checked(expectedRevision+1),accessibility) { Appearance=appearance }).Validate();
        var receipt=await _backend.CompareExchangeAsync(scope,expectedRevision,candidate,token).ConfigureAwait(false);
        if(!receipt.Succeeded) return new(false,receipt.Code,Diagnostic:receipt.Diagnostic);
        if(receipt.Preference!=candidate) return new(false,"InvalidThemeReceipt",Diagnostic:"The preference provider did not confirm the exact requested revision and scope.");
        string? diagnostic=null;
        // The exact commit receipt is already observed. Cancellation, cache or
        // observer faults after this point cannot turn it into an apparent failed
        // write and induce a duplicate retry.
        try
        {
            if(!await _authority.CanAccessAsync(scope,false,CancellationToken.None).ConfigureAwait(false))
                diagnostic="Theme saved. The active preference scope changed before it could be displayed.";
            else
            {
                try { await _backend.WriteCacheAsync(candidate,token).ConfigureAwait(false); }
                catch(Exception error) { RecordObserverFailure(error); diagnostic="Theme saved, but its offline cache could not be refreshed."; }
                if(!PublishChanged(scope)) diagnostic="Theme saved. One of its observers needs to refresh.";
            }
        }
        catch(Exception error) { RecordObserverFailure(error); diagnostic="Theme saved. Its current-scope observation could not be refreshed."; }
        return new(true,"Succeeded",new(candidate,name,false,false,diagnostic),diagnostic);
    }
    private static HomeThemeOperationResult Denied()=>new(false,"PermissionDenied",Diagnostic:"This theme preference scope is not authorised.");
    private void Invalidated(object? sender,HomeThemeScope scope) { if(!_closing) PublishChanged(scope); }
    private bool PublishChanged(HomeThemeScope scope)
    {
        var succeeded=true;
        foreach(var subscriber in Changed?.GetInvocationList() ?? [])
        {
            try { ((EventHandler<HomeThemeScope>)subscriber)(this,scope); }
            catch(Exception error) { RecordObserverFailure(error); succeeded=false; }
        }
        return succeeded;
    }
    private void RecordObserverFailure(Exception error)
    { _observerErrors.Enqueue(error); while(_observerErrors.Count>16) _observerErrors.TryDequeue(out _); }
    private Task<HomeThemeOperationResult> Track(Func<Task<HomeThemeOperationResult>> operation)
    {
        var start=new TaskCompletionSource();Task<HomeThemeOperationResult> task;
        lock(_operationGate)
        {
            ObjectDisposedException.ThrowIf(_closing,this);_operations.RemoveWhere(value=>value.IsCompleted);
            task=Run(start.Task,operation);_operations.Add(task);
        }
        start.SetResult();return task;
    }
    private static async Task<HomeThemeOperationResult> Run(Task start,Func<Task<HomeThemeOperationResult>> operation)
    { await start.ConfigureAwait(false);return await operation().ConfigureAwait(false); }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource start;Task disposal;
        lock(_operationGate)
        {
            if(_disposal is not null)return new(_disposal);
            _closing=true;start=new();disposal=CloseAsync(start.Task,_operations.ToArray());_disposal=disposal;
        }
        start.SetResult();return new(disposal);
    }
    private async Task CloseAsync(Task start,Task[] pending)
    {
        await start.ConfigureAwait(false);
        var errors=new List<Exception>();var joined=Task.WhenAll(pending);
        try { await joined.ConfigureAwait(false); }
        catch(Exception error) {if(joined.Exception is { InnerExceptions.Count:>0 } aggregate)errors.AddRange(aggregate.InnerExceptions);else errors.Add(error);}
        try { _backend.Changed-=Invalidated; } catch(Exception error) {errors.Add(error);}
        Changed=null;_disposed=true;
        if(errors.Count==1)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if(errors.Count>1)throw new AggregateException("Home theme originals settled with errors.",errors);
    }
    public void Dispose()
    {
        lock(_operationGate)
        {
            if(_disposed)return;
            if(_operations.Any(task=>!task.IsCompleted))throw new InvalidOperationException("Await DisposeAsync while theme operations are active.");
            _closing=true;_disposed=true;_backend.Changed-=Invalidated;Changed=null;
        }
    }
}
