using System.Text.RegularExpressions;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Xunit;

namespace HavenOS.Apps.Cards.CUI.MarkupTests;

/// <summary>
/// Parser/schema evidence only. This does not substitute for the real CUI runtime,
/// CUI button dispatch or Windows/mobile render and theme checks.
/// </summary>
public sealed class CardsMarkupTests
{
    private const string Resource = "HavenOS.Apps.Cards.CUI.MarkupTests.CardsWorkspace.cui";

    private static string Markup()
    {
        using var stream = typeof(CardsMarkupTests).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidDataException("The canonical authored Cards scene was not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void CanonicalMarkupParsesWithoutCuiLanguageErrors()
    {
        string source = Markup();
        var parser = new CuiRichParser();
        Assert.NotNull(parser.Parse(source, "CardsWorkspace.cui"));
        Assert.DoesNotContain(parser.Diagnostics.Diagnostics,
            diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
    }

    [Fact]
    public void RequiredActionsHaveStableNamesAndNoDuplicateControlIds()
    {
        string source = Markup();
        string[] required =
        [
            "9to1.Cards.Open", "9to1.Cards.CycleGrouping",
            "9to1.Cards.Next", "9to1.Cards.Previous", "9to1.Cards.Flip",
            "9to1.Cards.ToggleMode", "9to1.Cards.EditSide",
            "9to1.Cards.RateRed", "9to1.Cards.RateAmber", "9to1.Cards.RateGreen",
        ];
        foreach (string action in required)
            Assert.Contains($"action=\"{action}\"", source);
        string[] ids = Regex.Matches(source, "\\bid=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AuthoredSceneUsesExistingCuiControlsAndSemanticChrome()
    {
        string source = Markup();
        Assert.Contains("<Cui ", source);
        Assert.Contains("<ScrollViewer ", source);
        Assert.Contains("accessible-name=\"Focused vertical flashcard viewer\"", source);
        Assert.Contains("accessible-name=\"Subjects and topics\"", source);
        Assert.DoesNotContain("FluentTheme", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("font-family=\"Inter\"", source, StringComparison.OrdinalIgnoreCase);
    }
}
