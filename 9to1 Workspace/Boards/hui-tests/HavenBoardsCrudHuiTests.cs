using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Haven.UI;
using Haven.UI.Components;
using Xunit;
using HavenButton = Haven.UI.Components.Button;
using HavenInput = Haven.UI.Components.Input;

namespace CakeOS.Apps.Boards.Hui.Tests;

/// <summary>
/// Donor-parity coverage for the reconstructed group/card CRUD interactions:
/// every control emits a typed neutral command that the session persists, and
/// reopen restores the mutated board. Pointer drag remains a NEEDS-FROM-W4
/// shared primitive; these keyboard controls are the working equivalent.
/// </summary>
public sealed class HavenBoardsCrudHuiTests
{
    [Fact]
    public void Group_rename_input_commits_typed_command_with_edited_text()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var input = FindInput(scene, "Rename group To do");
        input.Text = "Next up";
        PressEnter(FindButton(scene, "Save group name To do"));

        var command = Assert.IsType<RenameGroupCommand>(requested);
        Assert.Equal("todo", command.GroupId);
        Assert.Equal("Next up", command.Title);
    }

    [Fact]
    public void Group_delete_and_board_add_group_emit_typed_commands()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        var requested = new List<HavenBoardCommand>();
        scene.CommandRequested += (_, command) => requested.Add(command);

        PressEnter(FindButton(scene, "Delete group Doing"));
        PressEnter(FindButton(scene, "Add group to board"));

        var remove = Assert.IsType<RemoveGroupCommand>(Assert.Single(requested, cmd => cmd is RemoveGroupCommand));
        Assert.Equal("doing", remove.GroupId);
        var create = Assert.IsType<CreateGroupCommand>(Assert.Single(requested, cmd => cmd is CreateGroupCommand));
        Assert.False(string.IsNullOrWhiteSpace(create.GroupId));
        Assert.Equal("New group", create.Title);
    }

    [Fact]
    public void Card_rename_and_delete_emit_typed_commands_with_edited_text()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        var requested = new List<HavenBoardCommand>();
        scene.CommandRequested += (_, command) => requested.Add(command);

        var input = FindInput(scene, "Rename card First task");
        input.Text = "Clarified task";
        PressEnter(FindButton(scene, "Save card name First task"));
        PressEnter(FindButton(scene, "Delete card Try AppFlowy Board"));

        var rename = Assert.IsType<RenameCardCommand>(Assert.Single(requested, cmd => cmd is RenameCardCommand));
        Assert.Equal("card-1", rename.CardId);
        Assert.Equal("Clarified task", rename.Title);
        var remove = Assert.IsType<RemoveCardCommand>(Assert.Single(requested, cmd => cmd is RemoveCardCommand));
        Assert.Equal("card-2", remove.CardId);
    }

    [Fact]
    public void Lane_projects_derived_card_count()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        var lane = Assert.IsType<Container>(scene.BoardLanes.Children[0]);
        Assert.Contains(
            lane.DescendantsAndSelf().OfType<Text>(),
            text => text.Content == "1 card");
    }

    [Fact]
    public async Task Crud_scene_commands_flush_to_disk_and_survive_reopen()
    {
        var root = TempBoardDirectory();
        try
        {
            using (var firstStore = new JsonFileHavenBoardStore(root))
            {
                await using var first = await HavenBoardsHuiSession.OpenAsync(firstStore);

                var renameInput = FindInput(first.Scene, "Rename group To do");
                renameInput.Text = "Next up";
                PressEnter(FindButton(first.Scene, "Save group name To do"));

                PressEnter(FindButton(first.Scene, "Delete card Persist locally"));

                var addGroup = FindButton(first.Scene, "Add group to board");
                PressEnter(addGroup);
                await first.FlushAsync();

                Assert.Equal("Next up", first.Snapshot.Groups.Single(group => group.Id == "todo").Title);
                Assert.DoesNotContain(
                    first.Snapshot.Groups.SelectMany(group => group.Cards),
                    card => card.Id == "card-3");
                Assert.Equal(4, first.Snapshot.Groups.Count);
                Assert.Equal("Saved locally", first.Scene.Status.Content);
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);

            Assert.Equal("Next up", reopened.Snapshot.Groups.Single(group => group.Id == "todo").Title);
            Assert.DoesNotContain(
                reopened.Snapshot.Groups.SelectMany(group => group.Cards),
                card => card.Id == "card-3");
            Assert.Equal(4, reopened.Snapshot.Groups.Count);
            var renamedInput = FindInput(reopened.Scene, "Rename group Next up");
            Assert.Equal("Next up", renamedInput.Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Remove_group_flushes_to_disk_and_survives_reopen()
    {
        var root = TempBoardDirectory();
        try
        {
            using (var firstStore = new JsonFileHavenBoardStore(root))
            {
                await using var first = await HavenBoardsHuiSession.OpenAsync(firstStore);
                PressEnter(FindButton(first.Scene, "Delete group Done"));
                await first.FlushAsync();

                Assert.DoesNotContain(first.Snapshot.Groups, group => group.Id == "done");
                Assert.Equal(2, first.Scene.BoardLanes.Children.OfType<Container>().Count());
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);
            Assert.DoesNotContain(reopened.Snapshot.Groups, group => group.Id == "done");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void PressEnter(HavenButton button)
    {
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(button.KeyDown(enter));
        Assert.True(button.KeyUp(enter));
    }

    private static HavenButton FindButton(HavenBoardsHuiScene scene, string accessibleName) =>
        scene.Root.DescendantsAndSelf()
            .OfType<HavenButton>()
            .Single(button => button.Accessibility.AccessibleName == accessibleName);

    private static HavenInput FindInput(HavenBoardsHuiScene scene, string accessibleName) =>
        scene.Root.DescendantsAndSelf()
            .OfType<HavenInput>()
            .Single(input => input.Accessibility.AccessibleName == accessibleName);

    private static string TempBoardDirectory() =>
        Path.Combine(Path.GetTempPath(), "cakeos-boards-crud-hui-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
