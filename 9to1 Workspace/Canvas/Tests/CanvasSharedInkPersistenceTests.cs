using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Canvas;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

/// <summary>Managed semantic/owner-boundary checks, not native rendering or app-route acceptance.</summary>
public sealed class CanvasSharedInkPersistenceTests
{
    [Fact]
    public void Authoritative_fragment_never_replays_erased_original_samples_into_native_renderer()
    {
        var stroke = Stroke(withFragment: true);
        var handler = new CanvasInkObjectHandler();
        var value = handler.Create(stroke.StrokeId, JsonSerializer.SerializeToElement(stroke));
        // Original input extends to X=100; the authoritative fragment ends at X=20.
        // This must reject before loading any native library, even on a qualified host.
        var error = Assert.Throws<NotSupportedException>(() => handler.Render(value));
        Assert.Contains("Authoritative stroke path geometry", error.Message);
        Assert.Equal(100, stroke.Samples[^1].X);
        Assert.Equal(20, stroke.PathGeometry!.Segments[^1].End.X);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(stroke), value.Content));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("present")]
    [InlineData("boards")]
    [InlineData("canvas")]
    [InlineData("spaces")]
    public void Declared_destination_context_preserves_fragment_input_and_metadata_through_native_file_and_Home_bundle(string destinationApp)
    {
        var artifact = CanvasArtifact.Create("Revision diagram");
        var stroke = Stroke(withFragment: true) with { LayerId = artifact.Pages[0].LayerOrder[0] };
        artifact.Pages[0].Strokes.Add(stroke);
        artifact.Pages[0].StrokeOrder.Add(stroke.StrokeId);
        var path = Path.Combine(Path.GetTempPath(), "shared-ink-" + Guid.NewGuid().ToString("N") + ".9to1c");
        CanvasArtifact reopened;
        try
        {
            File.WriteAllBytes(path, CanvasArtifactCodec.Serialize(artifact));
            reopened = CanvasArtifactCodec.Deserialize(File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
        Assert.Equal(artifact.ArtifactId, reopened.ArtifactId);
        Assert.Equal(artifact.RevisionId, reopened.RevisionId);
        var durableStroke = Assert.Single(reopened.Pages[0].Strokes);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(stroke), JsonSerializer.SerializeToElement(durableStroke)));

        var handler = new CanvasInkObjectHandler();
        var shared = new HomeProductivityEngine(handlers: [handler]);
        var value = handler.Create(durableStroke.StrokeId, JsonSerializer.SerializeToElement(durableStroke)) with
        {
            Formatting = JsonSerializer.SerializeToElement(new { futureInkFormatting = new { retained = true } }),
            Accessibility = JsonSerializer.SerializeToElement(new { altText = "Labelled revision diagram" }),
            Layout = JsonSerializer.SerializeToElement(new { anchor = "document", x = 125.5, y = -35 }),
            Extensions = new Dictionary<string, JsonElement> { ["futureOptional"] = JsonSerializer.SerializeToElement(true) }
        };
        // Unknown transform and sample properties stay in raw canonical shared content.
        var raw = JsonNode.Parse(value.Content.GetRawText())!.AsObject();
        raw["Transform"]!["futureTransformHint"] = "retained";
        raw["Samples"]![0]!["futureInputHint"] = "retained";
        value = value with { Content = JsonSerializer.SerializeToElement(raw) };
        var source = Context("canvas", artifact.ArtifactId.ToString(), artifact.RevisionId, [stroke.StrokeId]);
        var destination = Context(destinationApp, Guid.NewGuid().ToString(), Guid.NewGuid(), []);
        var bundle = shared.ParseBundle(shared.SerializeBundle(shared.SerializeSelection(source, [value])));
        Assert.True(shared.CanPaste(bundle, destination, out var code), code);
        var pasted = Assert.Single(shared.Paste(bundle, destination));
        Assert.NotEqual(value.ObjectId, pasted.ObjectId);
        var clone = pasted.Content.Deserialize<CanvasInkStroke>()!;
        Assert.Equal(pasted.ObjectId, clone.StrokeId);
        Assert.Equal(pasted.ObjectId, clone.RevisionId);
        Assert.Equal(stroke.LayerId, clone.LayerId); // destination owner must explicitly rebind placement
        Assert.True(JsonElement.DeepEquals(value.Content.GetProperty("PathGeometry"), pasted.Content.GetProperty("PathGeometry")));
        Assert.True(JsonElement.DeepEquals(value.Content.GetProperty("Samples"), pasted.Content.GetProperty("Samples")));
        Assert.True(JsonElement.DeepEquals(value.Content.GetProperty("Transform"), pasted.Content.GetProperty("Transform")));
        Assert.True(JsonElement.DeepEquals(value.Formatting, pasted.Formatting));
        Assert.True(JsonElement.DeepEquals(value.Accessibility, pasted.Accessibility));
        Assert.True(JsonElement.DeepEquals(value.Layout, pasted.Layout));
        Assert.True(pasted.Extensions!["futureOptional"].GetBoolean());

        var colored = handler.Transform(pasted, Color(pasted.ObjectId));
        Assert.Equal(pasted.ObjectId, colored.ObjectId);
        Assert.Equal(clone.RevisionId, colored.Content.GetProperty("RevisionId").GetGuid());
        Assert.Equal("#FF008000", colored.Content.GetProperty("ResolvedBrushProperties").GetProperty("Color").GetString());
        Assert.True(JsonElement.DeepEquals(pasted.Content.GetProperty("PathGeometry"), colored.Content.GetProperty("PathGeometry")));
        Assert.True(JsonElement.DeepEquals(pasted.Content.GetProperty("Samples"), colored.Content.GetProperty("Samples")));
        Assert.True(colored.Content.GetProperty("futureStrokeProperty").GetProperty("retained").GetBoolean());
        Assert.True(colored.Content.GetProperty("ResolvedBrushProperties").GetProperty("futureBrushProperty").GetBoolean());
        Assert.Equal("#FF000000", pasted.Content.GetProperty("ResolvedBrushProperties").GetProperty("Color").GetString());
        Assert.Equal(stroke.StrokeId, value.Content.GetProperty("StrokeId").GetGuid());
    }

    [Theory]
    [InlineData("write")]
    [InlineData("present")]
    [InlineData("boards")]
    [InlineData("canvas")]
    public async Task Shared_preparation_cannot_claim_a_commit_without_the_canonical_artifact_owner(string app)
    {
        var stroke = Stroke();
        var handler = new CanvasInkObjectHandler();
        var shared = new HomeProductivityEngine(handlers: [handler]);
        var value = handler.Create(stroke.StrokeId, JsonSerializer.SerializeToElement(stroke));
        var revision = Guid.NewGuid();
        var context = Context(app, Guid.NewGuid().ToString(), revision, [stroke.StrokeId]);
        var action = Color(stroke.StrokeId) with { ExpectedArtifactRevision = new(VersionId: revision) };
        var edit = await shared.ApplyActionAsync(context, action);
        Assert.False(edit.Succeeded);
        Assert.Equal("ArtifactExecutorUnavailable", edit.Code);
        Assert.Equal(HomeProductivityArtifactOutcome.NotExecuted, edit.Outcome);
        var insert = await shared.InsertObjectsAsync(context, [value], Guid.NewGuid().ToString());
        Assert.False(insert.Succeeded);
        Assert.Equal("ArtifactInsertionUnavailable", insert.Code);
        Assert.Equal(HomeProductivityArtifactOutcome.NotExecuted, insert.Outcome);
        var stale = await shared.ApplyActionAsync(context, action with { ExpectedArtifactRevision = new(VersionId: Guid.NewGuid()) });
        Assert.Equal("RevisionConflict", stale.Code);
        Assert.Equal(stroke.RevisionId, value.Content.GetProperty("RevisionId").GetGuid());
        Assert.Equal("#FF000000", value.Content.GetProperty("ResolvedBrushProperties").GetProperty("Color").GetString());
    }

    [Fact]
    public void Canonical_singleton_defaults_and_flips_are_transferable_without_claiming_native_support()
    {
        var stroke = Stroke() with { Samples = [new(3, 4)], Transform = new() { ScaleX = -1 } };
        var handler = new CanvasInkObjectHandler();
        var value = handler.Create(stroke.StrokeId, JsonSerializer.SerializeToElement(stroke));
        Assert.Equal(-1, value.Content.GetProperty("Transform").GetProperty("ScaleX").GetDouble());
        var raw = JsonNode.Parse(value.Content.GetRawText())!.AsObject();
        raw["Transform"] = new JsonObject();
        var sample = raw["Samples"]![0]!.AsObject();
        sample.Remove("Pressure"); sample.Remove("TiltX"); sample.Remove("TiltY"); sample.Remove("TimestampMilliseconds");
        var defaults = handler.Create(stroke.StrokeId, JsonSerializer.SerializeToElement(raw));
        Assert.Equal(.5, defaults.Content.Deserialize<CanvasInkStroke>()!.Samples[0].Pressure);
        var copy = handler.CloneForPaste(defaults, Guid.NewGuid());
        Assert.True(JsonElement.DeepEquals(defaults.Content.GetProperty("Samples"), copy.Content.GetProperty("Samples")));
        var error = Assert.Throws<NotSupportedException>(() => handler.Render(defaults));
        Assert.Contains("single-sample", error.Message);
    }

    [Theory]
    [InlineData("missingIdentity")]
    [InlineData("negativeTimestamp")]
    [InlineData("zeroScale")]
    [InlineData("pressureOutOfRange")]
    [InlineData("nullSample")]
    public void Invalid_canonical_input_is_rejected_before_shared_transfer(string defect)
    {
        var stroke = Stroke();
        var content = JsonNode.Parse(JsonSerializer.Serialize(stroke))!.AsObject();
        switch (defect)
        {
            case "missingIdentity": content.Remove("StrokeId"); break;
            case "negativeTimestamp": content["Samples"]![0]!["TimestampMilliseconds"] = -1; break;
            case "zeroScale": content["Transform"]!["ScaleY"] = 0; break;
            case "pressureOutOfRange": content["Samples"]![0]!["Pressure"] = 1.1; break;
            case "nullSample": content["Samples"]![0] = null; break;
        }
        Assert.Throws<InvalidDataException>(() => new CanvasInkObjectHandler().Create(stroke.StrokeId, JsonSerializer.SerializeToElement(content)));
    }

    [Theory]
    [InlineData("StrokeId", "strokeId")]
    [InlineData("Color", "color")]
    [InlineData("X", "x")]
    [InlineData("ScaleX", "scaleX")]
    public void Ambiguous_typed_fields_cannot_change_meaning_after_reopen(string field, string alias)
    {
        var stroke = Stroke();
        var json = JsonSerializer.Serialize(stroke);
        var parsed = JsonSerializer.SerializeToElement(stroke);
        var fields = field switch
        {
            "Color" => parsed.GetProperty("ResolvedBrushProperties"),
            "X" => parsed.GetProperty("Samples")[0],
            "ScaleX" => parsed.GetProperty("Transform"),
            _ => parsed
        };
        var token = "\"" + field + "\":" + fields.GetProperty(field).GetRawText();
        json = json.Replace(token, token + ",\"" + alias + "\":" + fields.GetProperty(field).GetRawText(), StringComparison.Ordinal);
        using var ambiguous = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() => new CanvasInkObjectHandler().Create(stroke.StrokeId, ambiguous.RootElement));
    }

    private static HomeProductivityContext Context(string app, string artifact, Guid revision, IReadOnlyList<Guid> selected) =>
        new(app, artifact, 0, selected, new HashSet<string> { "drawing.ink" }) { ArtifactRevision = new(VersionId: revision) };

    private static HomeProductivityAction Color(Guid id) =>
        new("ink.color", 1, "drawing.ink", [id], JsonSerializer.SerializeToElement(new { color = "#FF008000" }), 0);

    private static CanvasInkStroke Stroke(bool withFragment = false) => new()
    {
        LayerId = Guid.NewGuid(),
        Samples = [new(0, 0, .2, -12, 6, 1), new(20, 10, .8, -4, 9, 15), new(100, 20, .3, 8, -2, 32)],
        ResolvedBrushProperties = CanvasRnoteInkStyle.Default.BrushSnapshot() with
        { ExtensionData = new() { ["futureBrushProperty"] = JsonSerializer.SerializeToElement(true) } },
        ExtensionData = new() { ["futureStrokeProperty"] = JsonSerializer.SerializeToElement(new { retained = true }) },
        PathGeometry = withFragment ? new()
        {
            Start = new(0, 0, .2),
            Segments = [new() { Kind = CanvasStrokePathSegmentKind.Line, End = new(20, 10, .8) }]
        } : null
    };
}

/// <summary>Requires the genuine ABI 3 donor with selection API v1 and split eraser support.
/// Keep separate from the managed semantic suite; absence of native dependencies is NOT RUN.</summary>
public sealed class CanvasSharedInkNativeOwnerTests
{
    [Fact]
    [Trait("Category", "NativeRnote")]
    public void Bound_owner_renders_exact_fragment_after_split_and_reopen_and_rejects_changed_content_clone_and_stale_revision()
    {
        using var document = CanvasRnoteDocument.Create("Shared exact-path drawing");
        document.DrawStroke(NativeRnoteSplitMaterializationTests.LongStroke(), document.Identity.RevisionId);
        var preview = document.PreviewSplitErase(
            NativeRnoteSplitMaterializationTests.GenuineMiddleGesture(document.ExportRnote()), 1, document.Identity.RevisionId);
        document.ApplySplitErase(preview, new(document.Identity.RevisionId, Guid.NewGuid(), new("shared-ink-test", "Shared ink test")));
        var artifact = document.Snapshot;
        var stroke = artifact.Pages[0].Strokes.First(item => item.PathGeometry is not null);
        var handler = new CanvasInkObjectHandler(document, artifact.RevisionId);
        var value = handler.Create(stroke.StrokeId, JsonSerializer.SerializeToElement(stroke));
        var result = handler.Render(value);
        var binding = Assert.Single(result.RasterBindings);
        using (var selected = RnoteCanvasEngine.Open(document.ExportCanonicalStrokeSelection([stroke.StrokeId], artifact.RevisionId)))
        {
            var exact = CanvasSvgRasterizer.Render(selected.Render().Svg, 512, 512);
            Assert.Equal(exact.PremultipliedBgra, binding.Frame.CopyPixels());
        }
        Assert.Contains(binding.Frame.CopyPixels(), pixel => pixel != 0);
        Assert.Equal(stroke.StrokeId, binding.ObjectId);
        using var reopened = CanvasRnoteDocument.Open(document.Serialize());
        var restoredHandler = new CanvasInkObjectHandler(reopened, reopened.Identity.RevisionId);
        Assert.Equal(binding.Frame.CopyPixels(), Assert.Single(restoredHandler.Render(value).RasterBindings).Frame.CopyPixels());

        var proposed = handler.Transform(value, new("ink.color", 1, "drawing.ink", [stroke.StrokeId],
            JsonSerializer.SerializeToElement(new { color = "#FFFF0000" }), 0));
        Assert.Throws<InvalidOperationException>(() => handler.Render(proposed));
        Assert.Throws<InvalidOperationException>(() => handler.Render(handler.CloneForPaste(value, Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => new CanvasInkObjectHandler(document, Guid.Empty));
        document.DrawStroke([new(1000, 1000, .5), new(1100, 1100, .5)], document.Identity.RevisionId);
        Assert.Throws<InvalidOperationException>(() => handler.Render(value));
        // The renderer borrows its owner; it never closes or replaces the document.
        Assert.NotEqual(artifact.RevisionId, document.Identity.RevisionId);
        Assert.NotEmpty(document.Serialize());
    }
}
