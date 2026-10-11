namespace HavenOS.Apps.Cards;

/// <summary>
/// The same user-visible truth contract for Cards CUI, automation and review
/// summaries. A confirmed private review is never equated with confirmed Study
/// progress; even a durable queue is not a submitted Study update.
/// </summary>
public static class CardReviewStatusPresentation
{
    public static string Describe(CardReviewRating rating, CardReviewWriteSummary outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!outcome.ReviewSaved)
            return $"Review not saved: {outcome.ReviewCode}";
        string study = outcome.StudyState switch
        {
            CardStudyEvidenceState.StudySubmitted => "Study evidence submitted",
            CardStudyEvidenceState.StudyQueued => "Study evidence queued, progress not confirmed",
            CardStudyEvidenceState.Unlinked => "no linked Study topic",
            _ => "Study progress not confirmed",
        };
        return $"Review saved: {rating}; {study}";
    }
}

public sealed record CardReviewWriteSummary(
    bool ReviewSaved, string ReviewCode, CardStudyEvidenceState StudyState);
