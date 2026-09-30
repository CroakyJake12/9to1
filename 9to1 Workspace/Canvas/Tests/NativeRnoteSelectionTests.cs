using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

/// <summary>Executes the real controlled donor's additive selection API.</summary>
public sealed class NativeRnoteSelectionTests
{
    [Fact]
    public void Selected_export_preserves_stable_native_identity_and_original_paths_without_unselected_content()
    {
        using var source = RnoteCanvasEngine.Create();
        Assert.True(source.SupportsStructuredSelectionExport);
        Assert.Empty(source.ReadStrokeKeys());
        source.DrawStroke([new(15, 25, 0.2), new(45, 65, 0.7)]);
        var first = Assert.Single(source.ReadStrokeKeys());
        source.DrawStroke([new(900, 950, 0.3), new(930, 980, 0.8)]);
        var allKeys = source.ReadStrokeKeys();
        Assert.Equal(2, allKeys.Length);
        using var reopened = RnoteCanvasEngine.Open(source.Save());
        Assert.Equal(allKeys.ToArray(), reopened.ReadStrokeKeys().ToArray());
        var before = reopened.Save();
        var captured = new SingleEnumerationKeys(first);
        var selectedBytes = reopened.ExportSelectedStrokes(captured);
        Assert.Equal(1, captured.Enumerations);
        using var selected = RnoteCanvasEngine.Open(selectedBytes);
        Assert.Equal(first, Assert.Single(selected.ReadStrokeKeys()));
        using var fullJson = Parse(before);
        using var selectionJson = Parse(selectedBytes);
        var originalStrokes = Strokes(fullJson);
        var selectedStroke = Assert.Single(Strokes(selectionJson));
        Assert.Equal(2, originalStrokes.Length);
        Assert.Contains(originalStrokes, original => JsonElement.DeepEquals(original, selectedStroke));
        Assert.Single(originalStrokes, original => !JsonElement.DeepEquals(original, selectedStroke));
        // Rnote export bounds include page geometry, so two separated strokes
        // can occupy the same page-sized SVG. Inspect retained entities rather
        // than infer content isolation from the exported page width.
        Assert.NotEmpty(selected.Render().Svg);
        Assert.Equal(allKeys.ToArray(), reopened.ReadStrokeKeys().ToArray());
        using var afterJson = Parse(reopened.Save());
        Assert.True(JsonElement.DeepEquals(fullJson.RootElement, afterJson.RootElement));
    }

    [Fact]
    public void Invalid_or_failed_selection_does_not_modify_the_native_document()
    {
        using var source = RnoteCanvasEngine.Create();
        source.DrawStroke([new(5, 10, 0.2), new(35, 40, 0.6)]);
        var key = Assert.Single(source.ReadStrokeKeys());
        var before = source.Save();
        Assert.Throws<ArgumentException>(() => source.ExportSelectedStrokes([]));
        Assert.Throws<ArgumentException>(() => source.ExportSelectedStrokes([key, key]));
        Assert.Throws<InvalidOperationException>(() => source.ExportSelectedStrokes(ThrowingKeys(key)));
        Assert.Throws<InvalidOperationException>(() => source.ExportSelectedStrokes([ulong.MaxValue]));
        Assert.Equal(key, Assert.Single(source.ReadStrokeKeys()));
        using var beforeJson = Parse(before);
        using var afterJson = Parse(source.Save());
        Assert.True(JsonElement.DeepEquals(beforeJson.RootElement, afterJson.RootElement));
    }

    private static JsonDocument Parse(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static JsonElement[] Strokes(JsonDocument document) => document.RootElement.GetProperty("data")
        .GetProperty("engine_snapshot").GetProperty("stroke_components").EnumerateArray()
        .Select(component => component.GetProperty("value")).Where(value => value.ValueKind != JsonValueKind.Null).ToArray();

    private static IEnumerable<ulong> ThrowingKeys(ulong key)
    {
        yield return key;
        throw new InvalidOperationException("Caller enumeration failed.");
    }

    private sealed class SingleEnumerationKeys(ulong key) : IEnumerable<ulong>
    {
        public int Enumerations { get; private set; }
        public IEnumerator<ulong> GetEnumerator()
        {
            if (++Enumerations != 1) throw new InvalidOperationException("Keys enumerated twice.");
            yield return key;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
