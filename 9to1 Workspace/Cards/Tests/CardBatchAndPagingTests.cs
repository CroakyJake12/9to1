using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardBatchAndPagingTests
{
    private static CardSide Side(string text) => new()
    {
        DocumentFormat = "9to1.shared-productivity/1",
        Document = JsonDocument.Parse("""{"nodes":[{"kind":"text","value":"retain"}]}""").RootElement.Clone(),
        SearchText = text,
    };

    private static CardSet ThreeCardSet()
    {
        CardSet initial = CardSetOperations.Create("personal:owner", "Revision");
        return CardSetOperations.AddCards(initial,
            [(Side("Alpha"), Side("One")), (Side("Beta"), Side("Two")),
             (Side("Gamma"), Side("Three"))],
            initial.Revision, CardInteractionMode.Edit);
    }

    [Fact]
    public void AtomicBulkEditsPreserveUntouchedSidesAndStableIds()
    {
        CardSet original = ThreeCardSet();
        Guid firstId = original.Cards[0].CardId;
        Guid thirdId = original.Cards[2].CardId;

        CardSet updated = CardSetOperations.BulkEditSides(original,
            [new CardSideEdit(firstId, 1, Front: Side("Alpha updated")),
             new CardSideEdit(thirdId, 1, Back: Side("Third updated"))],
            original.Revision, CardInteractionMode.Edit);

        Assert.Equal(original.Revision + 1, updated.Revision);
        Assert.Equal(firstId, updated.Cards[0].CardId);
        Assert.Equal(thirdId, updated.Cards[2].CardId);
        Assert.Equal(2, updated.Cards[0].Revision);
        Assert.Equal(2, updated.Cards[2].Revision);
        Assert.Equal(1, updated.Cards[1].Revision);
        Assert.Equal("Alpha updated", updated.Cards[0].Front.SearchText);
        Assert.Equal("One", updated.Cards[0].Back.SearchText);
        Assert.Equal("Third updated", updated.Cards[2].Back.SearchText);
        Assert.Equal("Alpha", original.Cards[0].Front.SearchText);
        Assert.Equal("Three", original.Cards[2].Back.SearchText);
    }

    [Fact]
    public void FailedLaterEditRejectsWholeBatchWithoutMutatingSource()
    {
        CardSet set = ThreeCardSet();
        Guid firstId = set.Cards[0].CardId;
        Guid secondId = set.Cards[1].CardId;

        CardOperationException error = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.BulkEditSides(set,
                [new CardSideEdit(firstId, 1, Front: Side("Would change")),
                 new CardSideEdit(secondId, 999, Back: Side("Stale"))],
                set.Revision, CardInteractionMode.Edit));

        Assert.Equal(CardFailureCode.RevisionConflict, error.Code);
        Assert.Equal("Alpha", set.Cards[0].Front.SearchText);
        Assert.Equal("Two", set.Cards[1].Back.SearchText);
        Assert.Equal(2, set.Revision);
    }

    [Fact]
    public void BulkEditRejectsDuplicateCardAndEmptyEdit()
    {
        CardSet set = ThreeCardSet();
        Guid firstId = set.Cards[0].CardId;

        CardOperationException duplicate = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.BulkEditSides(set,
                [new CardSideEdit(firstId, 1, Front: Side("First")),
                 new CardSideEdit(firstId, 1, Back: Side("Second"))],
                set.Revision, CardInteractionMode.Edit));
        Assert.Equal(CardFailureCode.DuplicateCard, duplicate.Code);

        CardOperationException empty = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.BulkEditSides(set,
                [new CardSideEdit(firstId, 1)],
                set.Revision, CardInteractionMode.Edit));
        Assert.Equal(CardFailureCode.InvalidContent, empty.Code);
    }

    [Fact]
    public void ViewModeDeniesBatchEditsAndRenameButNotBrowsing()
    {
        CardSet set = ThreeCardSet();
        Guid firstId = set.Cards[0].CardId;
        Assert.Equal(CardFailureCode.ViewIsReadOnly,
            Assert.Throws<CardOperationException>(() =>
                CardSetOperations.BulkEditSides(set,
                    [new CardSideEdit(firstId, 1, Front: Side("Denied"))],
                    set.Revision, CardInteractionMode.View)).Code);
        Assert.Equal(CardFailureCode.ViewIsReadOnly,
            Assert.Throws<CardOperationException>(() =>
                CardSetOperations.RenameSet(set, "Denied",
                    set.Revision, CardInteractionMode.View)).Code);
        Assert.Equal(firstId, CardSetOperations.GetPage(set, 0, 1).Cards.Single().CardId);
    }

    [Fact]
    public void MoveVisibleKeepsTombstoneAtOriginalPhysicalSlot()
    {
        CardSet set = ThreeCardSet();
        Guid firstId = set.Cards[0].CardId;
        Guid deletedId = set.Cards[1].CardId;
        Guid thirdId = set.Cards[2].CardId;
        CardDeletePreview preview = CardSetOperations.PreviewDelete(set, [deletedId]);
        set = CardSetOperations.SoftDelete(set, preview, CardInteractionMode.Edit);

        CardSet moved = CardSetOperations.MoveVisible(set, thirdId, 0,
            set.Revision, CardInteractionMode.Edit);

        Assert.Equal(thirdId, moved.Cards[0].CardId);
        Assert.Equal(deletedId, moved.Cards[1].CardId);
        Assert.True(moved.Cards[1].IsDeleted);
        Assert.Equal(firstId, moved.Cards[2].CardId);
        Assert.Equal(new[] { thirdId, firstId },
            CardSetOperations.GetPage(moved, 0, 10).Cards.Select(card => card.CardId));
        Assert.Equal(set.Revision + 1, moved.Revision);
        CardSet noChange = CardSetOperations.MoveVisible(moved, thirdId, 0,
            moved.Revision, CardInteractionMode.Edit);
        Assert.Same(moved, noChange);
    }

    [Fact]
    public void PagingIsBoundedAndPreservesIdentityAndFilters()
    {
        CardSet set = ThreeCardSet();
        Guid firstId = set.Cards[0].CardId;

        CardPage first = CardSetOperations.GetPage(set, 0, 1);
        Assert.Equal(set.SetId, first.SetId);
        Assert.Equal(set.Revision, first.SetRevision);
        Assert.Single(first.Cards);
        Assert.Equal(firstId, first.Cards[0].CardId);
        Assert.True(first.HasMore);

        CardPage rest = CardSetOperations.GetPage(set, 1, 2);
        Assert.Equal(2, rest.Cards.Count);
        Assert.False(rest.HasMore);
        Assert.Equal(set.Cards[1].CardId, rest.Cards[0].CardId);
        Assert.Empty(CardSetOperations.GetPage(set, 100, 2).Cards);
        Assert.Single(CardSetOperations.GetPage(set, 0, 10, query: "beta").Cards);
        Assert.Single(CardSetOperations.GetPage(set, 0, 10, query: "tHrEe").Cards);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public void InvalidPaginationIsRejected(int offset, int size)
    {
        CardOperationException error = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.GetPage(ThreeCardSet(), offset, size));
        Assert.Equal(CardFailureCode.InvalidPage, error.Code);
    }

    [Fact]
    public void GroupFilteringAndReopenRetainSetAndCardIds()
    {
        CardSet set = ThreeCardSet();
        Guid first = set.Cards[0].CardId;
        set = CardSetOperations.AssignGrouping(set, first, "Biology", "Cells",
            new Dictionary<string, string> { ["paper"] = "Pure" },
            set.Revision, CardInteractionMode.Edit);

        string exported = CardSetOperations.ExportJson(set);
        CardSet restored = CardSetOperations.ImportJson(exported);
        Assert.Equal(first, restored.Cards[0].CardId);
        Assert.Equal(set.SetId, restored.SetId);
        Assert.Equal(set.ArtifactId, restored.ArtifactId);
        Assert.Single(CardSetOperations.GetPage(restored, 0, 3, subjectId: "Biology").Cards);
        Assert.Single(CardSetOperations.GetPage(restored, 0, 3, topicId: "Cells").Cards);
        Assert.Empty(CardSetOperations.GetPage(restored, 0, 3, topicId: "Other").Cards);
    }

    [Fact]
    public void RenameKeepsStableIdentityAndRefusesStaleRevision()
    {
        CardSet set = ThreeCardSet();
        CardSet renamed = CardSetOperations.RenameSet(set, "  Cells  ", set.Revision, CardInteractionMode.Edit);
        Assert.Equal("Cells", renamed.Title);
        Assert.Equal(set.SetId, renamed.SetId);
        Assert.Equal(set.ArtifactId, renamed.ArtifactId);
        Assert.Equal(set.Revision + 1, renamed.Revision);
        Assert.Equal(CardFailureCode.RevisionConflict,
            Assert.Throws<CardOperationException>(() =>
                CardSetOperations.RenameSet(renamed, "Old revision",
                    set.Revision, CardInteractionMode.Edit)).Code);
    }
}
