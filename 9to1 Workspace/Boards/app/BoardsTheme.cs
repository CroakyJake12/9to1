// BoardsTheme adapts the shared CUI Boards palette to the app's Light/Dark
// appearance toggle. Code-built controls and .cui chrome consume the same
// canonical theme tokens; only semantic error feedback remains app-specific.

using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
    private static BoardsThemeMode _standaloneMode = BoardsThemeMode.Light;
    private static CuiTheme? _resolvedPreference;
    private sealed record SurfaceState(Control Root, CuiThemePreferenceContext? Context);
    private static readonly AsyncLocal<SurfaceState?> Surface = new();

    public static BoardsThemeMode Mode => Surface.Value is { } scope
        ? (IsDarkBackground(Hex(ResourceColor(scope.Root, "CuiPanelBrush", StandalonePalette.Panel)))
            ? BoardsThemeMode.Dark : BoardsThemeMode.Light)
        : _standaloneMode;
    // When nobody has handed Boards the shared Haven theme choice, follow the theme the
    // application is actually running (CuiSurfacePaletteCatalog.ActiveTheme is set by the CUI
    // theme scope, framework/CUI/Themes/CuiThemeScope.cs:174) instead of inventing a hardcoded
    // Professional default. Measured on 28 October: nine visual-capture checks set the surface to
    // Glow and still got Professional back, i.e. Boards presented a theme the rest of the app was
    // not using. The catalog's own default is Professional, so behaviour is unchanged until a
    // theme is genuinely active; an explicitly loaded preference still takes precedence.
    public static CuiTheme ResolvedPreference => _resolvedPreference ?? CuiSurfacePaletteCatalog.ActiveTheme;
    internal static CuiThemePreferenceContext? SurfaceThemeContext => Surface.Value?.Context;
    internal static CuiPalette StandalonePalette => CuiSurfacePaletteCatalog.For("Boards",
        _standaloneMode == BoardsThemeMode.Dark ? CuiAppearance.Dark : CuiAppearance.Bright, ResolvedPreference);

    /// <summary>Borrow only this control's already resolved CUI resources for one paint/action.
    /// The scope flows through owned async rendering and restores its previous owner on exit.</summary>
    public static IDisposable UseSurface(Control? root, CuiThemePreferenceContext? context = null)
    {
        var previous = Surface.Value;
        if (root is not null)
        {
            var inherited = previous is not null && (ReferenceEquals(root, previous.Root) ||
                root.GetVisualAncestors().Any(parent => ReferenceEquals(parent, previous.Root))) ? previous.Context : null;
            Surface.Value = new(root, context ?? inherited);
        }
        return new SurfaceLease(previous);
    }
    private sealed class SurfaceLease(SurfaceState? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; Surface.Value = previous; }
    }

    /// <summary>Compatibility colors projected from the SAME custom/accessibility resources
    /// that the owning CUI loader resolved. Native paints do not reconstruct Home's theme.</summary>
    public static CuiPalette SharedPalette
    {
        get
        {
            var fallback = StandalonePalette;
            if (Surface.Value is not { } scope) return fallback;
            var root = scope.Root;
            Color Read(string key, Color value) => ResourceColor(root, key, value);
            return fallback with
            {
                TideBase = Read("CuiBackgroundBrush", fallback.TideBase),
                Accent = Read("CuiAccentBrush", fallback.Accent),
                AccentSecondary = Read("CuiAccentSecondaryBrush", fallback.AccentSecondary),
                AccentStrong = Read("CuiAccentTertiaryBrush", fallback.AccentStrong),
                AccentSoft = Read("CuiAccentSoftBrush", fallback.AccentSoft),
                AccentInk = Read("CuiAccentForegroundBrush", fallback.AccentInk),
                Text = Read("CuiTextBrush", fallback.Text), TextSoft = Read("CuiTextSoftBrush", fallback.TextSoft),
                Muted = Read("CuiMutedBrush", fallback.Muted), Muted2 = Read("CuiMuted2Brush", fallback.Muted2),
                Panel = Read("CuiPanelBrush", fallback.Panel), Panel2 = Read("CuiPanel2Brush", fallback.Panel2),
                Panel3 = Read("CuiPanel3Brush", fallback.Panel3), PanelHover = Read("CuiPanelHoverBrush", fallback.PanelHover),
                Line = Read("CuiLineBrush", fallback.Line), LineStrong = Read("CuiLineStrongBrush", fallback.LineStrong),
                Button = Read("CuiButtonBrush", fallback.Button), ButtonHover = Read("CuiButtonHoverBrush", fallback.ButtonHover),
                ButtonPressed = Read("CuiButtonPressedBrush", fallback.ButtonPressed), Focus = Read("CuiFocusBrush", fallback.Focus),
                Attention = Read("CuiAttentionBrush", fallback.Attention), AttentionBorder = Read("CuiAttentionBorderBrush", fallback.AttentionBorder),
                Theme = root.TryFindResource("CuiTheme", out var theme) && theme is CuiTheme resolved ? resolved : fallback.Theme,
            };
        }
    }
    private static Color ResourceColor(Control root, string key, Color fallback)
    {
        if (!root.TryFindResource(key, out var value)) return fallback;
        if (value is ISolidColorBrush solid) return solid.Color;
        if (value is IGradientBrush gradient && gradient.GradientStops.Count > 0)
            return gradient.GradientStops.OrderBy(stop => Math.Abs(stop.Offset - .5)).First().Color;
        return value is Color color ? color : fallback;
    }
    private static IBrush ScopedBrush(string key, string fallback) =>
        Surface.Value is { } scope && scope.Root.TryFindResource(key, out var value) && value is IBrush brush
            ? brush : Brush(fallback);

    public static BoardsPalette Current => FromSharedPalette(SharedPalette);
    // Only standalone settings publish this process-wide legacy event.
    public static event Action? Changed;

    /// <summary>Read the shared Haven theme choice into Boards' local resolver before the
    /// Boards CUI tree is built. This deliberately does NOT write the shared
    /// CuiSurfacePaletteCatalog: unrelated surfaces keep their host-owned theme.</summary>
    public static void LoadGlobalThemePreference(string? dataDirectory = null)
    {
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
        if (_standaloneMode == mode) return;
        _standaloneMode = mode;
        Changed?.Invoke();
    }

    public static void Toggle() =>
        SetMode(Mode == BoardsThemeMode.Light ? BoardsThemeMode.Dark : BoardsThemeMode.Light);

    public static SolidColorBrush Brush(string hex) =>
        new(Color.Parse(hex));

    public static IBrush AppBackgroundBrush => ScopedBrush("CuiBackgroundBrush", Current.AppBackground);
    public static IBrush SurfaceBrush => ScopedBrush("CuiPanelBrush", Current.Surface);
    public static IBrush CardBrush => ScopedBrush("CuiPanel2Brush", Current.Card);
    public static IBrush PageBackgroundBrush => ScopedBrush("CuiBackgroundBrush", Current.PageBackground);
    public static IBrush TextBrush => ScopedBrush("CuiTextBrush", Current.Text);
    public static IBrush SecondaryTextBrush => ScopedBrush("CuiMutedBrush", Current.SecondaryText);
    public static IBrush BorderBrush => ScopedBrush("CuiLineBrush", Current.Border);
    public static IBrush AccentBrush => ScopedBrush("CuiAccentBrush", Current.Accent);
    public static IBrush AccentContrastBrush => ScopedBrush("CuiAccentForegroundBrush", Current.AccentContrast);
    public static IBrush ErrorBrush => ScopedBrush("CuiAccentNegativeBrush", Current.Error);
    public static IBrush SuccessBrush => ScopedBrush("CuiSuccessBrush", Current.Success);

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
