using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Selects named authored fragments without generating presentation in the host.</summary>
internal static class DashboardCuiDocument
{
    private static readonly Lazy<CuiDocument> Source = new(Read);

    internal static CuiDocument Fragment(string id)
    {
        var document = Source.Value;
        var theme = document.Components.Single(component => component.IsThemeScope);
        var fragment = theme.Children.Single(component => component.AuthoredId == id);
        var scope = new CuiComponent(theme.Type, theme.Name, theme.Classes, theme.Properties, theme.Actions,
            [fragment], theme.Condition, theme.List, theme.Span, theme.DefaultTheme, theme.Repeat,
            theme.ElseChildren, theme.Groups, theme.IsDefinition, theme.PropertyRegion);
        return document with { Components = [scope] };
    }

    private static CuiDocument Read()
    {
        using var stream = typeof(NewDashboardPage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Dashboard.cui")
            ?? throw new InvalidDataException("The Home dashboard CUI document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Dashboard.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home dashboard CUI document is invalid.");
        return document;
    }
}
