namespace HavenOS.Apps.Cards;

/// <summary>Read-only metadata for a recoverable card tombstone. Rich contents and
/// personal reviews are not copied into another store or public trash index.</summary>
public sealed record CardRecoveryItem(
    Guid CardId, int CardRevision, int OriginalMembershipPosition,
    string FrontPreview, string BackPreview);

public sealed record CardRecoveryPage(Guid SetId, long SetRevision, int Offset,
    IReadOnlyList<CardRecoveryItem> Items, bool HasMore);

/// <summary>
/// Bounded, revision-stable read projection of the owning CardSet's recoverable
/// tombstones. The caller must load the source through authorised Files reads.
/// </summary>
public static class CardRecoveryQueries
{
    public static CardRecoveryPage GetPage(CardSet set, int offset, int limit,
        long? expectedSetRevision = null)
    {
        CardSetOperations.Validate(set);
        if (offset < 0 || limit is < 1 or > 100)
            throw new CardOperationException(CardFailureCode.InvalidPage,
                "A recovery page requires a nonnegative offset and 1–100 entries.");
        if (expectedSetRevision is not null && expectedSetRevision.Value != set.Revision)
            throw new CardOperationException(CardFailureCode.RevisionConflict,
                "Recovery membership changed. Refresh against the latest saved set.");

        // Only examine the requested range and a single look-ahead entry.
        var window = new List<CardRecoveryItem>(limit + 1);
        int deletedSeen = 0;
        for (int i = 0; i < set.Cards.Count && window.Count <= limit; i++)
        {
            CardEntry card = set.Cards[i];
            if (!card.IsDeleted) continue;
            if (deletedSeen++ < offset) continue;
            window.Add(new CardRecoveryItem(card.CardId, card.Revision, i,
                card.Front.SearchText, card.Back.SearchText));
        }
        bool hasMore = window.Count > limit;
        if (hasMore) window.RemoveAt(limit);
        return new CardRecoveryPage(set.SetId, set.Revision, offset, window.ToArray(), hasMore);
    }
}
