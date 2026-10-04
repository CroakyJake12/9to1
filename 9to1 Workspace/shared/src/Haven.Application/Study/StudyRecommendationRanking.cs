using Haven.Core;

namespace Haven.Application.Study;

/// <summary>Recorded difficulty, without a numeric mastery score or ordinal scheduler conversion.</summary>
public enum StudyRecordedDifficulty { Red = 1, Amber = 2, Green = 3 }
public enum StudyReviewEvidenceKind { CardSelfRating = 1, QuizAnswer = 2 }

/// <summary>Proposed canonical review projection. The owning private profile store must supply
/// current, attributed, permission-checked records; constructing this value grants no access.
/// SubjectIdAtReview is provenance; current placement comes from the same canonical Lesson.Id.</summary>
public sealed record StudyAttributedReviewEvidence(
    Guid ReviewRecordId,
    string PrincipalId,
    Guid SubjectIdAtReview,
    Guid? TopicId,
    StudyReviewEvidenceKind EvidenceKind,
    StudyRecordedDifficulty? RecordedDifficulty,
    DateTimeOffset ReviewedAt,
    DateTimeOffset? DueAt,
    string SourceOwnerAppKey,
    Guid SourceArtifactId,
    long SourceRevision,
    Guid? CardId,
    long ReviewRevision);

public enum StudyRecommendationBasis { RecordedWeak = 1, ReviewOverdue = 2, OldestReviewed = 3 }

public sealed record StudyTopicRecommendation(
    Guid SubjectId,
    Guid TopicId,
    Guid ReviewRecordId,
    long ReviewRevision,
    StudyRecommendationBasis Basis,
    StudyReviewEvidenceKind EvidenceKind,
    StudyRecordedDifficulty RecordedDifficulty,
    DateTimeOffset ReviewedAt,
    DateTimeOffset? DueAt,
    string SourceOwnerAppKey,
    Guid SourceArtifactId,
    long SourceRevision);

/// <summary>Pure ranking over the same canonical Lessons and attributed review projections.
/// It owns no registry, persistence, permission grant or alternate proficiency score.</summary>
public static class StudyRecommendationRanking
{
    public const int Version = 1;
    public const int MaximumRecommendations = 3;
    public const int MaximumTopics = 100_000;
    public const int MaximumEvidenceRecords = 1_000_000;

    public static IReadOnlyList<StudyTopicRecommendation> Rank(
        IReadOnlyList<Lesson> currentAuthorizedTopics,
        IReadOnlyList<StudyAttributedReviewEvidence> currentAuthorizedEvidence,
        string currentPrincipalId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentAuthorizedTopics);
        ArgumentNullException.ThrowIfNull(currentAuthorizedEvidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPrincipalId);
        if (currentPrincipalId.Length > 256)
            throw new ArgumentException("Principal identity exceeds the owner contract bound.", nameof(currentPrincipalId));
        if (now == default) throw new ArgumentException("The owning current time is unavailable.", nameof(now));
        if (currentAuthorizedTopics.Count > MaximumTopics || currentAuthorizedEvidence.Count > MaximumEvidenceRecords)
            throw new ArgumentException("Study ranking input exceeds the bounded owner snapshot policy.");
        cancellationToken.ThrowIfCancellationRequested();

        var topics = new Dictionary<Guid, Lesson>();
        var observedTopics = 0;
        foreach (var topic in currentAuthorizedTopics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++observedTopics > MaximumTopics)
                throw new ArgumentException("Study topic enumeration exceeds the bounded snapshot policy.", nameof(currentAuthorizedTopics));
            ArgumentNullException.ThrowIfNull(topic);
            if (topic.Id == Guid.Empty || topic.SubjectId == Guid.Empty || !topics.TryAdd(topic.Id, topic))
                throw new ArgumentException("The canonical topic snapshot has an empty or repeated Lesson identity.", nameof(currentAuthorizedTopics));
        }

        var latest = new Dictionary<Guid, StudyAttributedReviewEvidence>();
        var reviewIdentities = new HashSet<Guid>();
        var observedEvidence = 0;
        foreach (var evidence in currentAuthorizedEvidence)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++observedEvidence > MaximumEvidenceRecords)
                throw new ArgumentException("Study evidence enumeration exceeds the bounded snapshot policy.", nameof(currentAuthorizedEvidence));
            ArgumentNullException.ThrowIfNull(evidence);
            // A caller-supplied principal or topic is never an access grant.
            if (!string.Equals(evidence.PrincipalId, currentPrincipalId, StringComparison.Ordinal) ||
                evidence.TopicId is not { } topicId || !topics.ContainsKey(topicId))
                continue;
            if (evidence.ReviewRecordId == Guid.Empty || evidence.SubjectIdAtReview == Guid.Empty || evidence.SourceArtifactId == Guid.Empty ||
                evidence.SourceRevision < 1 || evidence.ReviewRevision < 1 || evidence.ReviewedAt == default ||
                evidence.DueAt == default(DateTimeOffset) ||
                string.IsNullOrWhiteSpace(evidence.SourceOwnerAppKey) || evidence.SourceOwnerAppKey.Length > 256 ||
                evidence.EvidenceKind is not (StudyReviewEvidenceKind.CardSelfRating or StudyReviewEvidenceKind.QuizAnswer) ||
                (evidence.EvidenceKind == StudyReviewEvidenceKind.CardSelfRating && evidence.CardId is null) ||
                (evidence.CardId is { } presentCardId && presentCardId == Guid.Empty))
                throw new ArgumentException("Review evidence has no valid retained owning identity/revision.", nameof(currentAuthorizedEvidence));
            if (!reviewIdentities.Add(evidence.ReviewRecordId))
                throw new ArgumentException("The current review snapshot repeats a review identity.", nameof(currentAuthorizedEvidence));
            if (evidence.RecordedDifficulty is null || evidence.ReviewedAt > now)
                continue;
            if (evidence.RecordedDifficulty is not (StudyRecordedDifficulty.Red or StudyRecordedDifficulty.Amber or StudyRecordedDifficulty.Green))
                throw new ArgumentException("Recorded difficulty is outside the versioned owner contract.", nameof(currentAuthorizedEvidence));
            if (!latest.TryGetValue(topicId, out var prior) || CompareLatest(evidence, prior) > 0)
                latest[topicId] = evidence;
        }

        // Select at most three without allocating/sorting every candidate. Cancellation
        // is checked on the original path, rather than inside a comparer that can wrap it.
        var ranked = new List<StudyTopicRecommendation>(MaximumRecommendations + 1);
        foreach (var pair in latest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = pair.Value;
            var weak = evidence.RecordedDifficulty == StudyRecordedDifficulty.Red;
            var overdue = evidence.DueAt is { } dueAt && dueAt <= now;
            var basis = weak ? StudyRecommendationBasis.RecordedWeak :
                overdue ? StudyRecommendationBasis.ReviewOverdue : StudyRecommendationBasis.OldestReviewed;
            var candidate = new StudyTopicRecommendation(topics[pair.Key].SubjectId, pair.Key, evidence.ReviewRecordId,
                evidence.ReviewRevision, basis, evidence.EvidenceKind, evidence.RecordedDifficulty!.Value,
                evidence.ReviewedAt, evidence.DueAt, evidence.SourceOwnerAppKey, evidence.SourceArtifactId, evidence.SourceRevision);
            var index = 0;
            while (index < ranked.Count && CompareRecommendation(ranked[index], candidate, now) <= 0) index++;
            ranked.Insert(index, candidate);
            if (ranked.Count > MaximumRecommendations) ranked.RemoveAt(MaximumRecommendations);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(ranked.ToArray());
    }

    private static int CompareRecommendation(StudyTopicRecommendation left, StudyTopicRecommendation right, DateTimeOffset now)
    {
        var comparison = (left.RecordedDifficulty == StudyRecordedDifficulty.Red ? 0 : 1)
            .CompareTo(right.RecordedDifficulty == StudyRecordedDifficulty.Red ? 0 : 1);
        if (comparison == 0) comparison = (left.DueAt is { } leftDue && leftDue <= now ? 0 : 1)
            .CompareTo(right.DueAt is { } rightDue && rightDue <= now ? 0 : 1);
        if (comparison == 0) comparison = left.ReviewedAt.CompareTo(right.ReviewedAt);
        return comparison != 0 ? comparison :
            string.CompareOrdinal(left.TopicId.ToString("D"), right.TopicId.ToString("D"));
    }

    private static int CompareLatest(StudyAttributedReviewEvidence left, StudyAttributedReviewEvidence right)
    {
        var comparison = left.ReviewedAt.CompareTo(right.ReviewedAt);
        if (comparison == 0) comparison = left.ReviewRevision.CompareTo(right.ReviewRevision);
        return comparison != 0 ? comparison :
            string.CompareOrdinal(left.ReviewRecordId.ToString("D"), right.ReviewRecordId.ToString("D"));
    }
}
