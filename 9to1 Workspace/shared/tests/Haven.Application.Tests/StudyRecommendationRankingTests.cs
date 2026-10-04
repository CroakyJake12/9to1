using Haven.Application.Study;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed class StudyRecommendationRankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private const string Principal = "actual-profile-test-principal";

    [Fact]
    public void Three_slots_are_shared_across_all_subjects_and_weak_precedes_overdue()
    {
        var topics = new[] { Topic(1, 101), Topic(2, 102), Topic(3, 103), Topic(4, 101), Topic(5, 102) };
        var evidence = new[]
        {
            Review(1, topics[0], StudyRecordedDifficulty.Green, Now.AddDays(-20), Now.AddDays(-1)),
            Review(2, topics[1], StudyRecordedDifficulty.Red, Now.AddDays(-2)),
            Review(3, topics[2], StudyRecordedDifficulty.Amber, Now.AddDays(-30), Now.AddDays(-2)),
            Review(4, topics[3], StudyRecordedDifficulty.Red, Now.AddDays(-8)),
            Review(5, topics[4], StudyRecordedDifficulty.Green, Now.AddDays(-40))
        };
        var result = StudyRecommendationRanking.Rank(topics, evidence, Principal, Now);
        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { topics[3].Id, topics[1].Id, topics[2].Id }, result.Select(item => item.TopicId));
        Assert.Equal(3, result.Select(item => item.SubjectId).Distinct().Count());
        Assert.All(result.Take(2), item => Assert.Equal(StudyRecommendationBasis.RecordedWeak, item.Basis));
        Assert.Equal(StudyRecommendationBasis.ReviewOverdue, result[2].Basis);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Fewer_recorded_topics_return_fewer_slots_and_unassessed_topics_do_not_get_a_score(int recorded)
    {
        var topics = new[] { Topic(1, 101), Topic(2, 101), Topic(3, 101), Topic(4, 101) };
        // Legacy manual metadata is retained source data, not an attributed personal assessment.
        topics[3] = topics[3] with { StructureJson = "{\"havenStudy\":{\"rag\":\"green\"}}" };
        var evidence = topics.Take(recorded).Select((topic, index) =>
            Review(index + 1, topic, StudyRecordedDifficulty.Amber, Now.AddDays(-10))).ToArray();
        var result = StudyRecommendationRanking.Rank(topics, evidence, Principal, Now);
        Assert.Equal(recorded, result.Count);
        Assert.DoesNotContain(result, item => item.TopicId == topics[3].Id);
        Assert.All(result, item => Assert.Equal(StudyRecordedDifficulty.Amber, item.RecordedDifficulty));
    }

    [Fact]
    public void Topic_ties_are_stable_and_latest_review_replaces_old_difficulty_without_duplicate_slots()
    {
        var first = Topic(1, 101);
        var second = Topic(2, 101);
        var third = Topic(3, 101);
        var evidence = new[]
        {
            Review(1, first, StudyRecordedDifficulty.Red, Now.AddDays(-20)),
            Review(2, first, StudyRecordedDifficulty.Green, Now.AddDays(-1)),
            Review(3, second, StudyRecordedDifficulty.Amber, Now.AddDays(-3)),
            Review(4, third, StudyRecordedDifficulty.Amber, Now.AddDays(-3))
        };
        var result = StudyRecommendationRanking.Rank(new[] { third, first, second }, evidence.Reverse().ToArray(), Principal, Now);
        Assert.Equal(new[] { second.Id, third.Id, first.Id }, result.Select(item => item.TopicId));
        Assert.Equal(StudyRecordedDifficulty.Green, result[^1].RecordedDifficulty);
        Assert.Equal(3, result.Select(item => item.TopicId).Distinct().Count());
        Assert.Equal(result, StudyRecommendationRanking.Rank(new[] { second, third, first }, evidence, Principal, Now));
    }

    [Fact]
    public void Renaming_or_moving_the_same_topic_preserves_review_identity_and_uses_current_authorized_subject()
    {
        var original = Topic(1, 101);
        var moved = original with { SubjectId = Id(102), Name = "Renamed topic" };
        var evidence = Review(1, original, StudyRecordedDifficulty.Red, Now.AddDays(-1));
        var result = Assert.Single(StudyRecommendationRanking.Rank(new[] { moved }, new[] { evidence }, Principal, Now));
        Assert.Equal(original.Id, result.TopicId);
        Assert.Equal(moved.SubjectId, result.SubjectId);
        Assert.Equal(original.SubjectId, evidence.SubjectIdAtReview);
        Assert.Equal(evidence.ReviewRecordId, result.ReviewRecordId);
        Assert.Equal(evidence.SourceRevision, result.SourceRevision);
    }

    [Fact]
    public void Foreign_principals_unlinked_missing_and_future_topics_do_not_create_recommendations()
    {
        var topic = Topic(1, 101);
        var valid = Review(1, topic, StudyRecordedDifficulty.Red, Now.AddDays(-1));
        var evidence = new[]
        {
            valid,
            Review(2, topic, StudyRecordedDifficulty.Red, Now.AddDays(-1)) with { PrincipalId = "other-principal" },
            Review(3, topic, StudyRecordedDifficulty.Red, Now.AddDays(-1)) with { TopicId = null },
            Review(4, Topic(2, 101), StudyRecordedDifficulty.Red, Now.AddDays(-1)),
            Review(5, topic, StudyRecordedDifficulty.Red, Now.AddDays(1)),
            Review(6, topic, StudyRecordedDifficulty.Amber, Now.AddDays(-1)) with { RecordedDifficulty = null }
        };
        var result = Assert.Single(StudyRecommendationRanking.Rank(new[] { topic }, evidence, Principal, Now));
        Assert.Equal(valid.ReviewRecordId, result.ReviewRecordId);
        Assert.Equal(valid.SourceArtifactId, result.SourceArtifactId);
    }

    [Fact]
    public void Repeated_canonical_topic_or_review_identity_is_refused()
    {
        var topic = Topic(1, 101);
        var review = Review(1, topic, StudyRecordedDifficulty.Red, Now.AddDays(-1));
        Assert.Throws<ArgumentException>(() => StudyRecommendationRanking.Rank(new[] { topic, topic }, new[] { review }, Principal, Now));
        Assert.Throws<ArgumentException>(() => StudyRecommendationRanking.Rank(new[] { topic }, new[] { review, review }, Principal, Now));
    }

    [Fact]
    public void Missing_original_review_or_owner_time_is_refused_instead_of_fabricating_age()
    {
        var topic = Topic(1, 101);
        var review = Review(1, topic, StudyRecordedDifficulty.Red, Now.AddDays(-1));
        Assert.Throws<ArgumentException>(() => StudyRecommendationRanking.Rank(new[] { topic },
            new[] { review with { ReviewedAt = default } }, Principal, Now));
        Assert.Throws<ArgumentException>(() => StudyRecommendationRanking.Rank(new[] { topic },
            new[] { review with { DueAt = default(DateTimeOffset) } }, Principal, Now));
        Assert.Throws<ArgumentException>(() => StudyRecommendationRanking.Rank(new[] { topic },
            new[] { review }, Principal, default));
    }

    [Fact]
    public void Original_cancellation_is_observed_without_a_sort_comparer_wrapping_it()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var topic = Topic(1, 101);
        var failure = Assert.ThrowsAny<OperationCanceledException>(() =>
            StudyRecommendationRanking.Rank(new[] { topic }, new[] { Review(1, topic, StudyRecordedDifficulty.Red, Now) },
                Principal, Now, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    private static Lesson Topic(int id, int subject) =>
        new(Id(id), Id(subject), "General", "Topic " + id, "{}", 0, Now.AddDays(-30), Now);
    private static Guid Id(int id) => Guid.Parse("00000000-0000-0000-0000-" + id.ToString("D12"));
    private static StudyAttributedReviewEvidence Review(int id, Lesson topic, StudyRecordedDifficulty difficulty,
        DateTimeOffset reviewedAt, DateTimeOffset? dueAt = null) =>
        new(Id(1000 + id), Principal, topic.SubjectId, topic.Id, StudyReviewEvidenceKind.CardSelfRating,
            difficulty, reviewedAt, dueAt, "cards", Id(2000 + id), 1, Id(3000 + id), 1);
}
