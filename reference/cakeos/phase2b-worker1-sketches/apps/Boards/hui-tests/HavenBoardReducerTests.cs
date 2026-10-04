using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Contract.Tests;

public class HavenBoardReducerTests
{
    [Fact]
    public void CreateDefault_ReturnsValidSnapshot()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        
        Assert.Equal("board-main", snapshot.Id);
        Assert.Equal("Haven Boards", snapshot.Title);
        Assert.Equal(1L, snapshot.Version);
        Assert.Equal(3, snapshot.Groups.Count);
        Assert.Contains(snapshot.Groups, g => g.Id == "todo");
        Assert.Contains(snapshot.Groups, g => g.Id == "doing");
        Assert.Contains(snapshot.Groups, g => g.Id == "done");
    }

    [Fact]
    public void CreateCardCommand_AddsCardToGroup()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var command = new CreateCardCommand("todo", "new-card", "New Task");
        
        var updated = HavenBoardReducer.Apply(snapshot, command);
        
        Assert.Equal(2L, updated.Version);
        var todoGroup = updated.Groups.First(g => g.Id == "todo");
        Assert.Contains(todoGroup.Cards, c => c.Id == "new-card" && c.Title == "New Task");
    }

    [Fact]
    public void MoveCardCommand_MovesCardBetweenGroups()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var command = new MoveCardCommand("todo", 0, "doing", 0);
        
        var updated = HavenBoardReducer.Apply(snapshot, command);
        
        Assert.Equal(2L, updated.Version);
        var todoGroup = updated.Groups.First(g => g.Id == "todo");
        var doingGroup = updated.Groups.First(g => g.Id == "doing");
        Assert.DoesNotContain(todoGroup.Cards, c => c.Id == "card-1");
        Assert.Contains(doingGroup.Cards, c => c.Id == "card-1");
    }

    [Fact]
    public void MoveGroupCommand_ReordersGroups()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var command = new MoveGroupCommand(0, 2);
        
        var updated = HavenBoardReducer.Apply(snapshot, command);
        
        Assert.Equal(2L, updated.Version);
        Assert.Equal("doing", updated.Groups[0].Id);
        Assert.Equal("done", updated.Groups[1].Id);
        Assert.Equal("todo", updated.Groups[2].Id);
    }

    [Fact]
    public void RenameGroupCommand_UpdatesGroupTitle()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var command = new RenameGroupCommand("todo", "Backlog");
        
        var updated = HavenBoardReducer.Apply(snapshot, command);
        
        Assert.Equal(2L, updated.Version);
        var group = updated.Groups.First(g => g.Id == "todo");
        Assert.Equal("Backlog", group.Title);
    }

    [Fact]
    public void AddAttachmentCommand_AddsAttachmentToCard()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var attachment = new HavenBoardAttachment("att-1", "image.png", "/path/to/image.png");
        var command = new AddAttachmentCommand("card-1", attachment);
        
        var updated = HavenBoardReducer.Apply(snapshot, command);
        
        Assert.Equal(2L, updated.Version);
        var card = updated.Groups.SelectMany(g => g.Cards).First(c => c.Id == "card-1");
        Assert.Contains(card.Attachments ?? [], a => a.Id == "att-1");
    }

    [Fact]
    public void SetFreeformCardFrameCommand_SetsPosition()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var command = new SetFreeformCardFrameCommand("card-1", 100, 200, 300, 200, 5);
        
        var updated = HavenBoardReducer.Apply(snapshot, command);
        
        Assert.Equal(2L, updated.Version);
        Assert.NotNull(updated.Freeform);
        var item = updated.Freeform!.Items.First(i => i.CardId == "card-1");
        Assert.Equal(100, item.X);
        Assert.Equal(200, item.Y);
        Assert.Equal(300, item.Width);
        Assert.Equal(200, item.Height);
        Assert.Equal(5, item.ZIndex);
    }

    [Fact]
    public void Validate_RejectsDuplicateCardIds()
    {
        var snapshot = new HavenBoardSnapshot(
            Id: "test",
            Title: "Test",
            Version: 1,
            Groups:
            [
                new HavenBoardGroup("g1", "Group 1", [new HavenBoardCard("card-1", "Card 1")]),
                new HavenBoardGroup("g2", "Group 2", [new HavenBoardCard("card-1", "Card 1 Duplicate")])
            ]);

        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Validate(snapshot));
    }

    [Fact]
    public void Validate_RejectsCyclicParentChain()
    {
        var snapshot = new HavenBoardSnapshot(
            Id: "test",
            Title: "Test",
            Version: 1,
            Groups:
            [
                new HavenBoardGroup("g1", "Group 1", 
                    [new HavenBoardCard("card-1", "Card 1", ParentCardId: "card-2"),
                     new HavenBoardCard("card-2", "Card 2", ParentCardId: "card-1")])
            ]);

        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Validate(snapshot));
    }
}