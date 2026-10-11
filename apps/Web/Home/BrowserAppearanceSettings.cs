using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;

namespace NineToOne.Web;

internal interface IBrowserHomeThemePreferences : ICuiThemePreferenceSource, IAsyncDisposable
{
    CuiThemeDefinitionCatalog Catalog { get; }
    string? Diagnostic { get; }
    bool CanChange { get; }
    Task<HomeThemeOperationResult> SetAsync(string name, CuiAccessibilitySettings? accessibility = null, CancellationToken token = default);
    Task RefreshAsync(CancellationToken token = default);
}

/// <summary>Borrowed Settings presentation of the single Home-owned preference service.</summary>
internal sealed class BrowserAppearanceSettings : ICuiWritableBindingContext, ICuiLifetimeAwareActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly ICuiBindingContext _bindings;
    private readonly ICuiActionDispatcher _actions;
    private readonly IBrowserHomeThemePreferences _preferences;
    private bool _busy, _disposed;
    private string? _operationStatus;
    private Exception? _presentationFailure;
    private static readonly Dictionary<string, string> ThemeCommands = new(StringComparer.Ordinal)
    {
        ["ThemeProfessional"] = "Professional", ["ThemeGlow"] = "Glow", ["ThemePlayful"] = "Playful",
        ["ThemeBubble"] = "Bubble", ["ThemeCinematic"] = "Cinematic"
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    public BrowserAppearanceSettings(ICuiBindingContext bindings, ICuiActionDispatcher actions, IBrowserHomeThemePreferences preferences)
    {
        _bindings = bindings; _actions = actions; _preferences = preferences;
        if (bindings is INotifyPropertyChanged observable) observable.PropertyChanged += OwnerChanged;
        preferences.Changed += PreferenceChanged;
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "ShowAppearanceSettings" => true,
            "AppearanceThemeName" => _preferences.Current.Name,
            "AppearanceScope" => "This browser · device-local appearance. Account theme synchronisation is not connected.",
            "AppearanceStatus" => (_operationStatus ?? _preferences.Diagnostic ?? "Your apps use the same Home appearance preference.") +
                (_presentationFailure is null ? "" : " Some controls could not refresh; reopen Settings to refresh the view."),
            "AppearanceCanChange" => !_disposed && !_busy && _preferences.CanChange,
            _ => null
        };
        if (value is not null) return true;
        if (path.StartsWith("AppearanceIs", StringComparison.Ordinal))
        {
            value = _preferences.Current.Name.Equals(path[12..], StringComparison.OrdinalIgnoreCase);
            return true;
        }
        return _bindings.TryGetValue(path, out value);
    }
    public bool TrySetValue(string path, object? value) => !_disposed && _bindings is ICuiWritableBindingContext writable && writable.TrySetValue(path, value);
    public bool? IsActionAvailable(string command) => _disposed ? false : command == "ThemeRefresh" ? !_busy :
        ThemeCommands.ContainsKey(command) ? !_busy && _preferences.CanChange : (_actions as ICuiActionAvailability)?.IsActionAvailable(command);
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        DispatchWithLifetimeAsync(command, parameter, new(default, cancellationToken));
    public async ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
    {
        if (_disposed) return;
        if (command != "ThemeRefresh" && !ThemeCommands.ContainsKey(command))
        {
            if (_actions is ICuiLifetimeAwareActionDispatcher tracked)
                await tracked.DispatchWithLifetimeAsync(command, parameter, lifetime);
            else
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.CallerCancellation, lifetime.ViewCancellation);
                await _actions.DispatchAsync(command, parameter, linked.Token);
            }
            return;
        }
        lifetime.CallerCancellation.ThrowIfCancellationRequested();
        lifetime.ViewCancellation.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) return;
        _busy = true; _operationStatus = null; _presentationFailure = null; Publish();
        try
        {
            if (command == "ThemeRefresh")
            {
                await _preferences.RefreshAsync(lifetime.CallerCancellation);
                _operationStatus = null;
            }
            else
            {
                var result = await _preferences.SetAsync(ThemeCommands[command], token: lifetime.CallerCancellation);
                _operationStatus = result.Succeeded ? result.Diagnostic ?? "Appearance saved on this browser." : result.Diagnostic ?? "Appearance could not be saved. Try again.";
            }
        }
        finally { _busy = false; Publish(); }
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args) => Publish(args);
    private void PreferenceChanged(object? sender, EventArgs args) { _operationStatus = null; Publish(); }
    private void Publish(PropertyChangedEventArgs? args = null)
    {
        if (_disposed) return;
        foreach (var observer in PropertyChanged?.GetInvocationList() ?? [])
            try { ((PropertyChangedEventHandler)observer)(this, args ?? new(null)); }
            catch (Exception error) { _presentationFailure ??= error; }
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
