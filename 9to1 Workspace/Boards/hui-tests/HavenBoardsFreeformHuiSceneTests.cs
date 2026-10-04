using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Haven.UI;
using Haven.UI.Components;
using Xunit;
using HavenButton = Haven.UI.Components.Button;

namespace CakeOS.Apps.Boards.Hui.Tests;

public sealed class HavenBoardsFreeformHuiSceneTests
{
    [Fact]
    public void Explicit_freeform_frame_is_projected_to_real_hui_canvas_geometry()
    {
        var snapshot = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new SetFreeformCardFrameCommand("card-2", 180, 260, 340, 190, 9));

        using var scene = new HavenBoardsFreeformHuiScene();
        scene.SetSnapshot(snapshot);

        Assert.Equal(HavenLayout.Canvas, scene.Surface.Layout);
        var card = FindCard(scene, "card-2");
        Assert.Equal(HavenLength.Px(180), card.GetValue(HavenProperties.Left));
        Assert.Equal(HavenLength.Px(260), card.GetValue(HavenProperties.Top));
        Assert.Equal(HavenLength.Px(340), card.GetValue(HavenProperties.Width));
        Assert.Equal(HavenLength.Px(190), card.GetValue(HavenProperties.Height));
        Assert.Equal(9, card.GetValue(HavenProperties.ZIndex));
        Assert.Equal("Freeform board card Try AppFlowy Board", card.Accessibility.AccessibleName);
    }

    [Fact]
    public void Keyboard_nudge_emits_typed_neutral_frame_command()
    {
        var snapshot = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new SetFreeformCardFrameCommand("card-1", 100, 120, 300, 170, 4));

        using var scene = new HavenBoardsFreeformHuiScene();
        scene.SetSnapshot(snapshot);

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var moveRight = FindButton(scene, "Move First task right on freeform board");
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(moveRight.KeyDown(enter));
        Assert.True(moveRight.KeyUp(enter));

        var command = Assert.IsType<SetFreeformCardFrameCommand>(requested);
        Assert.Equal("card-1", command.CardId);
        Assert.Equal(124, command.X);
        Assert.Equal(120, command.Y);
        Assert.Equal(300, command.Width);
        Assert.Equal(170, command.Height);
        Assert.Equal(4, command.ZIndex);
    }

    [Fact]
    public void Nudge_from_deterministic_default_position_creates_persistable_frame()
    {
        using var scene = new HavenBoardsFreeformHuiScene();
        scene.SetSnapshot(HavenBoardSnapshot.CreateDefault());

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var moveRight = FindButton(scene, "Move First task right on freeform board");
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(moveRight.KeyDown(enter));
        Assert.True(moveRight.KeyUp(enter));

        var command = Assert.IsType<SetFreeformCardFrameCommand>(requested);
        Assert.Equal(48, command.X);
        Assert.Equal(24, command.Y);
        Assert.Equal(280, command.Width);
        Assert.Equal(160, command.Height);
    }

    [Fact]
    public void Nudge_beyond_coordinate_limit_is_disabled_for_keyboard_and_accessibility()
    {
        var snapshot = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new SetFreeformCardFrameCommand(
                "card-1",
                HavenBoardReducer.FreeformCoordinateLimit,
                0));

        using var scene = new HavenBoardsFreeformHuiScene();
        scene.SetSnapshot(snapshot);

        HavenBoardCommand? requested = null;
        scene.CommandRequested += (_, command) => requested = command;

        var moveRight = FindButton(scene, "Move First task right on freeform board");
        Assert.False(moveRight.GetValue(HavenProperties.Enabled));
        Assert.False(moveRight.Accessibility.Enabled);
        Assert.True(moveRight.State.HasFlag(HavenElementState.Disabled));

        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(moveRight.KeyDown(enter));
        Assert.True(moveRight.KeyUp(enter));
        Assert.Null(requested);
    }

    [Fact]
    public async Task Freeform_keyboard_nudge_flushes_to_disk_and_survives_reopen()
    {
        var root = TempBoardDirectory();
        try
        {
            using (var firstStore = new JsonFileHavenBoardStore(root))
            {
                await using var first = await HavenBoardsHuiSession.OpenAsync(firstStore);
                await first.ExecuteAsync(new SetFreeformCardFrameCommand("card-2", 200, 240, 320, 180, 3));

                var moveDown = FindButton(first.FreeformScene, "Move Try AppFlowy Board down on freeform board");
                var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
                Assert.True(moveDown.KeyDown(enter));
                Assert.True(moveDown.KeyUp(enter));
                await first.FlushAsync();

                var frame = Assert.Single(first.Snapshot.Freeform!.Items);
                Assert.Equal(new HavenBoardFreeformItem("card-2", 200, 264, 320, 180, 3), frame);
                Assert.Equal(HavenLength.Px(264), FindCard(first.FreeformScene, "card-2").GetValue(HavenProperties.Top));
                Assert.Equal("Saved locally", first.FreeformScene.Status.Content);
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);
            var reopenedFrame = Assert.Single(reopened.Snapshot.Freeform!.Items);
            Assert.Equal(new HavenBoardFreeformItem("card-2", 200, 264, 320, 180, 3), reopenedFrame);
            Assert.Equal(HavenLength.Px(200), FindCard(reopened.FreeformScene, "card-2").GetValue(HavenProperties.Left));
            Assert.Equal(HavenLength.Px(264), FindCard(reopened.FreeformScene, "card-2").GetValue(HavenProperties.Top));
            Assert.Equal("Loaded locally", reopened.FreeformScene.Status.Content);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static Container FindCard(HavenBoardsFreeformHuiScene scene, string cardId) =>
        Assert.IsType<Container>(scene.Root.DescendantsAndSelf()
            .Single(element => element.Name == "FreeformCard_" + SafeName(cardId)));

    private static HavenButton FindButton(HavenBoardsFreeformHuiScene scene, string accessibleName) =>
        scene.Root.DescendantsAndSelf()
            .OfType<HavenButton>()
            .Single(button => button.Accessibility.AccessibleName == accessibleName);

    private static string SafeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());

    private static string TempBoardDirectory() =>
        Path.Combine(Path.GetTempPath(), "cakeos-boards-freeform-hui-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
