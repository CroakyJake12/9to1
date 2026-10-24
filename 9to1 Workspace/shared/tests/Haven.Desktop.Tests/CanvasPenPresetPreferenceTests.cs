using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class CanvasPenPresetPreferenceTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public void Custom_pen_preset_survives_service_restart()
    {
        var preferences = new UserPreferencesService(_paths);
        var saved = preferences.SaveCanvasPenPreset(new CanvasPenPresetPreference(
            string.Empty, "Study pen", "Pen", "#FF336699", .7, 5, "Uniform"));

        Assert.NotNull(saved);
        var reopened = new UserPreferencesService(_paths);
        var restored = Assert.Single(reopened.CanvasPenPresets, value => value.Name == "Study pen");
        Assert.Equal(saved!.Id, restored.Id);
        Assert.Equal("#FF336699", restored.Color);
        Assert.Equal(.7, restored.Opacity, 3);
        Assert.Equal(5, restored.Thickness, 3);
        Assert.Equal("Uniform", restored.Effect);
    }

    [Fact]
    public void Malformed_canvas_preset_payload_does_not_poison_other_preferences()
    {
        var preferences = new UserPreferencesService(_paths);
        preferences.SetDefaultTabAppKey("canvas-test-app");
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["canvasPenPresets"] = "not-an-object";
        var original = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, original);

        var reopened = new UserPreferencesService(_paths);
        Assert.Equal("canvas-test-app", reopened.DefaultTabAppKey);
        Assert.False(reopened.CanvasPenPresetPreferencesWritable);
        Assert.NotNull(reopened.CanvasPenPresetPreferencesDiagnostic);
        Assert.Contains(reopened.CanvasPenPresets, value => value.Id == "builtin-blue");
        Assert.Null(reopened.SaveCanvasPenPreset(new CanvasPenPresetPreference("canvas-new", "New pen", "Pen", "#FF123456", 1, 2, "Pressure")));
        Assert.False(reopened.RemoveCanvasPenPreset("canvas-safe"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Null_tool_and_effect_fields_are_read_only_and_original_payload_is_preserved()
    {
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var original = StoreCanvasPresetPayload("""
            {"version":1,"customPresets":[{"id":"canvas-safe","name":"Recovered pen","tool":null,"color":"#FF123456","opacity":0.8,"thickness":2,"effect":null}]}
            """);

        var reopened = new UserPreferencesService(_paths);
        Assert.False(reopened.CanvasPenPresetPreferencesWritable);
        Assert.NotNull(reopened.CanvasPenPresetPreferencesDiagnostic);
        Assert.Contains(reopened.CanvasPenPresets, value => value.Id == "builtin-blue");
        Assert.Empty(reopened.CanvasPenPresets.Where(value => value.Id == "canvas-safe"));
        Assert.Null(reopened.SaveCanvasPenPreset(new CanvasPenPresetPreference("canvas-new", "New pen", "Pen", "#FF123456", 1, 2, "Pressure")));
        Assert.False(reopened.RemoveCanvasPenPreset("canvas-safe"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Missing_custom_preset_id_remains_read_only_and_preserves_original_payload()
    {
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var original = StoreCanvasPresetPayload("""
            {"version":1,"customPresets":[{"id":"","name":"Recovered identity","tool":"Pen","color":"#FF123456","opacity":0.8,"thickness":2,"effect":"Pressure"}]}
            """);

        var reopened = new UserPreferencesService(_paths);
        Assert.False(reopened.CanvasPenPresetPreferencesWritable);
        Assert.NotNull(reopened.CanvasPenPresetPreferencesDiagnostic);
        Assert.Contains(reopened.CanvasPenPresets, value => value.Id == "builtin-blue");
        Assert.Null(reopened.SaveCanvasPenPreset(new CanvasPenPresetPreference("canvas-new", "New pen", "Pen", "#FF123456", 1, 2, "Pressure")));
        Assert.False(reopened.RemoveCanvasPenPreset("canvas-recovered"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Explicit_null_canvas_preset_payload_is_read_only_and_preserved()
    {
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var original = StoreCanvasPresetPayload("null");

        var reopened = new UserPreferencesService(_paths);

        Assert.False(reopened.CanvasPenPresetPreferencesWritable);
        Assert.NotNull(reopened.CanvasPenPresetPreferencesDiagnostic);
        Assert.Contains(reopened.CanvasPenPresets, value => value.Id == "builtin-blue");
        Assert.False(reopened.RemoveCanvasPenPreset("canvas-any"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Invalid_field_type_is_read_only_and_preserves_the_unknown_payload()
    {
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var original = StoreCanvasPresetPayload("""
            {"version":1,"customPresets":[{"id":"canvas-safe","name":"Pen","tool":42,"color":"#FF123456","opacity":0.8,"thickness":2,"effect":"Pressure"}]}
            """);

        var reopened = new UserPreferencesService(_paths);

        Assert.False(reopened.CanvasPenPresetPreferencesWritable);
        Assert.NotNull(reopened.CanvasPenPresetPreferencesDiagnostic);
        Assert.Contains(reopened.CanvasPenPresets, value => value.Id == "builtin-blue");
        Assert.Null(reopened.SaveCanvasPenPreset(new CanvasPenPresetPreference("canvas-new", "New pen", "Pen", "#FF123456", 1, 2, "Pressure")));
        Assert.False(reopened.RemoveCanvasPenPreset("canvas-safe"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Unknown_envelope_and_engine_fields_remain_read_only_and_unchanged()
    {
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var payloads = new[]
        {
            """{"version":1,"customPresets":[],"futureState":{"revision":7}}""",
            """{"version":1,"customPresets":[{"id":"canvas-engine","name":"Engine pen","tool":"Pen","color":"#FF010203","opacity":1,"thickness":2,"effect":"Pressure","engineParameters":{"nib":"fountain"}}]}"""
        };

        foreach (var payload in payloads)
        {
            var original = StoreCanvasPresetPayload(payload);
            var reopened = new UserPreferencesService(_paths);

            Assert.False(reopened.CanvasPenPresetPreferencesWritable);
            Assert.NotNull(reopened.CanvasPenPresetPreferencesDiagnostic);
            Assert.Contains(reopened.CanvasPenPresets, value => value.Id == "builtin-blue");
            Assert.Null(reopened.SaveCanvasPenPreset(new CanvasPenPresetPreference("canvas-new", "New pen", "Pen", "#FF123456", 1, 2, "Pressure")));
            Assert.False(reopened.RemoveCanvasPenPreset("canvas-engine"));
            Assert.Equal(original, File.ReadAllText(path));
        }
    }

    [Fact]
    public void Unrelated_preference_update_preserves_incompatible_canvas_preset_payload()
    {
        const string payload = """
            {"version":1,"customPresets":[{"id":"canvas-engine","name":"Engine pen","tool":"Pen","color":"#FF010203","opacity":1,"thickness":2,"effect":"Pressure","engineParameters":{"nib":"fountain"}}]}
            """;
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        _ = StoreCanvasPresetPayload(payload);
        var preferences = new UserPreferencesService(_paths);
        Assert.False(preferences.CanvasPenPresetPreferencesWritable);

        preferences.SetDefaultTabAppKey("preserve-canvas-payload");

        var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("preserve-canvas-payload", saved["defaultTabAppKey"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(payload), saved["canvasPenPresets"]));
    }

    private string StoreCanvasPresetPayload(string payload, Action<JsonObject>? updateRoot = null)
    {
        _ = new UserPreferencesService(_paths);
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["canvasPenPresets"] = JsonNode.Parse(payload);
        updateRoot?.Invoke(root);
        var original = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, original);
        return original;
    }

    [Fact]
    public void Future_canvas_preset_version_is_read_only_and_preserved()
    {
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var original = StoreCanvasPresetPayload(
            """{"version":99,"customPresets":[{"id":"future","name":"Future pen","tool":"Pen","color":"#FF010203","opacity":0.5,"thickness":4,"effect":"Pressure"}]}""",
            root => root["defaultTabAppKey"] = "future-safe");
        var preferences = new UserPreferencesService(_paths);

        Assert.Equal("future-safe", preferences.DefaultTabAppKey);
        Assert.False(preferences.CanvasPenPresetPreferencesWritable);
        Assert.Null(preferences.SaveCanvasPenPreset(new CanvasPenPresetPreference("", "Blocked", "Pen", "#FFFFFFFF", 1, 3, "Pressure")));
        Assert.False(preferences.RemoveCanvasPenPreset("future"));
        Assert.NotNull(preferences.CanvasPenPresetPreferencesDiagnostic);
        using var json = JsonDocument.Parse(original);
        Assert.Equal(99, json.RootElement.GetProperty("canvasPenPresets").GetProperty("version").GetInt32());
        Assert.Equal(original, File.ReadAllText(path));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-canvas-preset-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
