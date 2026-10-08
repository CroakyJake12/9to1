using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.CUI.Tests;

public sealed class CardCuiWorkspaceTests
{
    private static CardSide Side(string text) => new()
    {
        DocumentFormat = "9to1.home-productivity-bundle/1",
        Document = JsonDocument.Parse("{}").RootElement.Clone(),
        SearchText = text,
    };

    private static SceneState Build()
    {
        var empty = CardSetOperations.Create("owner:alpha", "A Level Law");
        var original = CardSetOperations.AddCards(empty,
            [(Side("Mens rea"), Side("Guilty mind")),
             (Side("Actus reus"), Side("Guilty act")),
             (Side("Causation"), Side("Result"))],
            empty.Revision, CardInteractionMode.Edit);
        var state = new SceneState(original);
        state.Ui = new CardCuiWorkspace(state,
            new CardMutationGateway(state, state, state),
            state, state);
        return state;
    }

    [Fact]
    public void RealCuiDocumentParsesWithExistingParser()
    {
        Assert.NotNull(CardCuiWorkspace.LoadDocument());
    }

    [Fact]
    public async Task OpensCanonicalSetNavigatesFlipsAndFiltersWithoutMutatingCards()
    {
        SceneState state = Build();
        CardCuiWorkspace ui = state.Ui!;
        Assert.False(ui.CanNext);
        await ui.DispatchAsync("9to1.Cards.Open", null);
        Assert.Equal("A Level Law", ui.SetTitle);
        Assert.Equal("1 / 3", ui.PositionLabel);
        Guid first = ui.SelectedCardId!.Value;
        await ui.DispatchAsync("9to1.Cards.Next", null);
        Assert.Equal("2 / 3", ui.PositionLabel);
        await ui.DispatchAsync("9to1.Cards.Flip", null);
        Assert.Equal("Back", ui.SideLabel);
        Assert.True(ui.TrySetValue("SearchText", "Causation"));
        Assert.Equal("1 / 1", ui.PositionLabel);
        Assert.Equal(state.Canonical.Cards[2].CardId, ui.SelectedCardId);
        Assert.False(ui.CanNext);
        Assert.False(ui.TrySetValue("SearchText", new string('z', 257)));
        Assert.Equal(first, state.Canonical.Cards[0].CardId);
        Assert.Equal(2, state.Canonical.Revision);
    }

    [Fact]
    public async Task EditModeRequiresAndObservesCanonicalCommitBeforePublishingSuccess()
    {
        SceneState state = Build();
        CardCuiWorkspace ui = state.Ui!;
        await ui.DispatchAsync("9to1.Cards.Open", null);
        Assert.False(ui.CanEdit);
        await ui.DispatchAsync("9to1.Cards.ToggleMode", null);
        Assert.True(ui.CanEdit);
        await ui.DispatchAsync("9to1.Cards.EditSide", null);
        Assert.Equal(1, state.Commits);
        Assert.Equal("Edited by canonical engine", state.Canonical.Cards[0].Front.SearchText);
        Assert.Contains("Saved revision", ui.Status);
        Assert.Equal(3, state.Canonical.Revision);
        Assert.Equal("Mens rea", state.Original.Cards[0].Front.SearchText);
    }

    [Fact]
    public async Task DeniedCommitLeavesCurrentDeckUntouchedAndReportsFailure()
    {
        SceneState state = Build();
        CardCuiWorkspace ui = state.Ui!;
        await ui.DispatchAsync("9to1.Cards.Open", null);
        await ui.DispatchAsync("9to1.Cards.ToggleMode", null);
        state.CanApprove = false;
        await ui.DispatchAsync("9to1.Cards.EditSide", null);
        Assert.Equal(0, state.Commits);
        Assert.Equal(2, state.Canonical.Revision);
        Assert.Contains("No changes saved", ui.Status);
    }

    [Fact]
    public async Task PersonalReviewRequiresActualReceiptButDoesNotMutateSharedDeck()
    {
        SceneState state = Build();
        CardCuiWorkspace ui = state.Ui!;
        await ui.DispatchAsync("9to1.Cards.Open", null);
        Assert.True(ui.CanRate);
        await ui.DispatchAsync("9to1.Cards.RateRed", null);
        Assert.Equal(CardReviewRating.Red, state.PersonalReview);
        Assert.Equal(2, state.Canonical.Revision);
        Assert.Contains("Review saved", ui.Status);
        state.CanWriteReview = false;
        await ui.DispatchAsync("9to1.Cards.RateGreen", null);
        Assert.Contains("Review not saved", ui.Status);
        Assert.Equal(CardReviewRating.Red, state.PersonalReview);
    }

    private sealed class SceneState(CardSet original) :
        ICardCuiSetSource, ICardCuiRichEditor, ICardCuiReviewOwner,
        ICardCanonicalMutationStore, ICardTrustedCallerSource, ICardMutationAdmission
    {
        public CardCuiWorkspace? Ui { get; set; }
        public CardSet Original { get; } = original;
        public CardSet Canonical { get; private set; } = original;
        public int Commits { get; private set; }
        public bool CanApprove { get; set; } = true;
        public bool CanWriteReview { get; set; } = true;
        public CardReviewRating? PersonalReview { get; private set; }

        public Task<CardSet?> OpenCurrentAsync(CancellationToken token) =>
            Task.FromResult<CardSet?>(Canonical);

        public Task<CardSide?> EditAsync(Guid setId, Guid cardId, bool front,
            CardSide current, CancellationToken token) =>
            Task.FromResult<CardSide?>(Side("Edited by canonical engine"));

        public Task<CardReviewWriteReceipt> RateCurrentAsync(Guid setId, Guid cardId,
            CardReviewRating rating, CancellationToken token)
        {
            if (!CanWriteReview)
                return Task.FromResult(new CardReviewWriteReceipt(false, "HomePermissionDenied"));
            PersonalReview = rating;
            return Task.FromResult(new CardReviewWriteReceipt(true, "Committed"));
        }

        public ValueTask<CardTrustedCaller?> ResolveAsync(CancellationToken token) =>
            ValueTask.FromResult<CardTrustedCaller?>(new("alpha", "verified-host"));

        public Task<CardSet?> ReadForPrincipalAsync(CardTrustedCaller caller, Guid setId,
            CancellationToken token) =>
            Task.FromResult<CardSet?>(Canonical.SetId == setId ? Canonical : null);

        public Task<CardCommitReceipt?> FindReceiptAsync(CardTrustedCaller caller, Guid setId,
            string operationId, string fingerprint, CancellationToken token) =>
            Task.FromResult<CardCommitReceipt?>(null);

        public ValueTask<CardAdmissionDecision> CheckAsync(CardTrustedCaller caller,
            CardMutation request, CardSet current, CancellationToken token) =>
            ValueTask.FromResult(new CardAdmissionDecision(CanApprove,
                CanApprove ? "Allowed" : "HomePermissionDenied"));

        public async Task<CardCommitReceipt> CommitAsync(CardTrustedCaller caller,
            CardMutation request, string fingerprint, CardSet successor,
            Func<CancellationToken, ValueTask<CardAdmissionDecision>> recheckAdmission,
            CancellationToken token)
        {
            if (!(await recheckAdmission(token)).Allowed)
                return new(CardCommitStatus.AdmissionRevoked, request.SetId,
                    request.OperationId, null, "HomePermissionDenied");
            if (Canonical.Revision != request.ExpectedSetRevision)
                return new(CardCommitStatus.RevisionConflict, request.SetId,
                    request.OperationId, Canonical.Revision, "RevisionConflict");
            Canonical = successor;
            Commits++;
            return new(CardCommitStatus.Committed, request.SetId,
                request.OperationId, successor.Revision, "Committed");
        }
    }
}
