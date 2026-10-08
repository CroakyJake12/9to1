using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Cards.Tests;

public sealed class CardNavigationSessionTests
{
    private static CardSide Side(string label) => new()
    {
        DocumentFormat = "9to1.shared-productivity/1",
        Document = JsonDocument.Parse("""{"objects":[{"type":"text"}]}""").RootElement.Clone(),
        SearchText = label,
    };

    private static CardSet Deck()
    {
        CardSet initial = CardSetOperations.Create("owner:personal", "Science");
        CardSet set = CardSetOperations.AddCards(initial,
            [(Side("Mitosis"), Side("Division")),
             (Side("Meiosis"), Side("Gametes")),
             (Side("Osmosis"), Side("Water"))],
            initial.Revision, CardInteractionMode.Edit);
        set = CardSetOperations.AssignGrouping(set, set.Cards[0].CardId,
            "Biology", "Cells", new Dictionary<string, string> { ["paper"] = "Paper 1" },
            set.Revision, CardInteractionMode.Edit);
        set = CardSetOperations.AssignGrouping(set, set.Cards[1].CardId,
            "Biology", "Reproduction", new Dictionary<string, string> { ["paper"] = "Paper 2" },
            set.Revision, CardInteractionMode.Edit);
        set = CardSetOperations.AssignGrouping(set, set.Cards[2].CardId,
            "Chemistry", "Solutions", new Dictionary<string, string> { ["paper"] = "Paper 1" },
            set.Revision, CardInteractionMode.Edit);
        return set;
    }

    [Fact]
    public void VerticalNavigationFlipsAndRestoresFrontOnCardChange()
    {
        CardSet set = Deck();
        var session = new CardNavigationSession();
        session.Load(set);
        CardFocusedWindow first = session.Window();
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(1, first.Position);
        Assert.Equal(set.Cards[0].CardId, first.SelectedCardId);
        Assert.Null(first.Previous);
        Assert.Equal(set.Cards[1].CardId, first.Next?.CardId);
        Assert.True(first.FrontVisible);
        Assert.False(session.Navigate(CardNavigationInput.ArrowLeft));
        Assert.True(session.Navigate(CardNavigationInput.Flip));
        Assert.False(session.Window().FrontVisible);
        Assert.True(session.Navigate(CardNavigationInput.ArrowDown));
        Assert.True(session.Window().FrontVisible);
        Assert.Equal(2, session.Window().Position);
        Assert.True(session.Navigate(CardNavigationInput.ArrowUp));
        Assert.False(session.Navigate(CardNavigationInput.ArrowUp));
    }

    [Fact]
    public void HorizontalPreferencesUseHorizontalArrows()
    {
        var session = new CardNavigationSession(new(CardNavigationDirection.Horizontal));
        session.Load(Deck());
        Assert.Equal(CardNavigationDirection.Horizontal, session.Preferences.Navigation);
        Assert.False(session.Navigate(CardNavigationInput.ArrowDown));
        Assert.True(session.Navigate(CardNavigationInput.ArrowRight));
        Assert.Equal(2, session.Window().Position);
        Assert.True(session.Navigate(CardNavigationInput.ArrowLeft));
        Assert.Equal(1, session.Window().Position);
        Assert.True(session.Move(1)); // touch/swipe calls logical direction, independent of layout.
    }

    [Fact]
    public void SubjectTopicAndCustomGroupingKeepCanonicalCardIDs()
    {
        CardSet set = Deck();
        var session = new CardNavigationSession();
        session.Load(set);
        session.SetGrouping(CardGroupingKind.Subject);
        Assert.Equal(new[] { "Biology", "Chemistry" }, session.GroupNames);
        Assert.Equal(2, session.Window().TotalCount);
        Assert.True(session.SelectGroup("Chemistry"));
        Assert.Equal(set.Cards[2].CardId, session.SelectedCardId);
        Assert.False(session.SelectGroup("Nonexistent"));

        session.SetGrouping(CardGroupingKind.Topic);
        Assert.Equal(new[] { "Cells", "Reproduction", "Solutions" }, session.GroupNames);
        Assert.True(session.SelectGroup("Reproduction"));
        Assert.Equal(set.Cards[1].CardId, session.SelectedCardId);

        session.SetGrouping(CardGroupingKind.Custom, "paper");
        Assert.Equal(new[] { "Paper 1", "Paper 2" }, session.GroupNames);
        Assert.True(session.SelectGroup("Paper 2"));
        Assert.Equal(set.Cards[1].CardId, session.SelectedCardId);
        Assert.Equal(set.SetId, session.Window().SetId);
        Assert.Equal(set.Revision, session.Window().SetRevision);
    }

    [Fact]
    public void FilteringCanProduceEmptyStateWithoutSelectingWrongCard()
    {
        CardSet set = Deck();
        var session = new CardNavigationSession();
        session.Load(set);
        session.SetSearch("NOMATCH");
        CardFocusedWindow empty = session.Window();
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.Position);
        Assert.Null(empty.SelectedCardId);
        Assert.Null(empty.Current);
        Assert.False(session.Flip());
        Assert.False(session.Move(1));
        session.SetSearch("osmo");
        Assert.Equal(set.Cards[2].CardId, session.SelectedCardId);
        Assert.True(session.Window().FrontVisible);
    }

    [Fact]
    public void ReloadSameSetKeepsStableFocusedIdentityAndClearsDeletedSelection()
    {
        CardSet set = Deck();
        var session = new CardNavigationSession();
        session.Load(set);
        Assert.True(session.Move(1));
        Guid focused = session.SelectedCardId!.Value;
        var amended = CardSetOperations.RenameSet(set, "Biology", set.Revision, CardInteractionMode.Edit);
        session.Load(amended);
        Assert.Equal(focused, session.SelectedCardId);
        var preview = CardSetOperations.PreviewDelete(amended, [focused]);
        var deleted = CardSetOperations.SoftDelete(amended, preview, CardInteractionMode.Edit);
        session.Load(deleted);
        Assert.NotEqual(focused, session.SelectedCardId);
        Assert.Equal(2, session.Window().TotalCount);
        Assert.True(session.Window().FrontVisible);
    }

    [Fact]
    public void DifferentSetClearsSelectionModeFlipAndGrouping()
    {
        CardSet one = Deck();
        var session = new CardNavigationSession();
        session.Load(one);
        session.SetMode(CardInteractionMode.Edit);
        session.SetGrouping(CardGroupingKind.Topic);
        Assert.True(session.Flip());
        CardSet two = CardSetOperations.Create("owner:personal", "Empty new set");
        session.Load(two);
        Assert.Equal(CardInteractionMode.View, session.Mode);
        Assert.Null(session.SelectedCardId);
        Assert.Null(session.Window().Current);
        Assert.True(session.FrontVisible);
    }

    [Fact]
    public void InvalidPreferencesAreRejectedWithoutChangingPreviousState()
    {
        var session = new CardNavigationSession();
        CardNavigationDirection original = session.Preferences.Navigation;
        Assert.Equal(CardFailureCode.InvalidState,
            Assert.Throws<CardOperationException>(() => session.SetDirection((CardNavigationDirection)999)).Code);
        Assert.Equal(original, session.Preferences.Navigation);
        Assert.Equal(CardFailureCode.InvalidState,
            Assert.Throws<CardOperationException>(() => session.SetGrouping(CardGroupingKind.Custom)).Code);
    }

    [Fact]
    public void NavigationNeverMutatesSharedCardRevisionsOrReviewRecords()
    {
        CardSet set = Deck();
        string before = CardSetOperations.ExportJson(set);
        var session = new CardNavigationSession();
        session.Load(set);
        session.Flip();
        session.Move(1);
        session.SetGrouping(CardGroupingKind.Subject);
        session.SetSearch("mitosis");
        session.SetMode(CardInteractionMode.Edit);
        Assert.Equal(before, CardSetOperations.ExportJson(set));
    }
}
