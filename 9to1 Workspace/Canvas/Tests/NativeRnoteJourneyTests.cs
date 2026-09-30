using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

/// <summary>Requires the actual controlled-donor native bridge, never a fixture substitute.</summary>
public sealed class NativeRnoteJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oversized_gzip_expansion_including_concatenated_members_is_rejected_before_native_parsing(bool concatenated)
    {
        using var output = new MemoryStream();
        // Produce a small compressed input with expansion over 256MiB. The
        // test and the production guard stream chunks, never allocating the
        // expanded content; no native parser is reached on this failure.
        // In the concatenated case each member is only 128MiB+1: rejecting
        // the combined input proves the guard consumed the second member,
        // matching the donor's MultiGzDecoder rather than just its first.
        for (var member = 0; member < (concatenated ? 2 : 1); member++)
        {
            using var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
            var chunk = new byte[8192];
            for (var index = 0; index < (concatenated ? 16384 : 32768); index++) gzip.Write(chunk);
            gzip.WriteByte(0);
        }
        var payload = output.ToArray();
        Assert.InRange(payload.Length, 1, 2 * 1024 * 1024);
        Assert.Contains("expanded", Assert.Throws<InvalidDataException>(() => RnoteCanvasEngine.Open(payload)).Message);
        Assert.Contains("expanded", Assert.Throws<InvalidDataException>(() => RnoteCanvasEngine.ImportXopp(payload)).Message);
    }

    [Fact]
    public void Resolved_solid_and_marker_styles_reach_actual_donor_and_do_not_restyle_previous_strokes()
    {
        using var document = CanvasRnoteDocument.Create();
        var actor = new CanvasActorContext("authorized-test-actor", "Actor");
        var red = new CanvasRnoteInkStyle(Color: "#FFFF0000", BaseWidth: 7, Opacity: 0.5);
        document.DrawStroke([new(10, 20, 0.2), new(30, 40, 0.8)], new(document.Snapshot.RevisionId, Guid.NewGuid(), actor), red);
        var blue = new CanvasRnoteInkStyle(CanvasRnoteInkKind.Marker, "#FF0000FF", 12, 0.25);
        document.DrawStroke([new(50, 60, 0.2), new(70, 90, 0.8)], new(document.Snapshot.RevisionId, Guid.NewGuid(), actor), blue);
        var strokes = document.Snapshot.Pages[0].Strokes;
        Assert.Equal("pen", strokes[0].ToolDefinitionId);
        Assert.Equal("highlighter", strokes[1].ToolDefinitionId);
        Assert.Equal("#FFFF0000", strokes[0].ResolvedBrushProperties.Color);
        Assert.Equal(7, strokes[0].ResolvedBrushProperties.BaseWidth);
        Assert.Equal(0.5, strokes[0].ResolvedBrushProperties.Opacity);
        Assert.Equal("marker", strokes[1].ResolvedBrushProperties.EngineParameters["brushStyle"].GetString());
        using var source = new MemoryStream(document.ExportRnote());
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var json = System.Text.Json.JsonDocument.Parse(gzip);
        var brushes = json.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("stroke_components")
            .EnumerateArray().Where(component => component.GetProperty("value").ValueKind != System.Text.Json.JsonValueKind.Null)
            .Select(component => component.GetProperty("value").GetProperty("brushstroke").GetProperty("style").GetProperty("smooth")).ToArray();
        Assert.Equal(2, brushes.Length);
        Assert.Equal(7, brushes[0].GetProperty("stroke_width").GetDouble());
        Assert.Equal(1, brushes[0].GetProperty("stroke_color").GetProperty("r").GetDouble());
        Assert.Equal(0.5, brushes[0].GetProperty("stroke_color").GetProperty("a").GetDouble());
        Assert.Equal(12, brushes[1].GetProperty("stroke_width").GetDouble());
        Assert.Equal(1, brushes[1].GetProperty("stroke_color").GetProperty("b").GetDouble());
        Assert.Equal(0.25, brushes[1].GetProperty("stroke_color").GetProperty("a").GetDouble());
        using var reopened = CanvasRnoteDocument.Open(document.Serialize());
        Assert.Equal(StableSvg(document.Render().Svg), StableSvg(reopened.Render().Svg));
        Assert.Equal(strokes.Select(stroke => stroke.StrokeId), reopened.Snapshot.Pages[0].Strokes.Select(stroke => stroke.StrokeId));
        var revision = document.Snapshot.RevisionId;
        Assert.Throws<ArgumentException>(() => document.DrawStroke([new(1, 2, 0.5), new(3, 4, 0.5)], new(revision, Guid.NewGuid(), actor), red with { BaseWidth = double.NaN }));
        Assert.Equal(revision, document.Snapshot.RevisionId);
        Assert.Equal(2, document.Snapshot.Pages[0].Strokes.Count);
    }

    [Fact]
    public void Caller_list_is_enumerated_once_and_canonical_and_native_strokes_use_the_same_snapshot()
    {
        var points = new[] { new RnotePointerSample(15, 25, 0.2), new RnotePointerSample(45, 65, 0.7) };
        var changing = new ChangingSamples(points);
        using var actual = CanvasRnoteDocument.Create();
        actual.DrawStroke(changing, actual.Snapshot.RevisionId);
        Assert.Equal(1, changing.Enumerations);
        var retained = Assert.Single(actual.Snapshot.Pages[0].Strokes).Samples;
        Assert.Equal(points.Select(point => point.X), retained.Select(point => point.X));
        Assert.Equal(points.Select(point => point.Pressure), retained.Select(point => point.Pressure));
        AssertDonorCapturedEndpoints(actual.ExportRnote(), points);
        using var reopened = CanvasRnoteDocument.Open(actual.Serialize());
        Assert.Equal(StableSvg(actual.Render().Svg), StableSvg(reopened.Render().Svg));
        using var publicEngine = RnoteCanvasEngine.Create();
        var direct = new ChangingSamples(points);
        publicEngine.DrawStroke(direct);
        Assert.Equal(1, direct.Enumerations);
        AssertDonorCapturedEndpoints(publicEngine.Save(), points);
    }

    [Fact]
    public void Failed_sample_capture_cannot_leave_a_partial_native_or_canonical_stroke()
    {
        using var document = CanvasRnoteDocument.Create();
        var revision = document.Snapshot.RevisionId;
        var frame = StableSvg(document.Render().Svg);
        Assert.Throws<InvalidOperationException>(() => document.DrawStroke(new ThrowingSamples(), revision));
        Assert.Equal(revision, document.Snapshot.RevisionId);
        Assert.Empty(document.Snapshot.Pages[0].Strokes);
        Assert.Equal(frame, StableSvg(document.Render().Svg));
        using var engine = RnoteCanvasEngine.Create();
        var nativeFrame = StableSvg(engine.Render().Svg);
        Assert.Throws<InvalidOperationException>(() => engine.DrawStroke(new ThrowingSamples()));
        Assert.Equal(nativeFrame, StableSvg(engine.Render().Svg));
    }

    [Fact]
    public void Unbounded_caller_enumeration_is_rejected_before_any_native_operation()
    {
        using var document = CanvasRnoteDocument.Create();
        var revision = document.Snapshot.RevisionId;
        Assert.Throws<ArgumentException>(() => document.DrawStroke(new EndlessSamples(), revision));
        Assert.Equal(revision, document.Snapshot.RevisionId);
        Assert.Empty(document.Snapshot.Pages[0].Strokes);
    }

    private sealed class ChangingSamples(RnotePointerSample[] first) : IReadOnlyList<RnotePointerSample>
    {
        public int Enumerations { get; private set; }
        public int Count => int.MaxValue;
        public RnotePointerSample this[int index] => throw new InvalidOperationException("Caller-owned indexer must not be consumed");
        public IEnumerator<RnotePointerSample> GetEnumerator()
        {
            Enumerations++;
            foreach (var point in first) yield return Enumerations == 1 ? point : new(900, 950, 0.1);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class ThrowingSamples : IReadOnlyList<RnotePointerSample>
    {
        public int Count => 2;
        public RnotePointerSample this[int index] => new(10, 20, 0.5);
        public IEnumerator<RnotePointerSample> GetEnumerator() { yield return new(10, 20, 0.5); throw new InvalidOperationException("Source changed during enumeration"); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class EndlessSamples : IReadOnlyList<RnotePointerSample>
    {
        public int Count => 2;
        public RnotePointerSample this[int index] => new(10, 20, 0.5);
        public IEnumerator<RnotePointerSample> GetEnumerator() { while (true) yield return new(10, 20, 0.5); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void AssertDonorCapturedEndpoints(byte[] rnote, RnotePointerSample[] expected)
    {
        using var source = new MemoryStream(rnote);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var json = System.Text.Json.JsonDocument.Parse(gzip);
        var components = json.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("stroke_components");
        var brush = Assert.Single(components.EnumerateArray(), component => component.GetProperty("value").ValueKind != System.Text.Json.JsonValueKind.Null)
            .GetProperty("value").GetProperty("brushstroke");
        var path = brush.GetProperty("path");
        var start = path.GetProperty("start");
        Assert.Equal(expected[0].X, start.GetProperty("pos")[0].GetDouble());
        Assert.Equal(expected[0].Y, start.GetProperty("pos")[1].GetDouble());
        Assert.Equal(expected[0].Pressure, start.GetProperty("pressure").GetDouble());
        var generated = path.GetProperty("segments").EnumerateArray().Select(segment => segment.GetProperty("lineto").GetProperty("end")).ToArray();
        foreach (var element in generated)
        {
            Assert.InRange(element.GetProperty("pos")[0].GetDouble(), expected.Min(point => point.X), expected.Max(point => point.X));
            Assert.InRange(element.GetProperty("pos")[1].GetDouble(), expected.Min(point => point.Y), expected.Max(point => point.Y));
        }
        // The donor's wall-clock-dependent smoothing creates interpolated
        // geometry, rather than retaining the authored final coordinate.
        // Prove that it progressed towards the captured endpoint and retained
        // its pressure; exact authored coordinates are asserted above in the
        // canonical graph. The bounds also exclude the adversarial second
        // enumeration's distant samples without inventing a precision claim.
        var end = generated[^1];
        Assert.True(Math.Abs(end.GetProperty("pos")[0].GetDouble() - expected[^1].X) < Math.Abs(end.GetProperty("pos")[0].GetDouble() - expected[0].X));
        Assert.True(Math.Abs(end.GetProperty("pos")[1].GetDouble() - expected[^1].Y) < Math.Abs(end.GetProperty("pos")[1].GetDouble() - expected[0].Y));
        Assert.Equal(expected[^1].Pressure, end.GetProperty("pressure").GetDouble());
    }

    [Fact]
    public void Native_draw_serialize_reopen_preserves_structured_ink_identity_pressure_tilt_and_render()
    {
        using var document = CanvasRnoteDocument.Create("Native engine journey");
        var original = document.Snapshot;
        var samples = new[]
        {
            new RnotePointerSample(120, 120, 0.15, 0.2, -0.1),
            new RnotePointerSample(150, 140, 0.35, 0.1, -0.2),
            new RnotePointerSample(190, 160, 0.65, 0.3, -0.3),
            new RnotePointerSample(235, 190, 0.9, 0.4, -0.4)
        };
        var strokeId = document.DrawStroke(samples, original.RevisionId);
        var before = document.Render();
        var state = document.Snapshot;
        Assert.Throws<InvalidOperationException>(() => document.DrawStroke(samples, original.RevisionId));
        document.Rename("Renamed native canvas", state.RevisionId);
        using var reopened = CanvasRnoteDocument.Open(document.Serialize());
        var after = reopened.Render();
        var restored = reopened.Snapshot;
        Assert.Equal(original.ArtifactId, restored.ArtifactId);
        Assert.Equal(original.PageOrder, restored.PageOrder);
        Assert.Equal("Renamed native canvas", restored.DisplayName);
        var stroke = Assert.Single(restored.Pages[0].Strokes);
        Assert.Equal(strokeId, stroke.StrokeId);
        Assert.Equal(samples.Select(sample => sample.Pressure), stroke.Samples.Select(sample => sample.Pressure));
        Assert.Equal(samples.Select(sample => sample.TiltX), stroke.Samples.Select(sample => sample.TiltX));
        Assert.Equal(samples.Select(sample => sample.TiltY), stroke.Samples.Select(sample => sample.TiltY));
        Assert.Equal(StableSvg(before.Svg), StableSvg(after.Svg));
        Assert.Equal((before.X, before.Y, before.Width, before.Height), (after.X, after.Y, after.Width, after.Height));
    }

    [Fact]
    public void Actual_xopp_import_retains_editable_donor_snapshot_and_reports_identity_translation_gate()
    {
        const string xml = "<?xml version=\"1.0\"?><xournal creator=\"Canvas test\" fileversion=\"4\"><title>Ink</title><page width=\"595\" height=\"842\"><background type=\"solid\" color=\"#ffffffff\" style=\"plain\"/><layer><stroke tool=\"pen\" color=\"#000000ff\" width=\"2\">10 20 30 40 50 35</stroke></layer></page></xournal>";
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(Encoding.UTF8.GetBytes(xml));
        using var imported = CanvasRnoteDocument.Import(compressed.ToArray(), "xopp", "Imported ink");
        var frame = imported.Render();
        Assert.Contains("<svg", Encoding.UTF8.GetString(frame.Svg));
        Assert.Contains(imported.CompatibilityReport.Issues, issue => issue.Disposition == CanvasCompatibilityDisposition.Blocked && issue.Feature == "Donor entity identity");
        using var reopened = CanvasRnoteDocument.Open(imported.Serialize());
        Assert.Equal(imported.Snapshot.ArtifactId, reopened.Snapshot.ArtifactId);
        Assert.Equal(StableSvg(frame.Svg), StableSvg(reopened.Render().Svg));
        Assert.Equal("xopp", reopened.CompatibilityReport.SourceFormat);
        using var rnote = CanvasRnoteDocument.Import(reopened.ExportRnote(), "rnote");
        Assert.Equal(StableSvg(frame.Svg), StableSvg(rnote.Render().Svg));
        Assert.Throws<InvalidDataException>(() => CanvasRnoteDocument.Import([1, 2, 3], "xopp"));
    }

    [Fact]
    public void Tampered_engine_snapshot_fails_before_native_open()
    {
        using var document = CanvasRnoteDocument.Create();
        var artifact = CanvasArtifactCodec.Deserialize(document.Serialize());
        var state = artifact.DocumentSettings.Properties["9to1.Canvas.RnoteState"];
        var json = state.GetRawText().Replace(state.GetProperty("Sha256").GetString()!, new string('0', 64), StringComparison.Ordinal);
        artifact.DocumentSettings.Properties["9to1.Canvas.RnoteState"] = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        Assert.Throws<InvalidDataException>(() => CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(artifact)));
    }

    [Fact]
    public void Canonical_history_and_operation_replay_keep_native_engine_and_stable_stroke_in_sync()
    {
        using var document = CanvasRnoteDocument.Create("History");
        var actor = new CanvasActorContext("test-authorized-native-input", "Native input");
        var request = new CanvasMutationRequest(document.Snapshot.RevisionId, Guid.NewGuid(), actor);
        var samples = new[] { new RnotePointerSample(10, 20, 0.3), new RnotePointerSample(30, 40, 0.8) };
        var id = document.DrawStroke(samples, request);
        var frame = StableSvg(document.Render().Svg);
        var afterDraw = document.Snapshot;
        Assert.Equal(id, document.DrawStroke(samples, request));
        Assert.Equal(afterDraw.RevisionId, document.Snapshot.RevisionId);
        Assert.Single(document.Snapshot.Pages[0].Strokes);
        Assert.Equal(frame, StableSvg(document.Render().Svg));
        Assert.Throws<InvalidOperationException>(() => document.DrawStroke([new(2, 3, 0.2), new(7, 8, 0.8)], request));
        Assert.Equal(afterDraw.RevisionId, document.Snapshot.RevisionId);
        document.Undo(new(afterDraw.RevisionId, Guid.NewGuid(), actor));
        Assert.Empty(document.Snapshot.Pages[0].Strokes);
        document.Redo(new(document.Snapshot.RevisionId, Guid.NewGuid(), actor));
        Assert.Equal(id, Assert.Single(document.Snapshot.Pages[0].Strokes).StrokeId);
        Assert.Equal(frame, StableSvg(document.Render().Svg));
        using var reopened = CanvasRnoteDocument.Open(document.Serialize());
        Assert.Equal(id, Assert.Single(reopened.Snapshot.Pages[0].Strokes).StrokeId);
        Assert.Equal(frame, StableSvg(reopened.Render().Svg));
    }

    private static string StableSvg(byte[] bytes)
    {
        var xml = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(bytes));
        var ids = xml.Descendants().Attributes("id").Select(attribute => attribute.Value).Distinct().ToArray();
        for (var index = 0; index < ids.Length; index++)
        {
            foreach (var attribute in xml.Descendants().Attributes())
                if (attribute.Name.LocalName == "id" && attribute.Value == ids[index]) attribute.Value = $"resource-{index}";
                else attribute.Value = attribute.Value.Replace("#" + ids[index], "#resource-" + index, StringComparison.Ordinal);
        }
        return xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }
}
