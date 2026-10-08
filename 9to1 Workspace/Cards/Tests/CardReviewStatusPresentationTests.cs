using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardReviewStatusPresentationTests
{
    [Theory]
    [InlineData(CardStudyEvidenceState.Unlinked, "no linked Study topic")]
    [InlineData(CardStudyEvidenceState.StudySubmitted, "Study evidence submitted")]
    [InlineData(CardStudyEvidenceState.StudyQueued, "queued, progress not confirmed")]
    [InlineData(CardStudyEvidenceState.StudyNotConfirmed, "Study progress not confirmed")]
    public void SavedReviewStatusKeepsStudyDeliveryTruthSeparate(
        CardStudyEvidenceState state, string expectedText)
    {
        string status = CardReviewStatusPresentation.Describe(CardReviewRating.Amber,
            new CardReviewWriteSummary(true, "Committed", state));

        Assert.Contains("Review saved: Amber", status);
        Assert.Contains(expectedText, status);
    }

    [Fact]
    public void DeniedReviewNeverReportsEitherPersonalSaveOrStudyProgress()
    {
        string status = CardReviewStatusPresentation.Describe(CardReviewRating.Red,
            new CardReviewWriteSummary(false, "HomePermissionDenied",
                CardStudyEvidenceState.StudySubmitted));
        Assert.Equal("Review not saved: HomePermissionDenied", status);
    }
}
