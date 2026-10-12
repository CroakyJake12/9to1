namespace CakeOS.Cui.Themes;

/// <summary>Observation issued by the owning Home settings service, including its authorised offline cache.</summary>
public sealed record CuiThemePreferenceSnapshot(string Name, long Revision, string ScopeId, bool FromCache,
    CuiAccessibilitySettings Accessibility, CuiAppearance? Appearance = null);

/// <summary>Home owns storage, authorisation and synchronisation. CUI only borrows this typed observation.</summary>
public interface ICuiThemePreferenceSource
{
    CuiThemePreferenceSnapshot Current { get; }
    event EventHandler? Changed;
}

public sealed record CuiThemeResolution(CuiResolvedTheme Theme, string RequestedName, string? Diagnostic, bool UsedFallback,
    CuiAccessibilitySettings Accessibility, long Revision = 0, string? ScopeId = null, bool FromCache = false,
    CuiAppearance? Appearance = null);

/// <summary>Per-host, nonpersistent interpretation of one authorised Home preference source.</summary>
public sealed class CuiThemePreferenceContext : IDisposable
{
    private readonly ICuiThemePreferenceSource _source;
    private readonly CuiThemeDefinitionCatalog _catalog;
    private CuiThemePreferenceSnapshot _lastSnapshot;
    private bool _disposed;
    private readonly HashSet<EventHandler> _failedObservers = [];
    public CuiThemeResolution Current { get; private set; }
    public long ObserverFailureCount { get; private set; }
    public string? ObserverDiagnostic { get; private set; }
    public event EventHandler? Changed;

    public CuiThemePreferenceContext(ICuiThemePreferenceSource source, CuiThemeDefinitionCatalog? catalog = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _catalog = catalog ?? new(); _lastSnapshot = source.Current; Current = Resolve(_lastSnapshot);
        source.Changed += SourceChanged;
    }
    public CuiThemeResolution ResolveScope(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("user_preference", StringComparison.OrdinalIgnoreCase) || name.Equals("Default", StringComparison.OrdinalIgnoreCase)) return Current;
        return Resolve(new(name, Current.Revision, Current.ScopeId ?? string.Empty, Current.FromCache, Current.Accessibility, Current.Appearance));
    }
    private CuiThemeResolution Resolve(CuiThemePreferenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Revision < 0 || string.IsNullOrWhiteSpace(snapshot.ScopeId))
            throw new ArgumentException("A Home theme observation requires a valid revision and explicit scope.", nameof(snapshot));
        var accessibility = snapshot.Accessibility.Validate();
        if (snapshot.Appearance is { } appearance && !Enum.IsDefined(appearance))
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Unknown CUI appearance.");
        _ = CuiTypography.ResolveInterfaceFontFamily(accessibility.InterfaceFontFamilyOverride);
        _ = CuiTypography.ResolveCodeFontFamily(accessibility.CodeFontFamilyOverride);
        var name = snapshot.Name?.Trim() ?? string.Empty;
        if (_catalog.TryResolve(name, out var theme) && theme is not null)
            return new(theme, name, null, false, accessibility, snapshot.Revision, snapshot.ScopeId, snapshot.FromCache, snapshot.Appearance);
        return new(_catalog.Resolve("Professional"), name,
            $"The preferred CUI theme '{name}' is unavailable or incompatible. Professional is being used.", true,
            accessibility, snapshot.Revision, snapshot.ScopeId, snapshot.FromCache, snapshot.Appearance);
    }
    private void SourceChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        var snapshot = _source.Current;
        var next = Resolve(snapshot);
        // A new scope may legitimately restart revision numbering; never merge scopes.
        if (next.ScopeId == Current.ScopeId && next.Revision < Current.Revision) return;
        var unchanged = snapshot == _lastSnapshot && next.Theme.Name == Current.Theme.Name && next.UsedFallback == Current.UsedFallback;
        if (unchanged && _failedObservers.Count == 0) return;
        Current = next; _lastSnapshot = snapshot;
        var observers = Changed?.GetInvocationList().Cast<EventHandler>().ToArray() ?? [];
        _failedObservers.IntersectWith(observers);
        foreach (var observer in observers)
        {
            if (_disposed) break;
            if (unchanged && !_failedObservers.Contains(observer)) continue;
            try
            {
                observer(this, EventArgs.Empty);
                _failedObservers.Remove(observer);
            }
            catch (Exception)
            {
                _failedObservers.Add(observer);
                if (ObserverFailureCount < long.MaxValue) ++ObserverFailureCount;
                // A subscriber is not the settings owner. Its failure cannot
                // undo a committed preference or prevent other views updating.
                // Retain only a bounded code, never exception/user content.
                ObserverDiagnostic = "CUI_THEME_OBSERVER_FAILED";
            }
        }
        if (_failedObservers.Count == 0) ObserverDiagnostic = null;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _source.Changed -= SourceChanged; _failedObservers.Clear(); Changed = null; }
}
