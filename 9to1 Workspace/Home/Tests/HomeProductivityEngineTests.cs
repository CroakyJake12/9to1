using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeProductivityEngineTests
{
    [Fact]
    public void Bundle_round_trip_preserves_unknown_optional_properties_and_stable_styles()
    {
        var engine = new HomeProductivityEngine();
        var extension = JsonDocument.Parse("{\"future\":\"keep\"}").RootElement.Clone();
        var content = JsonDocument.Parse("{\"text\":\"Hello\"}").RootElement.Clone();
        var item = new HomeProductivityObject(Guid.NewGuid(), "text.paragraph", 1, content,
            JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse("{}").RootElement.Clone(), [],
            JsonDocument.Parse("{}").RootElement.Clone(), new Dictionary<string, JsonElement> { ["x-extra"] = extension });
        var context = new HomeProductivityContext("write", "doc-1", 4, [item.ObjectId], new HashSet<string>(["text.paragraph"]));
        var parsed = engine.ParseBundle(engine.SerializeBundle(engine.SerializeSelection(context, [item])));
        Assert.Equal("keep", parsed.Objects.Single().Extensions!["x-extra"].GetProperty("future").GetString());
        Assert.True(engine.CanPaste(parsed, context, out var code));
        Assert.Equal("CanPaste", code);
        Assert.NotEqual(item.ObjectId, engine.Paste(parsed, context).Single().ObjectId);
    }

    [Fact]
    public void Compatibility_and_typed_actions_report_conflicts_without_silent_loss()
    {
        var engine = new HomeProductivityEngine();
        var compatibility = engine.GetCompatibility("write", "1.0", ["text.paragraph", "future.required"]);
        Assert.False(compatibility.Compatible);
        Assert.Equal("NeedsAttention", compatibility.State);
        var id = Guid.NewGuid();
        var context = new HomeProductivityContext("write", "doc-1", 7, [id], new HashSet<string>(["text.paragraph"]));
        var action = new HomeProductivityAction("format.bold", 1, "text.paragraph", [id],
            JsonDocument.Parse("{}").RootElement.Clone(), 6);
        Assert.Equal("RevisionConflict", engine.ApplyAction(context, action).Code);
        var unsupported = new HomeProductivityObject(id, "unknown.required", 2,
            JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse("{}").RootElement.Clone(),
            JsonDocument.Parse("{}").RootElement.Clone(), [], JsonDocument.Parse("{}").RootElement.Clone());
        Assert.Throws<InvalidDataException>(() => engine.ParseBundle(engine.SerializeBundle(new HomeProductivityObjectBundle(
            1, [unsupported], [], [], JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse("{}").RootElement.Clone()))));
    }
}
