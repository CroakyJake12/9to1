using System.Text.Json;

namespace Haven.Application.Automations;

/// <summary>Describes whether a graph value is present, explicitly null, unavailable, redacted, or failed.</summary>
public enum AutomationGraphValueState
{
    Value = 0,
    Null = 1,
    Unavailable = 2,
    Redacted = 3,
    Error = 4
}

/// <summary>A typed value passed between Automation graph ports without collapsing missing states.</summary>
public sealed record AutomationGraphValue(
    string DataType,
    AutomationGraphValueState State,
    JsonElement? JsonValue = null,
    string? ErrorCode = null,
    string? Message = null)
{
    public static AutomationGraphValue FromJson(string dataType, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataType);
        return value.ValueKind == JsonValueKind.Null
            ? Null(dataType)
            : new AutomationGraphValue(dataType, AutomationGraphValueState.Value, value.Clone());
    }

    public static AutomationGraphValue FromString(string value) =>
        new("string", AutomationGraphValueState.Value, JsonSerializer.SerializeToElement(value ?? throw new ArgumentNullException(nameof(value))));

    public static AutomationGraphValue Null(string dataType) =>
        new(RequireType(dataType), AutomationGraphValueState.Null);

    public static AutomationGraphValue Unavailable(string dataType, string? message = null) =>
        new(RequireType(dataType), AutomationGraphValueState.Unavailable, Message: message);

    public static AutomationGraphValue Redacted(string dataType, string? message = null) =>
        new(RequireType(dataType), AutomationGraphValueState.Redacted, Message: message);

    public static AutomationGraphValue Failure(string dataType, string code, string message) =>
        new(RequireType(dataType), AutomationGraphValueState.Error, ErrorCode: RequireType(code), Message: RequireType(message));

    public bool TryGetString(out string? value)
    {
        value = null;
        if (State != AutomationGraphValueState.Value || JsonValue is not { ValueKind: JsonValueKind.String } json)
            return false;
        value = json.GetString();
        return true;
    }

    /// <summary>Checks the JSON representation for known JSON Schema primitive names.
    /// Unknown graph-defined types remain opaque for their owning schema registry; no
    /// conversion or guessed custom-type rule is applied here.</summary>
    public bool HasJsonValueCompatibleWithDataType()
    {
        if (!Enum.IsDefined(State) || string.IsNullOrWhiteSpace(DataType)) return false;
        if (State == AutomationGraphValueState.Error)
            return JsonValue is null && !string.IsNullOrWhiteSpace(ErrorCode) && !string.IsNullOrWhiteSpace(Message);
        if (ErrorCode is not null) return false;
        if (State != AutomationGraphValueState.Value) return JsonValue is null;
        if (JsonValue is not { } json || json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;
        return DataType?.ToLowerInvariant() switch
        {
            "string" => json.ValueKind == JsonValueKind.String,
            "integer" => json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out _),
            "number" => json.ValueKind == JsonValueKind.Number,
            "boolean" => json.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => json.ValueKind == JsonValueKind.Object,
            "array" => json.ValueKind == JsonValueKind.Array,
            _ => true
        };
    }

    private static string RequireType(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", nameof(value)) : value.Trim();
}
