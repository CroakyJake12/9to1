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
    string ScopeDescription => "This browser · device-local appearance. Account theme synchronisation is not connected.";
    string SavedDescription => "Appearance saved on this browser.";
    Task<HomeThemeOperationResult> SetAsync(string name, CuiAccessibilitySettings? accessibility = null, CancellationToken token = default, CuiAppearance? appearance = null, CuiAccentPreference? accent = null);
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
    private readonly Action? _openThemeCreator;
    private string? _accentDraft;
    private string? _draftScope;
    private sealed record AccentChoice(string Name, string Colour, string AccessibleName) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch { "Name" => Name, "Colour" => Colour, "AccessibleName" => AccessibleName, _ => null };
            return value is not null;
        }
    }
    private static readonly IReadOnlyList<AccentChoice> AccentChoices = CuiAccentCatalog.Colours.Select(colour =>
    {
        var anchor = CuiAccentCatalog.Resolve(colour, CuiAppearance.Bright).Primary;
        var name = CuiAccentCatalog.Name(colour);
        return new AccentChoice(name, $"#{anchor.R:X2}{anchor.G:X2}{anchor.B:X2}", $"Use {name} accent");
    }).ToArray();
    private string? _operationStatus;
    private Exception? _presentationFailure;
    private static readonly Dictionary<string, string> ThemeCommands = new(StringComparer.Ordinal)
    {
        ["ThemeProfessional"] = "Professional", ["ThemeGlow"] = "Glow", ["ThemePlayful"] = "Playful",
        ["ThemeBubble"] = "Bubble", ["ThemeCinematic"] = "Cinematic"
    };
    private static readonly Dictionary<string, CuiAppearance> AppearanceCommands = new(StringComparer.Ordinal)
    {
        ["AppearanceSuperBright"] = CuiAppearance.SuperBright, ["AppearanceBright"] = CuiAppearance.Bright,
        ["AppearanceDark"] = CuiAppearance.Dark, ["AppearanceSuperDark"] = CuiAppearance.SuperDark
    };
    private CuiAppearance EffectiveAppearance => _preferences.Current.Appearance ?? CuiThemeScopeApplier.DetectAppearance();
    public event PropertyChangedEventHandler? PropertyChanged;
    public BrowserAppearanceSettings(ICuiBindingContext bindings, ICuiActionDispatcher actions, IBrowserHomeThemePreferences preferences, Action? openThemeCreator = null)
    {
        _bindings = bindings; _actions = actions; _preferences = preferences; _openThemeCreator = openThemeCreator;
        if (bindings is INotifyPropertyChanged observable) observable.PropertyChanged += OwnerChanged;
        preferences.Changed += PreferenceChanged;
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "ShowAppearanceSettings" => true,
            "AppearanceThemeName" => _preferences.Current.Name,
            "AppearanceModeName" => EffectiveAppearance switch { CuiAppearance.SuperBright => "Super Bright", CuiAppearance.SuperDark => "Super Dark", var mode => mode.ToString() },
            "AppearanceScope" => _preferences.ScopeDescription,
            "AppearanceStatus" => (_operationStatus ?? _preferences.Diagnostic ?? "Your apps use the same Home appearance preference.") +
                (_presentationFailure is null ? "" : " Some controls could not refresh; reopen Settings to refresh the view."),
            "AppearanceCanChange" => !_disposed && !_busy && _preferences.CanChange,
            "AppearanceAccentModeName" => _preferences.Current.EffectiveAccent.Mode switch { CuiAccentOverrideMode.AllApps => "All Apps", CuiAccentOverrideMode.IndividualApps => "Individual Apps", _ => "None" },
            "AppearanceAccentAllApps" => _preferences.Current.EffectiveAccent.Mode == CuiAccentOverrideMode.AllApps,
            "AppearanceAccentIndividualApps" => _preferences.Current.EffectiveAccent.Mode == CuiAccentOverrideMode.IndividualApps,
            "AppearanceAccentChoices" => AccentChoices,
            "AppearanceAccentColour" => _accentDraft ?? _preferences.Current.EffectiveAccent.AllAppsColour ?? CuiAccentPreference.InitialAllAppsColour,
            "AppearanceCanApplyAccent" => CanApplyAccent,
            "AppearanceCanCreateTheme" => !_disposed && !_busy && _preferences.CanChange && _openThemeCreator is not null,
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
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed) return false;
        if (path == "AppearanceAccentColour")
        {
            if (_busy || !_preferences.CanChange || _preferences.Current.EffectiveAccent.Mode != CuiAccentOverrideMode.AllApps) return false;
            _accentDraft = Convert.ToString(value) ?? string.Empty; _draftScope = _preferences.Current.ScopeId;
            Publish(new("AppearanceCanApplyAccent")); return true;
        }
        return _bindings is ICuiWritableBindingContext writable && writable.TrySetValue(path, value);
    }
    private bool CanApplyAccent
    {
        get
        {
            if (_disposed || _busy || !_preferences.CanChange || _preferences.Current.EffectiveAccent.Mode != CuiAccentOverrideMode.AllApps ||
                _draftScope is not null && _draftScope != _preferences.Current.ScopeId) return false;
            try { _ = CuiAccentPreference.CanonicalColour(_accentDraft ?? _preferences.Current.EffectiveAccent.AllAppsColour ?? CuiAccentPreference.InitialAllAppsColour); return true; }
            catch (ArgumentException) { return false; }
        }
    }
    private static bool IsAccentCommand(string command) => command is "AppearanceAccentNone" or "AppearanceAccentAllApps" or "AppearanceAccentIndividualApps" or "AppearanceAccentColour" or "AppearanceAccentApply";
    public bool? IsActionAvailable(string command) => _disposed ? false : command == "ThemeRefresh" ? !_busy :
        command == "CreateTheme" ? !_busy && _preferences.CanChange && _openThemeCreator is not null :
        command == "AppearanceAccentApply" ? CanApplyAccent :
        ThemeCommands.ContainsKey(command) || AppearanceCommands.ContainsKey(command) || IsAccentCommand(command) ? !_busy && _preferences.CanChange : (_actions as ICuiActionAvailability)?.IsActionAvailable(command);
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        DispatchWithLifetimeAsync(command, parameter, new(default, cancellationToken));
    public async ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
    {
        if (_disposed) return;
        if (command != "ThemeRefresh" && command != "CreateTheme" && !IsAccentCommand(command) && !ThemeCommands.ContainsKey(command) && !AppearanceCommands.ContainsKey(command))
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
        if (command == "CreateTheme") { _openThemeCreator?.Invoke(); return; }
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
                var name = ThemeCommands.GetValueOrDefault(command) ?? _preferences.Current.Name;
                var appearance = AppearanceCommands.TryGetValue(command, out var selected) ? selected : _preferences.Current.Appearance;
                var accent = _preferences.Current.EffectiveAccent;
                accent = command switch
                {
                    "AppearanceAccentNone" => accent.WithMode(CuiAccentOverrideMode.None),
                    "AppearanceAccentAllApps" => accent.WithMode(CuiAccentOverrideMode.AllApps).WithAllAppsColour(accent.AllAppsColour ?? CuiAccentPreference.InitialAllAppsColour),
                    "AppearanceAccentIndividualApps" => accent.WithMode(CuiAccentOverrideMode.IndividualApps),
                    "AppearanceAccentColour" => accent.WithMode(CuiAccentOverrideMode.AllApps).WithAllAppsColour(Convert.ToString(parameter)),
                    "AppearanceAccentApply" => accent.WithAllAppsColour(_accentDraft ?? accent.AllAppsColour ?? CuiAccentPreference.InitialAllAppsColour),
                    _ => accent
                };
                var result = await _preferences.SetAsync(name, token: lifetime.CallerCancellation, appearance: appearance, accent: accent);
                if (result.Succeeded) { _accentDraft = null; _draftScope = null; }
                _operationStatus = result.Succeeded ? result.Diagnostic ?? _preferences.SavedDescription : result.Diagnostic ?? "Appearance could not be saved. Try again.";
            }
        }
        finally { _busy = false; Publish(); }
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args) => Publish(args);
    private void PreferenceChanged(object? sender, EventArgs args)
    {
        if (_draftScope is not null && _draftScope != _preferences.Current.ScopeId) { _accentDraft = null; _draftScope = null; }
        _operationStatus = null; Publish();
    }
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
