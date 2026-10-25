using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class BoardsSectionIconSuggestionServiceTests
{
    [Fact]
    public void Parser_accepts_bounded_roundrect_circle_and_cutout_polygon_geometry()
    {
        var geometry = BoardsSectionIconSuggestionService.ParseResponse("""
            {"shapes":[
              {"kind":"roundRect","x":3,"y":3,"width":18,"height":18,"radius":3},
              {"kind":"circle","cx":12,"cy":12,"radius":4},
              {"kind":"polygon","points":[[8,8],[16,8],[12,16]],"cutout":true}
            ]}
            """);

        Assert.Equal(3, geometry.Shapes.Count);
    }

    [Theory]
    [InlineData("{\"shapes\":[{\"kind\":\"roundRect\",\"x\":-1,\"y\":3,\"width\":18,\"height\":18,\"radius\":3}]}")]
    [InlineData("{\"shapes\":[{\"kind\":\"roundRect\",\"x\":3,\"y\":3,\"width\":18,\"height\":18,\"radius\":3,\"url\":\"https://example.invalid\"}]}")]
    [InlineData("{\"shapes\":[{\"kind\":\"path\",\"d\":\"M0 0\"}]}")]
    public void Parser_rejects_unbounded_or_unapproved_model_output(string response)
    {
        Assert.Throws<InvalidDataException>(() => BoardsSectionIconSuggestionService.ParseResponse(response));
    }

    [Fact]
    public void Parser_rejects_oversized_output_before_json_parsing()
    {
        var response = "{" + new string(' ', 8192) + "}";
        Assert.Throws<InvalidDataException>(() => BoardsSectionIconSuggestionService.ParseResponse(response));
    }
}
