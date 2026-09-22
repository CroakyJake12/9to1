// BoardsTheme: the single source of product color truth for Boards.
// Semantic roles with Light/Dark branches. Code-built controls read these;
// .cui chrome keeps structural markup and receives the same values through
// ThemeApplier after load (no per-control magic hex in product code).
// Hues follow the established 9-1 Boards accent (#4A6FA5 slate blue).

using Avalonia.Media;

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

    public static readonly BoardsPalette Light = new(
        AppBackground: "#FFEDEDE9",
        Surface: "#FFF5F5F0",
        Card: "#FFFFFFFF",
        PageBackground: "#FFFFFFFF",
        Text: "#FF212121",
        SecondaryText: "#FF6B7280",
        Border: "#FFE0DED8",
        Accent: "#FF4A6FA5",
        AccentContrast: "#FFFFFFFF",
        Success: "#FF1E8E3E",
        Warning: "#FFE8710A",
        Error: "#FFD32F2F",
        CodeBackground: "#FFF1F3F4");

    public static readonly BoardsPalette Dark = new(
        AppBackground: "#FF1B1D21",
        Surface: "#FF23262B",
        Card: "#FF2C3036",
        PageBackground: "#FF26292F",
        Text: "#FFE8EAED",
        SecondaryText: "#FF9AA0A6",
        Border: "#FF3C4046",
        Accent: "#FF7AA5D2",
        AccentContrast: "#FF101418",
        Success: "#FF81C995",
        Warning: "#FFFDD663",
        Error: "#FFF28B82",
        CodeBackground: "#FF2B2F36");

    public static BoardsPalette Current => Mode == BoardsThemeMode.Dark ? Dark : Light;

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
    public static IBrush AccentBrush => Brush(Current.Accent);
    public static IBrush AccentContrastBrush => Brush(Current.AccentContrast);
    public static IBrush ErrorBrush => Brush(Current.Error);
    public static IBrush SuccessBrush => Brush(Current.Success);

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
