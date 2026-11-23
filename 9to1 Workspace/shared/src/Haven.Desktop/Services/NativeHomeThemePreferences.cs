using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Native renderer adapter over the existing preference file and canonical Home service; never an account-sync substitute.</summary>
internal sealed class NativeHomeThemePreferences : ICuiThemePreferenceSource, IAsyncDisposable
{
    private readonly HomeThemeScope _scope;
    private readonly IPlatformSettings? _platform;
    private volatile PlatformThemeVariant _systemVariant;
    private sealed record DisplayChange(object Target, int Brightness, int ColourContrast);
    private readonly HomeThemePreferenceService _service;
    private readonly FileBackend _backend;
    private readonly Action<Action> _dispatch;
    private readonly Action<CuiThemeResolution>? _publishThemePresentation;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private sealed class Operation(CancellationToken token)
    { internal CancellationToken Token = token; internal Task Task = null!; }
    private readonly List<Operation> _operations = [];
    private readonly object _operationsGate = new();
    private readonly AsyncLocal<Operation?> _executing = new();
    private readonly List<Exception> _observerFailures = [];
    private readonly Dictionary<string, SettingTarget> _targets = new(StringComparer.Ordinal);
    private static readonly int[] InterfaceScalePercentages = [100, 125, 150, 175];
    private readonly ScalePresentation _presentation;
    private sealed record ScalePreview(object? Owner, SettingTarget Target, double Scale);
    private ScalePreview? _scalePreview;
    private static readonly (string Name, string Colour)[] AccentPresets =
    [
        ("Electric blue", "#2455FF"), ("Iris", "#7145FF"),
        ("Lagoon", "#00B89C"), ("Sunset", "#FF6847")
    ];
    private static readonly string[] AccentAppNames = CuiAppAccentCatalog.Defaults.Select(app => app.Surface).ToArray();
    private HomeThemePreference? _preference;
    private sealed record SettingTarget(NativeHomeThemePreferences Owner, long Revision, string Key, string Value, string? AppId = null);
    private sealed record AccentChange(SettingTarget Target, string Colour)
    {
        internal HomeThemeOperationResult? Receipt { get; set; }
    }
    private sealed record ThemeCreation(SettingTarget Target, CuiCustomTheme Theme, string Library)
    {
        internal HomeThemeOperationResult? Receipt { get; set; }
    }
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
        Func<CancellationToken, ValueTask<bool>> authorise, Action<Action> dispatch,
        Action<CuiThemeResolution>? publishThemePresentation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferencePath);
        _scope = scope.Validate();
        if (scope.Scope != HomeDataScope.DeviceLocal) throw new NotSupportedException("Native account theme synchronization is not configured.");
        _dispatch = dispatch;
        _publishThemePresentation = publishThemePresentation;
        _backend = new(preferencePath, scope, projectLegacy, authorise);
        _service = new(_backend, _backend, new ThemeCatalog());
        _platform = Avalonia.Application.Current?.PlatformSettings;
        if (_platform is not null)
        {
            _systemVariant = _platform.GetColorValues().ThemeVariant;
            _platform.ColorValuesChanged += SystemColoursChanged;
        }
        _presentation = new(this);
        Context = new(_presentation);
        if (_publishThemePresentation is not null) Context.Changed += ContextChanged;
        _service.Changed += Invalidated;
    }

    // Theme consumers see the selected Settings page's transient scale. Persistence, draft
    // revision checks and other owners always read the committed ICuiThemePreferenceSource.
    private sealed class ScalePresentation(NativeHomeThemePreferences source) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current
        {
            get
            {
                lock (source._operationsGate)
                {
                    var saved = source.Current;
                    return source._scalePreview is { } preview && preview.Target.Revision == saved.Revision
                        ? saved with { Accessibility = saved.Accessibility with { DisplayScale = preview.Scale } }
                        : saved;
                }
            }
        }

        public event EventHandler? Changed;
        internal void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    }

    internal bool TryPreviewInterfaceScale(object owner, object? issuedTarget)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_operationsGate)
        {
            if (_closing || issuedTarget is not SettingTarget { Key: "interface-scale" } target ||
                !ReferenceEquals(target.Owner, this) || !_targets.Values.Any(candidate => ReferenceEquals(candidate, target)) ||
                target.Revision != _preference?.Revision || target.Revision != Current.Revision || Current.ScopeId != "device:" + _scope.OwnerId ||
                _scalePreview is { Owner: { } activeOwner } && !ReferenceEquals(activeOwner, owner))
                return false;
            var scale = InterfaceScale(target);
            if (_scalePreview is { } current && current.Scale == scale && ReferenceEquals(current.Owner, owner)) return true;
            _scalePreview = new(owner, target, scale);
        }
        _presentation.Notify();
        return true;
    }

    internal object? CaptureInterfaceScaleDraft(object owner, object? issuedTarget)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_operationsGate)
        {
            return !_closing && _scalePreview is { } preview && ReferenceEquals(preview.Owner, owner) &&
                ReferenceEquals(preview.Target, issuedTarget) && preview.Target.Revision == _preference?.Revision
                ? preview : null;
        }
    }

    internal void EndInterfaceScalePreview(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_operationsGate)
        {
            if (_scalePreview is not { } preview || !ReferenceEquals(preview.Owner, owner)) return;
            // A successful CAS and the authorised re-read are separate observations. Release the
            // page immediately but keep the confirmed scale on screen until that snapshot arrives.
            if (_preference is { } saved && saved.Revision > preview.Target.Revision &&
                saved.Accessibility.DisplayScale == preview.Scale && Current.Revision == preview.Target.Revision)
            {
                _scalePreview = preview with { Owner = null };
                return;
            }
            _scalePreview = null;
        }
        _presentation.Notify();
    }

    private static double InterfaceScale(SettingTarget target) =>
        int.Parse(target.Value, CultureInfo.InvariantCulture) / 100d;

    private void SystemColoursChanged(object? sender, PlatformColorValues colours)
    {
        if (_systemVariant == colours.ThemeVariant) return;
        _systemVariant = colours.ThemeVariant;
        lock (_operationsGate)
            if (_closing || _preference?.EffectiveDisplay().Mode != HomeDisplayMode.FollowSystem) return;
        Invalidated(this, _scope);
    }

    private CuiAppearance ResolveMode(HomeDisplayMode mode) =>
        mode == HomeDisplayMode.Dark || mode == HomeDisplayMode.FollowSystem && _systemVariant == PlatformThemeVariant.Dark
            ? CuiAppearance.Dark : CuiAppearance.Bright;

    private void ContextChanged(object? sender, EventArgs args)
    {
        try
        {
            _dispatch(() =>
            {
                if (_closing || Context.Current.ScopeId == "presentation-only") return;
                try { _publishThemePresentation?.Invoke(Context.Current); }
                catch (Exception error)
                {
                    lock (_operationsGate) _observerFailures.Add(error);
                    Diagnostic = "Home theme presentation could not be updated; the last valid palette is retained.";
                }
            });
        }
        catch (Exception error)
        {
            lock (_operationsGate) _observerFailures.Add(error);
            Diagnostic = "Home theme presentation update could not be scheduled; the last valid palette is retained.";
        }
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

    private static Color PreviewSurface(Color canvas, Color layer)
    {
        byte Channel(byte lower, byte upper) => (byte)Math.Round(lower + (upper - lower) * layer.A / 255d);
        return Color.FromRgb(Channel(canvas.R, layer.R), Channel(canvas.G, layer.G), Channel(canvas.B, layer.B));
    }

    private static IBrush PreviewGradient(Color start, Color middle, Color end) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = [new(start, 0), new(middle, .5), new(end, 1)]
    };

    private static void ApplyPresetPreview(Dictionary<string, object?> option, CuiPalette source, CuiAccessibilitySettings accessibility,
        CuiCustomTheme? custom = null)
    {
        var palette = CuiAccessibilityPalette.Resolve(source, accessibility);
        var dark = palette.TideBase.ToHsl().L < .5;
        var panel = PreviewSurface(palette.TideBase, palette.Panel2);
        var tile = PreviewSurface(palette.TideBase, palette.PanelHover);
        var ink = palette.Text;
        var accent = palette.Accent;
        var (caption, panelRadius, tileRadius) = palette.Theme switch
        {
            CuiTheme.Professional => ("Quiet, confident surfaces", 9, 4),
            CuiTheme.Glow => ("One ambient field of light", 9, 4),
            CuiTheme.Playful => ("Softly frosted tonal layers", 19, 12),
            CuiTheme.FrostedGlass => ("Soft translucent layers", 19, 12),
            CuiTheme.Bubble => ("Soft raised surfaces", 15, 8),
            CuiTheme.Metallic => ("Titanium", 4, 4),
            CuiTheme.Retro => ("Amber Console", 3, 4),
            CuiTheme.Western => ("Leather & Copper", 9, 4),
            _ => ("Layered light and depth", 9, 4)
        };
        IBrush background = new SolidColorBrush(panel);
        IBrush accentBrush = new SolidColorBrush(accent);
        IBrush? customTile = null;
        var resources = new ResourceDictionary();
        if (!accessibility.HighContrast || custom is not null)
        {
            CuiThemeResourceApplier.ApplyToResources(resources, source, accessibility);
            if (custom is not null)
            {
                CuiCustomThemeResourceApplier.ApplyToResources(resources, custom, accessibility);
                customTile = (IBrush)resources["CuiAccentSecondaryBrush"]!;
            }
            accentBrush = (IBrush)resources["CuiAccentPrimaryBrush"]!;
        }
        if (!accessibility.HighContrast)
        {
            switch (palette.Theme)
            {
                case CuiTheme.Professional:
                    background = new SolidColorBrush(palette.Panel3);
                    tile = palette.LineStrong;
                    break;
                case CuiTheme.Glow:
                    background = (IBrush)resources["CuiSurfaceMaterial"]!;
                    tile = palette.TideBase;
                    break;
                case CuiTheme.Playful:
                    background = PreviewGradient(palette.TideBase, palette.Panel, palette.Panel3);
                    tile = palette.Panel2;
                    break;
                case CuiTheme.FrostedGlass:
                    background = PreviewGradient(palette.TideBase, palette.TideColour, panel);
                    break;
                case CuiTheme.Bubble:
                    background = PreviewGradient(panel, PreviewSurface(palette.TideBase, palette.Panel), palette.TideBase);
                    break;
                case CuiTheme.Metallic:
                    background = PreviewGradient(palette.Line, palette.LineStrong, panel);
                    ink = CuiContrast.EnsureForegroundContrast(ink, palette.LineStrong, 4.5);
                    tile = palette.PanelHover;
                    break;
                case CuiTheme.Retro:
                    background = new SolidColorBrush(Color.Parse(dark ? "#241A0B" : "#EBD7AD"));
                    tile = Color.Parse(dark ? "#3F3526" : "#D4B77A");
                    ink = Color.Parse(dark ? "#FFD685" : "#482E0D");
                    break;
                case CuiTheme.Western:
                    background = (IBrush)resources["CuiPanel2Brush"]!;
                    break;
                case CuiTheme.Cinematic:
                    background = PreviewGradient(palette.Panel3, palette.TideBase, palette.Panel2);
                    tile = palette.Panel3;
                    break;
            }
        }
        option["Caption"] = custom is null ? caption : $"Custom · {CuiThemeCatalog.Resolve(custom.BaseTheme).DisplayName}";
        option["PreviewPanel"] = background;
        option["PreviewTile"] = customTile ?? new SolidColorBrush(tile);
        option["PreviewInk"] = new SolidColorBrush(ink);
        option["PreviewAccent"] = accentBrush;
        option["PreviewAccentRadius"] = custom is null ? 8d : 8d * custom.ButtonRoundnessPercent / 100;
        option["PreviewPanelRadius"] = panelRadius;
        option["PreviewTileRadius"] = tileRadius;
        option["PreviewFont"] = (custom, palette.Theme) switch
        {
            (not null, _) when string.IsNullOrWhiteSpace(accessibility.InterfaceFontFamilyOverride) =>
                CuiCustomThemeFonts.ResourceFamily(custom.Fonts.Main.Family),
            (null, CuiTheme.Retro) => "monospace",
            (null, CuiTheme.Western) => "serif",
            _ => CuiTypography.ResolveInterfaceFontFamily(accessibility.InterfaceFontFamilyOverride)
        };
    }

    internal static void BindAppearanceLayout(CuiControlLoader loader, CuiViewModel model)
    {
        loader.BindAuthoredControl("home-appearance-settings", _ =>
        {
            var fragment = new CuiContainerControl("Vertical Stack");
            fragment.SizeChanged += (_, args) =>
            {
                var compact = args.NewSize.Width < 640;
                model.Set("HomeAppearancePadding", compact ? "12 12 12 24" : "12 32 12 24");
                model.Set("HomeDisplayRows", compact ? "Auto Auto" : "Auto");
                model.Set("HomeDisplayColumns", compact ? "1fr" : "1fr 280");
                model.Set("HomeDisplayControlRow", compact ? 1 : 0);
                model.Set("HomeDisplayControlColumn", compact ? 0 : 1);
                model.Set("HomeDisplayControlAlignment", compact ? "Stretch" : "Right");
            };
            return fragment;
        });
    }

    internal static void UpdateDisplayReadout(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not CuiViewModel model) return;
        var key = args.PropertyName;
        if (key is "HomeBrightness" or "HomeColourContrast" && model.Get(key) is double number && double.IsFinite(number))
            model.Set(key + "Text", Math.Round(number) + "%");
        if (key is "HomeBrightness" or "HomeColourContrast" or "HomeBrightnessSaved" or "HomeColourContrastSaved" or "HomeAppearanceAvailable" or "HomeDisplayConflict")
        {
            bool Changed(string name) => model.Get(name) is double draft && model.Get(name + "Saved") is double saved &&
                double.IsFinite(draft) && Math.Round(draft) != saved;
            model.Set("HomeCanApplyLevels", model.Get("HomeAppearanceAvailable") is true && model.Get("HomeDisplayConflict") is not true &&
                (Changed("HomeBrightness") || Changed("HomeColourContrast")));
        }
    }

    internal void ApplyEditorBindings(CuiViewModel model)
    {
        var bindings = SettingsBindings(model.Get("HomeAccentAppIndex") is int index ? index : 0);
        var conflict = model.Get("HomeDisplayConflict") is true;
        foreach (var key in new[] { "HomeBrightness", "HomeColourContrast" })
        {
            var incoming = (double)bindings[key]!;
            if (model.Get(key + "Saved") is double saved)
            {
                if (saved == incoming) continue;
                if (model.Get(key) is double draft && Math.Round(draft) != saved && Math.Round(draft) != incoming)
                {
                    conflict = true;
                    continue;
                }
            }
            model.Set(key + "Saved", incoming);
            model.Set(key, incoming);
        }
        foreach (var (key, value) in bindings)
            if (key is not ("HomeBrightness" or "HomeColourContrast" or "HomeDisplayTarget")) model.Set(key, value);
        model.Set("HomeDisplayConflict", conflict);
        model.Set("HomeDisplayTarget", conflict ? null : bindings["HomeDisplayTarget"]);
    }

    internal static void ReloadDisplayDraft(CuiViewModel model)
    {
        model.Set("HomeBrightnessSaved", null);
        model.Set("HomeColourContrastSaved", null);
        model.Set("HomeDisplayConflict", false);
    }

    private CuiAppearance EffectiveAppearance(HomeThemePreference value)
    {
        var mode = value.EffectiveDisplay().Mode;
        if (mode != HomeDisplayMode.FollowSystem && value.Appearance is { } legacy && HomeDisplaySettings.FromLegacy(legacy) == mode)
            return Enum.Parse<CuiAppearance>(legacy);
        return ResolveMode(mode);
    }

    internal static object? CaptureDisplayDraft(object? target, CuiViewModel model)
    {
        if (target is not SettingTarget { Key: "display" }) return target;
        if (model.Get("HomeBrightness") is not double brightness || model.Get("HomeColourContrast") is not double contrast ||
            !double.IsFinite(brightness) || !double.IsFinite(contrast) ||
            brightness is < 0 or > 100 || contrast is < 0 or > 100) return null;
        return new DisplayChange(target, (int)Math.Round(brightness), (int)Math.Round(contrast));
    }

    internal IReadOnlyDictionary<string, object?> SettingsBindings(int accentAppIndex = 0)
    {
        lock (_operationsGate)
        {
        var value = _preference;
        object Target(string key, string next, string? appId = null) => IssueTarget(value!.Revision, key, next, appId);
        Dictionary<string, object?> Option(string name, string key, string next, bool selected) => new()
        { ["Name"] = name, ["Key"] = key is "reduce-motion" or "high-contrast" ? key : key + ":" + next,
          ["Target"] = Target(key, next), ["IsSelected"] = selected };
        var available = !_closing && value is not null;
        var display = value?.EffectiveDisplay() ?? new(HomeDisplayMode.FollowSystem);
        var themes = CuiThemeCatalog.All.OrderBy(theme => theme.Theme switch
        {
            CuiTheme.Professional => 0, CuiTheme.Glow => 1, CuiTheme.Playful => 2,
            CuiTheme.FrostedGlass => 3, CuiTheme.Bubble => 4, CuiTheme.Metallic => 5,
            CuiTheme.Retro => 6, CuiTheme.Western => 7, _ => 8
        }).ToArray();
        IReadOnlyList<CuiCustomTheme> savedThemes = [];
        if (value?.CustomThemesJson is { } library)
            try { savedThemes = CuiCustomThemeContract.DeserializeLibrary(library); }
            catch (ArgumentException) { }
        var requestedName = value?.ThemeName;
        var comicKey = CuiThemeCatalog.ComicSelectionKey(savedThemes.Select(theme => theme.Name));
        var comicSelected = CuiThemeCatalog.FindBuiltIn(requestedName)?.Theme == CuiTheme.Cinematic &&
            !savedThemes.Any(theme => string.Equals(theme.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        Dictionary<string, object?> ThemeOption(string name, string key, CuiTheme theme, CuiCustomTheme? custom = null)
        {
            var selected = custom is null && theme == CuiTheme.Cinematic
                ? comicSelected : !comicSelected && string.Equals(key, requestedName, StringComparison.OrdinalIgnoreCase);
            var option = Option(name, "theme", key, selected);
            option["HasPreview"] = true;
            var appearance = Current.Appearance ?? CuiThemeScopeApplier.DetectAppearance();
            var accent = Current.EffectiveAccent.ResolveColour("app.home");
            var palette = CuiSurfacePaletteCatalog.For("Home", appearance, theme, accent);
            ApplyPresetPreview(option, palette, Current.Accessibility,
                custom is null ? null : CuiCustomThemeAccentResolver.Resolve(custom, accent));
            return option;
        }
        var themeChoices = available
            ? themes.Select(theme => ThemeOption(theme.DisplayName,
                theme.Theme == CuiTheme.Cinematic ? comicKey : theme.DisplayName, theme.Theme))
                .Concat(savedThemes.Select(theme => ThemeOption(
                    string.Equals(theme.Name, CuiThemeCatalog.ComicName, StringComparison.OrdinalIgnoreCase)
                        ? theme.Name + " (custom)" : theme.Name, theme.Name, theme.BaseTheme, theme))).ToArray()
            : Array.Empty<Dictionary<string, object?>>();
        var savedAccent = value?.Accent ?? HomeThemeAccentPreference.Default;
        var perApp = savedAccent.Mode == HomeThemeAccentOverrideMode.IndividualApps;
        var apps = CuiAppAccentCatalog.Defaults;
        var app = perApp ? apps[Math.Clamp(accentAppIndex, 0, apps.Count - 1)] : null;
        var savedColour = app is null ? savedAccent.AllAppsColour : savedAccent.IndividualApps.GetValueOrDefault(app.AppId);
        Dictionary<string, object?> AccentMode(string name, HomeThemeAccentOverrideMode mode) =>
            Option(name, "accent-mode", mode.ToString(), savedAccent.Mode == mode);
        Dictionary<string, object?> AccentSwatch((string Name, string Colour) preset)
        {
            return new()
            {
                ["Name"] = preset.Name, ["Key"] = "accent-preset:" + preset.Colour,
                ["Target"] = Target("accent-preset", preset.Colour, app?.AppId),
                ["IsSelected"] = savedColour == preset.Colour, ["Colour"] = preset.Colour
            };
        }
        var highContrast = available ? Option("High contrast", "high-contrast", (!value!.Accessibility.HighContrast).ToString(), value.Accessibility.HighContrast) : null;
        var reduceMotion = available ? Option("Reduce motion", "reduce-motion", (!value!.Accessibility.ReduceMotion).ToString(), value.Accessibility.ReduceMotion) : null;
        Dictionary<string, object?> ScaleOption(int percent)
        {
            var scale = percent / 100d;
            var number = percent.ToString(CultureInfo.InvariantCulture);
            var choice = Option(number + "%", "interface-scale", number, value!.Accessibility.DisplayScale == scale);
            choice["Scale"] = scale;
            return choice;
        }
        return new Dictionary<string, object?>
        {
            ["HomeInterfaceScaleChoices"] = available ? InterfaceScalePercentages.Select(ScaleOption).ToArray() : Array.Empty<Dictionary<string, object?>>(),
            ["HomeInterfaceScaleText"] = available
                ? (value!.Accessibility.DisplayScale * 100).ToString("0.######", CultureInfo.InvariantCulture) + "%" : "Unavailable",
            ["HomeHighContrastTarget"] = highContrast?["Target"],
            ["HomeHighContrastEnabled"] = highContrast?["IsSelected"] is true,
            ["HomeHighContrastState"] = highContrast is null ? "Unavailable" : value!.Accessibility.HighContrast ? "On" : "Off",
            ["HomeReduceMotionTarget"] = reduceMotion?["Target"],
            ["HomeReduceMotionEnabled"] = reduceMotion?["IsSelected"] is true,
            ["HomeReduceMotionState"] = reduceMotion is null ? "Unavailable" : value!.Accessibility.ReduceMotion ? "On" : "Off",
            ["HomeAccentModeChoices"] = available ? new[]
            {
                AccentMode("Off", HomeThemeAccentOverrideMode.None), AccentMode("On", HomeThemeAccentOverrideMode.AllApps),
                AccentMode("On per app", HomeThemeAccentOverrideMode.IndividualApps)
            } : Array.Empty<Dictionary<string, object?>>(),
            ["HomeGlobalAccentChoices"] = available ? AccentPresets.Select(AccentSwatch).ToArray() : Array.Empty<Dictionary<string, object?>>(),
            ["HomeAccentPerApp"] = available && perApp,
            ["HomeAccentAppNames"] = AccentAppNames,
            ["HomeAccentColourLabel"] = app is null ? "Saved global colour" : "App colour",
            ["HomeAccentScopeName"] = app?.Surface,
            ["HomeAccentSampleColour"] = savedColour ?? app?.Colour ?? HomeThemeAccentPreference.InitialAllAppsColour,
            ["HomeAccentSavedColour"] = savedColour ?? (app is null ? "No global colour saved" : "Uses the app default"),
            ["HomeAccentEditorTarget"] = available ? Target("accent-draft", "colour", app?.AppId) : null,
            ["HomeAccentResetTarget"] = available && app is not null && savedColour is not null
                ? Target("accent-reset", "default", app.AppId) : null,
            ["HomeAccentCanReset"] = available && app is not null && savedColour is not null,
            ["HomeAppearanceAvailable"] = available,
            ["HomeBrightness"] = (double)display.Brightness,
            ["HomeColourContrast"] = (double)display.ColourContrast,
            ["HomeDisplayTarget"] = available ? Target("display", "levels") : null,
            ["HomeHighContrastActive"] = value?.Accessibility.HighContrast == true,
            ["HomeModeChoices"] = available ? Enum.GetValues<HomeDisplayMode>()
                .Select(mode => Option(mode == HomeDisplayMode.FollowSystem ? "Follow system" : mode.ToString(),
                    "mode", mode.ToString(), mode == display.Mode)).ToArray() : Array.Empty<Dictionary<string, object?>>(),
            ["HomeAppearanceScope"] = "Applies to this device profile.",
            ["HomeAppearanceStatus"] = Diagnostic ?? (value is null ? "Appearance has not been loaded." : "Saved theme: " + (comicSelected ? CuiThemeCatalog.ComicName : requestedName)),
            ["HomeThemeChoices"] = themeChoices,
            ["HomeAppearanceChoices"] = available ? new[] { "SuperBright", "Bright", "Dark", "SuperDark" }
                .Select(name => Option(name is "SuperBright" ? "Super bright" : name is "SuperDark" ? "Super dark" : name, "appearance", name, name == value!.Appearance)).ToArray() : Array.Empty<Dictionary<string, object?>>(),
            ["HomeAccessibilityChoices"] = available ? new[] { reduceMotion!, highContrast! } : Array.Empty<Dictionary<string, object?>>()
        };
        }
    }

    private SettingTarget IssueTarget(long revision, string key, string next, string? appId = null)
    {
        var cache = key + "\0" + next + "\0" + appId;
        if (!_targets.TryGetValue(cache, out var target) || target.Revision != revision)
            _targets[cache] = target = new(this, revision, key, next, appId);
        return target;
    }

    internal async Task<HomeThemeOperationResult> SaveThemeDraftAsync(CuiCustomTheme theme, CuiThemePreferenceSnapshot opening,
        Func<object, CancellationToken, Task> dispatch, CancellationToken token)
    {
        ThemeCreation change;
        lock (_operationsGate)
        {
            if (_closing || _preference is null || opening.ScopeId != "device:" + _scope.OwnerId || opening.Revision != _preference.Revision)
                return new(false, "RevisionConflict", Diagnostic: "Appearance changed. Review the current revision before saving this draft.");
            try
            {
                theme = theme.Validate();
                var existing = _preference.CustomThemesJson is { } library
                    ? CuiCustomThemeContract.DeserializeLibrary(library) : Array.Empty<CuiCustomTheme>();
                change = new(IssueTarget(opening.Revision, "create-theme", "new"), theme,
                    CuiCustomThemeContract.SerializeLibrary(existing.Append(theme)));
            }
            catch (ArgumentException error) { return new(false, "InvalidTheme", Diagnostic: error.Message); }
        }
        await dispatch(change, token);
        return change.Receipt ?? new(false, "SettingsUnavailable", Diagnostic: "Reopen Home for the current profile before saving.");
    }

    internal Task<HomeThemeOperationResult> ApplySettingsAsync(object? issuedTarget, CancellationToken token = default) =>
        issuedTarget switch
        {
            ThemeCreation creation => ApplyCreationAsync(creation, token),
            AccentChange accent => ApplyAccentChangeAsync(accent, token),
            _ => ApplySettingsCoreAsync(issuedTarget, token)
        };

    private async Task<HomeThemeOperationResult> ApplyCreationAsync(ThemeCreation creation, CancellationToken token) =>
        creation.Receipt = await ApplySettingsCoreAsync(creation, token);

    internal async Task<HomeThemeOperationResult> SaveAccentDraftAsync(object? openingTarget, string colour,
        Func<object, CancellationToken, Task> dispatch, CancellationToken token)
    {
        if (openingTarget is not SettingTarget { Key: "accent-draft" } target)
            return new(false, "SettingsUnavailable", Diagnostic: "Accent settings are unavailable.");
        try { colour = HomeThemeAccentPreference.CanonicalColour(colour.Trim()); }
        catch (ArgumentException) { return new(false, "InvalidColour", Diagnostic: "Enter a colour in #RRGGBB format."); }
        var change = new AccentChange(target, colour);
        await dispatch(change, token);
        return change.Receipt ?? new(false, "SettingsUnavailable", Diagnostic: "Reopen Home for the current profile before saving.");
    }

    private async Task<HomeThemeOperationResult> ApplyAccentChangeAsync(AccentChange change, CancellationToken token) =>
        change.Receipt = await ApplySettingsCoreAsync(change, token);

    private static HomeThemeAccentPreference SetTargetAccent(HomeThemeAccentPreference saved, SettingTarget target, string? colour) =>
        target.AppId is { } appId ? saved.WithAppColour(appId, colour) : saved.WithAllAppsColour(colour);

    private Task<HomeThemeOperationResult> ApplySettingsCoreAsync(object? issuedTarget, CancellationToken token)
    {
        var creation = issuedTarget as ThemeCreation;
        if (creation is not null) issuedTarget = creation.Target;
        var displayChange = issuedTarget as DisplayChange;
        if (displayChange is not null) issuedTarget = displayChange.Target;
        var accentChange = issuedTarget as AccentChange;
        if (accentChange is not null) issuedTarget = accentChange.Target;
        var scaleChange = issuedTarget as ScalePreview;
        if (scaleChange is not null) issuedTarget = scaleChange.Target;
        SettingTarget target;
        lock (_operationsGate)
        {
            if (issuedTarget is not SettingTarget observed || !ReferenceEquals(observed.Owner, this) ||
                !_targets.Values.Any(current => ReferenceEquals(current, observed)) || _preference is null ||
                observed.Revision != _preference.Revision || (observed.Key == "display") != (displayChange is not null) ||
                (observed.Key == "create-theme") != (creation is not null) ||
                (observed.Key == "accent-draft") != (accentChange is not null) ||
                (observed.Key == "interface-scale") != (scaleChange is not null) ||
                scaleChange is not null && !ReferenceEquals(_scalePreview, scaleChange))
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
                "interface-scale" => prior.Accessibility with { DisplayScale = InterfaceScale(target) },
                "reduce-motion" => prior.Accessibility with { ReduceMotion = bool.Parse(target.Value) },
                "high-contrast" => prior.Accessibility with { HighContrast = bool.Parse(target.Value) },
                _ => prior.Accessibility
            };
            var display = prior.EffectiveDisplay();
            var updatedDisplay = target.Key == "mode" ? display with { Mode = Enum.Parse<HomeDisplayMode>(target.Value) }
                : displayChange is null ? null
                : display with { Brightness = displayChange.Brightness, ColourContrast = displayChange.ColourContrast };
            var compatibleAppearance = target.Key == "mode" ? ResolveMode(updatedDisplay!.Mode).ToString() : prior.Appearance;
            var savedAccent = prior.Accent ?? HomeThemeAccentPreference.Default;
            HomeThemeAccentPreference? accent;
            try
            {
                accent = target.Key switch
                {
                    "accent-mode" => savedAccent.WithMode(Enum.Parse<HomeThemeAccentOverrideMode>(target.Value)),
                    "accent-preset" => SetTargetAccent(savedAccent, target, target.Value),
                    "accent-draft" => SetTargetAccent(savedAccent, target, accentChange!.Colour),
                    "accent-reset" => SetTargetAccent(savedAccent, target, null),
                    _ => prior.Accent
                };
            }
            catch (ArgumentException error)
            {
                return new(false, "InvalidAccent", Diagnostic: error.Message);
            }
            if (accent is { Mode: HomeThemeAccentOverrideMode.AllApps, AllAppsColour: null } && target.Key == "accent-mode")
                accent = accent.WithAllAppsColour(HomeThemeAccentPreference.InitialAllAppsColour);
            var actual = updatedDisplay is not null
                ? _service.SetDisplayAsync(prior, updatedDisplay, compatibleAppearance, current)
                : _service.SetAsync(_scope, creation?.Theme.Name ?? (target.Key == "theme" ? target.Value : prior.ThemeName),
                accessibility, target.Revision, current,
                appearance: target.Key == "appearance" ? target.Value : prior.Appearance,
                accent: accent, customThemesJson: creation?.Library ?? prior.CustomThemesJson);
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
                    Brightness = value.EffectiveDisplay().Brightness, ColourContrast = value.EffectiveDisplay().ColourContrast,
                    DisplayScale = access.DisplayScale, InterfaceFontFamilyOverride = access.InterfaceFontFamily,
                    CodeFontFamilyOverride = access.CodeFontFamily },
                EffectiveAppearance(value),
                value.Accent is { } accent ? new((CuiAccentOverrideMode)accent.Mode, accent.AllAppsColour, accent.IndividualApps) : null,
                resolved.UsedFallback ? null : value.CustomThemesJson)
            {
                SavedMigration = value.ThemeMigration is { } migration ? new(migration.OriginalName, migration.CanonicalId) : null
            };
            if (next.Revision < Current.Revision && next.ScopeId == Current.ScopeId) return;
            Diagnostic = resolved.Diagnostic;
            if (next == Current) return;
            lock (_operationsGate)
            {
                Current = next;
                if (_scalePreview is { } preview && preview.Target.Revision != next.Revision) _scalePreview = null;
            }
            _presentation.Notify();
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
        bool hadPreview;
        lock (_operationsGate)
        {
            if (_close is not null) return new(_close);
            _closing = true;
            hadPreview = _scalePreview is not null;
            _scalePreview = null;
            close = _close = CloseAsync(start.Task, _operations.ToArray());
        }
        try { if (hadPreview) _presentation.Notify(); }
        finally { start.SetResult(); }
        return new(close);
    }
    private async Task CloseAsync(Task start, Operation[] operations)
    {
        await start;
        var failures = new List<Exception>();
        lock (_operationsGate) failures.AddRange(_observerFailures);
        if (_platform is not null) _platform.ColorValuesChanged -= SystemColoursChanged;
        try { _service.Changed -= Invalidated; } catch (Exception error) { failures.Add(error); }
        if (_publishThemePresentation is not null)
            try { Context.Changed -= ContextChanged; } catch (Exception error) { failures.Add(error); }
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
            var match = CuiThemeCatalog.FindBuiltIn(name);
            canonical = CuiThemeCatalog.IsQualifiedComicKey(name.Trim()) ? name.Trim() : match?.DisplayName ?? "Professional";
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
