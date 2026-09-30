using System.Text.Json;
using System.Text.Json.Serialization;
namespace NineToOne.Accounts;

/// <summary>Canonical JSON conversion for shared account/Business DTOs, reused by durable state and native HTTP clients.</summary>
public static class AccountContractJson
{
    public static JsonSerializerOptions CreateOptions(JsonSerializerDefaults defaults=JsonSerializerDefaults.General)
    {
        var options=new JsonSerializerOptions(defaults);options.Converters.Add(new StringSetConverter());return options;
    }
    private sealed class StringSetConverter:JsonConverter<IReadOnlySet<string>>
    {
        public override IReadOnlySet<string> Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
            =>JsonSerializer.Deserialize<HashSet<string>>(ref reader,options)??throw new JsonException("invalid_set");
        public override void Write(Utf8JsonWriter writer,IReadOnlySet<string> value,JsonSerializerOptions options)
            =>JsonSerializer.Serialize(writer,value.ToArray(),options);
    }
}
