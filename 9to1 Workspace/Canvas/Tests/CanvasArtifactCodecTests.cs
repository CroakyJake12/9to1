using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasArtifactCodecTests
{
    [Theory]
    [InlineData(CanvasDocumentMode.Infinite)]
    [InlineData(CanvasDocumentMode.Paged)]
    public void Native_format_round_trip_preserves_stable_identity_mode_and_structured_ink(CanvasDocumentMode mode)
    {
        var artifact = CanvasArtifact.Create("Notebook", mode);
        var page = artifact.Pages.Single();
        var layer = page.Layers.Single();
        var stroke = new CanvasInkStroke
        {
            LayerId = layer.LayerId,
            ToolDefinitionId = "rnote.pen.ballpoint",
            Samples =
            [
                new CanvasStrokeSample(10, 20, 0.21, 14, -8, 1000),
                new CanvasStrokeSample(18, 31, 0.83, 20, -4, 1016)
            ],
            ResolvedBrushProperties = new CanvasBrushProperties
            {
                BaseWidth = 3.2,
                Color = "#FF112233",
                Opacity = 0.75,
                EngineParameters = new Dictionary<string, JsonElement>
                {
                    ["nib"] = JsonSerializer.SerializeToElement("elliptical")
                }
            }
        };
        page = page with { Strokes = [stroke], StrokeOrder = [stroke.StrokeId] };
        artifact.Pages = [page];
        artifact.PageOrder = [page.PageId];

        var content = CanvasArtifactCodec.Serialize(artifact);
        var restored = CanvasArtifactCodec.Deserialize(content);

        Assert.Equal(CanvasArtifactFile.FileExtension, ".9to1c");
        Assert.Equal(CanvasArtifactFile.CanonicalFormat, "9to1.Canvas");
        Assert.Equal(artifact.ArtifactId, restored.ArtifactId);
        Assert.Equal(artifact.RevisionId, restored.RevisionId);
        Assert.Equal(artifact.CanvasMode, restored.CanvasMode);
        Assert.Equal(page.PageId, Assert.Single(restored.Pages).PageId);
        var restoredStroke = Assert.Single(restored.Pages[0].Strokes);
        Assert.Equal(stroke.StrokeId, restoredStroke.StrokeId);
        Assert.Equal(stroke.Samples, restoredStroke.Samples);
        Assert.Equal(stroke.ResolvedBrushProperties.EngineId, restoredStroke.ResolvedBrushProperties.EngineId);
        Assert.Equal(stroke.ResolvedBrushProperties.Color, restoredStroke.ResolvedBrushProperties.Color);
        Assert.Equal(stroke.ResolvedBrushProperties.BaseWidth, restoredStroke.ResolvedBrushProperties.BaseWidth);
        Assert.Equal(stroke.ResolvedBrushProperties.Opacity, restoredStroke.ResolvedBrushProperties.Opacity);
        Assert.Equal("elliptical", restoredStroke.ResolvedBrushProperties.EngineParameters["nib"].GetString());
        Assert.Equal(mode == CanvasDocumentMode.Paged, restored.Pages[0].Bounds is not null);
    }

    [Fact]
    public void Unknown_optional_fields_survive_decode_and_reencode()
    {
        var artifact = CanvasArtifact.Create("Extensible");
        var json = JsonNode.Parse(Encoding.UTF8.GetString(CanvasArtifactCodec.Serialize(artifact)))!.AsObject();
        json["futureEnvelope"] = JsonNode.Parse("{\"enabled\":true}");
        var artifactNode = json["artifact"]!.AsObject();
        artifactNode["futureArtifactProperty"] = JsonNode.Parse("[1,2,3]");
        artifactNode["pages"]![0]!["futurePageProperty"] = JsonNode.Parse("\"retained\"");

        var decoded = CanvasArtifactCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var reencoded = JsonNode.Parse(Encoding.UTF8.GetString(CanvasArtifactCodec.Serialize(decoded)))!.AsObject();

        Assert.True(decoded.EnvelopeExtensionData!.ContainsKey("futureEnvelope"));
        Assert.True(decoded.ExtensionData!.ContainsKey("futureArtifactProperty"));
        Assert.Equal("true", reencoded["futureEnvelope"]!["enabled"]!.ToJsonString());
        Assert.Equal("[1,2,3]", reencoded["artifact"]!["futureArtifactProperty"]!.ToJsonString());
        Assert.Equal("\"retained\"", reencoded["artifact"]!["pages"]![0]!["futurePageProperty"]!.ToJsonString());
    }

    [Fact]
    public void Unsupported_format_and_schema_versions_are_rejected_without_guessing()
    {
        var bytes = CanvasArtifactCodec.Serialize(CanvasArtifact.Create());
        var formatJson = JsonNode.Parse(Encoding.UTF8.GetString(bytes))!.AsObject();
        formatJson["format"] = "other.canvas";
        var versionJson = JsonNode.Parse(Encoding.UTF8.GetString(bytes))!.AsObject();
        versionJson["schemaVersion"] = CanvasArtifact.CurrentSchemaVersion + 1;

        var formatError = Assert.Throws<CanvasArtifactFormatException>(
            () => CanvasArtifactCodec.Deserialize(Encoding.UTF8.GetBytes(formatJson.ToJsonString())));
        var versionError = Assert.Throws<CanvasArtifactFormatException>(
            () => CanvasArtifactCodec.Deserialize(Encoding.UTF8.GetBytes(versionJson.ToJsonString())));

        Assert.Equal(CanvasArtifactFormatErrorCode.UnsupportedFormat, formatError.Code);
        Assert.Equal(CanvasArtifactFormatErrorCode.UnsupportedSchemaVersion, versionError.Code);
    }

    [Fact]
    public void Invalid_page_object_and_layer_references_are_reported_as_typed_validation_issues()
    {
        var artifact = CanvasArtifact.Create();
        var page = artifact.Pages.Single();
        var item = new CanvasObject
        {
            ObjectTypeId = "shared.text",
            LayerId = Guid.NewGuid(),
            Geometry = new CanvasRect(0, 0, double.NaN, 100)
        };
        page = page with { Objects = [item], ObjectOrder = [item.ObjectId] };
        artifact.Pages = [page];
        artifact.PageOrder = [page.PageId];

        var exception = Assert.Throws<CanvasArtifactFormatException>(() => CanvasArtifactCodec.Serialize(artifact));

        Assert.Equal(CanvasArtifactFormatErrorCode.ValidationFailed, exception.Code);
        Assert.Contains(exception.Issues, issue => issue.Code == "unknown_layer");
        Assert.Contains(exception.Issues, issue => issue.Code == "invalid_geometry");
    }

    [Fact]
    public void Paged_mode_requires_explicit_positive_page_bounds()
    {
        var artifact = CanvasArtifact.Create(mode: CanvasDocumentMode.Paged);
        var page = artifact.Pages.Single() with { Bounds = new CanvasPageBounds(0, 0, 0, 800) };
        artifact.Pages = [page];

        var exception = Assert.Throws<CanvasArtifactFormatException>(() => CanvasArtifactCodec.Serialize(artifact));

        Assert.Contains(exception.Issues, issue => issue.Code == "invalid_page_bounds");
    }

    [Fact]
    public void Malformed_json_has_a_stable_invalid_document_error()
    {
        var exception = Assert.Throws<CanvasArtifactFormatException>(
            () => CanvasArtifactCodec.Deserialize(Encoding.UTF8.GetBytes("{broken")));

        Assert.Equal(CanvasArtifactFormatErrorCode.InvalidDocument, exception.Code);
    }
}
