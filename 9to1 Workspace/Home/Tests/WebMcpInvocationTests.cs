using System.Text.Json;
using Haven.Application;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class WebMcpInvocationTests
{
    private static WebMcpInvocationRequest Request(string origin = "https://example.test", bool supported = true,
        string args = "{\"count\":2}") => new(origin, "observed-document", "observed-browser-version",
        "navigator.modelContextTesting", supported, "page-tool",
        JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"count\"],\"additionalProperties\":false}"),
        JsonSerializer.Deserialize<JsonElement>(args));

    [Fact]
    public void Observed_supported_origin_and_valid_arguments_are_eligible_for_home_approval()
        => Assert.True(Request().IsValid());

    [Theory]
    [InlineData("file:///page")]
    [InlineData("https://user:password@example.test")]
    [InlineData("https://example.test/page")]
    [InlineData("https://example.test?query=1")]
    [InlineData("null")]
    public void Non_origins_never_reach_approval(string origin) => Assert.False(Request(origin).IsValid());

    [Fact]
    public void Unsupported_browser_or_schema_and_invalid_arguments_fail_closed()
    {
        Assert.False(Request(supported: false).IsValid());
        Assert.False(Request(args: "{\"count\":0}").IsValid());
        Assert.False(Request(args: "{\"count\":2,\"undeclared\":true}").IsValid());
        Assert.False((Request() with { InputSchema = JsonSerializer.Deserialize<JsonElement>("{\"$ref\":\"https://untrusted.test/schema\"}") }).IsValid());
        Assert.False((Request() with { DocumentId = "" }).IsValid());
    }
}
