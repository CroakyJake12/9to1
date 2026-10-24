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
    private sealed class Operation(CancellationToken token)
    { internal CancellationToken Token = token; internal Task Task = null!; }
    private readonly List<Operation> _operations = [];
    private readonly object _operationsGate = new();
    private readonly AsyncLocal<Operation?> _executing = new();
    private readonly List<Exception> _observerFailures = [];
    private readonly Dictionary<string, SettingTarget> _targets = new(StringComparer.Ordinal);
    private HomeThemePreference? _preference;
    private sealed record SettingTarget(NativeHomeThemePreferences Owner, long Revision, string Key, string Value);
    internal Task? OriginalClose => _close;
    internal sealed record AuthorityObservation(Task? Source, Exception Cause, CancellationToken RequestedToken);
    internal IReadOnlyList<AuthorityObservation> AuthorityObservations => _backend.AuthorityObservations;
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

    private Task<T> Track<T>(CancellationToken caller, Func<CancellationToken, Task<T>> source)
    {
        var start = new TaskCompletionSource();
        Operation operation;
        Task<T> actual;
        lock (_operationsGate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _operations.RemoveAll(item => item.Task.IsCompletedSuccessfully);
            if (_operations.Count >= 128) throw new InvalidOperationException("Home appearance operations must settle before more can be admitted.");
            operation = new(caller.CanBeCanceled ? caller : _stop.Token);
            async Task<T> Run()
            {
                await start.Task;
                var prior = _executing.Value; _executing.Value = operation;
                try { return await source(operation.Token); }
                finally { _executing.Value = prior; }
            }
            actual = Run(); operation.Task = actual; _operations.Add(operation);
        }
        start.SetResult(); return actual;
    }
    public Task RefreshAsync(CancellationToken token = default) => Track(token, async current =>
    { await RefreshCoreAsync(current); return true; });


    /// <summary>One original appearance write and its later presentation observations. Only an
    /// actual successful Set receipt confirms the write; later callbacks cannot replay it or
    /// turn saved bytes into an unconfirmed failed-write outcome. This is no authority grant.</summary>
    internal sealed class SettingsWriteOutcome(NativeHomeThemePreferences owner)
    {
        internal sealed record PostCommitObservation(Task? Source, Exception Cause);
        private readonly object _gate = new();
        private readonly List<PostCommitObservation> _observations = [];
        private int _attempted;
        private HomeThemeOperationResult? _confirmed;
        internal HomeThemeOperationResult? ConfirmedWrite { get { lock (_gate) return _confirmed; } }
        internal IReadOnlyList<PostCommitObservation> PostCommitObservations
        { get { lock (_gate) return _observations.ToArray(); } }
        internal const string SavedObservationDiagnostic =
            "Appearance saved. Reopen Home to refresh the current profile or view.";

        internal async Task<HomeThemeOperationResult> ApplyAsync(object? issuedTarget, CancellationToken token = default)
        {
            if (Interlocked.CompareExchange(ref _attempted, 1, 0) != 0)
                throw new InvalidOperationException("This original appearance write was already attempted; inspect its receipt instead of replaying it.");
            var result = await owner.ApplySettingsAsync(issuedTarget, token);
            if (result.Succeeded)
            {
                if (result.Value is not { } saved || saved.Preference.Scope != owner._scope)
                    throw new InvalidDataException("The original appearance write returned no exact owning scope receipt.");
                lock (_gate) _confirmed = result;
            }
            return result;
        }

        internal bool RetainPostCommitFailure(Task? source, Exception cause)
        {
            ArgumentNullException.ThrowIfNull(cause);
            lock (_gate)
            {
                if (_confirmed is null) return false;
                _observations.Add(new(source, cause));
                return true;
            }
        }

        /// <summary>Joins the SAME callback Task, preserving its exact failure separately from
        /// the confirmed write. An unconfirmed write may never use this observation boundary.</summary>
        internal async Task ObserveAfterWriteAsync(Func<Task> observe)
        {
            ArgumentNullException.ThrowIfNull(observe);
            if (ConfirmedWrite is null) throw new InvalidOperationException("Confirm the original write before observing presentation.");
            Task? actual = null;
            try
            {
                actual = observe() ?? throw new InvalidOperationException("The original appearance observation returned no Task.");
                await actual;
            }
            catch (Exception cause)
            {
                if (!RetainPostCommitFailure(actual, cause)) throw;
            }
        }
    }

    internal SettingsWriteOutcome CreateSettingsWriteOutcome() => new(this);

    internal IReadOnlyDictionary<string, object?> SettingsBindings()
    {
        lock (_operationsGate)
        {
        var value = _preference;
        object Target(string key, string next)
        {
            var cache = key + "\0" + next;
            if (!_targets.TryGetValue(cache, out var target) || target.Revision != value!.Revision)
                _targets[cache] = target = new(this, value!.Revision, key, next);
            return target;
        }
        Dictionary<string, object?> Option(string name, string key, string next, bool selected) => new()
        { ["Name"] = name, ["Key"] = key is "reduce-motion" or "high-contrast" ? key : key + ":" + next,
          ["Target"] = Target(key, next), ["IsSelected"] = selected };
        var available = !_closing && value is not null;
        var names = CuiThemeCatalog.All.Select(theme => theme.DisplayName).ToList();
        if (value?.CustomThemesJson is { } custom)
            try { names.AddRange(CuiCustomThemeContract.DeserializeLibrary(custom).Select(theme => theme.Name)); }
            catch (ArgumentException) { }
        return new Dictionary<string, object?>
        {
            ["HomeAppearanceAvailable"] = available,
            ["HomeAppearanceScope"] = "Applies to this device profile.",
            ["HomeAppearanceStatus"] = Diagnostic ?? (value is null ? "Appearance has not been loaded." : "Saved theme: " + value.ThemeName),
            ["HomeThemeChoices"] = available ? names.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => Option(name, "theme", name, name == value!.ThemeName)).ToArray() : Array.Empty<Dictionary<string, object?>>(),
            ["HomeAppearanceChoices"] = available ? new[] { "SuperBright", "Bright", "Dark", "SuperDark" }
                .Select(name => Option(name is "SuperBright" ? "Super bright" : name is "SuperDark" ? "Super dark" : name, "appearance", name, name == value!.Appearance)).ToArray() : Array.Empty<Dictionary<string, object?>>(),
            ["HomeAccessibilityChoices"] = available ? new[]
            {
                Option("Reduce motion", "reduce-motion", (!value!.Accessibility.ReduceMotion).ToString(), value.Accessibility.ReduceMotion),
                Option("High contrast", "high-contrast", (!value.Accessibility.HighContrast).ToString(), value.Accessibility.HighContrast)
            } : Array.Empty<Dictionary<string, object?>>()
        };
        }
    }

    internal Task<HomeThemeOperationResult> ApplySettingsAsync(object? issuedTarget, CancellationToken token = default)
    {
        SettingTarget target;
        lock (_operationsGate)
        {
            if (issuedTarget is not SettingTarget observed || !ReferenceEquals(observed.Owner, this) ||
                !_targets.Values.Any(current => ReferenceEquals(current, observed)) || _preference is null ||
                observed.Revision != _preference.Revision)
                return Task.FromResult(new HomeThemeOperationResult(false, "SettingsIntentRetired", Diagnostic: "Appearance changed. Refresh before choosing again."));
            target = observed;
        }
        return Track<HomeThemeOperationResult>(token, async current =>
        {
            HomeThemePreference? prior;
            lock (_operationsGate) prior = _preference;
            if (prior is null || target.Revision != prior.Revision)
                return new(false, "RevisionConflict", Diagnostic: "Appearance changed. Refresh before choosing again.");
            var accessibility = target.Key switch
            {
                "reduce-motion" => prior.Accessibility with { ReduceMotion = bool.Parse(target.Value) },
                "high-contrast" => prior.Accessibility with { HighContrast = bool.Parse(target.Value) },
                _ => prior.Accessibility
            };
            var actual = _service.SetAsync(_scope, target.Key == "theme" ? target.Value : prior.ThemeName,
                accessibility, target.Revision, current,
                appearance: target.Key == "appearance" ? target.Value : prior.Appearance,
                accent: prior.Accent, customThemesJson: prior.CustomThemesJson);
            HomeThemeOperationResult result;
            try { result = await actual; }
            catch (OperationCanceledException error) when (_closing && actual.IsCanceled &&
                current == _stop.Token && error.CancellationToken == current)
            { return new(false, "SettingsRetired", Diagnostic: "Appearance closed before the operation completed."); }
            // The exact CAS receipt is authoritative. A later refresh/cancellation must not
            // replace a confirmed save with an invented failed-write outcome.
            if (result.Succeeded && result.Value is { } saved)
            {
                Diagnostic = result.Diagnostic;
                lock (_operationsGate)
                    if (_preference is null || saved.Preference.Revision >= _preference.Revision)
                    { _preference = saved.Preference; _targets.Clear(); }
            }
            else Diagnostic = result.Diagnostic ?? (result.Code switch
            {
                "RevisionConflict" or "SettingsIntentRetired" => "Appearance changed in another window. Refresh before choosing again.",
                "PermissionDenied" => "Reopen Home for the current profile.",
                _ => "Appearance could not be saved. Refresh and try again."
            });
            return result;
        });
    }

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        await _refresh.WaitAsync(token);
        try
        {
            var result = await _service.GetAsync(_scope, token);
            if (_closing) return;
            if (!result.Succeeded || result.Value is null)
                throw new UnauthorizedAccessException(result.Diagnostic ?? result.Code);
            // Recheck current authority/scope after the asynchronous read and before
            // publishing; a revoked actor must not receive the observed value.
            if (!await _backend.CanAccessAsync(_scope, false, token).ConfigureAwait(false)) return;
            var resolved = result.Value;
            var value = resolved.Preference;
            lock (_operationsGate)
            {
                if (_preference is { } known && value.Revision < known.Revision && !resolved.UsedFallback) return;
                if (_preference is null || value.Revision >= _preference.Revision)
                {
                    if (_preference != value) _targets.Clear();
                    _preference = value;
                }
            }
            var access = value.Accessibility;
            var next = new CuiThemePreferenceSnapshot(resolved.UsedFallback ? resolved.EffectiveThemeName : value.ThemeName,
                resolved.UsedFallback ? Math.Max(value.Revision, Current.Revision) : value.Revision,
                "device:" + value.Scope.OwnerId, resolved.FromCache,
                new() { ReduceMotion = access.ReduceMotion, HighContrast = access.HighContrast,
                    DisplayScale = access.DisplayScale, InterfaceFontFamilyOverride = access.InterfaceFontFamily,
                    CodeFontFamilyOverride = access.CodeFontFamily },
                value.Appearance is null ? null : Enum.Parse<CuiAppearance>(value.Appearance),
                ToCuiAccentPreference(value.Accent),
                resolved.UsedFallback ? null : value.CustomThemesJson);
            if (next.Revision < Current.Revision && next.ScopeId == Current.ScopeId) return;
            Diagnostic = resolved.Diagnostic;
            if (next == Current) return;
            Current = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _refresh.Release(); }
    }

    private static CuiAccentPreference? ToCuiAccentPreference(HomeThemeAccentPreference? accent)
    {
        if (accent is null) return null;
        IReadOnlyDictionary<string, string> choices = accent.IndividualApps;
        if (accent.Mode == HomeThemeAccentOverrideMode.IndividualApps)
        {
            // Project Home's effective per-app fallback into the transient CUI
            // observation. This is not a second store: explicit Home values win,
            // and the synthesized entries are never written back to preferences.
            var effective = accent.IndividualApps.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var app in CuiAppAccentCatalog.Defaults)
            {
                if (effective.Count == HomeThemeAccentPreference.MaximumIndividualAppChoices) break;
                effective.TryAdd(app.AppId, HomeThemeAccentPreference.InitialIndividualAppColour);
            }
            choices = effective;
        }
        return new((CuiAccentOverrideMode)accent.Mode, accent.AllAppsColour, choices);
    }

    private void Invalidated(object? sender, HomeThemeScope scope)
    {
        if (scope != _scope || _closing || Interlocked.Exchange(ref _notificationPosted, 1) != 0) return;
        _dispatch(() =>
        {
            Interlocked.Exchange(ref _notificationPosted, 0);
            if (_closing) return;
            _refreshQueued = true;
            if (!_refreshRunning)
            {
                _refreshRunning = true;
                try { _ = Track(_stop.Token, async token => { await ObserveRefreshAsync(); return true; }); }
                catch (ObjectDisposedException) when (_closing) { _refreshRunning = false; }
                catch (Exception error)
                {
                    _refreshRunning = false;
                    lock (_operationsGate) _observerFailures.Add(error);
                    Diagnostic = "Home appearance refresh could not be started; the saved preference is preserved.";
                }
            }
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
        if (_executing.Value is { Task.IsCompleted: false })
            throw new InvalidOperationException("An appearance operation cannot join its own source close.");
        var start = new TaskCompletionSource();
        Task close;
        lock (_operationsGate)
        {
            if (_close is not null) return new(_close);
            _closing = true;
            close = _close = CloseAsync(start.Task, _operations.ToArray());
        }
        start.SetResult(); return new(close);
    }
    private async Task CloseAsync(Task start, Operation[] operations)
    {
        await start;
        var failures = new List<Exception>();
        lock (_operationsGate) failures.AddRange(_observerFailures);
        try { _service.Changed -= Invalidated; } catch (Exception error) { failures.Add(error); }
        try { _backend.Dispose(); } catch (Exception error) { failures.Add(error); }
        try { _stop.Cancel(); } catch (Exception error) { failures.Add(error); }
        foreach (var operation in operations)
            try { await operation.Task; }
            catch (OperationCanceledException error) when (_closing && operation.Task.IsCanceled &&
                operation.Token == _stop.Token && _stop.IsCancellationRequested && error.CancellationToken == _stop.Token) { }
            catch (Exception error) { failures.Add(error); }
        // Every private service call is a joined child of one of the exact operations above.
        // Dispose synchronously only after that closure is terminal; failures remain retained here.
        try { _service.Dispose(); } catch (Exception error) { failures.Add(error); }
        try { Context.Dispose(); } catch (Exception error) { failures.Add(error); }
        Changed = null; _stop.Dispose(); _refresh.Dispose();
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
        private readonly object _observationsGate = new();
        private readonly List<AuthorityObservation> _observations = [];
        internal IReadOnlyList<AuthorityObservation> AuthorityObservations
        { get { lock (_observationsGate) return _observations.ToArray(); } }
        private void RetainAuthority(Task? source, Exception cause, CancellationToken token)
        { lock (_observationsGate) _observations.Add(new(source, cause, token)); }
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
        public async ValueTask<bool> CanAccessAsync(HomeThemeScope scope, bool write, CancellationToken cancellationToken = default)
        {
            if (!Supports(scope)) return false;
            Task<bool> actual;
            try { actual = _authorise(cancellationToken).AsTask(); }
            catch (OperationCanceledException error)
            {
                RetainAuthority(null, error, cancellationToken);
                throw new AggregateException("Home appearance authority callback threw before returning its source.", error);
            }
            try { return await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                RetainAuthority(actual, error, cancellationToken);
                if (error is OperationCanceledException && actual.IsFaulted)
                {
                    // Preserve the original faulted Task, requested token and exact exception chain.
                    // Only this faulted-OCE case is wrapped so an async boundary cannot relabel it
                    // as cancellation; genuine raw cancellation and every other fault are unchanged.
                    throw new AggregateException("Home appearance authority source faulted.", actual.Exception!.InnerExceptions);
                }
                throw;
            }
        }
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
