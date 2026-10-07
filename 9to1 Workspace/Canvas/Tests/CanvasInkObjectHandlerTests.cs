using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasInkObjectHandlerTests
{
    [Fact]
    public void Shared_handler_renders_actual_native_stroke_preserves_identity_unknown_data_and_detaches_pixels()
    {
        using var native = CanvasRnoteDocument.Create("Shared drawing source");
        var id = native.DrawStroke([new(10, 10, .2), new(35, 40, .7), new(80, 65, .5)], native.Snapshot.RevisionId);
        var stroke = Assert.Single(native.Snapshot.Pages[0].Strokes) with
        { ExtensionData = new() { ["futureProperty"] = JsonSerializer.SerializeToElement(new { retained = true }) } };
        var handler = new CanvasInkObjectHandler();
        var shared = new HomeProductivityEngine(handlers: [handler]);
        var value = shared.CreateObject("drawing.ink", id, JsonSerializer.SerializeToElement(stroke));
        var result = shared.RenderObject(value);
        var binding = Assert.Single(result.RasterBindings);
        Assert.Equal(id, binding.ObjectId);
        Assert.Contains("Type=\"spe.raster\"", result.CuiSource);
        Assert.Contains(binding.ControlId, result.CuiSource);
        Assert.Contains("content.futureProperty", result.RetainedUnsupportedProperties);
        var pixels = binding.Frame.CopyPixels();
        Assert.Contains(pixels, pixel => pixel != 0);
        Array.Clear(pixels);
        Assert.Contains(binding.Frame.CopyPixels(), pixel => pixel != 0);
        var recolored = handler.Transform(value, new("ink.color", 1, "drawing.ink", [id], JsonSerializer.SerializeToElement(new { color = "#FFFF0000" }), 0));
        var changed = recolored.Content.Deserialize<CanvasInkStroke>()!;
        Assert.Equal(id, changed.StrokeId);
        Assert.Equal(stroke.RevisionId, changed.RevisionId); // pure transformation cannot mint an owner revision
        Assert.Equal("#FFFF0000", changed.ResolvedBrushProperties.Color);
        Assert.True(changed.ExtensionData!["futureProperty"].GetProperty("retained").GetBoolean());
        Assert.NotEqual(binding.Frame.CopyPixels(), Assert.Single(shared.RenderObject(recolored).RasterBindings).Frame.CopyPixels());
        Assert.Equal(stroke.ResolvedBrushProperties.Color, value.Content.Deserialize<CanvasInkStroke>()!.ResolvedBrushProperties.Color);
        var copiedId = Guid.NewGuid();
        var copy = handler.CloneForPaste(value, copiedId);
        var copiedStroke = copy.Content.Deserialize<CanvasInkStroke>()!;
        Assert.Equal(copiedId, copy.ObjectId);
        Assert.Equal(copiedId, copiedStroke.StrokeId);
        Assert.Equal(copiedId, copiedStroke.RevisionId);
        Assert.Equal(stroke.LayerId, copiedStroke.LayerId);
        Assert.True(copiedStroke.ExtensionData!["futureProperty"].GetProperty("retained").GetBoolean());
        Assert.Equal(id, value.Content.Deserialize<CanvasInkStroke>()!.StrokeId);
    }

    [Fact]
    public async Task Shared_ink_rejects_identity_mismatch_unsupported_brush_and_unavailable_owner_without_fake_commit()
    {
        using var native = CanvasRnoteDocument.Create();
        var id = native.DrawStroke([new(10, 10, .5), new(30, 40, .5)], native.Snapshot.RevisionId);
        var stroke = Assert.Single(native.Snapshot.Pages[0].Strokes);
        var handler = new CanvasInkObjectHandler();
        Assert.Throws<InvalidDataException>(() => handler.Create(Guid.NewGuid(), JsonSerializer.SerializeToElement(stroke)));
        Assert.Throws<NotSupportedException>(() => handler.Create(id, JsonSerializer.SerializeToElement(stroke with
        { ResolvedBrushProperties = stroke.ResolvedBrushProperties with { EngineId = "foreign" } })));
        var value = handler.Create(id, JsonSerializer.SerializeToElement(stroke));
        Assert.Throws<ArgumentException>(() => handler.Transform(value, new("ink.color", 1, "drawing.ink", [id], JsonSerializer.SerializeToElement(new { color = "invalid" }), 0)));
        var transformed = handler.Create(id, JsonSerializer.SerializeToElement(stroke with { Transform = new() { ScaleX = 2 } }));
        Assert.Throws<NotSupportedException>(() => handler.Render(transformed));
        var shared = new HomeProductivityEngine(handlers: [handler]);
        var revision = new HomeProductivityArtifactRevision(VersionId: native.Snapshot.RevisionId);
        var context = new HomeProductivityContext("canvas", native.Snapshot.ArtifactId.ToString(), 0, [id], new HashSet<string> { "drawing.ink" }) { ArtifactRevision = revision };
        var action = new HomeProductivityAction("ink.color", 1, "drawing.ink", [id], JsonSerializer.SerializeToElement(new { color = "#FF00FF00" }), 0) { ExpectedArtifactRevision = revision };
        var result = await shared.ApplyActionAsync(context, action, default);
        Assert.False(result.Succeeded);
        Assert.Equal("ArtifactExecutorUnavailable", result.Code);
        Assert.Equal(HomeProductivityArtifactOutcome.NotExecuted, result.Outcome);
        Assert.Equal(stroke.RevisionId, native.Snapshot.Pages[0].Strokes[0].RevisionId);
    }
}
