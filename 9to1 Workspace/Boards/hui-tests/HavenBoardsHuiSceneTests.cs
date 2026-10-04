using System.Text;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Haven.UI;
using Haven.UI.Components;
using Xunit;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace CakeOS.Apps.Boards.Hui.Tests;

public sealed class HavenBoardsHuiSceneTests
{
    [Fact]
    public void SetSnapshot_builds_accessible_lanes_cards_and_disabled_states()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        Assert.Equal("Haven Boards", scene.BoardTitle.Content);
        Assert.Equal(3, scene.BoardLanes.Children.OfType<Container>().Count());

        var firstLane = Assert.IsType<Container>(scene.BoardLanes.Children[0]);
        Assert.Equal("Board group To do", firstLane.Accessibility.AccessibleName);

        var firstCard = scene.Root.DescendantsAndSelf()
            .Single(element => element.Name == "BoardCard_card1");
        Assert.Equal("Board card First task", firstCard.Accessibility.AccessibleName);

        var moveUp = FindButton(scene, "Move First task up");
        Assert.False(moveUp.GetValue(HavenProperties.Enabled));
        Assert.False(moveUp.Accessibility.Enabled);
        Assert.True(moveUp.State.HasFlag(HavenElementState.Disabled));
    }

    [Fact]
    public void Enabled_keyboard_move_emits_typed_neutral_command()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var moveNext = FindButton(scene, "Move First task to Doing");
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);

        Assert.True(moveNext.KeyDown(enter));
        Assert.True(moveNext.KeyUp(enter));

        var command = Assert.IsType<MoveCardCommand>(requested);
        Assert.Equal("todo", command.FromGroupId);
        Assert.Equal(0, command.FromIndex);
        Assert.Equal("doing", command.ToGroupId);
        Assert.Equal(1, command.ToIndex);
    }

    [Fact]
    public void Disabled_keyboard_move_cannot_emit_command()
    {
        using var scene = new HavenBoardsHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var moveUp = FindButton(scene, "Move First task up");
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);

        Assert.True(moveUp.KeyDown(enter));
        Assert.True(moveUp.KeyUp(enter));
        Assert.Null(requested);
    }

    [Fact]
    public void Status_visibility_tracks_empty_and_non_empty_messages()
    {
        using var scene = new HavenBoardsHuiScene();

        scene.SetStatus("Saved locally");
        Assert.Equal("Saved locally", scene.Status.Content);
        Assert.Equal(HavenVisibility.Visible, scene.Status.GetValue(HavenProperties.Visibility));

        scene.SetStatus(null);
        Assert.Equal(string.Empty, scene.Status.Content);
        Assert.Equal(HavenVisibility.Collapsed, scene.Status.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public async Task Open_execute_dispose_reopen_preserves_durable_snapshot_and_scene()
    {
        var root = TempBoardDirectory();
        try
        {
            HavenBoardSnapshot expected;
            using (var firstStore = new JsonFileHavenBoardStore(root))
            {
                await using var first = await HavenBoardsHuiSession.OpenAsync(firstStore);
                await first.ExecuteAsync(new CreateCardCommand("todo", "offline-card", "Offline card"));
                await first.ExecuteAsync(new MoveCardCommand("todo", 1, "doing", 1));
                expected = first.Snapshot;
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);

            Assert.Equal(expected.Version, reopened.Snapshot.Version);
            Assert.Equal(expected.Groups.Select(group => group.Id), reopened.Snapshot.Groups.Select(group => group.Id));
            Assert.Equal(
                expected.Groups.SelectMany(group => group.Cards).Select(card => (group: GroupFor(expected, card.Id), card.Id, card.Title)),
                reopened.Snapshot.Groups.SelectMany(group => group.Cards).Select(card => (group: GroupFor(reopened.Snapshot, card.Id), card.Id, card.Title)));

            var card = reopened.Scene.Root.DescendantsAndSelf()
                .Single(element => element.Name == "BoardCard_offlinecard");
            Assert.Equal("Board card Offline card", card.Accessibility.AccessibleName);
            Assert.Equal("Loaded locally", reopened.Scene.Status.Content);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Keyboard_scene_command_flushes_to_disk_and_survives_reopen()
    {
        var root = TempBoardDirectory();
        try
        {
            using (var firstStore = new JsonFileHavenBoardStore(root))
            {
                await using var first = await HavenBoardsHuiSession.OpenAsync(firstStore);
                var moveNext = FindButton(first.Scene, "Move First task to Doing");
                var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);

                Assert.True(moveNext.KeyDown(enter));
                Assert.True(moveNext.KeyUp(enter));
                await first.FlushAsync();

                Assert.DoesNotContain(first.Snapshot.Groups[0].Cards, card => card.Id == "card-1");
                Assert.Equal(new[] { "card-2", "card-1" }, first.Snapshot.Groups[1].Cards.Select(card => card.Id));
                Assert.Equal("Saved locally", first.Scene.Status.Content);
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);
            Assert.DoesNotContain(reopened.Snapshot.Groups[0].Cards, card => card.Id == "card-1");
            Assert.Equal(new[] { "card-2", "card-1" }, reopened.Snapshot.Groups[1].Cards.Select(card => card.Id));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Attachment_blob_metadata_and_hui_count_survive_reopen()
    {
        var root = TempBoardDirectory();
        var boardRoot = Path.Combine(root, "boards");
        var attachmentRoot = Path.Combine(root, "attachments");
        var attachmentStore = new ContentAddressedHavenBoardAttachmentStore(attachmentRoot);
        var expectedBytes = Encoding.UTF8.GetBytes("Haven Boards local attachment");

        try
        {
            HavenBoardAttachment attachment;
            using (var firstStore = new JsonFileHavenBoardStore(boardRoot))
            {
                await using var first = await HavenBoardsHuiSession.OpenAsync(firstStore);
                await using var input = new MemoryStream(expectedBytes);
                attachment = await attachmentStore.ImportAsync(
                    "board-main",
                    "../brief.txt",
                    input,
                    "att-local");

                await first.ExecuteAsync(new AddAttachmentCommand("card-1", attachment));
                var firstCard = first.Scene.Root.DescendantsAndSelf()
                    .Single(element => element.Name == "BoardCard_card1");
                Assert.Contains(
                    firstCard.DescendantsAndSelf().OfType<HavenText>(),
                    text => text.Content == "1 attachment");
            }

            using var reopenedStore = new JsonFileHavenBoardStore(boardRoot);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);
            var reopenedCard = reopened.Snapshot.Groups
                .SelectMany(group => group.Cards)
                .Single(card => card.Id == "card-1");
            var reopenedAttachment = Assert.Single(reopenedCard.Attachments!);
            Assert.Equal(attachment, reopenedAttachment);

            var sceneCard = reopened.Scene.Root.DescendantsAndSelf()
                .Single(element => element.Name == "BoardCard_card1");
            Assert.Contains(
                sceneCard.DescendantsAndSelf().OfType<HavenText>(),
                text => text.Content == "1 attachment");

            await using var opened = Assert.IsAssignableFrom<Stream>(
                await attachmentStore.OpenReadAsync("board-main", reopenedAttachment));
            using var copy = new MemoryStream();
            await opened.CopyToAsync(copy);
            Assert.Equal(expectedBytes, copy.ToArray());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static HavenButton FindButton(HavenBoardsHuiScene scene, string accessibleName) =>
        scene.Root.DescendantsAndSelf()
            .OfType<HavenButton>()
            .Single(button => button.Accessibility.AccessibleName == accessibleName);

    private static string GroupFor(HavenBoardSnapshot snapshot, string cardId) =>
        snapshot.Groups.Single(group => group.Cards.Any(card => card.Id == cardId)).Id;

    private static string TempBoardDirectory() =>
        Path.Combine(Path.GetTempPath(), "cakeos-boards-hui-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
