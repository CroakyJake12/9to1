using Avalonia.Media;

namespace Haven.Desktop.HavenUI.Tokens;

/// <summary>
/// Resolves authored document fonts without applying the user's interface font preference.
/// Substitution affects shaping only: document models retain the authored family name.
/// </summary>
internal static class HavenDocumentFont
{
    private static readonly FontFamily Calibri = new("Calibri, avares://Haven/Assets/Fonts/Carlito#Carlito");
    private static readonly FontFamily Carlito = new("Carlito, avares://Haven/Assets/Fonts/Carlito#Carlito");
    private static readonly FontFamily Montserrat = new("avares://Haven/Assets/Fonts/MontserratStatic#Montserrat");

    internal static FontFamily Resolve(string requested)
    {
        // Prefer an installed authored Calibri face. The bundled, metric-compatible Carlito
        // fallback is the same unmodified OFL font used by the pinned LibreOffice donor.
        if (requested.Equals("Calibri", StringComparison.OrdinalIgnoreCase)) return Calibri;
        if (requested.Equals("Carlito", StringComparison.OrdinalIgnoreCase)) return Carlito;
        if (requested.Equals(HavenUiFont.DefaultFamily, StringComparison.OrdinalIgnoreCase)) return Montserrat;
        return new FontFamily(requested);
    }
}
