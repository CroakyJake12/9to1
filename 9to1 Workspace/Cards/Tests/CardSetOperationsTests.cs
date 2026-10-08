using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardSetOperationsTests
{
    private static CardSide Side(string value, string? search = null) => new()
    {
        DocumentFormat = "9to1.shared-productivity/1",
        Document = JsonDocument.Parse(value).RootElement.Clone(),
        SearchText = search ?? string.Empty,
    };

    private static CardSet NewSet() => CardSetOperations.Create("personal:owner-01", "Biology");

    private static CardSet WithTwoCards()
    {
        CardSet empty = NewSet();
        return CardSetOperations.AddCards(empty,
            [(Side("""{"blocks":[{"type":"text","value":"Cell"}]}""", "Cell"),
              Side("""{"blocks":[{"type":"text","value":"Basic unit"}]}""", "Basic unit")),
             (Side("""{"blocks":[{"type":"text","value":"Mitosis"}]}""", "Mitosis"),
              Side("""{"blocks":[{"type":"text","value":"Division"}]}""", "Division"))],
            empty.Revision, CardInteractionMode.Edit);
    }

    [Fact]
    public void BulkCreateKeepsStableDistinctIdsAndOrderedMembership()
    {
        CardSet set = WithTwoCards();
        Assert.Equal(2, set.Cards.Count);
        Assert.All(set.Cards, card => Assert.NotEqual(Guid.Empty, card.CardId));
        Assert.NotEqual(set.Cards[0].CardId, set.Cards[1].CardId);
        Assert.Equal(2, set.Revision);
        Assert.Equal("Cell", set.Cards[0].Front.SearchText);
    }

    [Fact]
    public void RichStructuredGraphAndInkSurviveExportImport()
    {
        CardSet start = NewSet();
        CardSide front = Side("""
            {"nodes":[{"kind":"text","runs":[{"bold":true,"value":"Cell"}]},
                      {"kind":"graph","axes":["x","y"],"points":[[1,2],[3,4]]},
                      {"kind":"ink","strokes":[{"points":[[0,1],[2,3]],"colour":"#ABCDEF"}]}],
             "unrecognisedEngineBlock":{"retain":true}}
            """, "Cell");
        CardSide back = Side("""{"nodes":[{"kind":"image","assetId":"asset-01"}]}""", "Illustration");
        CardSet set = CardSetOperations.AddCards(start, [(front, back)], start.Revision, CardInteractionMode.Edit);

        string json = CardSetOperations.ExportJson(set);
        CardSet reopened = CardSetOperations.ImportJson(json);
        Assert.Equal(set.SetId, reopened.SetId);
        Assert.Equal(set.ArtifactId, reopened.ArtifactId);
        Assert.Equal(set.Cards[0].CardId, reopened.Cards[0].CardId);
        Assert.Equal(front.Document.GetRawText(), reopened.Cards[0].Front.Document.GetRawText());
        Assert.Equal("asset-01", reopened.Cards[0].Back.Document.GetProperty("nodes")[0].GetProperty("assetId").GetString());
    }

    [Fact]
    public void ViewModeCannotMutateButCanPreparePersonalReview()
    {
        CardSet set = WithTwoCards();
        Guid id = set.Cards[0].CardId;
        CardOperationException failure = Assert.Throws<CardOperationException>(
            () => CardSetOperations.EditSide(set, id, true, Side("""{"blocks":[]}"""),
                set.Revision, CardInteractionMode.View));
        Assert.Equal(CardFailureCode.ViewIsReadOnly, failure.Code);
        CardReviewRecord learnerA = CardSetOperations.PreparePersonalReview(
            set, id, "student-A", CardReviewRating.Red, DateTimeOffset.Parse("2026-10-08T12:00:00+00:00"));
        CardReviewRecord learnerB = CardSetOperations.PreparePersonalReview(
            set, id, "student-B", CardReviewRating.Green, DateTimeOffset.Parse("2026-10-08T12:01:00+00:00"));
        Assert.Equal(set.Cards[0].CardId, learnerA.CardId);
        Assert.NotEqual(learnerA.PrincipalId, learnerB.PrincipalId);
        Assert.NotEqual(learnerA.Rating, learnerB.Rating);
        Assert.DoesNotContain("student-A", CardSetOperations.ExportJson(set));
    }

    [Fact]
    public void RevisionConflictsRefuseStaleUpdatesWithoutChangingInput()
    {
        CardSet set = WithTwoCards();
        Guid id = set.Cards[0].CardId;
        CardOperationException failure = Assert.Throws<CardOperationException>(
            () => CardSetOperations.EditSide(set, id, true, Side("""{"blocks":[]}"""),
                set.Revision - 1, CardInteractionMode.Edit));
        Assert.Equal(CardFailureCode.RevisionConflict, failure.Code);
        Assert.Equal(2, set.Revision);
        Assert.Equal("Cell", set.Cards[0].Front.SearchText);
    }

    [Fact]
    public void DeletionPreviewRequiresCurrentRevisionAndSupportsRestore()
    {
        CardSet set = WithTwoCards();
        Guid id = set.Cards[0].CardId;
        CardDeletePreview preview = CardSetOperations.PreviewDelete(set, [id]);
        Assert.Equal(1, preview.Count);
        CardSet updated = CardSetOperations.SoftDelete(set, preview, CardInteractionMode.Edit);
        Assert.Single(CardSetOperations.Search(updated, ""));
        Assert.Equal(id, updated.Cards[0].CardId);
        Assert.Throws<CardOperationException>(() =>
            CardSetOperations.SoftDelete(updated, preview, CardInteractionMode.Edit));
        CardSet restored = CardSetOperations.Restore(updated, id, updated.Revision, CardInteractionMode.Edit);
        Assert.Equal(2, CardSetOperations.Search(restored, "").Count);
        Assert.Equal(id, restored.Cards[0].CardId);
    }

    [Fact]
    public void GroupingNeverDuplicatesCardsOrChangesIdentity()
    {
        CardSet set = WithTwoCards();
        Guid id = set.Cards[0].CardId;
        set = CardSetOperations.AssignGrouping(set, id, "Science", "Cells",
            new Dictionary<string, string> { ["exam"] = "Paper 1" }, set.Revision, CardInteractionMode.Edit);
        var topic = CardSetOperations.Group(set, CardGroupingKind.Topic);
        var custom = CardSetOperations.Group(set, CardGroupingKind.Custom, "exam");
        Assert.Equal(2, topic.Sum(group => group.Value.Count));
        Assert.Equal(2, custom.Sum(group => group.Value.Count));
        Assert.Equal(id, topic["Cells"].Single().CardId);
        Assert.Equal(id, custom["Paper 1"].Single().CardId);
    }

    [Fact]
    public void BulkValidationIsAllOrNothingAndDuplicateCreatesNewIdentity()
    {
        CardSet set = WithTwoCards();
        Guid original = set.Cards[0].CardId;
        CardSide badSide = Side("""{"nodes":[]}""") with { DocumentFormat = "" };
        Assert.Throws<CardOperationException>(() =>
            CardSetOperations.AddCards(set, [(Side("""{"nodes":[]}"""), Side("""{"nodes":[]}""")),
                (badSide, Side("""{"nodes":[]}"""))], set.Revision, CardInteractionMode.Edit));
        Assert.Equal(2, set.Cards.Count);
        CardSet duplicate = CardSetOperations.Duplicate(set, original, set.Revision, CardInteractionMode.Edit);
        Assert.Equal(3, duplicate.Cards.Count);
        Assert.NotEqual(original, duplicate.Cards[1].CardId);
        Assert.Equal(set.Cards[0].Front.Document.GetRawText(),
            duplicate.Cards[1].Front.Document.GetRawText());
    }

    [Fact]
    public void FutureSchemaIsRejectedAndUnknownFieldsRoundTrip()
    {
        CardSet set = WithTwoCards();
        string json = CardSetOperations.ExportJson(set);
        using JsonDocument future = JsonDocument.Parse("""{"futureValue":{"keep":"yes"}}""");
        CardSet augmented = set with
        {
            Extensions = new Dictionary<string, JsonElement>
            {
                ["FutureData"] = future.RootElement.GetProperty("futureValue").Clone(),
            },
        };
        string replay = CardSetOperations.ExportJson(CardSetOperations.ImportJson(
            CardSetOperations.ExportJson(augmented)));
        Assert.Contains("\"FutureData\"", replay);
        Assert.Equal("yes", CardSetOperations.ImportJson(replay).Extensions!["FutureData"].GetProperty("keep").GetString());
        CardOperationException failure = Assert.Throws<CardOperationException>(
            () => CardSetOperations.ExportJson(set with { SchemaVersion = 12 }));
        Assert.Equal(CardFailureCode.UnsupportedSchema, failure.Code);
    }
}
