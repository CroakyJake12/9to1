using System.Text.Json;

namespace HavenOS.Apps.Cards;

/// <summary>
/// Pure revision-aware Cards domain operations. Callers must first obtain Home/Files
/// permission, provide trusted actor/scope, commit with the owning store's CAS,
/// and use the existing Study and shared-document engines. No I/O or grants here.
/// </summary>
public static class CardSetOperations
{
    public static CardSet Create(string ownerScope, string title, Guid? setId = null, Guid? artifactId = null)
    {
        if (string.IsNullOrWhiteSpace(ownerScope) || string.IsNullOrWhiteSpace(title))
            throw Fail(CardFailureCode.InvalidState, "Owner/scope and title are required.");
        if (setId == Guid.Empty || artifactId == Guid.Empty)
            throw Fail(CardFailureCode.InvalidState, "Stable IDs must not be empty.");
        return new CardSet
        {
            SetId = setId ?? Guid.NewGuid(),
            ArtifactId = artifactId ?? Guid.NewGuid(),
            OwnerScope = ownerScope,
            Title = title.Trim(),
        };
    }

    // An atomic bulk operation: invalid inputs produce no changed CardSet.
    // The persistence owner commits the returned revision using compare-and-swap.
    public static CardSet AddCards(CardSet set, IEnumerable<(CardSide Front, CardSide Back)> draftCards,
        long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        ArgumentNullException.ThrowIfNull(draftCards);
        var drafts = draftCards.ToArray();
        if (drafts.Length == 0)
            throw Fail(CardFailureCode.InvalidState, "At least one card is required.");
        foreach (var (front, back) in drafts)
        {
            if (front is null || back is null)
                throw Fail(CardFailureCode.InvalidContent, "Both card sides are required.");
            front.Validate();
            back.Validate();
        }

        var result = set.Cards.ToList();
        foreach (var (front, back) in drafts)
        {
            result.Add(new CardEntry
            {
                CardId = Guid.NewGuid(),
                Front = CloneSide(front),
                Back = CloneSide(back),
            });
        }
        return Next(set, result);
    }

    public static CardSet EditSide(CardSet set, Guid cardId, bool isFront, CardSide replacement,
        long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        ArgumentNullException.ThrowIfNull(replacement);
        replacement.Validate();
        var cards = set.Cards.ToArray();
        int index = ActiveCardIndex(cards, cardId);
        var original = cards[index];
        var content = CloneSide(replacement);
        cards[index] = isFront
            ? original with { Front = content, Revision = checked(original.Revision + 1) }
            : original with { Back = content, Revision = checked(original.Revision + 1) };
        return Next(set, cards);
    }

    /// <summary>
    /// Apply multiple side edits as one atomic set revision. An invalid or stale
    /// card rejects the entire batch; no caller-owned source is modified.
    /// </summary>
    public static CardSet BulkEditSides(CardSet set, IEnumerable<CardSideEdit> edits,
        long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        ArgumentNullException.ThrowIfNull(edits);
        CardSideEdit[] batch = edits.ToArray();
        if (batch.Length == 0)
            throw Fail(CardFailureCode.InvalidContent, "At least one side edit is required.");

        var updated = set.Cards.ToArray();
        var seen = new HashSet<Guid>();
        foreach (CardSideEdit edit in batch)
        {
            if (edit is null || !seen.Add(edit.CardId))
                throw Fail(CardFailureCode.DuplicateCard, "Each CardID may occur only once in an atomic edit.");
            if (edit.Front is null && edit.Back is null)
                throw Fail(CardFailureCode.InvalidContent, "A side edit must replace at least one side.");
            edit.Front?.Validate();
            edit.Back?.Validate();

            int index = ActiveCardIndex(updated, edit.CardId);
            CardEntry original = updated[index];
            if (original.Revision != edit.ExpectedCardRevision)
                throw Fail(CardFailureCode.RevisionConflict,
                    $"Card {edit.CardId:D} is at revision {original.Revision}, not {edit.ExpectedCardRevision}.");

            updated[index] = original with
            {
                Front = edit.Front is null ? original.Front : CloneSide(edit.Front),
                Back = edit.Back is null ? original.Back : CloneSide(edit.Back),
                Revision = checked(original.Revision + 1),
            };
        }

        return Next(set, updated);
    }

    public static CardSet Duplicate(CardSet set, Guid cardId, long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        var cards = set.Cards.ToList();
        int index = ActiveCardIndex(cards, cardId);
        CardEntry original = cards[index];
        cards.Insert(index + 1, original with
        {
            CardId = Guid.NewGuid(),
            Front = CloneSide(original.Front),
            Back = CloneSide(original.Back),
            Revision = 1,
            Extensions = CloneExtensions(original.Extensions),
            CustomGroups = original.CustomGroups is null ? null
                : original.CustomGroups.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        });
        return Next(set, cards);
    }

    public static CardSet AssignGrouping(CardSet set, Guid cardId, string? subjectId, string? topicId,
        IReadOnlyDictionary<string, string>? customGroups, long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        var cards = set.Cards.ToArray();
        int index = ActiveCardIndex(cards, cardId);
        CardEntry original = cards[index];
        cards[index] = original with
        {
            SubjectId = subjectId,
            TopicId = topicId,
            CustomGroups = customGroups?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Revision = checked(original.Revision + 1),
        };
        return Next(set, cards);
    }

    public static CardSet RenameSet(CardSet set, string title,
        long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        if (string.IsNullOrWhiteSpace(title))
            throw Fail(CardFailureCode.InvalidState, "A set title is required.");
        string renamed = title.Trim();
        return string.Equals(set.Title, renamed, StringComparison.Ordinal)
            ? set
            : set with { Title = renamed, Revision = checked(set.Revision + 1) };
    }

    /// <summary>
    /// Reorder by visible position, without moving a hidden tombstone into a
    /// visible viewport or changing its original physical membership slot.
    /// </summary>
    public static CardSet MoveVisible(CardSet set, Guid cardId, int destinationIndex,
        long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        var live = set.Cards.Where(card => !card.IsDeleted).ToList();
        int currentIndex = live.FindIndex(card => card.CardId == cardId);
        if (currentIndex < 0)
        {
            _ = ActiveCardIndex(set.Cards, cardId);
            throw Fail(CardFailureCode.CardNotFound, "CardID is not present in this set.");
        }
        if (destinationIndex < 0 || destinationIndex >= live.Count)
            throw Fail(CardFailureCode.InvalidOrdering, "Visible destination is outside the live card list.");
        if (destinationIndex == currentIndex)
            return set;
        CardEntry moving = live[currentIndex];
        live.RemoveAt(currentIndex);
        live.Insert(destinationIndex, moving);
        int liveIndex = 0;
        CardEntry[] reordered = set.Cards.Select(card =>
            card.IsDeleted ? card : live[liveIndex++]).ToArray();
        return Next(set, reordered);
    }

    // newIndex addresses the entire ordered membership including hidden tombstones.
    public static CardSet Move(CardSet set, Guid cardId, int newIndex, long expectedRevision,
        CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        var cards = set.Cards.ToList();
        int oldIndex = ActiveCardIndex(cards, cardId);
        if (newIndex < 0 || newIndex >= cards.Count)
            throw Fail(CardFailureCode.InvalidOrdering, "The destination index is outside the set.");
        var card = cards[oldIndex];
        cards.RemoveAt(oldIndex);
        cards.Insert(newIndex, card);
        return Next(set, cards);
    }

    public static CardDeletePreview PreviewDelete(CardSet set, IEnumerable<Guid> cardIds)
    {
        Validate(set);
        ArgumentNullException.ThrowIfNull(cardIds);
        Guid[] ids = cardIds.ToArray();
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length)
            throw Fail(CardFailureCode.DuplicateCard, "A nonempty set of distinct CardIDs is required.");
        foreach (Guid id in ids)
            _ = ActiveCardIndex(set.Cards, id);
        return new CardDeletePreview(set.Revision, ids);
    }

    // Callers MUST obtain the inherited destructive-operation approval before committing.
    public static CardSet SoftDelete(CardSet set, CardDeletePreview preview, CardInteractionMode mode)
    {
        ArgumentNullException.ThrowIfNull(preview);
        EnsureWritable(set, preview.ExpectedSetRevision, mode);
        var cards = set.Cards.ToArray();
        if (preview.CardIds.Count == 0 || preview.CardIds.Distinct().Count() != preview.CardIds.Count)
            throw Fail(CardFailureCode.DuplicateCard, "A nonempty set of distinct CardIDs is required.");
        foreach (Guid id in preview.CardIds)
        {
            int index = ActiveCardIndex(cards, id);
            cards[index] = cards[index] with
            {
                IsDeleted = true,
                Revision = checked(cards[index].Revision + 1),
            };
        }
        return Next(set, cards);
    }

    public static CardSet Restore(CardSet set, Guid cardId, long expectedRevision, CardInteractionMode mode)
    {
        EnsureWritable(set, expectedRevision, mode);
        var cards = set.Cards.ToArray();
        int index = Array.FindIndex(cards, card => card.CardId == cardId);
        if (index < 0)
            throw Fail(CardFailureCode.CardNotFound, "CardID is not present in this set.");
        if (!cards[index].IsDeleted)
            throw Fail(CardFailureCode.InvalidState, "Card has not been deleted.");
        cards[index] = cards[index] with
        {
            IsDeleted = false,
            Revision = checked(cards[index].Revision + 1),
        };
        return Next(set, cards);
    }

    public static IReadOnlyList<CardEntry> Search(CardSet set, string searchText)
    {
        Validate(set);
        ArgumentNullException.ThrowIfNull(searchText);
        string query = searchText.Trim();
        return set.Cards.Where(card => !card.IsDeleted
            && (query.Length == 0
                || card.Front.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || card.Back.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>
    /// Return at most 100 visible entries and one has-more flag, suitable for
    /// a virtualised viewport. SearchText is a derived index, not rich content.
    /// </summary>
    public static CardPage GetPage(CardSet set, int offset, int pageSize,
        string? query = null, string? subjectId = null, string? topicId = null)
    {
        Validate(set);
        if (offset < 0 || pageSize < 1 || pageSize > 100)
            throw Fail(CardFailureCode.InvalidPage, "Page offset must be nonnegative and size must be 1–100.");

        string search = query?.Trim() ?? string.Empty;
        IEnumerable<CardEntry> filtered = set.Cards.Where(card => !card.IsDeleted
            && (subjectId is null || string.Equals(card.SubjectId, subjectId, StringComparison.Ordinal))
            && (topicId is null || string.Equals(card.TopicId, topicId, StringComparison.Ordinal))
            && (search.Length == 0
                || card.Front.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase)
                || card.Back.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase)));
        CardEntry[] window = filtered.Skip(offset).Take(pageSize + 1).ToArray();
        bool hasMore = window.Length > pageSize;
        return new CardPage(set.SetId, set.Revision, offset,
            hasMore ? window[..pageSize] : window, hasMore);
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<CardEntry>> Group(
        CardSet set, CardGroupingKind kind, string? customGroupKey = null)
    {
        Validate(set);
        if (kind == CardGroupingKind.Custom && string.IsNullOrWhiteSpace(customGroupKey))
            throw Fail(CardFailureCode.InvalidState, "Custom grouping requires a key.");
        return set.Cards.Where(card => !card.IsDeleted)
            .GroupBy(card => kind switch
            {
                CardGroupingKind.Subject => card.SubjectId ?? "Unassigned",
                CardGroupingKind.Topic => card.TopicId ?? "Unassigned",
                CardGroupingKind.Custom => card.CustomGroups is not null
                    && card.CustomGroups.TryGetValue(customGroupKey!, out string? label)
                    && !string.IsNullOrWhiteSpace(label) ? label : "Unassigned",
                _ => set.SetId.ToString("D"),
            }, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<CardEntry>)group.ToArray(), StringComparer.Ordinal);
    }

    // Rating is allowed in View but never mutates the shared CardSet. The caller
    // must persist this via the user's review store and issue Study evidence when
    // a valid canonical TopicID has been established by Study.
    public static CardReviewRecord PreparePersonalReview(
        CardSet set, Guid cardId, string principalId, CardReviewRating rating, DateTimeOffset reviewedAt)
    {
        Validate(set);
        if (string.IsNullOrWhiteSpace(principalId))
            throw Fail(CardFailureCode.InvalidState, "An authenticated principal is required.");
        if (!Enum.IsDefined(rating))
            throw Fail(CardFailureCode.InvalidState, "Unknown review rating.");
        CardEntry card = set.Cards[ActiveCardIndex(set.Cards, cardId)];
        return new CardReviewRecord(principalId, cardId, set.SetId, card.TopicId, rating,
            "CardsReview", reviewedAt);
    }

    public static string ExportJson(CardSet set)
    {
        Validate(set);
        return JsonSerializer.Serialize(set);
    }

    public static CardSet ImportJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        CardSet? set = JsonSerializer.Deserialize<CardSet>(json);
        if (set is null)
            throw Fail(CardFailureCode.InvalidState, "The Cards payload is empty.");
        Validate(set);
        return set;
    }

    private static CardSet Next(CardSet set, IEnumerable<CardEntry> cards) =>
        set with { Revision = checked(set.Revision + 1), Cards = cards.ToArray() };

    private static void EnsureWritable(CardSet set, long expectedRevision, CardInteractionMode mode)
    {
        Validate(set);
        if (mode != CardInteractionMode.Edit)
            throw Fail(CardFailureCode.ViewIsReadOnly, "Card content cannot be changed from View mode.");
        if (set.Revision != expectedRevision)
            throw Fail(CardFailureCode.RevisionConflict,
                $"Expected set revision {expectedRevision}, but current revision is {set.Revision}.");
    }

    private static int ActiveCardIndex(IReadOnlyList<CardEntry> cards, Guid cardId)
    {
        int index = -1;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].CardId != cardId) continue;
            index = i;
            break;
        }
        if (index < 0)
            throw Fail(CardFailureCode.CardNotFound, "CardID is not present in this set.");
        if (cards[index].IsDeleted)
            throw Fail(CardFailureCode.CardAlreadyDeleted, "This card is deleted.");
        return index;
    }

    private static void Validate(CardSet? set)
    {
        if (set is null || set.SetId == Guid.Empty || set.ArtifactId == Guid.Empty
            || string.IsNullOrWhiteSpace(set.Title) || string.IsNullOrWhiteSpace(set.OwnerScope)
            || set.Cards is null || set.Revision < 1)
            throw Fail(CardFailureCode.InvalidState, "The card set has invalid identity or metadata.");
        if (set.SchemaVersion != 1)
            throw Fail(CardFailureCode.UnsupportedSchema, "This Cards schema requires migration.");
        var unique = new HashSet<Guid>();
        foreach (CardEntry card in set.Cards)
        {
            if (card is null || card.CardId == Guid.Empty || !unique.Add(card.CardId)
                || card.Revision < 1 || card.Front is null || card.Back is null)
                throw Fail(CardFailureCode.InvalidState, "Invalid or duplicated card membership.");
            card.Front.Validate();
            card.Back.Validate();
        }
    }

    private static CardSide CloneSide(CardSide side) => side with
    {
        Document = side.Document.Clone(),
        Extensions = CloneExtensions(side.Extensions),
    };

    private static Dictionary<string, JsonElement>? CloneExtensions(Dictionary<string, JsonElement>? source) =>
        source?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

    private static CardOperationException Fail(CardFailureCode code, string message) => new(code, message);
}
