using System.Text.Json;

namespace CakeOS.Cui.Themes;

/// <summary>
/// Reads the theme selected by Haven's UserPreferencesService without creating
/// a second settings owner. Standalone native hosts consume the same file;
/// Haven.Desktop remains responsible for writing and migrating preferences.
/// </summary>
public static class CuiThemePreferenceReader
{
    public static CuiTheme Read(string? dataDirectory = null)
    {
        try
        {
            var directory = dataDirectory ?? Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
            if (string.IsNullOrWhiteSpace(directory))
                directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Haven");

            using var stream = File.OpenRead(Path.Combine(directory, "preferences.json"));
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("havenUiThemeName", out var value)
                && value.ValueKind == JsonValueKind.String)
                return CuiThemeCatalog.Resolve(CuiThemeCatalog.Parse(value.GetString())).Theme;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }

        return CuiTheme.Glow;
    }
}
