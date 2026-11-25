using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeSettingsDestinationPage
{
    private sealed record KeyboardReferenceRow(string Key, string Label, string Chord);

    // Avalonia's common keyboard navigation and the workspace's popup/activation conventions.
    private static readonly KeyboardReferenceRow[] NavigationKeys =
    [
        new("next-control", "Next control", "Tab"),
        new("previous-control", "Previous control", "Shift + Tab")
    ];

    private static readonly KeyboardReferenceRow[] MenuKeys =
    [
        new("close-popover", "Close a popover", "Esc"),
        new("activate", "Activate", "Enter / Space")
    ];

    private static readonly KeyboardReferenceRow[] EssentialKeys = [.. NavigationKeys, .. MenuKeys];

    private void PublishKeyboardReference()
    {
        (string title, KeyboardReferenceRow[] rows) = _category switch
        {
            "haven-shortcuts" => ("Keyboard essentials", EssentialKeys),
            "haven-key-navigation" => ("Navigation keys", NavigationKeys),
            "haven-key-menus" => ("Menus and controls", MenuKeys),
            _ => (string.Empty, [])
        };
        _bindings.Set("SettingsCatalogHasKeyboard", rows.Length > 0);
        _bindings.Set("SettingsCatalogKeyboardTitle", title);
        _bindings.Set("SettingsCatalogKeyboardRows", rows.Select(row => new Dictionary<string, object?>
        {
            ["Key"] = row.Key,
            ["Label"] = row.Label,
            ["Chord"] = row.Chord
        }).ToArray());
    }

    private static CuiComponent LoadKeyboardReference()
    {
        using var stream = typeof(HomeSettingsDestinationPage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.SettingsKeyboard.cui")
            ?? throw new InvalidDataException("The Settings keyboard reference is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "HomeSettingsKeyboard.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Settings keyboard reference is invalid: " + string.Join("; ",
                parser.Diagnostics.Diagnostics.Select(item => item.Code + " " + item.Message)));
        return document.Components.SelectMany(component => component.DescendantsAndSelf())
            .Single(component => component.AuthoredId == "home-settings-keyboard-map");
    }
}
