using System.Reflection;
using System.Text.Json;
using Avalonia.Media;

namespace CakeOS.Hui.Renderer;

/// <summary>
/// Real CakeOS/HUI theme pipeline. Values come from HUI/theme/tokens.json
/// (embedded at build; optional file override). The extended Haven vocabulary
/// is DERIVED from those file values with documented mix/lighten operations —
/// never hard-coded per-app colours. Missing/unparseable override files fall
/// back to the embedded theme (fail-closed); unknown token names throw.
/// </summary>
public sealed class CakeTheme
{
    private const string EmbeddedResourceName = "CakeOS.Hui.Renderer.haven-tokens.json";
    private readonly Dictionary<string, Color> _colors;

    private CakeTheme(Dictionary<string, Color> colors) => _colors = colors;

    public static CakeTheme Load(string? overridePath = null)
    {
        var json = LoadJson(overridePath) ?? LoadEmbedded();
        var baseColors = ParseBaseColors(json);
        return new CakeTheme(BuildVocabulary(baseColors));
    }

    public Color Resolve(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        token = token.Trim();
        // Literal colors are part of the token language so applications can
        // show data-driven colours (e.g. ink swatches) without inventing
        // one-off tokens: #RRGGBB[AA] or solid(r,g,b) with 0-255 channels.
        if (token.StartsWith('#'))
            return ParseHex(token);
        if (token.StartsWith("solid(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(')'))
            return ParseSolid(token);
        if (_colors.TryGetValue(token, out var color))
            return color;
        var match = _colors.FirstOrDefault(kvp =>
            string.Equals(kvp.Key, token, StringComparison.OrdinalIgnoreCase));
        if (match.Key is not null)
            return match.Value;
        throw new ArgumentException($"Unknown HUI brush token '{token}'.", nameof(token));
    }

    private static Color ParseSolid(string token)
    {
        var inner = token.Substring("solid(".Length, token.Length - "solid(".Length - 1);
        var parts = inner.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !byte.TryParse(parts[0], out var r)
            || !byte.TryParse(parts[1], out var g)
            || !byte.TryParse(parts[2], out var b))
        {
            throw new ArgumentException($"Invalid HUI solid color '{token}'; expected solid(r,g,b).", nameof(token));
        }
        return Color.FromRgb(r, g, b);
    }

    public IBrush Brush(Haven.UI.HavenBrush brush, double opacity)
    {
        var color = brush switch
        {
            Haven.UI.HavenSolidBrush solid => Color.FromArgb(solid.A, solid.R, solid.G, solid.B),
            Haven.UI.HavenTokenBrush token => Resolve(token.Token),
            _ => throw new NotSupportedException($"Unsupported HUI brush {brush.GetType().Name}."),
        };
        return new SolidColorBrush(ApplyOpacity(color, opacity));
    }

    private static string? LoadJson(string? overridePath)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(overridePath)
                ? Environment.GetEnvironmentVariable("CAKEOS_HUI_THEME_PATH")
                : overridePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static string LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException("Embedded HUI theme is missing from the renderer assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, Color> ParseBaseColors(string json)
    {
        using var document = JsonDocument.Parse(json);
        var colors = document.RootElement.GetProperty("colors");
        Color Get(string name) => ParseHex(colors.TryGetProperty(name, out var value)
            ? value.GetString() ?? throw new InvalidDataException($"HUI theme color '{name}' is empty.")
            : throw new InvalidDataException($"HUI theme is missing color '{name}'."));
        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["background"] = Get("background"),
            ["surface"] = Get("surface"),
            ["foreground"] = Get("foreground"),
            ["muted"] = Get("muted"),
            ["border"] = Get("border"),
            ["accent"] = Get("accent"),
            ["accentStrong"] = Get("accentStrong"),
            ["accentContrast"] = Get("accentContrast"),
            ["warning"] = Get("warning"),
            ["danger"] = Get("danger"),
        };
    }

    private static Dictionary<string, Color> BuildVocabulary(Dictionary<string, Color> b)
    {
        var black = Color.FromRgb(0, 0, 0);
        var map = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            // Direct file values.
            ["Background"] = b["background"],
            ["Surface"] = b["surface"],
            ["Border"] = b["border"],
            ["Accent"] = b["accent"],
            ["AccentStrong"] = b["accentStrong"],
            ["AccentContrast"] = b["accentContrast"],
            ["Warning"] = b["warning"],
            ["Danger"] = b["danger"],
            // Text ramps derived from foreground/muted.
            ["TextPrimary"] = b["foreground"],
            ["Foreground"] = b["foreground"],
            ["TextSecondary"] = b["muted"],
            ["TextSoft"] = Mix(b["muted"], b["background"], 0.45),
            ["TextMuted"] = Mix(b["muted"], b["background"], 0.25),
            ["TextOnAccent"] = b["accentContrast"],
            ["TextOnDanger"] = b["foreground"],
            ["ButtonTextPrimary"] = b["accentContrast"],
            ["ButtonTextSecondary"] = b["foreground"],
            // Accent family derived from the file accent.
            ["AccentHover"] = Lighten(b["accent"], 0.15),
            ["AccentSubtle"] = WithAlpha(b["accent"], 0.16),
            ["AccentSecondary"] = Mix(b["accent"], b["foreground"], 0.35),
            ["AccentSecondaryHover"] = Lighten(Mix(b["accent"], b["foreground"], 0.35), 0.12),
            ["AccentMuted"] = WithAlpha(b["accent"], 0.20),
            ["AccentTertiaryHover"] = Mix(b["accent"], b["background"], 0.30),
            ["AccentGlow"] = b["accent"],
            ["AccentSecondaryGlow"] = Mix(b["accent"], b["foreground"], 0.35),
            ["AccentTertiaryGlow"] = b["accent"],
            // Surfaces derived from surface/background.
            ["SurfaceRaised"] = Lighten(b["surface"], 0.07),
            ["SurfaceSubtle"] = Mix(b["surface"], b["background"], 0.50),
            ["SurfaceSecondary"] = Mix(b["surface"], b["background"], 0.25),
            ["SurfaceElevated"] = Lighten(b["surface"], 0.13),
            ["Overlay"] = WithAlpha(black, 0.55),
            ["Card"] = Lighten(b["surface"], 0.07),
            ["Shadow"] = black,
            ["DangerHover"] = Lighten(b["danger"], 0.12),
            ["DangerGlow"] = b["danger"],
            ["Transparent"] = Color.FromArgb(0, 0, 0, 0),
            ["None"] = Color.FromArgb(0, 0, 0, 0),
        };
        return map;
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            return Color.FromRgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        if (hex.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }
        throw new InvalidDataException($"HUI theme color '{hex}' is not #RRGGBB or #AARRGGBB.");
    }

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        byte Mix(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * t), 0d, 255d);
        return Color.FromArgb(Mix(a.A, b.A), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    private static Color Lighten(Color color, double amount) =>
        Mix(color, Color.FromRgb(255, 255, 255), amount);

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0d, 255d), color.R, color.G, color.B);

    private static Color ApplyOpacity(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0d, 255d);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
