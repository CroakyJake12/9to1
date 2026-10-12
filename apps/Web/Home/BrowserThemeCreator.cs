using System.ComponentModel;
using System.Globalization;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>A presentation-only draft. The original Home/account owner admits every save.</summary>
internal sealed class BrowserThemeCreator : ICuiWritableBindingContext, ICuiLifetimeAwareActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    internal const string PreviewThemeName = "9to1 Draft Theme Preview";
    private readonly ICuiBindingContext _bindings;
    private readonly ICuiActionDispatcher _actions;
    private readonly IBrowserHomeThemePreferences _preferences;
    private readonly Func<CuiCustomTheme, CuiThemePreferenceSnapshot, CancellationToken, Task<HomeThemeOperationResult>> _save;
    private readonly string _previewId = Guid.NewGuid().ToString("D");
    private ThemeDraft? _draft = new(CuiCustomThemeContract.CreateDefault());
    private CuiThemePreferenceSnapshot? _opening;
    private bool _open, _busy, _disposed, _presentationFailure;
    private string? _status, _validation, _previewFingerprint;
    private int _accentIndex, _fontIndex;
    private bool CanEdit => _open && !_busy && !_disposed;
    private bool SameOwner => _opening is { } original && original.ScopeId == _preferences.Current.ScopeId
        && original.Revision == _preferences.Current.Revision;
    private bool CanUseCurrentRevision => CanEdit && _preferences.CanChange && _opening is { } original
        && original.ScopeId == _preferences.Current.ScopeId && original.Revision != _preferences.Current.Revision;
    private bool CanSave => CanEdit && _preferences.CanChange && SameOwner && _validation is null;
    private AccentDraft Accent => _draft!.Accents[_accentIndex];
    private ColourDraft Colour => Accent.Kind switch
    {
        1 => Accent.Stops[Accent.StopIndex].Colour,
        2 => Accent.PatternColourIndex == 0 ? Accent.PatternForeground : Accent.PatternBackground,
        _ => Accent.Solid
    };
    private FontDraft Font => _draft!.Fonts[_fontIndex];
    public event PropertyChangedEventHandler? PropertyChanged;

    internal BrowserThemeCreator(ICuiBindingContext bindings, ICuiActionDispatcher actions,
        IBrowserHomeThemePreferences preferences,
        Func<CuiCustomTheme, CuiThemePreferenceSnapshot, CancellationToken, Task<HomeThemeOperationResult>> save)
    {
        _bindings = bindings; _actions = actions; _preferences = preferences; _save = save;
        if (bindings is INotifyPropertyChanged observable) observable.PropertyChanged += OwnerChanged;
        preferences.Changed += PreferencesChanged;
    }

    internal void Open()
    {
        if (_disposed || _busy || !_preferences.CanChange) return;
        if (_open) return; // Clicking Create Theme again must not discard unsaved work.
        _opening = _preferences.Current;
        _draft = new(CuiCustomThemeContract.CreateDefault());
        _accentIndex = _fontIndex = 0;
        _open = true; _status = null; _presentationFailure = false;
        ValidateAndPreview(); Publish();
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        if (!path.StartsWith("ThemeCreator", StringComparison.Ordinal)) return _bindings.TryGetValue(path, out value);
        var key = path[12..];
        value = key switch
        {
            "Open" => _open && !_disposed,
            "CanEdit" => CanEdit,
            "CanSave" => CanSave,
            "CanUseCurrentRevision" => CanUseCurrentRevision,
            "FocusName" => CanEdit,
            "Status" => Status,
            "AccentNames" => new[] { "Primary", "Secondary", "Tertiary" },
            "FillKinds" => new[] { "Solid Colour", "Gradient", "Pattern" },
            "GradientKinds" => new[] { "Linear", "Radial", "Conic" },
            "PatternKinds" => new[] { "Stripes", "Checkerboard", "Dots" },
            "PatternColourNames" => new[] { "Foreground", "Background" },
            "FontRoles" => new[] { "Main", "Primary Button", "Secondary Button", "Tertiary Button", "Heading", "Body", "Code" },
            "FontFamilies" => CuiCustomThemeFonts.Families,
            _ => null
        };
        if (value is not null) return true;
        if (_draft is null) { value = key.EndsWith("Index", StringComparison.Ordinal) ? 0 : string.Empty; return true; }
        value = key switch
        {
            "Name" => _draft.Name,
            "AccentIndex" => _accentIndex,
            "FillKindIndex" => Accent.Kind,
            "IsGradient" => Accent.Kind == 1,
            "IsPattern" => Accent.Kind == 2,
            "GradientKindIndex" => Accent.GradientKind,
            "GradientHasAngle" => Accent.GradientKind is 0 or 2,
            "GradientHasCentre" => Accent.GradientKind is 1 or 2,
            "GradientHasRadius" => Accent.GradientKind == 1,
            "GradientAngle" => Accent.GradientAngle,
            "GradientCenterX" => Accent.CenterX,
            "GradientCenterY" => Accent.CenterY,
            "GradientRadius" => Accent.Radius,
            "StopNames" => Accent.Stops.Select((stop, index) => $"Stop {index + 1} · {stop.Position}%").ToArray(),
            "StopIndex" => Accent.StopIndex,
            "StopPosition" => Accent.Stops[Accent.StopIndex].Position,
            "CanEditStopPosition" => CanEdit && Accent.StopIndex > 0 && Accent.StopIndex < Accent.Stops.Count - 1,
            "CanAddStop" => CanEdit && Accent.Stops.Count < 16,
            "CanRemoveStop" => CanEdit && Accent.StopIndex > 0 && Accent.StopIndex < Accent.Stops.Count - 1,
            "PatternKindIndex" => Accent.PatternKind,
            "PatternSize" => Accent.PatternSize,
            "PatternSpacing" => Accent.PatternSpacing,
            "PatternAngle" => Accent.PatternAngle,
            "PatternColourIndex" => Accent.PatternColourIndex,
            "ColourLabel" => Accent.Kind switch { 1 => $"Stop {Accent.StopIndex + 1} colour", 2 => Accent.PatternColourIndex == 0 ? "Pattern foreground colour" : "Pattern background colour", _ => "Colour" },
            "ColourHex" => Colour.Hex,
            "ColourSwatch" => Colour.ValidHex,
            "ColourRed" => Colour.Channel(0),
            "ColourGreen" => Colour.Channel(1),
            "ColourBlue" => Colour.Channel(2),
            "Transparency" => Colour.Transparency,
            "BackgroundBlur" => Colour.Blur,
            "FontRoleIndex" => _fontIndex,
            "FontFamilyIndex" => CuiCustomThemeFonts.Families.ToList().IndexOf(Font.Family),
            "FontWeight" => Font.Weight,
            "FontWeights" => CuiCustomThemeFonts.Weights(Font.Family),
            "FontWeightIndex" => CuiCustomThemeFonts.Weights(Font.Family).ToList().IndexOf(int.TryParse(Font.Weight, out var weight) ? weight : 0),
            "FontSize" => Font.Size,
            "Roundness" => _draft.Roundness,
            "RoundnessLabel" => $"{_draft.Roundness:0}%",
            _ => null
        };
        return value is not null;
    }

    private string Status => (_status ?? (!_open ? string.Empty : !SameOwner
        ? CanUseCurrentRevision
            ? $"Saved appearance is now {_preferences.Current.Name}. Your draft is preserved. Review it, then use the current revision to save."
            : "Your account changed. Your draft is still here; saving into another account is disabled. Close and reopen the creator for that account."
        : _validation ?? "Preview only. Save theme applies it to your account."))
        + (_presentationFailure ? " Some creator controls could not refresh." : string.Empty);

    public bool TrySetValue(string path, object? value)
    {
        if (!path.StartsWith("ThemeCreator", StringComparison.Ordinal))
            return !_disposed && _bindings is ICuiWritableBindingContext writable && writable.TrySetValue(path, value);
        if (!CanEdit || _draft is null) return false;
        var key = path[12..]; var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        bool Select(int maximum, Action<int> assign)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selected) || selected < 0 || selected >= maximum) return false;
            assign(selected); return true;
        }
        switch (key)
        {
            case "Name": _draft.Name = text; break;
            case "AccentIndex": if (!Select(3, selected => _accentIndex = selected)) return false; break;
            case "FillKindIndex": if (!Select(3, selected => Accent.Kind = selected)) return false; break;
            case "GradientKindIndex": if (!Select(3, selected => Accent.GradientKind = selected)) return false; break;
            case "GradientAngle": Accent.GradientAngle = text; break;
            case "GradientCenterX": Accent.CenterX = text; break;
            case "GradientCenterY": Accent.CenterY = text; break;
            case "GradientRadius": Accent.Radius = text; break;
            case "StopIndex": if (!Select(Accent.Stops.Count, selected => Accent.StopIndex = selected)) return false; break;
            case "StopPosition": Accent.Stops[Accent.StopIndex].Position = text; break;
            case "PatternKindIndex": if (!Select(3, selected => Accent.PatternKind = selected)) return false; break;
            case "PatternSize": Accent.PatternSize = text; break;
            case "PatternSpacing": Accent.PatternSpacing = text; break;
            case "PatternAngle": Accent.PatternAngle = text; break;
            case "PatternColourIndex": if (!Select(2, selected => Accent.PatternColourIndex = selected)) return false; break;
            case "ColourHex": Colour.Hex = text; break;
            case "ColourRed": case "ColourGreen": case "ColourBlue":
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var channel) || !double.IsFinite(channel) || channel < 0 || channel > 255) return false;
                Colour.SetChannel(key == "ColourRed" ? 0 : key == "ColourGreen" ? 1 : 2, (int)Math.Round(channel)); break;
            case "Transparency": Colour.Transparency = text; break;
            case "BackgroundBlur": Colour.Blur = text; break;
            case "FontRoleIndex": if (!Select(7, selected => _fontIndex = selected)) return false; break;
            case "FontFamilyIndex":
                if (!Select(CuiCustomThemeFonts.Families.Count, selected =>
                {
                    Font.Family = CuiCustomThemeFonts.Families[selected];
                    if (!int.TryParse(Font.Weight, out var weight) || !CuiCustomThemeFonts.Weights(Font.Family).Contains(weight))
                        Font.Weight = CuiCustomThemeFonts.Weights(Font.Family)[0].ToString(CultureInfo.InvariantCulture);
                })) return false; break;
            case "FontWeight": Font.Weight = text; break;
            case "FontWeightIndex": if (!Select(CuiCustomThemeFonts.Weights(Font.Family).Count, selected => Font.Weight = CuiCustomThemeFonts.Weights(Font.Family)[selected].ToString(CultureInfo.InvariantCulture))) return false; break;
            case "FontSize": Font.Size = text; break;
            case "Roundness":
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var roundness) || !double.IsFinite(roundness) || roundness < 0 || roundness > 100) return false;
                _draft.Roundness = Math.Round(roundness); break;
            default: return false;
        }
        _status = null; ValidateAndPreview(); Publish(); return true;
    }

    public bool? IsActionAvailable(string command) => command switch
    {
        "ThemeCreatorSave" => CanSave,
        "ThemeCreatorCancel" => CanEdit,
        "ThemeCreatorUseCurrentRevision" => CanUseCurrentRevision,
        "ThemeCreatorAddStop" => CanEdit && Accent.Stops.Count < 16,
        "ThemeCreatorRemoveStop" => CanEdit && Accent.StopIndex > 0 && Accent.StopIndex < Accent.Stops.Count - 1,
        "ThemeCreatorSelectPrimary" or "ThemeCreatorSelectSecondary" or "ThemeCreatorSelectTertiary" => CanEdit,
        _ => _disposed ? false : (_actions as ICuiActionAvailability)?.IsActionAvailable(command)
    };
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        DispatchWithLifetimeAsync(command, parameter, new(default, cancellationToken));

    public async ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
    {
        if (_disposed) return;
        if (!command.StartsWith("ThemeCreator", StringComparison.Ordinal))
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
        if (IsActionAvailable(command) != true || _draft is null) return;
        switch (command)
        {
            case "ThemeCreatorCancel": Close(); return;
            case "ThemeCreatorUseCurrentRevision": _opening = _preferences.Current; break;
            case "ThemeCreatorSelectPrimary": _accentIndex = 0; break;
            case "ThemeCreatorSelectSecondary": _accentIndex = 1; break;
            case "ThemeCreatorSelectTertiary": _accentIndex = 2; break;
            case "ThemeCreatorAddStop":
                // Insert in the largest remaining interval, keeping endpoint stops intact.
                var pairs = Accent.Stops.Zip(Accent.Stops.Skip(1), (left, right) => (left, right)).ToArray();
                try
                {
                    var interval = pairs.Select((pair, index) => (pair, index, width: Number(pair.right.Position, "Stop position") - Number(pair.left.Position, "Stop position"))).MaxBy(item => item.width);
                    var position = (Number(interval.pair.left.Position, "Stop position") + Number(interval.pair.right.Position, "Stop position")) / 2;
                    Accent.StopIndex = interval.index + 1;
                    Accent.Stops.Insert(Accent.StopIndex, new(Format(position), new(interval.pair.left.Colour.Build())));
                }
                catch (CuiCustomThemeException error) { _status = error.Message; Publish(); return; }
                break;
            case "ThemeCreatorRemoveStop": Accent.Stops.RemoveAt(Accent.StopIndex); Accent.StopIndex--; break;
            case "ThemeCreatorSave":
                CuiCustomTheme theme;
                try { theme = BuildValidated(); }
                catch (CuiCustomThemeException error) { _validation = error.Message; Publish(); return; }
                _busy = true; _status = "Saving theme…"; Publish();
                try
                {
                    // The delegate compares this exact opening owner/revision, then uses
                    // the original account CAS operation. View retirement never invents settlement.
                    var result = await _save(theme, _opening!, lifetime.CallerCancellation);
                    if (result.Succeeded) { _status = result.Diagnostic ?? "Theme saved."; _busy = false; Close(); }
                    else _status = result.Diagnostic ?? "The theme could not be saved. Your draft is preserved.";
                }
                catch (OperationCanceledException) { _status = "Saving was interrupted. Your draft is preserved; check saved appearance before retrying."; throw; }
                catch (Exception) { _status = "Saving could not be confirmed. Your draft is preserved; check saved appearance before retrying."; throw; }
                finally { _busy = false; Publish(); }
                return;
        }
        _status = null; ValidateAndPreview(); Publish();
    }

    private CuiCustomTheme BuildValidated()
    {
        var result = _draft!.Build().Validate();
        if (string.Equals(result.Name, PreviewThemeName, StringComparison.OrdinalIgnoreCase) || _preferences.Catalog.TryResolve(result.Name, out _))
            throw new CuiCustomThemeException("CUI_CUSTOM_NAME_COLLISION", "That theme name is already in use. Choose another name.");
        return result;
    }
    private void ValidateAndPreview()
    {
        if (_draft is null) return;
        try { _ = BuildValidated(); _validation = null; }
        catch (CuiCustomThemeException error) { _validation = error.Message; }
        try
        {
            var preview = (_draft.Build() with { Id = _previewId, Name = PreviewThemeName }).Validate();
            var fingerprint = CuiCustomThemeContract.Serialize(preview);
            if (fingerprint != _previewFingerprint)
            {
                _preferences.Catalog.UpsertCustomTheme(preview, ephemeral: true);
                _previewFingerprint = fingerprint;
            }
        }
        catch (CuiCustomThemeException) { /* Keep the last valid preview while an input is incomplete. */ }
    }
    private void Close()
    {
        _open = false; Publish(); // Remove visible consumers before the ephemeral named scope.
        _preferences.Catalog.RemoveCustomTheme(_previewId);
        _previewFingerprint = null;
        _draft = new(CuiCustomThemeContract.CreateDefault()); _opening = null; _validation = null;
    }
    private void OwnerChanged(object? sender, PropertyChangedEventArgs args) => Publish(args.PropertyName);
    private void PreferencesChanged(object? sender, EventArgs args) => Publish();
    private void Publish(string? name = null)
    {
        if (_disposed) return;
        foreach (var observer in PropertyChanged?.GetInvocationList() ?? [])
            try { ((PropertyChangedEventHandler)observer)(this, new(name)); }
            catch (Exception) { _presentationFailure = true; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _open = false; _disposed = true;
        if (_bindings is INotifyPropertyChanged observable) observable.PropertyChanged -= OwnerChanged;
        _preferences.Changed -= PreferencesChanged;
        _preferences.Catalog.RemoveCustomTheme(_previewId);
        PropertyChanged = null;
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static double Number(string value, string label) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
        ? number : throw new CuiCustomThemeException("CUI_CUSTOM_NUMBER", $"{label} must be a finite number.");
    private sealed class ThemeDraft
    {
        public string Id, Name;
        public CuiTheme BaseTheme;
        public AccentDraft[] Accents;
        public FontDraft[] Fonts;
        public double Roundness;
        public ThemeDraft(CuiCustomTheme source)
        {
            Id = source.Id; Name = source.Name; BaseTheme = source.BaseTheme; Roundness = source.ButtonRoundnessPercent;
            Accents = [new(source.PrimaryAccent), new(source.SecondaryAccent), new(source.TertiaryAccent)];
            Fonts = [new(source.Fonts.Main), new(source.Fonts.PrimaryButton), new(source.Fonts.SecondaryButton), new(source.Fonts.TertiaryButton), new(source.Fonts.Heading), new(source.Fonts.Body), new(source.Fonts.Code)];
        }
        public CuiCustomTheme Build() => new(1, Id, Name, BaseTheme, Accents[0].Build(), Accents[1].Build(), Accents[2].Build(),
            new(Fonts[0].Build(), Fonts[1].Build(), Fonts[2].Build(), Fonts[3].Build(), Fonts[4].Build(), Fonts[5].Build(), Fonts[6].Build()), Roundness);
    }
    private sealed class AccentDraft
    {
        public int Kind, GradientKind, PatternKind, StopIndex, PatternColourIndex;
        public string GradientAngle = "0", CenterX = "50", CenterY = "50", Radius = "50", PatternSize = "8", PatternSpacing = "8", PatternAngle = "0";
        public ColourDraft Solid, PatternForeground, PatternBackground;
        public List<StopDraft> Stops;
        public AccentDraft(CuiCustomFill source)
        {
            Kind = (int)source.Kind; Solid = new(source.Colour); PatternForeground = new(source.Pattern?.Foreground ?? source.Colour); PatternBackground = new(source.Pattern?.Background ?? source.Colour with { TransparencyPercent = 85 });
            var gradient = source.Gradient;
            Stops = gradient?.Stops.Select(stop => new StopDraft(Format(stop.Offset * 100), new(stop.Colour))).ToList()
                ?? [new("0", new(source.Colour)), new("100", new(source.Colour with { TransparencyPercent = 65 }))];
            if (gradient is not null) { GradientKind = (int)gradient.Kind; GradientAngle = Format(gradient.Angle); CenterX = Format(gradient.CenterX * 100); CenterY = Format(gradient.CenterY * 100); Radius = Format(gradient.Radius * 100); }
            if (source.Pattern is { } pattern) { PatternKind = (int)pattern.Kind; PatternSize = Format(pattern.Size); PatternSpacing = Format(pattern.Spacing); PatternAngle = Format(pattern.Angle); }
        }
        public CuiCustomFill Build() => new((CuiCustomFillKind)Kind, Solid.Build(),
            new((CuiCustomGradientKind)GradientKind, Stops.Select(stop => new CuiCustomGradientStop(Number(stop.Position, "Stop position") / 100, stop.Colour.Build())).ToArray(),
                Number(GradientAngle, "Gradient angle"), Number(CenterX, "Gradient centre X") / 100, Number(CenterY, "Gradient centre Y") / 100, Number(Radius, "Gradient radius") / 100),
            new((CuiCustomPatternKind)PatternKind, PatternForeground.Build(), PatternBackground.Build(), Number(PatternSize, "Pattern size"), Number(PatternSpacing, "Pattern spacing"), Number(PatternAngle, "Pattern angle")));
    }
    private sealed class StopDraft(string position, ColourDraft colour) { public string Position = position; public ColourDraft Colour = colour; }
    private sealed class ColourDraft(CuiCustomColour source)
    {
        private string _hex = source.Hex, _validHex = source.Hex;
        public string Hex
        {
            get => _hex;
            set
            {
                _hex = value;
                if (value.Length == 7 && value[0] == '#' && !value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF".AsSpan())) _validHex = value;
            }
        }
        public string Transparency = Format(source.TransparencyPercent), Blur = Format(source.BackgroundBlur);
        public string ValidHex => _validHex;
        public int Channel(int channel) => int.Parse(ValidHex.AsSpan(channel * 2 + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        public void SetChannel(int channel, int value)
        {
            var channels = new[] { Channel(0), Channel(1), Channel(2) }; channels[channel] = value;
            Hex = $"#{channels[0]:X2}{channels[1]:X2}{channels[2]:X2}";
        }
        public CuiCustomColour Build() => new(Hex, Number(Transparency, "Transparency"), Number(Blur, "Gaussian background blur"));
    }
    private sealed class FontDraft(CuiCustomFont source)
    {
        public string Family = source.Family, Weight = source.Weight.ToString(CultureInfo.InvariantCulture), Size = Format(source.Size);
        public CuiCustomFont Build() => new(Family, int.TryParse(Weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) ? weight : 0, Number(Size, "Font size"));
    }
}
