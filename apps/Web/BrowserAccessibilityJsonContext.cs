using System.Text.Json.Serialization;

namespace NineToOne.Web;

/// <summary>Explicit metadata for native peer snapshots in the browser runtime.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserAccessibilityBridge.Snapshot))]
internal partial class BrowserAccessibilityJsonContext : JsonSerializerContext
{
}
