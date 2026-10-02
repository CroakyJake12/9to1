using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed class NativeRnoteTrashEraserTests
{
    [Fact]
    public void Genuine_trash_eraser_removes_colliding_native_strokes_and_retains_unrelated_entities_after_reopen()
    {
        using var original = RnoteCanvasEngine.Create();
        original.DrawStroke([new(20,30,.2),new(80,90,.8)]);
        var first = Assert.Single(original.ReadStrokeKeys());
        original.DrawStroke([new(500,600,.4),new(540,660,.7)]);
        var second = Assert.Single(original.ReadStrokeKeys().Where(key => key != first));
        var originalBytes = original.Save();
        var retained = original.ExportSelectedStrokes([second]);
        using var candidate = RnoteCanvasEngine.Open(originalBytes);
        candidate.EraseWholeStrokes([new(49,59,.5),new(51,61,.5)],20);
        Assert.Equal(second, Assert.Single(candidate.ReadStrokeKeys()));
        Assert.Equal(new[] { first,second }.Order(), original.ReadStrokeKeys().Order());
        using var reopened = RnoteCanvasEngine.Open(candidate.Save());
        Assert.Equal(second, Assert.Single(reopened.ReadStrokeKeys()));
        AssertSameSelectedStateWithKnownChronologyAdvance(retained,reopened.ExportSelectedStrokes([second]),1);
        Assert.True(SameNative(originalBytes,original.Save()));
    }

    [Fact]
    public void Invalid_eraser_input_and_missed_strokes_leave_native_entities_unchanged()
    {
        using var engine = RnoteCanvasEngine.Create();
        engine.DrawStroke([new(20,30,.2),new(80,90,.8)]);
        var key = Assert.Single(engine.ReadStrokeKeys());
        var before = engine.ExportSelectedStrokes([key]);
        Assert.Throws<ArgumentException>(() => engine.EraseWholeStrokes([new(20,30,.5),new(21,31,.5)],double.NaN));
        Assert.Throws<ArgumentException>(() => engine.EraseWholeStrokes([new(double.NaN,30,.5),new(21,31,.5)],20));
        engine.EraseWholeStrokes([new(800,800,.5),new(810,810,.5)],20);
        Assert.Equal(key,Assert.Single(engine.ReadStrokeKeys()));
        Assert.True(SameNative(before,engine.ExportSelectedStrokes([key])));
    }
    internal static void AssertSameSelectedStateWithKnownChronologyAdvance(byte[] left, byte[] right, uint erasedStrokeCount)
    {
        using var leftStream = new GZipStream(new MemoryStream(left),CompressionMode.Decompress);
        using var rightStream = new GZipStream(new MemoryStream(right),CompressionMode.Decompress);
        using var before = JsonDocument.Parse(leftStream);
        using var after = JsonDocument.Parse(rightStream);
        var beforeClock = before.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("chrono_counter").GetUInt32();
        var afterClock = after.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("chrono_counter").GetUInt32();
        // Actual donor set_trashed updates the trashed entity's chronology once.
        // This global counter advances; the selected entity's own chronology,
        // native key, every path/control/pressure/style, and all other fields stay exact.
        Assert.Equal(checked(beforeClock+erasedStrokeCount),afterClock);
        var normalizedAfter = JsonNode.Parse(after.RootElement.GetRawText())!;
        normalizedAfter["data"]!["engine_snapshot"]!["chrono_counter"] = beforeClock;
        Assert.True(JsonElement.DeepEquals(before.RootElement,JsonSerializer.SerializeToElement(normalizedAfter)),
            "Any selected native state difference beyond the proven global chronology advance must fail.");
    }

    private static bool SameNative(byte[] left, byte[] right)
    {
        using var leftStream = new GZipStream(new MemoryStream(left), CompressionMode.Decompress);
        using var rightStream = new GZipStream(new MemoryStream(right), CompressionMode.Decompress);
        using var leftJson = JsonDocument.Parse(leftStream);
        using var rightJson = JsonDocument.Parse(rightStream);
        return JsonElement.DeepEquals(leftJson.RootElement, rightJson.RootElement);
    }
}
