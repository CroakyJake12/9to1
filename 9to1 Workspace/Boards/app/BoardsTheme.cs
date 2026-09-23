// BoardsTheme adapts the shared CUI Boards palette to the app's Light/Dark
// appearance toggle. Code-built controls and .cui chrome consume the same
// canonical Glow tokens; only semantic error feedback remains app-specific.

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

    /// <summary>Boards uses the shared CUI Glow identity with a light or dark appearance.</summary>
    public static CuiPalette SharedPalette => CuiSurfacePaletteCatalog.For(
        "Boards",
        Mode == BoardsThemeMode.Dark ? CuiAppearance.Dark : CuiAppearance.Bright,
        CuiTheme.Glow);

    public static BoardsPalette Current => FromSharedPalette(SharedPalette);

    public static event Action? Changed;

    public static void SetMode(BoardsThemeMode mode)
    {
        if (Mode == mode)
            return;
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
    public static IBrush AccentBrush
    {
        get
        {
            // The canonical applier supplies a three-stop Glow gradient. Keep
            // a semantic solid fallback for isolated controls before startup.
            if (Application.Current?.TryGetResource("CuiAccentBrush", null, out var accent) == true
                && accent is IBrush accentBrush)
                return accentBrush;
            return Brush(Current.Accent);
        }
    }
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
