using System.Text.Json.Nodes;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Native renderer adapter over the existing preference file and canonical Home service; never an account-sync substitute.</summary>
internal sealed class NativeHomeThemePreferences : ICuiThemePreferenceSource, IAsyncDisposable
{
    private readonly HomeThemeScope _scope;
    private readonly HomeThemePreferenceService _service;
    private readonly FileBackend _backend;
    private readonly Action<Action> _dispatch;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _operations = [];
    private bool _closing, _refreshQueued, _refreshRunning;
    private int _notificationPosted;
    private Task? _close;
    public CuiThemePreferenceSnapshot Current { get; private set; } =
        new("Professional", 0, "presentation-only", false, new());
    public string? Diagnostic { get; private set; }
    public event EventHandler? Changed;
    public CuiThemePreferenceContext Context { get; }

    internal NativeHomeThemePreferences(string preferencePath, HomeThemeScope scope,
        Func<JsonObject, HomeThemeScope, HomeThemePreference> projectLegacy,
        Func<CancellationToken, ValueTask<bool>> authorise, Action<Action> dispatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferencePath);
        _scope = scope.Validate();
        if (scope.Scope != HomeDataScope.DeviceLocal) throw new NotSupportedException("Native account theme synchronization is not configured.");
        _dispatch = dispatch;
        _backend = new(preferencePath, scope, projectLegacy, authorise);
        _service = new(_backend, _backend, new ThemeCatalog());
        Context = new(this);
        _service.Changed += Invalidated;
    }

    public Task RefreshAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        _operations.RemoveAll(task => task.IsCompletedSuccessfully);
        if (_operations.Count >= 128)
            throw new InvalidOperationException("Home appearance observations must settle before more can be admitted.");
        var operation = RefreshCoreAsync(token);
        _operations.Add(operation);
        return operation;
    }
    private async Task RefreshCoreAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        await _refresh.WaitAsync(linked.Token);
        try
        {
            var result = await _service.GetAsync(_scope, linked.Token);
            if (_closing) return;
            if (!result.Succeeded || result.Value is null)
                throw new UnauthorizedAccessException(result.Diagnostic ?? result.Code);
            var resolved = result.Value;
            var value = resolved.Preference;
            var access = value.Accessibility;
            var next = new CuiThemePreferenceSnapshot(resolved.UsedFallback ? resolved.EffectiveThemeName : value.ThemeName,
                resolved.UsedFallback ? Math.Max(value.Revision, Current.Revision) : value.Revision,
                "device:" + value.Scope.OwnerId, resolved.FromCache,
                new() { ReduceMotion = access.ReduceMotion, HighContrast = access.HighContrast,
                    DisplayScale = access.DisplayScale, InterfaceFontFamilyOverride = access.InterfaceFontFamily,
                    CodeFontFamilyOverride = access.CodeFontFamily },
                value.Appearance is null ? null : Enum.Parse<CuiAppearance>(value.Appearance),
                value.Accent is { } accent ? new((CuiAccentOverrideMode)accent.Mode, accent.AllAppsColour, accent.IndividualApps) : null,
                resolved.UsedFallback ? null : value.CustomThemesJson);
            if (next.Revision < Current.Revision && next.ScopeId == Current.ScopeId) return;
            Diagnostic = resolved.Diagnostic;
            if (next == Current) return;
            Current = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _refresh.Release(); }
    }

    private void Invalidated(object? sender, HomeThemeScope scope)
    {
        if (scope != _scope || _closing || Interlocked.Exchange(ref _notificationPosted, 1) != 0) return;
        _dispatch(() =>
        {
            Interlocked.Exchange(ref _notificationPosted, 0);
            if (_closing) return;
            _refreshQueued = true;
            if (!_refreshRunning) { _refreshRunning = true; _ = ObserveRefreshAsync(); }
        });
    }
    private async Task ObserveRefreshAsync()
    {
        try { while (_refreshQueued && !_closing) { _refreshQueued = false; await RefreshAsync(); } }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception error)
        {
            // Retain the original failed operation, expose degradation, and preserve the last valid visual tree.
            Diagnostic = "Home appearance refresh failed; the last valid presentation is retained. " + error.GetType().Name;
            System.Diagnostics.Trace.TraceError("Native Home theme refresh failed: {0}", error);
        }
        finally { _refreshRunning = false; }
    }

    public ValueTask DisposeAsync()
    {
        if (_close is not null) return new(_close);
        _closing = true;
        _service.Changed -= Invalidated;
        _backend.Dispose();
        _stop.Cancel();
        return new(_close = CloseAsync());
    }
    private async Task CloseAsync()
    {
        var failures = new List<Exception>();
        foreach (var task in _operations)
            try { await task; } catch (OperationCanceledException) when (_closing) { } catch (Exception error) { failures.Add(error); }
        try { await _service.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
        Context.Dispose(); Changed = null; _stop.Dispose(); _refresh.Dispose();
        if (failures.Count != 0) throw new AggregateException("Native Home theme operations settled with failures.", failures);
    }

    private sealed class ThemeCatalog : IHomeCustomThemeCatalog
    {
        public bool TryResolve(string name, out string canonical)
        {
            var match = CuiThemeCatalog.All.FirstOrDefault(theme => string.Equals(theme.DisplayName, name, StringComparison.OrdinalIgnoreCase));
            canonical = match?.DisplayName ?? "Professional";
            return match is not null;
        }
        public bool TryResolve(string name, string library, out string canonical)
        {
            canonical = "Professional";
            try
            {
                var match = CuiCustomThemeContract.DeserializeLibrary(library).FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match is not null) { canonical = match.Name; return true; }
                return TryResolve(name, out canonical);
            }
            catch (ArgumentException) { return false; }
        }
    }

    private sealed class FileBackend : IHomeThemePreferenceBackend, IHomeThemeScopeAuthority, IDisposable
    {
        private readonly string _path;
        private readonly Func<JsonObject, HomeThemeScope, HomeThemePreference> _projectLegacy;
        private readonly HomeThemeScope _scope;
        private readonly Func<CancellationToken, ValueTask<bool>> _authorise;
        private readonly FileSystemWatcher _watcher;
        private bool _disposed;
        public event EventHandler<HomeThemeScope>? Changed;
        internal FileBackend(string path, HomeThemeScope scope, Func<JsonObject, HomeThemeScope, HomeThemePreference> projectLegacy, Func<CancellationToken, ValueTask<bool>> authorise)
        {
            _path = path; _scope = scope; _projectLegacy = projectLegacy; _authorise = authorise;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _watcher = new(Path.GetDirectoryName(path)!, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            _watcher.Changed += FileChanged; _watcher.Created += FileChanged; _watcher.Deleted += FileChanged; _watcher.Renamed += FileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        private void FileChanged(object sender, FileSystemEventArgs args) { if (!_disposed) Changed?.Invoke(this, _scope); }
        public bool Supports(HomeThemeScope scope) => !_disposed && scope == _scope && scope.Scope == HomeDataScope.DeviceLocal;
        public async ValueTask<bool> CanAccessAsync(HomeThemeScope scope, bool write, CancellationToken cancellationToken = default) =>
            Supports(scope) && await _authorise(cancellationToken).ConfigureAwait(false);
        public async Task<HomeThemeReadResult> ReadAsync(HomeThemeScope scope, CancellationToken cancellationToken = default)
        {
            if (!await CanAccessAsync(scope, false, cancellationToken)) return new(HomeThemeReadState.PermissionDenied);
            try { return new(HomeThemeReadState.Available, HomeNativePreferenceFile.Resolve(HomeNativePreferenceFile.Read(_path), scope, _projectLegacy)); }
            catch (UnauthorizedAccessException) { return new(HomeThemeReadState.PermissionDenied); }
            catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException or InvalidDataException or InvalidOperationException)
            { return new(HomeThemeReadState.InvalidData, Diagnostic: "Native Home preferences are incompatible; the file was preserved."); }
        }
        public Task<HomeThemeReadResult> ReadCacheAsync(HomeThemeScope scope, CancellationToken cancellationToken = default) => ReadAsync(scope, cancellationToken);
        public async Task<HomeThemeWriteResult> CompareExchangeAsync(HomeThemeScope scope, long expectedRevision, HomeThemePreference candidate, CancellationToken cancellationToken = default)
        {
            if (!await CanAccessAsync(scope, true, cancellationToken)) return new(false, "PermissionDenied");
            var result = await HomeNativePreferenceFile.CompareExchangeAsync(_path, scope, expectedRevision, candidate,
                _projectLegacy, token => CanAccessAsync(scope, true, token), cancellationToken);
            if (result.Succeeded) Changed?.Invoke(this, scope);
            return result;
        }
        public Task WriteCacheAsync(HomeThemePreference preference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Supports(preference.Scope)) throw new UnauthorizedAccessException();
            // DeviceLocal is already canonical local state; do not create or overwrite a competing cache.
            return Task.CompletedTask;
        }
        public void Dispose() { if (_disposed) return; _disposed = true; _watcher.Dispose(); Changed = null; }
    }
}
