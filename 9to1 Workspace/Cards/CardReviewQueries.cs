namespace HavenOS.Apps.Cards;

public enum CardReviewQueueMode
{
    All,
    DifficultFirst,
    DifficultOnly,
}

public sealed record CardReviewQueueItem(
    Guid CardId,
    CardReviewRating? Rating,
    DateTimeOffset? ReviewedAt);

public sealed record CardReviewPage(
    Guid SetId,
    long SetRevision,
    int Offset,
    IReadOnlyList<CardReviewQueueItem> Cards,
    bool HasMore);

/// <summary>
/// Read-only review projections from the principal's private review records.
/// The caller must obtain the principal ID from the established authenticated
/// identity, query the authorised private review owner, and never trust an
/// unverified principal ID supplied by a page, tool or other user.
/// </summary>
public static class CardReviewQueries
{
    public static CardReviewPage GetPage(CardSet set,
        IEnumerable<CardReviewRecord> authorisedReviews,
        string principalId, CardReviewQueueMode mode,
        int offset, int pageSize, long? expectedSetRevision = null)
    {
        CardSetOperations.Validate(set);
        ArgumentNullException.ThrowIfNull(authorisedReviews);
        if (string.IsNullOrWhiteSpace(principalId))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "The review owner must be an authenticated principal.");
        if (!Enum.IsDefined(mode))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Unknown review queue mode.");
        if (offset < 0 || pageSize is < 1 or > 100)
            throw new CardOperationException(CardFailureCode.InvalidPage,
                "Page offset must be nonnegative and size must be 1–100.");
        if (expectedSetRevision is not null && set.Revision != expectedSetRevision.Value)
            throw new CardOperationException(CardFailureCode.RevisionConflict,
                "The requested review page belongs to a different set revision.");

        var liveIds = set.Cards.Where(card => !card.IsDeleted)
            .Select(card => card.CardId).ToHashSet();
        // Defence in depth: even if an owner returned extraneous rows, data
        // from another principal or set cannot influence this projection.
        var latest = authorisedReviews
            .Where(review => review is not null
                && string.Equals(review.PrincipalId, principalId, StringComparison.Ordinal)
                && review.SetId == set.SetId && liveIds.Contains(review.CardId))
            .GroupBy(review => review.CardId)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(review => review.ReviewedAt).First());

        IEnumerable<CardReviewQueueItem> queue = set.Cards
            .Where(card => !card.IsDeleted)
            .Select(card =>
            {
                if (!latest.TryGetValue(card.CardId, out CardReviewRecord? record))
                    return new CardReviewQueueItem(card.CardId, null, null);
                return new CardReviewQueueItem(card.CardId, record.Rating, record.ReviewedAt);
            });

        if (mode == CardReviewQueueMode.DifficultOnly)
            queue = queue.Where(card => card.Rating is CardReviewRating.Red or CardReviewRating.Amber);
        if (mode == CardReviewQueueMode.DifficultFirst)
            queue = queue.OrderBy(card => card.Rating switch
            {
                CardReviewRating.Red => 0,
                CardReviewRating.Amber => 1,
                null => 2,
                CardReviewRating.Green => 3,
                _ => 4,
            });

        CardReviewQueueItem[] window = queue.Skip(offset).Take(pageSize + 1).ToArray();
        bool hasMore = window.Length > pageSize;
        return new CardReviewPage(set.SetId, set.Revision, offset,
            hasMore ? window[..pageSize] : window, hasMore);
    }
}
