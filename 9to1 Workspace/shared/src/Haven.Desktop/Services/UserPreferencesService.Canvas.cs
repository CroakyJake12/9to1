using System.Text.Json;

namespace Haven.Desktop;

public sealed record CanvasPenPresetPreference(
    string Id,
    string Name,
    string Tool,
    string Color,
    double Opacity,
    double Thickness,
    string Effect);

public sealed record CanvasPenPresetPreferenceState(
    int Version,
    IReadOnlyList<CanvasPenPresetPreference> CustomPresets);

public sealed partial class UserPreferencesService
{
    private const int CurrentCanvasPenPresetVersion = 1;
    private sealed record CanvasPenPresetReadState(CanvasPenPresetPreferenceState State, bool Writable);
    private static readonly HashSet<string> CanvasPresetStateProperties = new(StringComparer.OrdinalIgnoreCase)
        { "version", "customPresets" };
    private static readonly HashSet<string> CanvasPresetProperties = new(StringComparer.OrdinalIgnoreCase)
        { "id", "name", "tool", "color", "opacity", "thickness", "effect" };

    private static IReadOnlyList<CanvasPenPresetPreference> BuiltInCanvasPenPresets { get; } =
    [
        new("builtin-blue", "Blue pen", "Pen", "#FF2F80ED", 1, 3, "Pressure"),
        new("builtin-black", "Black pen", "Pen", "#FF111111", 1, 3, "Pressure"),
        new("builtin-red", "Red pen", "Pen", "#FFE5484D", 1, 3, "Pressure"),
        new("builtin-highlighter", "Yellow highlighter", "Highlighter", "#FFFFC928", 0.28, 4, "Uniform")
    ];

    public int CanvasPenPresetVersion => CurrentCanvasPenPresetVersion;

    public bool CanvasPenPresetPreferencesWritable
    {
        get => CanvasPresetReadState.Writable;
    }

    /// <summary>Explains why saved custom presets are read-only; original data remains untouched.</summary>
    public string? CanvasPenPresetPreferencesDiagnostic
    {
        get
        {
            var read = CanvasPresetReadState;
            if (read.Writable) return null;
            return read.State.Version > CurrentCanvasPenPresetVersion
                ? "Custom pen presets were saved by a newer version. Original data is preserved; built-in pens remain available and custom edits are read-only."
                : "Saved custom pen presets are incompatible. Original data is preserved; built-in pens remain available and custom edits are read-only.";
        }
    }

    public IReadOnlyList<CanvasPenPresetPreference> CanvasPenPresets
    {
        get
        {
            var read = CanvasPresetReadState;
            if (!read.Writable) return BuiltInCanvasPenPresets;
            var custom = read.State.CustomPresets.Select(value => NormaliseCanvasPreset(value, custom: true))
                .Cast<CanvasPenPresetPreference>().ToArray();
            return BuiltInCanvasPenPresets.Concat(custom).ToArray();
        }
    }

    public CanvasPenPresetPreference? SaveCanvasPenPreset(CanvasPenPresetPreference preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!CanvasPenPresetPreferencesWritable) return null;
        var saved = NormaliseCanvasPreset(preset, custom: true);
        if (saved is null) return null;
        var custom = CanvasPenPresets
            .Where(value => !value.Id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase))
            .Where(value => !value.Id.Equals(saved.Id, StringComparison.OrdinalIgnoreCase))
            .Append(saved)
            .ToArray();
        _preferences = _preferences with
        {
            CanvasPenPresets = JsonSerializer.SerializeToElement(new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, custom), JsonOptions)
        };
        Save();
        return saved;
    }

    public bool RemoveCanvasPenPreset(string id)
    {
        if (!CanvasPenPresetPreferencesWritable || string.IsNullOrWhiteSpace(id) || id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase)) return false;
        var existing = CanvasPenPresets
            .Where(value => !value.Id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var custom = existing.Where(value => !value.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (custom.Length == existing.Length) return false;
        _preferences = _preferences with
        {
            CanvasPenPresets = JsonSerializer.SerializeToElement(new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, custom), JsonOptions)
        };
        Save();
        return true;
    }

    private CanvasPenPresetReadState CanvasPresetReadState => ReadCanvasPresetState(_preferences.CanvasPenPresets);

    private static CanvasPenPresetReadState ReadCanvasPresetState(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value)
            return IncompatibleCanvasPresetState();
        if (!HasOnlyProperties(value, CanvasPresetStateProperties)) return IncompatibleCanvasPresetState();
        var customPresets = value.EnumerateObject()
            .FirstOrDefault(property => property.Name.Equals("customPresets", StringComparison.OrdinalIgnoreCase));
        if (customPresets.Value.ValueKind != JsonValueKind.Array ||
            customPresets.Value.EnumerateArray().Any(preset => !HasOnlyProperties(preset, CanvasPresetProperties)))
            return IncompatibleCanvasPresetState();
        try
        {
            var state = JsonSerializer.Deserialize<CanvasPenPresetPreferenceState>(value.GetRawText(), JsonOptions);
            if (state is null) return IncompatibleCanvasPresetState();
            var writable = state.Version == CurrentCanvasPenPresetVersion && state.CustomPresets is not null &&
                state.CustomPresets.All(IsValidStoredCanvasPreset) &&
                state.CustomPresets.Select(preset => preset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == state.CustomPresets.Count;
            return new(state, writable);
        }
        catch (JsonException)
        {
            return IncompatibleCanvasPresetState();
        }
        catch (NotSupportedException)
        {
            return IncompatibleCanvasPresetState();
        }
    }

    private static CanvasPenPresetReadState IncompatibleCanvasPresetState() =>
        new(new CanvasPenPresetPreferenceState(0, []), false);

    private static bool HasOnlyProperties(JsonElement value, IReadOnlySet<string> allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) return false;
        return true;
    }

    private static bool IsValidStoredCanvasPreset(CanvasPenPresetPreference? value) =>
        value is not null && !string.IsNullOrWhiteSpace(value.Id) &&
        !value.Id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(value.Name) && value.Name.Length <= 80 &&
        (string.Equals(value.Tool, "Pen", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value.Tool, "Highlighter", StringComparison.OrdinalIgnoreCase)) &&
        IsCanvasPresetColour(value.Color) && double.IsFinite(value.Opacity) && value.Opacity is >= 0.05 and <= 1 &&
        double.IsFinite(value.Thickness) && value.Thickness is >= 0.5 and <= 64 &&
        (string.Equals(value.Effect, "Pressure", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value.Effect, "Uniform", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value.Effect, "Marker", StringComparison.OrdinalIgnoreCase));

    private static bool IsCanvasPresetColour(string? value)
    {
        var colour = value?.Trim();
        return colour is not null && colour.StartsWith('#') && colour.Length is 7 or 9 &&
            colour.Skip(1).All(Uri.IsHexDigit);
    }

    private static CanvasPenPresetPreference? NormaliseCanvasPreset(CanvasPenPresetPreference? value, bool custom)
    {
        if (value is null) return null;
        var tool = string.Equals(value.Tool, "Highlighter", StringComparison.OrdinalIgnoreCase) ? "Highlighter" : "Pen";
        var effect = string.Equals(value.Effect, "Uniform", StringComparison.OrdinalIgnoreCase) ? "Uniform"
            : string.Equals(value.Effect, "Marker", StringComparison.OrdinalIgnoreCase) ? "Marker"
            : "Pressure";
        var color = NormaliseCanvasColour(value.Color, tool == "Highlighter" ? "#FFFFC928" : "#FF2F80ED");
        var id = value.Id?.Trim() ?? string.Empty;
        if (custom && (id.Length == 0 || id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase)))
            id = "canvas-pen-" + Guid.NewGuid().ToString("N");
        if (!custom && id.Length == 0) return null;
        var name = string.IsNullOrWhiteSpace(value.Name) ? "Custom pen" : value.Name.Trim();
        if (name.Length > 80) name = name[..80];
        return new CanvasPenPresetPreference(
            id,
            name,
            tool,
            color,
            Math.Clamp(double.IsFinite(value.Opacity) ? value.Opacity : 1, 0.05, 1),
            Math.Clamp(double.IsFinite(value.Thickness) ? value.Thickness : 3, 0.5, 64),
            effect);
    }

    private static string NormaliseCanvasColour(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var color = value.Trim();
        return color.StartsWith('#') && color.Length is 7 or 9 && color.Skip(1).All(Uri.IsHexDigit)
            ? color.ToUpperInvariant()
            : fallback;
    }

    private sealed partial record Preferences
    {
        public JsonElement? CanvasPenPresets { get; init; } =
            JsonSerializer.SerializeToElement(new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, []), JsonOptions);
    }
}
