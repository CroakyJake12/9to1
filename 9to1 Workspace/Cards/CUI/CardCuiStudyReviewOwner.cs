namespace HavenOS.Apps.Cards.CUI;

/// <summary>
/// Connects the real Cards scene to the canonical private review / Study
/// orchestration. Home must supply a coordinator constructed from the SAME
/// current actor, Files, private review and Study services as other surfaces.
/// This adapter grants no authority and owns no persistent review database.
/// </summary>
public sealed class CardCuiStudyReviewOwner(CardStudyReviewCoordinator coordinator)
    : ICardCuiReviewOwner
{
    public async Task<CardReviewWriteReceipt> RateCurrentAsync(
        Guid setId, Guid cardId, CardReviewRating rating,
        CancellationToken cancellationToken)
    {
        if (setId == Guid.Empty || cardId == Guid.Empty)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Choose an existing set and card to review.");

        CardReviewOutcome outcome = await coordinator.RateAsync(
            new CardReviewRequest(Guid.NewGuid().ToString("D"), setId, cardId, rating),
            cancellationToken).ConfigureAwait(false);

        // Only the private review owner can assert that personal review
        // evidence was committed. Study progression is a distinct receipt.
        return new(outcome.ReviewSaved, outcome.ReviewCode, outcome.StudyState);
    }
}
