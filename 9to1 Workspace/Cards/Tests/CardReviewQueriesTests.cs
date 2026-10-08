using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardReviewQueriesTests
{
    private static CardSide Side(string label) => new()
    {
        DocumentFormat = "9to1.shared-productivity/1",
        Document = JsonDocument.Parse("""{"nodes":[]}""").RootElement.Clone(),
        SearchText = label,
    };

    private static CardSet ThreeCards()
    {
        CardSet empty = CardSetOperations.Create("shared:classroom", "Maths");
        return CardSetOperations.AddCards(empty,
            [(Side("A"), Side("A1")), (Side("B"), Side("B1")),
             (Side("C"), Side("C1"))],
            empty.Revision, CardInteractionMode.Edit);
    }

    [Fact]
    public void DifficultReviewsUseOnlyTheRequestedPrincipalAndSameSet()
    {
        CardSet set = ThreeCards();
        Guid red = set.Cards[0].CardId;
        Guid amber = set.Cards[1].CardId;
        Guid green = set.Cards[2].CardId;
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-08T12:00:00+00:00");
        CardReviewRecord[] rows =
        [
            new("alice", red, set.SetId, null, CardReviewRating.Red, "CardsReview", now),
            new("alice", amber, set.SetId, null, CardReviewRating.Amber, "CardsReview", now),
            new("alice", green, set.SetId, null, CardReviewRating.Green, "CardsReview", now),
            new("bob", green, set.SetId, null, CardReviewRating.Red, "CardsReview", now.AddHours(1)),
            new("alice", green, Guid.NewGuid(), null, CardReviewRating.Red, "CardsReview", now.AddHours(2)),
        ];

        CardReviewPage page = CardReviewQueries.GetPage(set, rows, "alice",
            CardReviewQueueMode.DifficultOnly, 0, 10);
        Assert.Equal(2, page.Cards.Count);
        Assert.Equal(red, page.Cards[0].CardId);
        Assert.Equal(amber, page.Cards[1].CardId);
        Assert.DoesNotContain(page.Cards, card => card.CardId == green);

        CardReviewPage bob = CardReviewQueries.GetPage(set, rows, "bob",
            CardReviewQueueMode.DifficultOnly, 0, 10);
        Assert.Single(bob.Cards);
        Assert.Equal(green, bob.Cards[0].CardId);
    }

    [Fact]
    public void LatestReviewWinsWithoutMutatingTheSharedDeck()
    {
        CardSet set = ThreeCards();
        Guid cardId = set.Cards[0].CardId;
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-08T13:00:00+00:00");
        CardReviewRecord[] rows =
        [
            new("alice", cardId, set.SetId, null, CardReviewRating.Red, "CardsReview", now),
            new("alice", cardId, set.SetId, null, CardReviewRating.Green, "CardsReview", now.AddMinutes(10)),
        ];

        CardReviewPage reviews = CardReviewQueries.GetPage(set, rows, "alice",
            CardReviewQueueMode.All, 0, 100);
        Assert.Equal(3, reviews.Cards.Count);
        Assert.Equal(CardReviewRating.Green, reviews.Cards[0].Rating);
        Assert.Null(reviews.Cards[1].Rating);
        Assert.Equal(2, set.Revision);
        Assert.DoesNotContain("alice", CardSetOperations.ExportJson(set));
    }

    [Fact]
    public void DifficultFirstRanksRedAmberUnratedThenGreen()
    {
        CardSet set = ThreeCards();
        Guid red = set.Cards[2].CardId;
        Guid green = set.Cards[0].CardId;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CardReviewRecord[] rows =
        [
            new("alice", green, set.SetId, null, CardReviewRating.Green, "CardsReview", now),
            new("alice", red, set.SetId, null, CardReviewRating.Red, "CardsReview", now),
        ];

        CardReviewPage page = CardReviewQueries.GetPage(set, rows, "alice",
            CardReviewQueueMode.DifficultFirst, 0, 2);
        Assert.Equal(red, page.Cards[0].CardId);
        Assert.Equal(set.Cards[1].CardId, page.Cards[1].CardId);
        Assert.True(page.HasMore);
        CardReviewPage tail = CardReviewQueries.GetPage(set, rows, "alice",
            CardReviewQueueMode.DifficultFirst, 2, 2, expectedSetRevision: set.Revision);
        Assert.Single(tail.Cards);
        Assert.Equal(green, tail.Cards[0].CardId);
        Assert.False(tail.HasMore);
    }

    [Fact]
    public void DeletedCardIsNeverReturnedFromPersonalReviewQueue()
    {
        CardSet set = ThreeCards();
        Guid removed = set.Cards[0].CardId;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CardReviewRecord[] rows =
        [
            new("alice", removed, set.SetId, null, CardReviewRating.Red, "CardsReview", now),
        ];
        CardDeletePreview preview = CardSetOperations.PreviewDelete(set, [removed]);
        set = CardSetOperations.SoftDelete(set, preview, CardInteractionMode.Edit);

        Assert.Empty(CardReviewQueries.GetPage(set, rows, "alice",
            CardReviewQueueMode.DifficultOnly, 0, 10).Cards);
        Assert.Equal(2, CardReviewQueries.GetPage(set, rows, "alice",
            CardReviewQueueMode.All, 0, 10).Cards.Count);
    }

    [Fact]
    public void RevisionMismatchAndUntrustedPrincipalSelectionFail()
    {
        CardSet set = ThreeCards();
        Assert.Equal(CardFailureCode.RevisionConflict,
            Assert.Throws<CardOperationException>(() =>
                CardReviewQueries.GetPage(set, Array.Empty<CardReviewRecord>(), "alice",
                    CardReviewQueueMode.All, 0, 10, set.Revision + 1)).Code);
        Assert.Equal(CardFailureCode.InvalidState,
            Assert.Throws<CardOperationException>(() =>
                CardReviewQueries.GetPage(set, Array.Empty<CardReviewRecord>(), "",
                    CardReviewQueueMode.All, 0, 10)).Code);
    }

    [Fact]
    public void CardPagingRefusesMixingTwoDifferentSetRevisions()
    {
        CardSet old = ThreeCards();
        CardSet newer = CardSetOperations.RenameSet(old, "Changed", old.Revision, CardInteractionMode.Edit);
        CardOperationException error = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.GetPage(newer, 0, 10, expectedRevision: old.Revision));
        Assert.Equal(CardFailureCode.RevisionConflict, error.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":\"1\"}")]
    [InlineData("{\"SchemaVersion\":false}")]
    [InlineData("{not-json}")]
    public void ImportsWithoutValidVersionOrJsonAreRejected(string json)
    {
        CardOperationException error = Assert.Throws<CardOperationException>(() =>
            CardSetOperations.ImportJson(json));
        Assert.Equal(CardFailureCode.InvalidContent, error.Code);
    }
}
