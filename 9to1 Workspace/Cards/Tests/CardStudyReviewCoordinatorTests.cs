using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardStudyReviewCoordinatorTests
{
    private static CardSide Side(string value) => new()
    {
        DocumentFormat = "9to1.shared-productivity/1",
        Document = JsonDocument.Parse("""{"blocks":[{"kind":"text"}]}""").RootElement.Clone(),
        SearchText = value,
    };

    private static CardSet Deck(string? topic = null)
    {
        CardSet empty = CardSetOperations.Create("personal:alice", "Cells");
        CardSet cards = CardSetOperations.AddCards(empty,
            [(Side("Mitosis"), Side("Division"))], empty.Revision, CardInteractionMode.Edit);
        if (topic is not null)
            cards = CardSetOperations.AssignGrouping(cards, cards.Cards[0].CardId,
                "Biology", topic, null, cards.Revision, CardInteractionMode.Edit);
        return cards;
    }

    private static CardReviewRequest Request(CardSet set, string id = "review-1",
        CardReviewRating rating = CardReviewRating.Red) =>
        new(id, set.SetId, set.Cards[0].CardId, rating);

    private sealed class OwnerState(CardSet set, string principal = "alice") :
        ICardTrustedCallerSource, ICardCanonicalMutationStore,
        ICardPrivateReviewOwner, ICardStudyTopicAuthority, ICardStudyEvidenceOwner
    {
        private readonly Dictionary<string, (string Fingerprint, CardPrivateReviewReceipt Receipt)> _committed = [];
        public CardSet Set { get; set; } = set;
        public string Principal { get; } = principal;
        public int PersonalCommits { get; private set; }
        public int StudyAttempts { get; private set; }
        public CardStudyDeliveryStatus DeliveryStatus { get; set; } = CardStudyDeliveryStatus.Submitted;
        public CardStudyLinkStatus LinkStatus { get; set; } = CardStudyLinkStatus.Valid;
        public bool AllowPersonalWrite { get; set; } = true;
        public bool StudyTransportUnavailable { get; set; }
        public bool ReturnMismatchedStudyOperation { get; set; }
        public bool ReturnBadPrivateReceipt { get; set; }
        public CardReviewRecord? LastStudyReview { get; private set; }

        public ValueTask<CardTrustedCaller?> ResolveAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<CardTrustedCaller?>(new(Principal, "issued-host-session"));

        public Task<CardSet?> ReadForPrincipalAsync(CardTrustedCaller caller, Guid setId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CardSet?>(caller.PrincipalId == "alice" && setId == Set.SetId
                ? Set : null);

        public Task<CardPrivateReviewReceipt> RecordAsync(CardTrustedCaller caller,
            CardReviewRequest request, string fingerprint, CardReviewRecord proposed,
            long observedSetRevision, CancellationToken token)
        {
            if (_committed.TryGetValue(request.OperationId, out var old))
            {
                if (old.Fingerprint != fingerprint)
                    throw new CardOperationException(CardFailureCode.InvalidState,
                        "Attempt to replay a different review payload.");
                return Task.FromResult(old.Receipt with
                {
                    Status = CardPrivateReviewStatus.AlreadyCommitted,
                });
            }
            if (!AllowPersonalWrite || Set.Revision != observedSetRevision)
                return Task.FromResult(new CardPrivateReviewReceipt(
                    CardPrivateReviewStatus.Denied, caller.PrincipalId, request.SetId,
                    request.CardId, request.OperationId, fingerprint, null, "NotCommitted"));

            var receipt = new CardPrivateReviewReceipt(CardPrivateReviewStatus.Committed,
                caller.PrincipalId, request.SetId, request.CardId,
                request.OperationId, fingerprint, proposed.ReviewedAt, "Committed", proposed);
            _committed.Add(request.OperationId, (fingerprint, receipt));
            PersonalCommits++;
            return Task.FromResult(ReturnBadPrivateReceipt
                ? receipt with { PrincipalId = "wrong-principal" } : receipt);
        }

        public Task<CardStudyLinkDecision> CheckLinkAsync(CardTrustedCaller caller,
            CardReviewRecord review, CancellationToken token) =>
            Task.FromResult(LinkStatus == CardStudyLinkStatus.Valid
                ? new CardStudyLinkDecision(review.TopicId is null
                    ? CardStudyLinkStatus.Unlinked : LinkStatus, review.TopicId,
                    review.TopicId is null ? "TopicNotLinked" : "Valid")
                : new CardStudyLinkDecision(LinkStatus, null, LinkStatus.ToString()));

        public Task<CardStudyDeliveryReceipt> DeliverAsync(CardTrustedCaller caller,
            CardReviewRecord saved, CardPrivateReviewReceipt receipt, string topic,
            CancellationToken token)
        {
            StudyAttempts++;
            LastStudyReview = saved;
            if (StudyTransportUnavailable)
                throw new IOException("Study provider cannot be reached.");
            return Task.FromResult(new CardStudyDeliveryReceipt(
                DeliveryStatus,
                ReturnMismatchedStudyOperation ? "different-operation" : receipt.OperationId,
                DeliveryStatus.ToString()));
        }

        // The review coordinator uses only Files' authorised read port.
        public Task<CardCommitReceipt?> FindReceiptAsync(CardTrustedCaller caller,
            Guid setId, string operationId, string fingerprint,
            CancellationToken token) => throw new NotSupportedException();
        public Task<CardCommitReceipt> CommitAsync(CardTrustedCaller caller,
            CardMutation request, string fingerprint, CardSet successor,
            Func<CancellationToken, ValueTask<CardAdmissionDecision>> recheckAdmission,
            CancellationToken token) => throw new NotSupportedException();
    }

    private static CardStudyReviewCoordinator Service(OwnerState owners) =>
        new(owners, owners, owners, owners, owners);

    [Fact]
    public async Task LinkedReviewPersistsPersonalRatingBeforeActualStudyEvidence()
    {
        CardSet set = Deck("topic-verified-1");
        var owner = new OwnerState(set);
        CardReviewOutcome result = await Service(owner).RateAsync(Request(set));

        Assert.True(result.ReviewSaved);
        Assert.False(result.Replayed);
        Assert.Equal(CardStudyEvidenceState.StudySubmitted, result.StudyState);
        Assert.Equal(1, owner.PersonalCommits);
        Assert.Equal(1, owner.StudyAttempts);
        Assert.Equal("topic-verified-1", owner.LastStudyReview?.TopicId);
        Assert.Equal("alice", owner.LastStudyReview?.PrincipalId);
        Assert.Equal(set.SetId, owner.LastStudyReview?.SetId);
        Assert.Equal(set.Revision, owner.Set.Revision);
        Assert.DoesNotContain("alice", CardSetOperations.ExportJson(set));
    }

    [Fact]
    public async Task UnlinkedReviewPersistsWithoutInventingTopicProgress()
    {
        CardSet set = Deck();
        var owner = new OwnerState(set);
        CardReviewOutcome result = await Service(owner).RateAsync(Request(set));

        Assert.True(result.ReviewSaved);
        Assert.Equal(CardStudyEvidenceState.Unlinked, result.StudyState);
        Assert.Equal(0, owner.StudyAttempts);
        Assert.Equal(1, owner.PersonalCommits);
    }

    [Theory]
    [InlineData(CardStudyLinkStatus.NotAuthorised)]
    [InlineData(CardStudyLinkStatus.Unavailable)]
    public async Task UnverifiedTopicNeverCreatesStudyProgress(CardStudyLinkStatus linkStatus)
    {
        CardSet set = Deck("topic-candidate");
        var owner = new OwnerState(set) { LinkStatus = linkStatus };

        CardReviewOutcome result = await Service(owner).RateAsync(Request(set));

        Assert.True(result.ReviewSaved);
        Assert.Equal(CardStudyEvidenceState.StudyNotConfirmed, result.StudyState);
        Assert.Equal(0, owner.StudyAttempts);
    }

    [Theory]
    [InlineData(CardStudyDeliveryStatus.QueuedDurably, CardStudyEvidenceState.StudyQueued)]
    [InlineData(CardStudyDeliveryStatus.Unavailable, CardStudyEvidenceState.StudyNotConfirmed)]
    [InlineData(CardStudyDeliveryStatus.Rejected, CardStudyEvidenceState.StudyNotConfirmed)]
    public async Task StudyOutcomesNeverConflateQueuedWithConfirmedProgress(
        CardStudyDeliveryStatus delivery, CardStudyEvidenceState expected)
    {
        CardSet set = Deck("valid-topic");
        var owner = new OwnerState(set) { DeliveryStatus = delivery };

        CardReviewOutcome result = await Service(owner).RateAsync(Request(set));

        Assert.True(result.ReviewSaved);
        Assert.Equal(expected, result.StudyState);
        Assert.Equal(1, owner.PersonalCommits);
        Assert.Equal(1, owner.StudyAttempts);
    }

    [Fact]
    public async Task FailedPersonalReviewCannotTriggerStudy()
    {
        CardSet set = Deck("topic-valid");
        var owner = new OwnerState(set) { AllowPersonalWrite = false };

        CardReviewOutcome result = await Service(owner).RateAsync(Request(set));

        Assert.False(result.ReviewSaved);
        Assert.Equal(CardStudyEvidenceState.ReviewNotSaved, result.StudyState);
        Assert.Equal(0, owner.PersonalCommits);
        Assert.Equal(0, owner.StudyAttempts);
    }

    [Fact]
    public async Task StudyTransportFailureDoesNotUndoAlreadyCommittedPersonalReview()
    {
        CardSet set = Deck("valid-topic");
        var owner = new OwnerState(set) { StudyTransportUnavailable = true };

        CardReviewOutcome outcome = await Service(owner).RateAsync(Request(set));

        Assert.True(outcome.ReviewSaved);
        Assert.Equal(CardStudyEvidenceState.StudyNotConfirmed, outcome.StudyState);
        Assert.Equal("StudyTransportUnavailable", outcome.StudyCode);
        Assert.Equal(1, owner.PersonalCommits);
        Assert.Equal(1, owner.StudyAttempts);
    }

    [Fact]
    public async Task AStudyReceiptForAnotherOperationIsNeverAccepted()
    {
        CardSet set = Deck("verified-topic");
        var owner = new OwnerState(set) { ReturnMismatchedStudyOperation = true };

        CardOperationException error = await Assert.ThrowsAsync<CardOperationException>(
            () => Service(owner).RateAsync(Request(set)));

        Assert.Equal(CardFailureCode.InvalidState, error.Code);
        Assert.Equal(1, owner.PersonalCommits);
    }

    [Fact]
    public async Task PrivateReviewReceiptCannotSubstituteAnotherPrincipal()
    {
        CardSet set = Deck("topic");
        var owner = new OwnerState(set) { ReturnBadPrivateReceipt = true };

        CardOperationException error = await Assert.ThrowsAsync<CardOperationException>(
            () => Service(owner).RateAsync(Request(set)));

        Assert.Equal(CardFailureCode.InvalidState, error.Code);
        Assert.Equal(0, owner.StudyAttempts);
    }

    [Fact]
    public async Task RepeatedOperationUsesOriginalPersistedReviewAndDoesNotDuplicatePersonalEvidence()
    {
        CardSet set = Deck("old-topic");
        var owner = new OwnerState(set);
        CardStudyReviewCoordinator coordinator = Service(owner);
        CardReviewRequest request = Request(set);

        CardReviewOutcome first = await coordinator.RateAsync(request);
        CardSet updated = CardSetOperations.AssignGrouping(set,
            set.Cards[0].CardId, "Biology", "different-topic", null,
            set.Revision, CardInteractionMode.Edit);
        owner.Set = updated;
        CardReviewOutcome replay = await coordinator.RateAsync(request);

        Assert.True(first.ReviewSaved);
        Assert.True(replay.Replayed);
        Assert.Equal(1, owner.PersonalCommits);
        Assert.Equal(2, owner.StudyAttempts);
        Assert.Equal("old-topic", owner.LastStudyReview?.TopicId);
        Assert.Equal(first.ReviewedAtUtc, replay.ReviewedAtUtc);
    }

    [Fact]
    public async Task ChangedRatingWithReusedOperationIdIsDenied()
    {
        CardSet set = Deck("linked");
        var owner = new OwnerState(set);
        var service = Service(owner);
        _ = await service.RateAsync(Request(set, "once", CardReviewRating.Red));

        var different = Request(set, "once", CardReviewRating.Green);
        CardOperationException error = await Assert.ThrowsAsync<CardOperationException>(
            () => service.RateAsync(different));

        Assert.Equal(CardFailureCode.InvalidState, error.Code);
        Assert.Equal(1, owner.PersonalCommits);
    }

    [Fact]
    public async Task RetiredCallerCannotObserveOrWriteAnotherLearnersPrivateReviews()
    {
        CardSet set = Deck("linked");
        var owner = new OwnerState(set, "bob");

        CardOperationException error = await Assert.ThrowsAsync<CardOperationException>(
            () => Service(owner).RateAsync(Request(set)));

        Assert.Equal(CardFailureCode.CardNotFound, error.Code);
        Assert.Equal(0, owner.PersonalCommits);
        Assert.Equal(0, owner.StudyAttempts);
    }

    [Fact]
    public async Task DeletedCardCannotBeReviewedOrCreateNewProgress()
    {
        CardSet set = Deck("topic-a");
        CardSet deleted = CardSetOperations.SoftDelete(set,
            CardSetOperations.PreviewDelete(set, [set.Cards[0].CardId]),
            CardInteractionMode.Edit);
        var owner = new OwnerState(deleted);

        CardOperationException error = await Assert.ThrowsAsync<CardOperationException>(
            () => Service(owner).RateAsync(Request(deleted)));

        Assert.Equal(CardFailureCode.CardAlreadyDeleted, error.Code);
        Assert.Equal(0, owner.StudyAttempts);
    }
}
