using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardRecoveryQueriesTests
{
    private static CardSide Side(string name) => new()
    {
        DocumentFormat = "9to1.shared-productivity/1",
        Document = JsonDocument.Parse("""{"nodes":[{"kind":"text","weight":"bold"}]}""").RootElement.Clone(),
        SearchText = name,
    };

    private static CardSet WithDeletedCards(int count)
    {
        CardSet empty = CardSetOperations.Create("personal:owner", "Recovery");
        CardSet set = CardSetOperations.AddCards(empty,
            Enumerable.Range(0, count).Select(i => (Side($"Question {i}"), Side($"Answer {i}"))),
            empty.Revision, CardInteractionMode.Edit);
        CardDeletePreview preview = CardSetOperations.PreviewDelete(set,
            set.Cards.Where((_, i) => i % 2 == 0).Select(card => card.CardId));
        return CardSetOperations.SoftDelete(set, preview, CardInteractionMode.Edit);
    }

    [Fact]
    public void RecoveryWindowListsOnlyDeletedCardsWithOriginalMembershipPositions()
    {
        CardSet set = WithDeletedCards(7);
        CardRecoveryPage page = CardRecoveryQueries.GetPage(set, 0, 2);
        Assert.Equal(set.SetId, page.SetId);
        Assert.Equal(set.Revision, page.SetRevision);
        Assert.Equal(new[] { 0, 2 }, page.Items.Select(item => item.OriginalMembershipPosition));
        Assert.Equal(set.Cards[0].CardId, page.Items[0].CardId);
        Assert.Equal(set.Cards[2].CardId, page.Items[1].CardId);
        Assert.Equal("Question 0", page.Items[0].FrontPreview);
        Assert.True(page.HasMore);
        Assert.All(page.Items, item => Assert.True(set.Cards.Single(c => c.CardId == item.CardId).IsDeleted));
    }

    [Fact]
    public void SuccessiveBoundedPagesDoNotDuplicateOrSkipTombstones()
    {
        CardSet set = WithDeletedCards(12);
        var found = new List<Guid>();
        int offset = 0;
        while (true)
        {
            CardRecoveryPage page = CardRecoveryQueries.GetPage(set, offset, 2, set.Revision);
            found.AddRange(page.Items.Select(item => item.CardId));
            if (!page.HasMore) break;
            offset += page.Items.Count;
        }
        Assert.Equal(6, found.Count);
        Assert.Equal(found.Count, found.Distinct().Count());
        Assert.Equal(set.Cards.Where(card => card.IsDeleted).Select(card => card.CardId), found);
    }

    [Fact]
    public void RestorationPreservesSameCardIdentityAndRichContent()
    {
        CardSet set = WithDeletedCards(3);
        CardRecoveryItem item = CardRecoveryQueries.GetPage(set, 0, 10).Items[0];
        CardEntry original = set.Cards.Single(card => card.CardId == item.CardId);

        CardSet restored = CardSetOperations.Restore(set, item.CardId,
            set.Revision, CardInteractionMode.Edit);

        CardEntry result = restored.Cards.Single(card => card.CardId == item.CardId);
        Assert.False(result.IsDeleted);
        Assert.Equal(original.CardId, result.CardId);
        Assert.True(JsonElement.DeepEquals(original.Front.Document, result.Front.Document));
        Assert.True(JsonElement.DeepEquals(original.Back.Document, result.Back.Document));
        Assert.DoesNotContain(CardRecoveryQueries.GetPage(restored, 0, 10).Items,
            entry => entry.CardId == item.CardId);
        Assert.Equal(set.Revision + 1, restored.Revision);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public void InvalidRecoveryPagesAreRejected(int offset, int size)
    {
        CardSet set = WithDeletedCards(3);
        CardOperationException error = Assert.Throws<CardOperationException>(() =>
            CardRecoveryQueries.GetPage(set, offset, size));
        Assert.Equal(CardFailureCode.InvalidPage, error.Code);
    }

    [Fact]
    public void OldRecoveryPageRevisionCannotBeUsedAgainstNewDeck()
    {
        CardSet set = WithDeletedCards(3);
        CardRecoveryPage page = CardRecoveryQueries.GetPage(set, 0, 1);
        CardSet newer = CardSetOperations.RenameSet(set, "Changed",
            set.Revision, CardInteractionMode.Edit);
        CardOperationException error = Assert.Throws<CardOperationException>(() =>
            CardRecoveryQueries.GetPage(newer, 0, 1, page.SetRevision));
        Assert.Equal(CardFailureCode.RevisionConflict, error.Code);
    }
}
