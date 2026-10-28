// BoardsTheme adapts the shared CUI Boards palette to the app's Light/Dark
// appearance toggle. Code-built controls and .cui chrome consume the same
// canonical theme tokens; only semantic error feedback remains app-specific.

using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using CakeOS.Cui.Themes;

namespace CakeOS.Apps.Boards.App;

public enum BoardsThemeMode
{
    Light = 0,
    Dark = 1,
}

public sealed record BoardsPalette(
    string AppBackground,
    string Surface,
    string Card,
    string PageBackground,
    string Text,
    string SecondaryText,
    string Border,
    string Accent,
    string AccentContrast,
    string Success,
    string Warning,
    string Error,
    string CodeBackground);

public static class BoardsTheme
{
    public static BoardsThemeMode Mode { get; private set; } = BoardsThemeMode.Light;

    /// <summary>Boards uses its owner-resolved semantic palette for surfaces, actions and icons.</summary>
    public static CuiPalette SharedPalette
    {
        get
        {
            var appearance = _ownerPreference?.Appearance ?? (Mode == BoardsThemeMode.Dark ? CuiAppearance.Dark : CuiAppearance.Bright);
            return _ownerPreference is { } owner
                ? CuiSurfacePaletteCatalog.For("Boards", appearance, owner.Theme.BaseTheme,
                    owner.EffectiveAccent.ResolveColour("app.boards"), "app.boards")
                : CuiSurfacePaletteCatalog.For("Boards", appearance, ResolvedPreference);
        }
    }

    public static BoardsPalette Current => FromSharedPalette(SharedPalette);

    public static event Action? Changed;

    /// <summary>Boards resolves its own effective preference without mutating the shared
    /// host catalog: the owner-corrected theme contract requires child apps to read the
    /// user's preference and keep unrelated surfaces unchanged (owner UX03 report).</summary>
    private static CuiTheme? _resolvedPreference;
    private static CuiThemeResolution? _ownerPreference;

    /// <summary>Borrow the actual Home observation; Boards never persists or changes it.</summary>
    public static void ApplyOwnerPreference(CuiThemeResolution preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (_ownerPreference == preference) return;
        _ownerPreference = preference;
        _resolvedPreference = preference.Theme.BaseTheme;
        if (preference.Appearance is { } appearance)
            Mode = appearance is CuiAppearance.Dark or CuiAppearance.SuperDark ? BoardsThemeMode.Dark : BoardsThemeMode.Light;
        Changed?.Invoke();
    }

    public static CuiTheme ResolvedPreference => _resolvedPreference ?? CuiTheme.Professional;

    /// <summary>Read the shared Haven theme choice into Boards' local resolver before the
    /// Boards CUI tree is built. This deliberately does NOT write the shared
    /// CuiSurfacePaletteCatalog: unrelated surfaces keep their host-owned theme.</summary>
    public static void LoadGlobalThemePreference(string? dataDirectory = null)
    {
        _ownerPreference = null;
        _resolvedPreference = CuiThemePreferenceReader.Read(dataDirectory);
    }

    /// <summary>Seed Boards' Light/Dark mode from the Home appearance preference. A fresh
    /// development profile has no board-local settings file at all, and the previous default
    /// ("Light") force-enabled light appearance on open even when Home runs dark, which the
    /// owner reported. Appearance 2 (Dark) and 3 (SuperDark) mean dark; 0/1 stay light. A
    /// saved board-local dark setting is applied afterwards and still wins.</summary>
    public static void LoadGlobalAppearancePreference(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Haven");
        try
        {
            using var stream = File.OpenRead(Path.Combine(directory, "preferences.json"));
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                document.RootElement.TryGetProperty("havenUiAppearance", out var value) &&
                value.ValueKind == System.Text.Json.JsonValueKind.Number &&
                value.TryGetInt32(out var appearance))
                SetMode(appearance is >= 2 and <= 3 ? BoardsThemeMode.Dark : BoardsThemeMode.Light);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.Text.Json.JsonException) { }
    }

    public static void SetMode(BoardsThemeMode mode)
    {
        if (Mode == mode && _ownerPreference is null)
            return;
        _ownerPreference = null;
        Mode = mode;
        Changed?.Invoke();
    }

    public static void Toggle() =>
        SetMode(Mode == BoardsThemeMode.Light ? BoardsThemeMode.Dark : BoardsThemeMode.Light);

    public static SolidColorBrush Brush(string hex) =>
        new(Color.Parse(hex));

    public static IBrush AppBackgroundBrush => Brush(Current.AppBackground);
    public static IBrush SurfaceBrush => Brush(Current.Surface);
    public static IBrush CardBrush => Brush(Current.Card);
    public static IBrush PageBackgroundBrush => Brush(Current.PageBackground);
    public static IBrush TextBrush => Brush(Current.Text);
    public static IBrush SecondaryTextBrush => Brush(Current.SecondaryText);
    public static IBrush BorderBrush => Brush(Current.Border);
    public static IBrush AccentBrush => Brush(Current.Accent);
    public static IBrush AccentContrastBrush => Brush(Current.AccentContrast);
    public static IBrush ErrorBrush => Brush(Current.Error);
    public static IBrush SuccessBrush => Brush(Current.Success);

    private static BoardsPalette FromSharedPalette(CuiPalette palette) => new(
        AppBackground: Hex(palette.TideBase),
        Surface: Hex(palette.Panel),
        Card: Hex(palette.Panel2),
        PageBackground: Hex(palette.TideBase),
        Text: Hex(palette.Text),
        SecondaryText: Hex(palette.Muted),
        Border: Hex(palette.Line),
        Accent: Hex(palette.Accent),
        AccentContrast: Hex(palette.AccentInk),
        Success: Hex(palette.AccentStrong),
        Warning: Hex(palette.Attention),
        Error: Mode == BoardsThemeMode.Dark ? "#FFF28B82" : "#FFD32F2F",
        CodeBackground: Hex(palette.Panel3));

    private static string Hex(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryBrush(string? hex, out IBrush brush)
    {
        brush = Brushes.Transparent;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        try
        {
            brush = Brush(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>True when a background is dark enough to need light text.</summary>
    public static bool IsDarkBackground(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        try
        {
            var color = Color.Parse(hex.Trim());
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
            return luminance < 0.35;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Readable text for an authored background when no foreground was authored.</summary>
    public static IBrush ContrastText(string? backgroundHex)
    {
        // Theme text remains correct on transparent document blocks. An authored
        // light highlight needs an explicit dark foreground in dark appearance.
        if (string.IsNullOrWhiteSpace(backgroundHex))
            return TextBrush;
        return IsDarkBackground(backgroundHex) ? Brush("#FFF5F5F5") : Brush("#FF212121");
    }
}
