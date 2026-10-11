using System.Globalization;

namespace CakeOS.Cui.Language;

/// <summary>Canonical CUI grid sizes. Donor star syntax never crosses the authoring boundary.</summary>
public static class CuiGridSizes
{
    public static bool IsValid(string text)
    {
        var tokens = Tokens(text);
        return tokens.Length > 0 && tokens.All(token => token.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            || Number(token.EndsWith("fr", StringComparison.OrdinalIgnoreCase) ? token[..^2] : token));
    }
    public static string ToNative(string text)
    {
        if (!IsValid(text)) throw new FormatException("Grid sizes require Auto, nonnegative logical pixels or positive fractional units such as 1fr.");
        return string.Join(',', Tokens(text).Select(token => token.EndsWith("fr", StringComparison.OrdinalIgnoreCase) ? token[..^2] + "*" : token));
    }
    private static string[] Tokens(string text) => text.Split([',', ' ', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static bool Number(string token) => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0;
}
