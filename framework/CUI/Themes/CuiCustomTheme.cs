using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CakeOS.Cui.Themes;

// Account-safe values only. This contract contains no renderer objects, source code,
// asset URLs, paths or callbacks. The account owner stores canonical JSON after validation.
public enum CuiCustomFillKind { SolidColour = 0, Gradient = 1, Pattern = 2 }
public enum CuiCustomGradientKind { Linear = 0, Radial = 1, Conic = 2 }
public enum CuiCustomPatternKind { Stripes = 0, Checkerboard = 1, Dots = 2 }
public sealed record CuiCustomColour(string Hex, double TransparencyPercent = 0, double BackgroundBlur = 0);
public sealed record CuiCustomGradientStop(double Offset, CuiCustomColour Colour);
public sealed record CuiCustomGradient(CuiCustomGradientKind Kind, IReadOnlyList<CuiCustomGradientStop> Stops,
    double Angle = 0, double CenterX = .5, double CenterY = .5, double Radius = .5);
public sealed record CuiCustomPattern(CuiCustomPatternKind Kind, CuiCustomColour Foreground, CuiCustomColour Background,
    double Size = 8, double Spacing = 8, double Angle = 0);
public sealed record CuiCustomFill(CuiCustomFillKind Kind, CuiCustomColour Colour,
    CuiCustomGradient? Gradient = null, CuiCustomPattern? Pattern = null);
public sealed record CuiCustomFont(string Family, int Weight, double Size);
public sealed record CuiCustomFonts(CuiCustomFont Main, CuiCustomFont PrimaryButton, CuiCustomFont SecondaryButton,
    CuiCustomFont TertiaryButton, CuiCustomFont Heading, CuiCustomFont Body, CuiCustomFont Code);
public sealed record CuiCustomTheme(int SchemaVersion, string Id, string Name, CuiTheme BaseTheme,
    CuiCustomFill PrimaryAccent, CuiCustomFill SecondaryAccent, CuiCustomFill TertiaryAccent,
    CuiCustomFonts Fonts, double ButtonRoundnessPercent)
{
    public const int CurrentSchemaVersion = 1;
    public CuiCustomTheme Validate() => CuiCustomThemeContract.Validate(this);
}

public sealed class CuiCustomThemeException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

/// <summary>Bundled, deterministic font choices; an arbitrary installed family is not portable.</summary>
public static class CuiCustomThemeFonts
{
    public static IReadOnlyList<string> Families { get; } = Array.AsReadOnly(new[] { "Montserrat", "Inter", "DejaVu Sans" });
    public static IReadOnlyList<int> Weights(string family) => family switch
    {
        "Montserrat" => Array.AsReadOnly(new[] { 400, 500, 600, 700, 800, 900 }),
        "Inter" => Array.AsReadOnly(new[] { 400, 500, 600, 700 }),
        "DejaVu Sans" => Array.AsReadOnly(new[] { 400 }),
        _ => throw new CuiCustomThemeException("CUI_CUSTOM_FONT", "Select a bundled font family.")
    };
    public static string ResourceFamily(string family) => family switch
    {
        "Montserrat" => "avares://CakeOS.Cui.Runtime/Assets/Fonts/MontserratStatic#Montserrat",
        "Inter" => "avares://CakeOS.Cui.Runtime/Assets/Fonts/Inter#Inter",
        "DejaVu Sans" => "avares://CakeOS.Cui.Runtime/Assets/Fonts/DejaVuSans#DejaVu Sans",
        _ => throw new CuiCustomThemeException("CUI_CUSTOM_FONT", "Select a bundled font family.")
    };
}

public static class CuiCustomThemeContract
{
    public const int MaximumJsonLength = 32768;
    public const int MaximumLibraryJsonLength = 1048576;
    public const int MaximumThemes = 32;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    private static readonly CuiCustomThemeJsonContext JsonContext = new(JsonOptions);

    public static CuiCustomTheme CreateDefault(string name = "My theme", string? id = null)
    {
        var normal = new CuiCustomFont("Montserrat", 500, 14);
        static CuiCustomFill Fill(string hex) => new(CuiCustomFillKind.SolidColour, new(hex));
        return new(1, id ?? Guid.NewGuid().ToString("D"), name, CuiTheme.Professional,
            Fill("#687BCA"), Fill("#756AAD"), Fill("#577A82"),
            new(normal, normal with { Weight = 600 }, normal, normal, normal with { Size = 24, Weight = 700 },
                normal, new("DejaVu Sans", 400, 13)), 35);
    }

    public static CuiCustomTheme Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonLength)
            throw Error("JSON", "A custom theme must be a non-empty bounded JSON document.");
        try
        {
            return Validate(JsonSerializer.Deserialize(json, JsonContext.CuiCustomTheme)
                ?? throw Error("JSON", "A custom theme cannot be null."));
        }
        catch (JsonException) { throw Error("JSON", "The custom theme document is malformed or has unknown fields."); }
    }

    public static string Serialize(CuiCustomTheme theme)
    {
        var json = JsonSerializer.Serialize(Validate(theme), JsonContext.CuiCustomTheme);
        if (json.Length > MaximumJsonLength) throw Error("JSON", "The custom theme exceeds its size limit.");
        return json;
    }

    public static IReadOnlyList<CuiCustomTheme> DeserializeLibrary(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumLibraryJsonLength)
            throw Error("LIBRARY", "The custom theme library is empty or exceeds its size limit.");
        try
        {
            return ValidateLibrary(JsonSerializer.Deserialize(json, JsonContext.CuiCustomThemeArray)
                ?? throw Error("LIBRARY", "The custom theme library cannot be null."));
        }
        catch (JsonException) { throw Error("LIBRARY", "The custom theme library is malformed."); }
    }

    public static string SerializeLibrary(IEnumerable<CuiCustomTheme> themes)
    {
        var json = JsonSerializer.Serialize(ValidateLibrary(themes).ToArray(), JsonContext.CuiCustomThemeArray);
        if (json.Length > MaximumLibraryJsonLength) throw Error("LIBRARY", "The custom theme library exceeds its size limit.");
        return json;
    }

    private static IReadOnlyList<CuiCustomTheme> ValidateLibrary(IEnumerable<CuiCustomTheme> themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        var values = themes.Take(MaximumThemes + 1).Select(Validate).ToArray();
        if (values.Length > MaximumThemes) throw Error("LIBRARY", "An account can store up to 32 custom themes.");
        if (values.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != values.Length ||
            values.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
            throw Error("LIBRARY", "Custom theme IDs and names must be unique within the account.");
        return Array.AsReadOnly(values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Validates every inactive branch too, then copies mutable caller collections.</summary>
    public static CuiCustomTheme Validate(CuiCustomTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (theme.SchemaVersion != CuiCustomTheme.CurrentSchemaVersion) throw Error("VERSION", "Unsupported custom theme version.");
        if (!Guid.TryParseExact(theme.Id, "D", out var id)) throw Error("ID", "Custom themes require a stable UUID.");
        var name = theme.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl)) throw Error("NAME", "Theme names must contain 1–64 printable characters.");
        if (CuiThemeCatalog.All.Any(item => string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
            throw Error("NAME", "A custom theme cannot replace a built-in theme.");
        if (!Enum.IsDefined(theme.BaseTheme)) throw Error("BASE", "Unknown base theme.");
        RequireRange(theme.ButtonRoundnessPercent, 0, 100, "Button roundness");
        var fonts = theme.Fonts ?? throw Error("FONT", "All seven font roles are required.");
        return theme with
        {
            Id = id.ToString("D"), Name = name,
            PrimaryAccent = ValidateFill(theme.PrimaryAccent), SecondaryAccent = ValidateFill(theme.SecondaryAccent),
            TertiaryAccent = ValidateFill(theme.TertiaryAccent),
            Fonts = new(ValidateFont(fonts.Main), ValidateFont(fonts.PrimaryButton), ValidateFont(fonts.SecondaryButton),
                ValidateFont(fonts.TertiaryButton), ValidateFont(fonts.Heading), ValidateFont(fonts.Body), ValidateFont(fonts.Code))
        };
    }

    public static CuiCustomFill ValidateFill(CuiCustomFill fill)
    {
        if (fill is null || !Enum.IsDefined(fill.Kind)) throw Error("FILL", "Select a supported accent type.");
        var colour = ValidateColour(fill.Colour);
        CuiCustomGradient? gradient = null;
        if (fill.Gradient is { } source)
        {
            if (!Enum.IsDefined(source.Kind)) throw Error("GRADIENT", "Unknown gradient type.");
            RequireRange(source.Angle, -360, 360, "Gradient angle");
            RequireRange(source.CenterX, 0, 1, "Gradient centre X"); RequireRange(source.CenterY, 0, 1, "Gradient centre Y");
            RequireRange(source.Radius, .01, 2, "Gradient radius");
            if (source.Stops is null || source.Stops.Count is < 2 or > 16) throw Error("GRADIENT", "Gradients require 2–16 stops.");
            var stops = new List<CuiCustomGradientStop>(); double previous = -1;
            foreach (var stop in source.Stops)
            {
                if (stop is null) throw Error("GRADIENT", "A gradient stop cannot be null.");
                RequireRange(stop.Offset, 0, 1, "Gradient stop");
                if (stop.Offset < previous) throw Error("GRADIENT", "Gradient stops must be ordered.");
                previous = stop.Offset; stops.Add(stop with { Colour = ValidateColour(stop.Colour) });
            }
            if (stops[0].Offset != 0 || stops[^1].Offset != 1) throw Error("GRADIENT", "The first and last stops must be at 0 and 1.");
            gradient = source with { Stops = new ReadOnlyCollection<CuiCustomGradientStop>(stops) };
        }
        CuiCustomPattern? pattern = null;
        if (fill.Pattern is { } tile)
        {
            if (!Enum.IsDefined(tile.Kind)) throw Error("PATTERN", "Unknown pattern type.");
            RequireRange(tile.Size, 1, 128, "Pattern size"); RequireRange(tile.Spacing, 0, 128, "Pattern spacing");
            RequireRange(tile.Angle, -360, 360, "Pattern angle");
            pattern = tile with { Foreground = ValidateColour(tile.Foreground), Background = ValidateColour(tile.Background) };
        }
        if (fill.Kind == CuiCustomFillKind.Gradient && gradient is null) throw Error("GRADIENT", "A gradient accent requires gradient settings.");
        if (fill.Kind == CuiCustomFillKind.Pattern && pattern is null) throw Error("PATTERN", "A pattern accent requires pattern settings.");
        return fill with { Colour = colour, Gradient = gradient, Pattern = pattern };
    }

    public static CuiCustomColour ValidateColour(CuiCustomColour colour)
    {
        if (colour is null || colour.Hex is null || colour.Hex.Length != 7 || colour.Hex[0] != '#' || colour.Hex.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF".AsSpan()))
            throw Error("COLOUR", "Colours use #RRGGBB; transparency is a separate percentage.");
        RequireRange(colour.TransparencyPercent, 0, 100, "Transparency"); RequireRange(colour.BackgroundBlur, 0, 32, "Gaussian background blur");
        return colour with { Hex = colour.Hex.ToUpperInvariant() };
    }

    private static CuiCustomFont ValidateFont(CuiCustomFont font)
    {
        if (font is null) throw Error("FONT", "All font roles are required.");
        var family = CuiCustomThemeFonts.Families.FirstOrDefault(value => string.Equals(value, font.Family?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (family is null || !CuiCustomThemeFonts.Weights(family).Contains(font.Weight)) throw Error("FONT", "Select a bundled family and an available weight.");
        RequireRange(font.Size, 8, 72, "Font size");
        return font with { Family = family };
    }

    private static void RequireRange(double value, double min, double max, string label)
    {
        if (!double.IsFinite(value) || value < min || value > max) throw Error("RANGE", $"{label} must be between {min} and {max}.");
    }
    private static CuiCustomThemeException Error(string code, string message) => new("CUI_CUSTOM_" + code, message);
}

[JsonSerializable(typeof(CuiCustomTheme))]
[JsonSerializable(typeof(CuiCustomTheme[]))]
internal partial class CuiCustomThemeJsonContext : JsonSerializerContext;
