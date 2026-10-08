using System.Text.Json;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Cards.HomeIntegration.Tests;

public sealed class CardHomeProductivityBridgeTests
{
    private static JsonElement EmptyObject() =>
        JsonDocument.Parse("{}").RootElement.Clone();

    [Fact]
    public void SharedObjectEnvelopePreservesOwnedObjectIdentityAndPayload()
    {
        Guid objectId = Guid.NewGuid();
        JsonElement content = JsonDocument.Parse("""
            {"text":"Cells", "style":{"bold":true}, "future":{"preserve":[1,2,3]}}
            """).RootElement.Clone();

        var item = new HomeProductivityObject(objectId, "text.paragraph", 1,
            content, EmptyObject(), EmptyObject(), [], EmptyObject());
        var bundle = new HomeProductivityObjectBundle(
            1, [item], [], [], EmptyObject(), EmptyObject());

        CardSide captured = CardHomeProductivityBridge.CaptureBundle(bundle, "Cells");
        var reopened = JsonSerializer.Deserialize<HomeProductivityObjectBundle>(
            captured.Document.GetRawText());

        Assert.Equal(CardHomeProductivityBridge.BundleFormat, captured.DocumentFormat);
        Assert.NotNull(reopened);
        Assert.Equal(objectId, reopened!.Objects.Single().ObjectId);
        Assert.True(JsonElement.DeepEquals(content, reopened.Objects[0].Content));
    }

    [Fact]
    public void ActualHomeEngineRejectsUnsupportedObjectTypesWithoutFlatteningThem()
    {
        var engine = new HomeProductivityEngine();
        var unknown = new HomeProductivityObject(Guid.NewGuid(),
            "cards.unknown-required-object", 1,
            EmptyObject(), EmptyObject(), EmptyObject(), [], EmptyObject());
        var bundle = new HomeProductivityObjectBundle(
            1, [unknown], [], [], EmptyObject(), EmptyObject());
        CardSide side = CardHomeProductivityBridge.CaptureBundle(bundle);

        CardProductivityInspection inspection = CardHomeProductivityBridge.Inspect(
            side, engine, Guid.NewGuid(), 1);

        Assert.False(inspection.Compatible);
        Assert.Equal("RequiredSchemaUnsupported", inspection.Code);
        Assert.Contains("cards.unknown-required-object", inspection.UnsupportedObjectTypes);
        Assert.Equal(engine.EngineVersion, inspection.EngineVersion);
        Assert.True(JsonElement.DeepEquals(unknown.Content,
            JsonSerializer.Deserialize<HomeProductivityObjectBundle>(
                side.Document.GetRawText())!.Objects.Single().Content));
    }

    [Fact]
    public void ActualHomeParagraphHandlerAcceptsCompatibleRichCardContent()
    {
        var engine = new HomeProductivityEngine();
        NotesBlock paragraph = NotesBlock.CreateParagraph("Cells");
        paragraph.StyleId = string.Empty;
        paragraph.Runs.Add(new NotesTextRun { Text = "Cells", Bold = true });
        HomeProductivityObject canonical = HomeNotesSharedObjects.Project(paragraph);
        var bundle = new HomeProductivityObjectBundle(
            1, [canonical], [], [], EmptyObject(), EmptyObject());
        CardSide side = CardHomeProductivityBridge.CaptureBundle(bundle, "Cells");

        CardProductivityInspection inspection = CardHomeProductivityBridge.Inspect(
            side, engine, Guid.NewGuid(), 1);

        Assert.True(inspection.Compatible, inspection.Code);
        Assert.Equal("Compatible", inspection.Code);
        Assert.Empty(inspection.UnsupportedObjectTypes);
        Assert.Equal(paragraph.Id, bundle.Objects.Single().ObjectId);
    }

    [Fact]
    public void UnknownLegacyContentIsPreservedAndNotDeclaredRenderable()
    {
        var engine = new HomeProductivityEngine();
        var side = new CardSide
        {
            DocumentFormat = "unknown-rich-format/2",
            Document = EmptyObject(),
        };

        CardProductivityInspection result = CardHomeProductivityBridge.Inspect(
            side, engine, Guid.NewGuid(), 1);
        Assert.False(result.Compatible);
        Assert.Equal("DocumentAdapterRequired", result.Code);
        Assert.Equal("{}", side.Document.GetRawText());
    }
}
