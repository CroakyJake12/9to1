using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class HavenBoardGenerativePlanTests
{
    [Fact]
    public void Planner_allows_bounded_structural_commands_and_builds_preview()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var plan = HavenBoardGenerativePlanner.CreatePlan(
            snapshot,
            [
                new CreateCardCommand("todo", "generated_card", "Generated card"),
                new SetFreeformCardFrameCommand("generated_card", 120, 80, 300, 180, 4)
            ]);

        Assert.Equal(snapshot.Version, plan.BaseVersion);
        Assert.Equal(snapshot.Version + 2, plan.Preview.Version);
        Assert.Contains(
            plan.Preview.Groups.SelectMany(group => group.Cards),
            card => card.Id == "generated_card" && card.Title == "Generated card");
        Assert.Contains(
            plan.Preview.Freeform!.Items,
            item => item.CardId == "generated_card" && item.X == 120 && item.Y == 80);
    }

    [Fact]
    public void Planner_rejects_attachment_mutation_and_generated_field_abuse()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var attachment = new HavenBoardAttachment(
            "generated-attachment",
            "unsafe.txt",
            "sha256:" + new string('a', 64));

        Assert.Throws<InvalidOperationException>(() => HavenBoardGenerativePlanner.CreatePlan(
            snapshot,
            [new AddAttachmentCommand("card-1", attachment)]));
        Assert.Throws<InvalidOperationException>(() => HavenBoardGenerativePlanner.CreatePlan(
            snapshot,
            [new CreateCardCommand("todo", "../escape", "Generated")]));
        Assert.Throws<InvalidOperationException>(() => HavenBoardGenerativePlanner.CreatePlan(
            snapshot,
            [new CreateCardCommand(
                "todo",
                "safe-id",
                new string('x', HavenBoardGenerativePlanner.MaxGeneratedTitleLength + 1))]));
    }

    [Fact]
    public void Planner_rejects_empty_and_oversized_batches()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        Assert.Throws<ArgumentException>(() =>
            HavenBoardGenerativePlanner.CreatePlan(snapshot, Array.Empty<HavenBoardCommand>()));

        var oversized = Enumerable.Range(0, HavenBoardGenerativePlanner.MaxCommandsPerPlan + 1)
            .Select(_ => (HavenBoardCommand)new MoveGroupCommand(0, 0))
            .ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            HavenBoardGenerativePlanner.CreatePlan(snapshot, oversized));
    }

    [Fact]
    public void Prepared_command_collection_cannot_be_replaced_after_review()
    {
        var plan = HavenBoardGenerativePlanner.CreatePlan(
            HavenBoardSnapshot.CreateDefault(),
            [new CreateCardCommand("todo", "generated", "Generated")]);

        Assert.False(plan.Commands is HavenBoardCommand[]);
        var list = Assert.IsAssignableFrom<IList<HavenBoardCommand>>(plan.Commands);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            list[0] = new RenameGroupCommand("todo", "Tampered"));
    }
}
