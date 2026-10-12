using System.Collections.Frozen;
using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace NineToOne.Web;

/// <summary>Borrowed app-header presentation of Home-owned accent preferences.</summary>
internal sealed class BrowserAppAccentSettings : ICuiWritableBindingContext, ICuiLifetimeAwareActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    // Persist stable canonical anchors; a later appearance change must not silently
    // turn an explicitly chosen colour into a different stored preference.
    private static readonly FrozenDictionary<string, string> Colours = CuiAccentCatalog.Colours.ToFrozenDictionary(
        colour => CuiAccentCatalog.Name(colour), colour =>
        {
            var value = CuiAccentCatalog.Resolve(colour, CuiAppearance.Bright).Primary;
            return $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        }, StringComparer.Ordinal);
    private readonly ICuiBindingContext _bindings;
    private readonly ICuiActionDispatcher _actions;
    private readonly IBrowserHomeThemePreferences _preferences;
    private readonly string _appId;
    private bool _busy, _disposed;
    private string? _status;
    private string? _observerFailure;
    private bool IndividualMode => _preferences.Current.EffectiveAccent.Mode == CuiAccentOverrideMode.IndividualApps;
    private bool CanChange => !_disposed && !_busy && IndividualMode && _preferences.CanChange;
    private string? EffectiveColour => _preferences.Current.EffectiveAccent.ResolveColour(_appId) ?? CuiAppAccentCatalog.Find(_appId)?.Colour;
    public event PropertyChangedEventHandler? PropertyChanged;

    public BrowserAppAccentSettings(ICuiBindingContext bindings, ICuiActionDispatcher actions,
        IBrowserHomeThemePreferences preferences, string appId)
    {
        ArgumentNullException.ThrowIfNull(bindings); ArgumentNullException.ThrowIfNull(actions); ArgumentNullException.ThrowIfNull(preferences);
        _ = CuiAccentPreference.Default.WithAppColour(appId, null); // Validate the supplied stable owner identity.
        _bindings = bindings; _actions = actions; _preferences = preferences; _appId = appId;
        if (bindings is INotifyPropertyChanged observable) observable.PropertyChanged += OwnerChanged;
        preferences.Changed += PreferenceChanged;
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "ShowAppAccentSettings" => !_disposed && IndividualMode,
            "AppAccentCanChange" => CanChange,
            "AppAccentCanReset" => CanChange && _preferences.Current.EffectiveAccent.IndividualApps.ContainsKey(_appId),
            "AppAccentStatus" => (_status ?? _preferences.Diagnostic ?? (EffectiveColour is { } colour
                ? "App accent colour: " + colour : "App accent colour: theme default"))
                + (_observerFailure is null ? string.Empty : " Reopen the app to refresh its controls."),
            _ => null
        };
        if (value is not null) return true;
        if (path.StartsWith("AppAccentSwatch", StringComparison.Ordinal) && Colours.TryGetValue(path[15..], out var swatch))
        { value = "color:" + swatch; return true; }
        if (path.StartsWith("AppAccentIs", StringComparison.Ordinal) && Colours.TryGetValue(path[11..], out var option))
        { value = string.Equals(EffectiveColour, option, StringComparison.OrdinalIgnoreCase); return true; }
        return _bindings.TryGetValue(path, out value);
    }
    public bool TrySetValue(string path, object? value) => !_disposed && _bindings is ICuiWritableBindingContext writable && writable.TrySetValue(path, value);
    private static bool Owns(string command) => command.StartsWith("AppAccent", StringComparison.Ordinal);
    public bool? IsActionAvailable(string command) => _disposed ? false : !Owns(command)
        ? (_actions as ICuiActionAvailability)?.IsActionAvailable(command)
        : command == "AppAccentReset" ? CanChange && _preferences.Current.EffectiveAccent.IndividualApps.ContainsKey(_appId)
        : Colours.ContainsKey(command[9..]) && CanChange;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        DispatchWithLifetimeAsync(command, parameter, new(default, cancellationToken));
    public async ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
    {
        if (_disposed) return;
        if (!Owns(command))
        {
            if (_actions is ICuiLifetimeAwareActionDispatcher tracked) await tracked.DispatchWithLifetimeAsync(command, parameter, lifetime);
            else
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.CallerCancellation, lifetime.ViewCancellation);
                await _actions.DispatchAsync(command, parameter, linked.Token);
            }
            return;
        }
        lifetime.CallerCancellation.ThrowIfCancellationRequested(); lifetime.ViewCancellation.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) return;
        var original = _preferences.Current;
        var colour = command == "AppAccentReset" ? null : Colours[command[9..]];
        var next = original.EffectiveAccent.WithAppColour(_appId, colour);
        if (next.Equals(original.EffectiveAccent)) return;
        _busy = true; _status = null; _observerFailure = null; Publish();
        try
        {
            // Observer callbacks may synchronously change mode/scope or retire this
            // view before admission. Never submit a candidate from that stale state.
            if (_disposed || !IndividualMode || !_preferences.CanChange || _preferences.Current.ScopeId != original.ScopeId
                || _preferences.Current.Revision != original.Revision || !_preferences.Current.EffectiveAccent.Equals(original.EffectiveAccent)) return;
            lifetime.CallerCancellation.ThrowIfCancellationRequested(); lifetime.ViewCancellation.ThrowIfCancellationRequested();
            // Once admitted, Home retains the write across view retirement, exactly
            // like the existing appearance controller. Its receipt owns the outcome.
            var result = await _preferences.SetAsync(original.Name, token: lifetime.CallerCancellation, accent: next);
            _status = result.Succeeded ? result.Diagnostic ?? "App colour saved." : result.Diagnostic ?? "App colour could not be saved. Try again.";
        }
        finally { _busy = false; Publish(); }
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args) => Publish(args);
    private void PreferenceChanged(object? sender, EventArgs args) { _status = null; Publish(); }
    private void Publish(PropertyChangedEventArgs? args = null)
    {
        if (_disposed) return;
        foreach (var observer in PropertyChanged?.GetInvocationList() ?? [])
            try { ((PropertyChangedEventHandler)observer)(this, args ?? new(null)); }
            catch (Exception error) { _observerFailure ??= error.GetType().Name; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_bindings is INotifyPropertyChanged observable) observable.PropertyChanged -= OwnerChanged;
        _preferences.Changed -= PreferenceChanged;
        PropertyChanged = null;
    }
}
