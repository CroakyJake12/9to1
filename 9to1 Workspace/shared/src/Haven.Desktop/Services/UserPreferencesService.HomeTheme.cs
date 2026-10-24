using System.Text.Json.Nodes;
using Haven.Core;
using Haven.Desktop.HavenUI.Tokens;
using HavenOS.Home.Core;

namespace Haven.Desktop;

public sealed partial class UserPreferencesService
{
    internal string HomeThemePreferencePath => _path;

    internal static HomeThemePreference ProjectLegacyHomeTheme(JsonObject document, HomeThemeScope scope) =>
        HomeNativePreferenceFile.ProjectLegacyTheme(document, scope, (name, appearance) =>
        {
            var colour = AccentColourCatalog.Parse(name)
                ?? throw new InvalidDataException("The saved native accent is unsupported and was preserved.");
            var anchor = AccentColourCatalog.Resolve(colour, (HavenUiAppearance)appearance).Primary;
            return "#" + anchor[^6..];
        });
}
