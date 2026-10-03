using System.Text.Json;
using NineToOne.Cui.AI;
using Xunit;

public sealed class ActionSchemaTests
{
    private const string Schema = """
        {"type":"object","required":["id","count"],"additionalProperties":false,
         "properties":{"id":{"type":"string","minLength":1},"count":{"type":"integer","minimum":1},
          "tags":{"type":"array","items":{"type":"string"}}}}
        """;
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"x\",\"count\":\"1\"}")]
    [InlineData("{\"id\":\"x\",\"count\":0}")]
    [InlineData("{\"id\":\"x\",\"count\":1,\"extra\":true}")]
    [InlineData("{\"id\":\"x\",\"count\":1,\"tags\":[1]}")]
    public void InvalidTypedArgumentsFailClosed(string json)
    {
        using var arguments = JsonDocument.Parse(json);
        Assert.False(ActionJsonSchemaValidator.Validate(Schema, arguments.RootElement, out var error));
        Assert.NotEmpty(error);
    }
    [Fact] public void SupportedSchemaAcceptsValidInputAndUnsupportedKeywordCannotBeIgnored()
    {
        using var value = JsonDocument.Parse("{\"id\":\"x\",\"count\":1,\"tags\":[\"one\"]}");
        Assert.True(ActionJsonSchemaValidator.Validate(Schema, value.RootElement, out _));
        Assert.False(ActionJsonSchemaValidator.IsSupported("{\"type\":\"object\",\"$ref\":\"https://example.test/schema\"}"));
    }
}
