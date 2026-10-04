using System.Text.Json;
using NineToOne.Cui.AI;
namespace Haven.Application;
/// <summary>Compatibility entry point for the canonical shared typed-action schema validator.</summary>
public static class ExtensionJsonSchemaValidator
{
    public static bool IsSupported(string json) => ActionJsonSchemaValidator.IsSupported(json);
    public static bool Validate(string schemaJson, JsonElement value, out string safeProblem) =>
        ActionJsonSchemaValidator.Validate(schemaJson, value, out safeProblem);
}
