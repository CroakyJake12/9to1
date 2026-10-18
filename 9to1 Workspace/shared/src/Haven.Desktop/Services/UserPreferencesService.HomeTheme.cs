using System.Text.Json.Nodes;
using Haven.Core;
using Haven.Desktop.HavenUI.Tokens;
using HavenOS.Home.Core;

namespace Haven.Desktop;

public sealed partial class UserPreferencesService
{
    internal string HomeThemePreferencePath => _path;

    internal static HomeThemePreference ProjectLegacyHomeTheme(JsonObject document, HomeThemeScope scope)
    {
        var name = document["havenUiThemeName"]?.GetValue<string>() ?? "Professional";
        var appearance = (HavenUiAppearance)(document["havenUiAppearance"]?.GetValue<int>() ?? (int)HavenUiAppearance.SuperDark);
        if (!Enum.IsDefined(appearance)) throw new InvalidDataException("The saved native appearance is unsupported and was preserved.");
        var accent = HomeThemeAccentPreference.Default;
        if (document["overrideAccentColour"]?.GetValue<bool>() == true)
        {
            var colour = AccentColourCatalog.Parse(document["accentColourName"]?.GetValue<string>());
            if (colour is null) throw new InvalidDataException("The saved native accent is unsupported and was preserved.");
            var anchor = AccentColourCatalog.Resolve(colour.Value, appearance).Primary;
            accent = new(HomeThemeAccentOverrideMode.AllApps, "#" + anchor[^6..]);
        }
        return new(HomeThemePreference.CurrentSchemaVersion, scope, name, 0,
            new(InterfaceFontFamily: document["fontFamilyName"]?.GetValue<string>()))
        { Appearance = appearance.ToString(), Accent = accent };
    }
}
