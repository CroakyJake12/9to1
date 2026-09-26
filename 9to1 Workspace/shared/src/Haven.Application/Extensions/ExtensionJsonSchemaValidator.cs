using System.Text.Json;
using System.Text.RegularExpressions;

namespace Haven.Application;

/// <summary>Small fail-closed JSON Schema subset shared by Plugin manifest validation and invocation.</summary>
public static partial class ExtensionJsonSchemaValidator
{
    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "type", "properties", "required", "additionalProperties", "items", "enum",
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength",
        "minItems", "maxItems", "pattern", "description", "title", "default", "examples"
    };

    [GeneratedRegex("\\A(?:[\\s\\S]{0,256000})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex BoundedJson();

    public static bool IsSupported(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !BoundedJson().IsMatch(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return IsSchemaNodeSupported(document.RootElement, 0) &&
                   document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object";
        }
        catch (JsonException) { return false; }
    }

    public static bool Validate(string schemaJson, JsonElement value, out string safeProblem)
    {
        safeProblem = string.Empty;
        if (!IsSupported(schemaJson)) { safeProblem = "The declared schema is unsupported."; return false; }
        using var schema = JsonDocument.Parse(schemaJson);
        return ValidateNode(schema.RootElement, value, "$", 0, out safeProblem);
    }

    private static bool IsSchemaNodeSupported(JsonElement schema, int depth)
    {
        if (depth > 32 || schema.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in schema.EnumerateObject())
        {
            if (!SupportedKeywords.Contains(property.Name)) return false;
            if (property.Name == "properties")
            {
                if (property.Value.ValueKind != JsonValueKind.Object || property.Value.EnumerateObject().Any(item => !IsSchemaNodeSupported(item.Value, depth + 1))) return false;
            }
            else if (property.Name == "items" && property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) && !IsSchemaNodeSupported(property.Value, depth + 1)) return false;
            else if (property.Name == "additionalProperties" && property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.True)
            {
                if (property.Value.ValueKind == JsonValueKind.Object && !IsSchemaNodeSupported(property.Value, depth + 1)) return false;
            }
        }
        return true;
    }

    private static bool ValidateNode(JsonElement schema, JsonElement value, string path, int depth, out string problem)
    {
        problem = string.Empty;
        if (depth > 32) { problem = "JSON value nesting exceeds the schema validation limit."; return false; }
        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array && !values.EnumerateArray().Any(item => JsonEquivalent(item, value)))
        { problem = $"{path} is not a declared value."; return false; }
        if (!schema.TryGetProperty("type", out var typeNode) || typeNode.ValueKind != JsonValueKind.String) return true;
        var type = typeNode.GetString();
        var valid = type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false
        };
        if (!valid) { problem = $"{path} must be {type}."; return false; }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                foreach (var item in required.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !value.TryGetProperty(item.GetString()!, out _))
                    { problem = $"{path}.{item.GetString()} is required."; return false; }
            var properties = schema.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object ? declared : default;
            schema.TryGetProperty("additionalProperties", out var additional);
            foreach (var item in value.EnumerateObject())
            {
                if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(item.Name, out var child))
                {
                    if (!ValidateNode(child, item.Value, path + "." + item.Name, depth + 1, out problem)) return false;
                }
                else if (additional.ValueKind == JsonValueKind.False)
                { problem = $"{path}.{item.Name} is not allowed."; return false; }
                else if (additional.ValueKind == JsonValueKind.Object && !ValidateNode(additional, item.Value, path + "." + item.Name, depth + 1, out problem)) return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("minItems", out var minItems) && minItems.TryGetInt32(out var minimum) && value.GetArrayLength() < minimum)
            { problem = $"{path} contains too few items."; return false; }
            if (schema.TryGetProperty("maxItems", out var maxItems) && maxItems.TryGetInt32(out var maximum) && value.GetArrayLength() > maximum)
            { problem = $"{path} contains too many items."; return false; }
            if (schema.TryGetProperty("items", out var itemSchema) && itemSchema.ValueKind == JsonValueKind.Object)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (!ValidateNode(itemSchema, item, $"{path}[{index}]", depth + 1, out problem)) return false;
                    index++;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            if (schema.TryGetProperty("minLength", out var minLength) && minLength.TryGetInt32(out var minimum) && text.Length < minimum)
            { problem = $"{path} is shorter than the minimum length."; return false; }
            if (schema.TryGetProperty("maxLength", out var maxLength) && maxLength.TryGetInt32(out var maximum) && text.Length > maximum)
            { problem = $"{path} exceeds the maximum length."; return false; }
            if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String &&
                !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            { problem = $"{path} does not match the declared format."; return false; }
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            var number = value.GetDouble();
            if (schema.TryGetProperty("minimum", out var min) && min.TryGetDouble(out var minimum) && number < minimum) { problem = $"{path} is below the minimum."; return false; }
            if (schema.TryGetProperty("maximum", out var max) && max.TryGetDouble(out var maximum) && number > maximum) { problem = $"{path} is above the maximum."; return false; }
            if (schema.TryGetProperty("exclusiveMinimum", out var exMin) && exMin.TryGetDouble(out var exMinimum) && number <= exMinimum) { problem = $"{path} is below the exclusive minimum."; return false; }
            if (schema.TryGetProperty("exclusiveMaximum", out var exMax) && exMax.TryGetDouble(out var exMaximum) && number >= exMaximum) { problem = $"{path} exceeds the exclusive maximum."; return false; }
        }
        return true;
    }

    private static bool JsonEquivalent(JsonElement left, JsonElement right) =>
        left.ValueKind == right.ValueKind && string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
}
