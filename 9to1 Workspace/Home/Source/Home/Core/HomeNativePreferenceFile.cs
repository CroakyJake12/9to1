using System.Text.Json;
using System.Text.Json.Nodes;

namespace HavenOS.Home.Core;

/// <summary>A path observation captured by the trusted original Home producer. This is not
/// profile authority or a settings-write grant; the native owner still checks its actual actor.</summary>
public sealed record HomeNativePreferenceBinding(string Path, bool CanWrite, string? Diagnostic = null);

/// <summary>Atomic adapter for the existing native preferences.json owner. No second preference store or account replica.</summary>
public static class HomeNativePreferenceFile
{
    public const string ThemeKey = "homeThemePreference";
    private const string RevisionKey = "homeThemePreferenceRevision";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ThemeFields = ["havenUiThemeName", "havenUiAppearance", "fontFamilyName", "overrideAccentColour", "accentColourName"];

    /// <summary>Preserve the existing standalone startup reader's native preference location,
    /// independently of Home's candidate artifact/database roots. Never migrate competing files.</summary>
    public static HomeNativePreferenceBinding CaptureExistingReaderBinding(string artifactDataDirectory, string? capturedReaderDataDirectory = null)
    {
        var configured = capturedReaderDataDirectory ?? Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = string.IsNullOrWhiteSpace(configured) ? System.IO.Path.Combine(appData, "Haven") : configured;
        var reader = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, "preferences.json"));
        var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(artifactDataDirectory, "preferences.json"));
        if (string.IsNullOrWhiteSpace(configured) && string.IsNullOrWhiteSpace(appData))
            return new(reader, false, "The operating system did not supply a native preference location. Your current appearance is preserved; changes are unavailable.");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (!comparer.Equals(reader, candidate) && File.Exists(candidate))
            return new(reader, false, "Home has saved appearance settings in another location. Both files have been preserved; appearance changes are unavailable.");
        return new(reader, true);
    }

    /// <summary>Faithful legacy projection shared by native hosts. Colour resolution is supplied
    /// by the existing native palette catalogue; unknown values are preserved through refusal.</summary>
    public static HomeThemePreference ProjectLegacyTheme(JsonObject document, HomeThemeScope scope,
        Func<string?, int, string> resolveAccent)
    {
        var name = document["havenUiThemeName"]?.GetValue<string>() ?? "Professional";
        var appearance = document["havenUiAppearance"]?.GetValue<int>() ?? 3;
        var appearanceName = appearance switch
        { 0 => "SuperBright", 1 => "Bright", 2 => "Dark", 3 => "SuperDark", _ => throw new InvalidDataException("The saved native appearance is unsupported and was preserved.") };
        var accent = HomeThemeAccentPreference.Default;
        if (document["overrideAccentColour"]?.GetValue<bool>() == true)
            accent = new(HomeThemeAccentOverrideMode.AllApps, resolveAccent(document["accentColourName"]?.GetValue<string>(), appearance));
        return new(HomeThemePreference.CurrentSchemaVersion, scope, name, 0,
            new(InterfaceFontFamily: document["fontFamilyName"]?.GetValue<string>()))
        { Appearance = appearanceName, Accent = accent };
    }

    public static JsonObject Read(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonNode.Parse(File.ReadAllText(path), new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject ?? throw new InvalidDataException("Native preferences require an object."); }
        catch (JsonException error) { throw new InvalidDataException("Native preferences are corrupt; the file was preserved.", error); }
    }

    public static HomeThemePreference Resolve(JsonObject document, HomeThemeScope scope,
        Func<JsonObject, HomeThemeScope, HomeThemePreference> projectLegacy)
    {
        if (document[ThemeKey] is not null)
        {
            HomeThemePreference stored;
            try { stored = document[ThemeKey]!.Deserialize<HomeThemePreference>(Json) ?? throw new InvalidDataException("Missing Home theme preference."); }
            catch (JsonException error) { throw new InvalidDataException("Home theme preference is incompatible; preserve it for recovery.", error); }
            stored.Validate();
            if (stored.Scope != scope) throw new UnauthorizedAccessException("The native preference belongs to a different Home scope.");
            return stored;
        }
        return (projectLegacy(document, scope) with { Revision = ReadRevision(document) }).Validate();
    }

    private static long ReadRevision(JsonObject document)
    {
        if (document[RevisionKey] is null) return 0;
        var value = document[RevisionKey]!.GetValue<long>();
        if (value < 0) throw new InvalidDataException("Invalid native preference revision.");
        return value;
    }

    /// <summary>Legacy setters share the exact file/lease and update only fields changed since their own observation.</summary>
    public static JsonObject MergeLegacy(string path, JsonObject baseline, JsonObject proposed,
        Func<JsonObject, HomeThemeScope, HomeThemePreference> projectLegacy)
    {
        using var lease = Acquire(path, CancellationToken.None);
        var current = Read(path);
        var changed = proposed.Where(pair => !pair.Key.Equals(ThemeKey, StringComparison.OrdinalIgnoreCase) && !pair.Key.Equals(RevisionKey, StringComparison.OrdinalIgnoreCase) && !JsonNode.DeepEquals(baseline[pair.Key], pair.Value))
            .Select(pair => pair.Key).ToArray();
        foreach (var key in changed)
        {
            // A second owner changed the same field: preserve it and report a conflict.
            if (!JsonNode.DeepEquals(current[key], baseline[key]) && !JsonNode.DeepEquals(current[key], proposed[key]))
                throw new InvalidOperationException("Native preferences changed in another session. Refresh before saving.");
            current[key] = proposed[key]?.DeepClone();
        }
        var themeChanged = ThemeFields.Where(field => changed.Contains(field, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (current[ThemeKey] is not null)
        {
            var stored = current[ThemeKey]!.Deserialize<HomeThemePreference>(Json) ?? throw new InvalidDataException("Missing Home theme record.");
            stored.Validate(); // Unknown/corrupt envelopes must never be rewritten by an unrelated legacy setting.
            if (themeChanged.Length > 0)
            {
                var legacy = projectLegacy(current, stored.Scope);
                var next = stored with
                {
                    Revision = checked(stored.Revision + 1),
                    ThemeName = themeChanged.Contains("havenUiThemeName") ? legacy.ThemeName : stored.ThemeName,
                    Appearance = themeChanged.Contains("havenUiAppearance") ? legacy.Appearance : stored.Appearance,
                    Accessibility = themeChanged.Contains("fontFamilyName") ? stored.Accessibility with { InterfaceFontFamily = legacy.Accessibility.InterfaceFontFamily } : stored.Accessibility,
                    Accent = themeChanged.Any(key => key is "overrideAccentColour" or "accentColourName")
                        ? new HomeThemeAccentPreference(legacy.Accent?.Mode ?? HomeThemeAccentOverrideMode.None,
                            legacy.Accent?.AllAppsColour ?? stored.Accent?.AllAppsColour, stored.Accent?.IndividualApps)
                        : stored.Accent
                };
                PutTheme(current, next.Validate());
            }
        }
        else if (themeChanged.Length > 0) current[RevisionKey] = checked(ReadRevision(current) + 1);
        if (changed.Length > 0) Write(path, current);
        return current;
    }

    public static async Task<HomeThemeWriteResult> CompareExchangeAsync(string path, HomeThemeScope scope,
        long revision, HomeThemePreference candidate, Func<JsonObject, HomeThemeScope, HomeThemePreference> projectLegacy,
        Func<CancellationToken, ValueTask<bool>> authorise, CancellationToken token)
    {
        using var lease = await AcquireAsync(path, token).ConfigureAwait(false);
        var current = Read(path);
        var stored = Resolve(current, scope, projectLegacy);
        if (stored.Revision != revision) return new(false, "RevisionConflict");
        if (candidate.Scope != scope || candidate.Revision != checked(revision + 1)) return new(false, "InvalidThemeReceipt");
        candidate.Validate();
        PutTheme(current, candidate);
        // Compatible legacy readers observe the same selection; no custom name is mapped to Glow.
        current["havenUiThemeName"] = candidate.ThemeName;
        if (candidate.Appearance is { } appearance)
            current["havenUiAppearance"] = appearance switch { "SuperBright" => 0, "Bright" => 1, "Dark" => 2, "SuperDark" => 3, _ => throw new ArgumentException("Invalid appearance.") };
        current["fontFamilyName"] = candidate.Accessibility.InterfaceFontFamily;
        // Recheck current authority while holding the same file lease immediately before atomic replacement.
        if (!await authorise(token).ConfigureAwait(false)) return new(false, "PermissionDenied");
        token.ThrowIfCancellationRequested();
        Write(path, current);
        return new(true, "Succeeded", candidate);
    }

    private static void PutTheme(JsonObject document, HomeThemePreference preference)
    {
        var serialized = JsonSerializer.SerializeToNode(preference, Json)!.AsObject();
        var envelope = document[ThemeKey] as JsonObject;
        if (envelope is null)
        {
            envelope = new JsonObject();
            document[ThemeKey] = envelope;
        }
        // Replace known typed fields while preserving compatible additive members.
        // Nested structured records (scope/accessibility/accent) merge their known
        // fields instead of replacing the whole object, so unknown siblings survive.
        // Known collections/dictionaries such as accent.individualApps are replaced
        // as complete values, allowing deliberate removals to take effect.
        foreach (var pair in serialized)
        {
            if (pair.Key is "scope" or "accessibility" or "accent" &&
                pair.Value is JsonObject sourceNested &&
                envelope[pair.Key] is JsonObject targetNested)
            {
                foreach (var nested in sourceNested)
                    targetNested[nested.Key] = nested.Value?.DeepClone();
            }
            else
            {
                envelope[pair.Key] = pair.Value?.DeepClone();
            }
        }
        document[RevisionKey] = preference.Revision;
    }

    private static void Write(string path, JsonObject value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value, Json); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FileStream TryAcquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static FileStream Acquire(string path, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return TryAcquire(path); }
            catch (IOException) when (Environment.TickCount64 < deadline) { Thread.Sleep(10); }
        }
    }
    private static async Task<FileStream> AcquireAsync(string path, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return TryAcquire(path); }
            catch (IOException) when (Environment.TickCount64 < deadline) { await Task.Delay(10, token).ConfigureAwait(false); }
        }
    }
}
