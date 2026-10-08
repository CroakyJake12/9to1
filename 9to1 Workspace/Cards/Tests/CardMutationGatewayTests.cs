using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardMutationGatewayTests
{
    private static CardMutation.Rename Rename(CardSet set, string operation, string title) =>
        new(operation, set.SetId, set.Revision, CardInteractionMode.Edit, title);

    private static CardMutationGateway Build(FakeStore store, FakeAdmission admission,
        string principal = "student-a") =>
        new(store, new FakeCaller(principal), admission);

    [Fact]
    public async Task ConfirmedRenameRequiresCommitReceiptAndDeduplicatesReplay()
    {
        CardSet set = CardSetOperations.Create("personal:student-a", "Before");
        var store = new FakeStore(set);
        var approval = new FakeAdmission();
        var gateway = Build(store, approval);
        CardMutation.Rename command = Rename(set, "rename-1", "After");

        CardMutationOutcome first = await gateway.ExecuteAsync(command);
        Assert.True(first.Succeeded);
        Assert.False(first.Replayed);
        Assert.Equal("After", store.Current.Title);
        Assert.Equal(2, first.CommittedRevision);
        Assert.Equal(1, store.CommitCount);

        CardMutationOutcome replay = await gateway.ExecuteAsync(command);
        Assert.True(replay.Succeeded);
        Assert.True(replay.Replayed);
        Assert.Equal(2, replay.CommittedRevision);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(2, store.ReceiptFindCount); // once before first commit, once on replay
    }

    [Fact]
    public async Task DeniedRequestNeverMutatesOrReachesCommit()
    {
        CardSet set = CardSetOperations.Create("personal:student-a", "Protected");
        var store = new FakeStore(set);
        var admission = new FakeAdmission { Allowed = false };
        var gateway = Build(store, admission);

        CardMutationOutcome refused = await gateway.ExecuteAsync(Rename(set, "rename-2", "Denied"));

        Assert.False(refused.Succeeded);
        Assert.Equal("HomePermissionDenied", refused.Code);
        Assert.Equal("Protected", store.Current.Title);
        Assert.Equal(0, store.CommitCount);
        Assert.Equal(1, admission.CheckCount);
    }

    [Fact]
    public async Task RevokedCommitTimeAuthorityPreservesSource()
    {
        CardSet set = CardSetOperations.Create("personal:student-a", "Protected");
        var store = new FakeStore(set);
        var admission = new FakeAdmission { DenyAtCommit = true };
        var gateway = Build(store, admission);
        CardMutationOutcome refused = await gateway.ExecuteAsync(Rename(set, "rename-3", "Denied"));

        Assert.False(refused.Succeeded);
        Assert.Equal("AdmissionRevoked", refused.Code);
        Assert.Equal("Protected", store.Current.Title);
        Assert.Equal(0, store.CommitCount);
        Assert.Equal(2, admission.CheckCount);
    }

    [Fact]
    public async Task CompetingRevisionReturnsTruthfulConflictWithoutOverwriting()
    {
        CardSet set = CardSetOperations.Create("personal:student-a", "Original");
        var store = new FakeStore(set)
        {
            BeforeCommit = () => CardSetOperations.RenameSet(set,
                "Other writer", set.Revision, CardInteractionMode.Edit),
        };
        var gateway = Build(store, new FakeAdmission());
        CardMutationOutcome result = await gateway.ExecuteAsync(Rename(set, "rename-4", "Mine"));

        Assert.False(result.Succeeded);
        Assert.Equal("RevisionConflict", result.Code);
        Assert.Equal("Other writer", store.Current.Title);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task ReplayedOperationIdWithChangedPayloadMustFailClosed()
    {
        CardSet set = CardSetOperations.Create("personal:student-a", "First");
        var store = new FakeStore(set);
        var gateway = Build(store, new FakeAdmission());
        _ = await gateway.ExecuteAsync(Rename(set, "same-id", "Second"));

        var changed = Rename(set, "same-id", "Unexpected different text");
        await Assert.ThrowsAsync<CardOperationException>(() => gateway.ExecuteAsync(changed));
        Assert.Equal("Second", store.Current.Title);
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task DeleteRequiresExpectedVersionBeforeAnyCommitAttempt()
    {
        CardSet empty = CardSetOperations.Create("personal:student-a", "Deck");
        CardSet set = CardSetOperations.AddCards(empty,
            [(NewSide(), NewSide())], empty.Revision, CardInteractionMode.Edit);
        var store = new FakeStore(set);
        var gateway = Build(store, new FakeAdmission());

        var stale = new CardMutation.Delete("delete-1", set.SetId, set.Revision - 1,
            CardInteractionMode.Edit, [set.Cards[0].CardId]);
        var failed = await Assert.ThrowsAsync<CardOperationException>(() => gateway.ExecuteAsync(stale));

        Assert.Equal(CardFailureCode.RevisionConflict, failed.Code);
        Assert.False(store.Current.Cards[0].IsDeleted);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task CreatesRealFrontAndBackAndAcknowledgesRevisionOnlyAfterCommit()
    {
        CardSet initial = CardSetOperations.Create("personal:student-a", "Questions");
        var store = new FakeStore(initial);
        var gateway = Build(store, new FakeAdmission());
        var command = new CardMutation.AddCards("new-1", initial.SetId,
            initial.Revision, CardInteractionMode.Edit,
            [new CardDraft(NewSide(), NewSide()), new CardDraft(NewSide(), NewSide())]);

        CardMutationOutcome result = await gateway.ExecuteAsync(command);

        Assert.True(result.Succeeded);
        Assert.Equal(initial.Revision + 1, result.CommittedRevision);
        Assert.Equal(2, store.Current.Cards.Count);
        Assert.All(store.Current.Cards, card => Assert.NotEqual(Guid.Empty, card.CardId));
        Assert.NotEqual(store.Current.Cards[0].CardId, store.Current.Cards[1].CardId);
        Assert.Equal(1, store.CommitCount);
        CardMutationOutcome replay = await gateway.ExecuteAsync(command);
        Assert.True(replay.Replayed);
        Assert.Equal(2, store.Current.Cards.Count);
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task DuplicateKeepsRichContentButUsesNewStableCardId()
    {
        CardSet empty = CardSetOperations.Create("personal:student-a", "Questions");
        CardSet start = CardSetOperations.AddCards(empty, [(NewSide(), NewSide())],
            empty.Revision, CardInteractionMode.Edit);
        var store = new FakeStore(start);
        var gateway = Build(store, new FakeAdmission());
        var command = new CardMutation.Duplicate("duplicate-1", start.SetId,
            start.Revision, CardInteractionMode.Edit, start.Cards[0].CardId);

        var result = await gateway.ExecuteAsync(command);

        Assert.True(result.Succeeded);
        Assert.Equal(2, store.Current.Cards.Count);
        Assert.Equal(start.Cards[0].CardId, store.Current.Cards[0].CardId);
        Assert.NotEqual(start.Cards[0].CardId, store.Current.Cards[1].CardId);
        Assert.True(System.Text.Json.JsonElement.DeepEquals(start.Cards[0].Front.Document,
            store.Current.Cards[1].Front.Document));
    }

    [Fact]
    public async Task ViewModeNeverCreatesOrDuplicatesCards()
    {
        CardSet initial = CardSetOperations.Create("personal:student-a", "Questions");
        var store = new FakeStore(initial);
        var gateway = Build(store, new FakeAdmission());
        var add = new CardMutation.AddCards("create-view", initial.SetId,
            initial.Revision, CardInteractionMode.View, [new CardDraft(NewSide(), NewSide())]);

        var failure = await Assert.ThrowsAsync<CardOperationException>(() => gateway.ExecuteAsync(add));

        Assert.Equal(CardFailureCode.ViewIsReadOnly, failure.Code);
        Assert.Empty(store.Current.Cards);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task InvalidBulkCreationDoesNotCommitPartialResults()
    {
        CardSet initial = CardSetOperations.Create("personal:student-a", "Questions");
        var store = new FakeStore(initial);
        var gateway = Build(store, new FakeAdmission());
        CardSide invalid = NewSide() with { DocumentFormat = "" };
        var command = new CardMutation.AddCards("bad-create", initial.SetId,
            initial.Revision, CardInteractionMode.Edit,
            [new CardDraft(NewSide(), NewSide()), new CardDraft(invalid, NewSide())]);

        var failure = await Assert.ThrowsAsync<CardOperationException>(() => gateway.ExecuteAsync(command));

        Assert.Equal(CardFailureCode.InvalidContent, failure.Code);
        Assert.Empty(store.Current.Cards);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task DeniedCreationCannotManufactureCommittedCards()
    {
        CardSet initial = CardSetOperations.Create("personal:student-a", "Questions");
        var store = new FakeStore(initial);
        var gateway = Build(store, new FakeAdmission { Allowed = false });
        var command = new CardMutation.AddCards("no-create", initial.SetId,
            initial.Revision, CardInteractionMode.Edit, [new CardDraft(NewSide(), NewSide())]);

        CardMutationOutcome outcome = await gateway.ExecuteAsync(command);
        Assert.False(outcome.Succeeded);
        Assert.Empty(store.Current.Cards);
        Assert.Equal(0, store.CommitCount);
    }

    private static CardSide NewSide() => new()
    {
        DocumentFormat = "9to1.shared-productivity/1",
        Document = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
    };

    private sealed class FakeCaller(string principal) : ICardTrustedCallerSource
    {
        public ValueTask<CardTrustedCaller?> ResolveAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<CardTrustedCaller?>(new(principal, "host-issuer-proof"));
    }

    private sealed class FakeAdmission : ICardMutationAdmission
    {
        public bool Allowed { get; set; } = true;
        public bool DenyAtCommit { get; set; }
        public int CheckCount { get; private set; }

        public ValueTask<CardAdmissionDecision> CheckAsync(CardTrustedCaller caller,
            CardMutation request, CardSet current, CancellationToken cancellationToken)
        {
            CheckCount++;
            bool allowed = Allowed && !(DenyAtCommit && CheckCount > 1);
            return ValueTask.FromResult(allowed
                ? new CardAdmissionDecision(true, "Allowed")
                : new CardAdmissionDecision(false, "HomePermissionDenied"));
        }
    }

    /// <summary>Test double only, not a product persistence implementation.</summary>
    private sealed class FakeStore(CardSet current) : ICardCanonicalMutationStore
    {
        private readonly Dictionary<(string PrincipalId, string OperationId),
            (string Fingerprint, CardCommitReceipt Receipt)> _receipts = new();
        public CardSet Current { get; private set; } = current;
        public int CommitCount { get; private set; }
        public int ReceiptFindCount { get; private set; }
        public Func<CardSet>? BeforeCommit { get; init; }

        public Task<CardSet?> ReadForPrincipalAsync(CardTrustedCaller caller, Guid setId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CardSet?>(setId == Current.SetId ? Current : null);

        public Task<CardCommitReceipt?> FindReceiptAsync(CardTrustedCaller caller, Guid setId,
            string operationId, string requestFingerprint, CancellationToken cancellationToken)
        {
            ReceiptFindCount++;
            if (!_receipts.TryGetValue((caller.PrincipalId, operationId), out var previous))
                return Task.FromResult<CardCommitReceipt?>(null);
            if (!StringComparer.Ordinal.Equals(previous.Fingerprint, requestFingerprint))
                throw new CardOperationException(CardFailureCode.InvalidState,
                    "Operation ID was reused for a different request.");
            return Task.FromResult<CardCommitReceipt?>(previous.Receipt with
            {
                Status = CardCommitStatus.AlreadyCommitted,
            });
        }

        public async Task<CardCommitReceipt> CommitAsync(CardTrustedCaller caller,
            CardMutation request, string requestFingerprint, CardSet successor,
            Func<CancellationToken, ValueTask<CardAdmissionDecision>> recheckAdmission,
            CancellationToken cancellationToken)
        {
            if (!((await recheckAdmission(cancellationToken)).Allowed))
                return new(CardCommitStatus.AdmissionRevoked, request.SetId,
                    request.OperationId, null, "AdmissionRevoked");
            if (BeforeCommit is not null)
                Current = BeforeCommit();
            if (Current.Revision != request.ExpectedSetRevision)
                return new(CardCommitStatus.RevisionConflict, request.SetId,
                    request.OperationId, Current.Revision, "RevisionConflict");

            Current = successor;
            CommitCount++;
            var receipt = new CardCommitReceipt(CardCommitStatus.Committed,
                request.SetId, request.OperationId, successor.Revision, "Committed");
            _receipts[(caller.PrincipalId, request.OperationId)] = (requestFingerprint, receipt);
            return receipt;
        }
    }
}
