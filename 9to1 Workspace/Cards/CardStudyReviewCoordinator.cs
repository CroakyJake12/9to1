using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Cards;

/// <summary>
/// A review write uses the same authenticated actor as the canonical Cards read.
/// The host creates the operation ID; the user cannot choose another principal.
/// </summary>
public sealed record CardReviewRequest(
    string OperationId, Guid SetId, Guid CardId, CardReviewRating Rating);

public enum CardPrivateReviewStatus
{
    Committed,
    AlreadyCommitted,
    Denied,
    RevisionConflict,
    Unavailable,
}

public sealed record CardPrivateReviewReceipt(
    CardPrivateReviewStatus Status,
    string PrincipalId,
    Guid SetId,
    Guid CardId,
    string OperationId,
    string RequestFingerprint,
    DateTimeOffset? ReviewedAtUtc,
    string Code,
    CardReviewRecord? SavedReview = null);

/// <summary>
/// The *existing* private review owner is responsible for persisted, replay-safe
/// principal-specific data and for checking current Files/Home permission and
/// card membership when committing. Cards never stores this in the shared deck.
/// </summary>
public interface ICardPrivateReviewOwner
{
    Task<CardPrivateReviewReceipt> RecordAsync(
        CardTrustedCaller caller, CardReviewRequest request, string fingerprint,
        CardReviewRecord proposedReview, long observedSetRevision,
        CancellationToken cancellationToken);
}

public enum CardStudyLinkStatus
{
    Unlinked,
    Valid,
    NotAuthorised,
    Unavailable,
}

public sealed record CardStudyLinkDecision(CardStudyLinkStatus Status,
    string? CanonicalTopicId, string Code);

/// <summary>
/// The existing Study owner verifies TopicID against the authenticated learner
/// and current scope. A text-only TopicID on a card is not proof of a Study link.
/// </summary>
public interface ICardStudyTopicAuthority
{
    Task<CardStudyLinkDecision> CheckLinkAsync(
        CardTrustedCaller caller, CardReviewRecord review,
        CancellationToken cancellationToken);
}

public enum CardStudyDeliveryStatus
{
    Submitted,
    QueuedDurably,
    Rejected,
    Unavailable,
}

public sealed record CardStudyDeliveryReceipt(
    CardStudyDeliveryStatus Status, string OperationId, string Code);

/// <summary>
/// Study owns the attributed evidence and durable delivery outbox. Returning
/// QueuedDurably requires observed persistence; dispatch alone is not a queue.
/// </summary>
public interface ICardStudyEvidenceOwner
{
    Task<CardStudyDeliveryReceipt> DeliverAsync(
        CardTrustedCaller caller, CardReviewRecord savedReview,
        CardPrivateReviewReceipt savedReceipt, string canonicalTopicId,
        CancellationToken cancellationToken);
}

public enum CardStudyEvidenceState
{
    ReviewNotSaved,
    Unlinked,
    StudySubmitted,
    StudyQueued,
    StudyNotConfirmed,
}

public sealed record CardReviewOutcome(
    bool ReviewSaved,
    bool Replayed,
    string ReviewCode,
    string? StudyCode,
    CardStudyEvidenceState StudyState,
    string OperationId, Guid SetId, Guid CardId,
    DateTimeOffset? ReviewedAtUtc);

/// <summary>
/// Real orchestration over distinct canonical owners: Files/permission-backed
/// deck read, private review storage, Study topic admission, Study evidence.
/// No in-memory production repository, guessed topic, or UI-only completion.
/// </summary>
public sealed class CardStudyReviewCoordinator(
    ICardTrustedCallerSource callers,
    ICardCanonicalMutationStore decks,
    ICardPrivateReviewOwner privateReviews,
    ICardStudyTopicAuthority topics,
    ICardStudyEvidenceOwner study,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<CardReviewOutcome> RateAsync(
        CardReviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationId) || request.OperationId.Length > 128
            || request.SetId == Guid.Empty || request.CardId == Guid.Empty
            || !Enum.IsDefined(request.Rating))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Review requires bounded operation ID, actual SetID/CardID, and known rating.");

        CardTrustedCaller caller = await callers.ResolveAsync(cancellationToken)
            ?? throw new CardOperationException(CardFailureCode.InvalidState,
                "A live authenticated reviewer is required.");
        if (string.IsNullOrWhiteSpace(caller.PrincipalId)
            || string.IsNullOrWhiteSpace(caller.SessionEvidenceId))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "The authenticated host returned incomplete reviewer identity.");

        CardSet snapshot = await decks.ReadForPrincipalAsync(caller, request.SetId,
                cancellationToken)
            ?? throw new CardOperationException(CardFailureCode.CardNotFound,
                "The authorised review set is unavailable.");
        CardSetOperations.Validate(snapshot);
        if (snapshot.SetId != request.SetId)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "The owning Files service returned a different set.");

        CardReviewRecord prepared = CardSetOperations.PreparePersonalReview(
            snapshot, request.CardId, caller.PrincipalId,
            request.Rating, _clock.GetUtcNow());
        // Bind the request fingerprint to exactly these data, including the
        // original set revision. The persisted private review owner MUST reject
        // reusing an operation ID with any different fingerprint.
        string fingerprint = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                Caller = caller.PrincipalId,
                request.OperationId,
                request.SetId,
                request.CardId,
                request.Rating,
            })));

        CardPrivateReviewReceipt receipt = await privateReviews.RecordAsync(
            caller, request, fingerprint, prepared, snapshot.Revision,
            cancellationToken);
        if (receipt.PrincipalId != caller.PrincipalId || receipt.SetId != request.SetId
            || receipt.CardId != request.CardId || receipt.OperationId != request.OperationId
            || !StringComparer.Ordinal.Equals(receipt.RequestFingerprint, fingerprint))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Private review owner returned a mismatched or unverified receipt.");
        bool saved = receipt.Status is CardPrivateReviewStatus.Committed or
            CardPrivateReviewStatus.AlreadyCommitted;
        if (!saved)
            return new(false, false, receipt.Code, null,
                CardStudyEvidenceState.ReviewNotSaved,
                request.OperationId, request.SetId, request.CardId, null);

        // On replay the card's live topic and revision may have changed.
        // Use ONLY the persisted owner's exact committed review (including
        // TopicID/evidence time), never the current snapshot as a substitute.
        CardReviewRecord committed = receipt.SavedReview
            ?? throw new CardOperationException(CardFailureCode.InvalidState,
                "The committed private review is unavailable from its owning store.");
        if (receipt.ReviewedAtUtc is null
            || receipt.ReviewedAtUtc != committed.ReviewedAt
            || committed.PrincipalId != caller.PrincipalId
            || committed.SetId != request.SetId
            || committed.CardId != request.CardId
            || committed.Rating != request.Rating
            || !StringComparer.Ordinal.Equals(committed.EvidenceType, "CardsReview"))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "The committed review's identity, rating or timestamp does not match its receipt.");
        try
        {
            CardStudyLinkDecision link = await topics.CheckLinkAsync(
                caller, committed, cancellationToken);
            if (link.Status == CardStudyLinkStatus.Unlinked)
                return Outcome(CardStudyEvidenceState.Unlinked, link.Code);
            if (link.Status != CardStudyLinkStatus.Valid
                || string.IsNullOrWhiteSpace(link.CanonicalTopicId)
                || !StringComparer.Ordinal.Equals(link.CanonicalTopicId, committed.TopicId))
                return Outcome(CardStudyEvidenceState.StudyNotConfirmed, link.Code);

            CardStudyDeliveryReceipt delivery = await study.DeliverAsync(
                caller, committed, receipt, link.CanonicalTopicId,
                cancellationToken);
            if (!StringComparer.Ordinal.Equals(delivery.OperationId, request.OperationId))
                throw new CardOperationException(CardFailureCode.InvalidState,
                    "Study returned evidence for a different review operation.");

            return Outcome(delivery.Status switch
            {
                CardStudyDeliveryStatus.Submitted => CardStudyEvidenceState.StudySubmitted,
                CardStudyDeliveryStatus.QueuedDurably => CardStudyEvidenceState.StudyQueued,
                _ => CardStudyEvidenceState.StudyNotConfirmed,
            }, delivery.Code);
        }
        catch (IOException)
        {
            // Only Study transmission/verification failed. Never erase or
            // misreport the actually acknowledged private review.
            return Outcome(CardStudyEvidenceState.StudyNotConfirmed, "StudyTransportUnavailable");
        }
        catch (TimeoutException)
        {
            return Outcome(CardStudyEvidenceState.StudyNotConfirmed, "StudyTransportTimeout");
        }
        catch (HttpRequestException)
        {
            return Outcome(CardStudyEvidenceState.StudyNotConfirmed, "StudyNetworkUnavailable");
        }

        CardReviewOutcome Outcome(CardStudyEvidenceState state, string code) =>
            new(true, receipt.Status == CardPrivateReviewStatus.AlreadyCommitted,
                receipt.Code, code, state, request.OperationId, request.SetId,
                request.CardId, receipt.ReviewedAtUtc);
    }
}
