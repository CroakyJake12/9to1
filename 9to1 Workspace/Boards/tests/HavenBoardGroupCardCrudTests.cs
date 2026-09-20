using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

/// <summary>
/// Donor-parity coverage for the AppFlowy Board controller CRUD surface that the
/// neutral contract previously lacked: group add/insert/remove, card remove and
/// title update, and group rename reachability. Every behaviour maps to a donor
/// controller operation (addGroup/insertGroup/removeGroup, removeGroupItem,
/// updateGroupItem/replaceOrInsertItem, updateGroupName).
/// </summary>
public sealed class HavenBoardGroupCardCrudTests
{
    [Fact]
    public void Create_group_appends_with_stable_id_and_bumps_version()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var updated = HavenBoardReducer.Apply(snapshot, new CreateGroupCommand("review", "Review"));

        Assert.Equal(4, updated.Groups.Count);
        Assert.Equal("review", updated.Groups[3].Id);
        Assert.Equal("Review", updated.Groups[3].Title);
        Assert.Empty(updated.Groups[3].Cards);
        Assert.Equal(snapshot.Version + 1, updated.Version);
    }

    [Fact]
    public void Create_group_supports_donor_insert_index_and_clamps_edges()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var inserted = HavenBoardReducer.Apply(snapshot, new CreateGroupCommand("urgent", "Urgent", ToIndex: 1));
        Assert.Equal(new[] { "todo", "urgent", "doing", "done" }, inserted.Groups.Select(group => group.Id));

        var clamped = HavenBoardReducer.Apply(snapshot, new CreateGroupCommand("later", "Later", ToIndex: 99));
        Assert.Equal("later", clamped.Groups[^1].Id);
    }

    [Fact]
    public void Create_group_rejects_duplicate_blank_and_oversized_ids()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(snapshot, new CreateGroupCommand("todo", "Duplicate")));
        Assert.Contains("already exists", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(snapshot, new CreateGroupCommand("   ", "Blank")));
        Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(snapshot, new CreateGroupCommand(new string('g', 129), "Long")));
    }

    [Fact]
    public void Create_group_normalises_blank_title()
    {
        var updated = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new CreateGroupCommand("untitled", "   "));

        Assert.Equal("Untitled group", updated.Groups.Single(group => group.Id == "untitled").Title);
    }

    [Fact]
    public void Remove_group_drops_cards_frames_and_orphans_external_children()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        snapshot = HavenBoardReducer.Apply(snapshot, new SetCardParentCommand("card-2", "card-1"));
        snapshot = HavenBoardReducer.Apply(snapshot, new SetFreeformCardFrameCommand("card-1", 10, 20));

        var updated = HavenBoardReducer.Apply(snapshot, new RemoveGroupCommand("todo"));

        Assert.DoesNotContain(updated.Groups, group => group.Id == "todo");
        Assert.DoesNotContain(
            updated.Groups.SelectMany(group => group.Cards),
            card => card.Id == "card-1");
        Assert.Empty(updated.Freeform?.Items ?? []);
        var orphaned = updated.Groups.SelectMany(group => group.Cards).Single(card => card.Id == "card-2");
        Assert.Null(orphaned.ParentCardId);
        HavenBoardReducer.Validate(updated);
    }

    [Fact]
    public void Remove_group_rejects_unknown_id()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(HavenBoardSnapshot.CreateDefault(), new RemoveGroupCommand("missing")));

        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Remove_card_drops_frame_and_orphans_children_without_touching_siblings()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        snapshot = HavenBoardReducer.Apply(snapshot, new SetCardParentCommand("card-2", "card-1"));
        snapshot = HavenBoardReducer.Apply(snapshot, new SetFreeformCardFrameCommand("card-1", 10, 20));
        snapshot = HavenBoardReducer.Apply(snapshot, new SetFreeformCardFrameCommand("card-2", 40, 50));

        var updated = HavenBoardReducer.Apply(snapshot, new RemoveCardCommand("card-1"));

        Assert.DoesNotContain(
            updated.Groups.SelectMany(group => group.Cards),
            card => card.Id == "card-1");
        var orphaned = updated.Groups.SelectMany(group => group.Cards).Single(card => card.Id == "card-2");
        Assert.Null(orphaned.ParentCardId);
        Assert.Equal(
            [new HavenBoardFreeformItem("card-2", 40, 50)],
            updated.Freeform!.Items);
        HavenBoardReducer.Validate(updated);
    }

    [Fact]
    public void Remove_card_rejects_unknown_id()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(HavenBoardSnapshot.CreateDefault(), new RemoveCardCommand("missing")));

        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rename_card_updates_title_and_normalises_blank()
    {
        var renamed = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new RenameCardCommand("card-1", "  Clarified task  "));
        Assert.Equal(
            "Clarified task",
            renamed.Groups.SelectMany(group => group.Cards).Single(card => card.Id == "card-1").Title);

        var blank = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new RenameCardCommand("card-1", "   "));
        Assert.Equal(
            "Untitled card",
            blank.Groups.SelectMany(group => group.Cards).Single(card => card.Id == "card-1").Title);
    }

    [Fact]
    public void Rename_card_rejects_unknown_id()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(HavenBoardSnapshot.CreateDefault(), new RenameCardCommand("missing", "Name")));

        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Event_adapter_maps_donor_crud_shapes_to_typed_commands()
    {
        Assert.Equal(
            new CreateGroupCommand("review", "Review", 1),
            AppFlowyBoardEventAdapter.CreateGroup("review", "Review", 1));
        Assert.Equal(
            new RemoveGroupCommand("todo"),
            AppFlowyBoardEventAdapter.RemoveGroup("todo"));
        Assert.Equal(
            new RenameGroupCommand("todo", "Next up"),
            AppFlowyBoardEventAdapter.RenameGroup("todo", "Next up"));
        Assert.Equal(
            new CreateCardCommand("todo", "card-9", "Added"),
            AppFlowyBoardEventAdapter.AddCard("todo", "card-9", "Added"));
        Assert.Equal(
            new RemoveCardCommand("card-9"),
            AppFlowyBoardEventAdapter.RemoveCard("card-9"));
        Assert.Equal(
            new RenameCardCommand("card-9", "Edited"),
            AppFlowyBoardEventAdapter.RenameCard("card-9", "Edited"));
    }

    [Fact]
    public void Structural_collaboration_boundary_accepts_group_and_card_crud()
    {
        var batch = HavenBoardCollaborationBatch.Create(
            "board-main",
            3,
            "owner",
            [
                new CreateGroupCommand("review", "Review"),
                new RenameGroupCommand("review", "Needs review"),
                new RenameCardCommand("card-1", "Edited"),
                new RemoveCardCommand("card-3"),
                new RemoveGroupCommand("done")
            ]);

        Assert.Equal(5, batch.Commands.Count);
    }

    [Fact]
    public void Generative_planner_accepts_group_and_card_crud_with_preview()
    {
        var plan = HavenBoardGenerativePlanner.CreatePlan(
            HavenBoardSnapshot.CreateDefault(),
            [
                new CreateGroupCommand("review", "Review"),
                new RenameCardCommand("card-1", "Edited"),
                new RemoveCardCommand("card-3")
            ]);

        Assert.Equal(
            new[] { "card-1", "card-2" },
            plan.Preview.Groups.SelectMany(group => group.Cards).Select(card => card.Id).OrderBy(id => id));
        Assert.Contains(plan.Preview.Groups, group => group.Id == "review");
        Assert.Equal(
            "Edited",
            plan.Preview.Groups.SelectMany(group => group.Cards).Single(card => card.Id == "card-1").Title);
    }

    [Fact]
    public void Generative_planner_rejects_generated_group_id_abuse()
    {
        Assert.Throws<InvalidOperationException>(() => HavenBoardGenerativePlanner.CreatePlan(
            HavenBoardSnapshot.CreateDefault(),
            [new CreateGroupCommand("../outside", "Bad")]));
    }
}
