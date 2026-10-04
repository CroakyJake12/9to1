using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Haven.UI;
using Xunit;

namespace CakeOS.Apps.Boards.Hui.Tests;

public sealed class HavenBoardGenerativeReviewHuiTests
{
    [Fact]
    public void Review_scene_requires_active_plan_and_keyboard_apply_emits_only_plan_id()
    {
        using var scene = new HavenBoardGenerativeReviewHuiScene();
        Assert.False(scene.ApplyButton.Accessibility.Enabled);
        Assert.True(scene.ApplyButton.State.HasFlag(HavenElementState.Disabled));
        Assert.False(scene.CancelButton.Accessibility.Enabled);

        var plan = HavenBoardGenerativePlanner.CreatePlan(
            HavenBoardSnapshot.CreateDefault(),
            [new CreateCardCommand("todo", "review-card", "Review card")]);
        scene.SetPlan(plan);

        Assert.True(scene.ApplyButton.Accessibility.Enabled);
        Assert.False(scene.ApplyButton.State.HasFlag(HavenElementState.Disabled));
        Assert.Contains("1 proposed change", scene.Summary.Content, StringComparison.Ordinal);
        Assert.Contains(scene.CommandList.Children, child =>
            child.Accessibility.AccessibleName?.Contains("Create card 'Review card'", StringComparison.Ordinal) == true);

        Guid? requested = null;
        scene.ApplyRequested += (_, planId) => requested = planId;
        var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(scene.ApplyButton.KeyDown(enter));
        Assert.True(scene.ApplyButton.KeyUp(enter));
        Assert.Equal(plan.Id, requested);

        scene.ClearPlan("Cancelled");
        Assert.False(scene.ApplyButton.Accessibility.Enabled);
        Assert.True(scene.ApplyButton.State.HasFlag(HavenElementState.Disabled));
    }

    [Fact]
    public async Task Explicit_keyboard_apply_then_undo_round_trips_through_review_flow()
    {
        var root = TempBoardDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var board = await HavenBoardsHuiSession.OpenAsync(store);
            await using var review = new HavenBoardGenerativeReviewFlow(board);
            var plan = review.PreparePlan(
                [new CreateCardCommand("todo", "review-applied", "Applied from review")]);

            var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
            Assert.True(review.Scene.ApplyButton.KeyDown(enter));
            Assert.True(review.Scene.ApplyButton.KeyUp(enter));
            await review.FlushAsync();

            Assert.Null(review.ActivePlanId);
            Assert.Equal(plan.Id, review.LastAppliedPlanId);
            Assert.Contains(board.Snapshot.Groups[0].Cards, card => card.Id == "review-applied");
            Assert.Equal("Generated changes applied locally.", review.Scene.Status.Content);

            await review.UndoLastAsync();
            Assert.Null(review.LastAppliedPlanId);
            Assert.DoesNotContain(board.Snapshot.Groups[0].Cards, card => card.Id == "review-applied");
            Assert.Equal("Generated changes undone locally.", review.Scene.Status.Content);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Explicit_keyboard_cancel_discards_plan_without_board_mutation()
    {
        var root = TempBoardDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var board = await HavenBoardsHuiSession.OpenAsync(store);
            await using var review = new HavenBoardGenerativeReviewFlow(board);
            var before = board.Snapshot;
            review.PreparePlan(
                [new CreateCardCommand("todo", "review-cancelled", "Must not apply")]);

            var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
            Assert.True(review.Scene.CancelButton.KeyDown(enter));
            Assert.True(review.Scene.CancelButton.KeyUp(enter));
            await review.FlushAsync();

            Assert.Null(review.ActivePlanId);
            Assert.Equal(before.Version, board.Snapshot.Version);
            Assert.DoesNotContain(board.Snapshot.Groups[0].Cards, card => card.Id == "review-cancelled");
            Assert.Equal("Generated changes cancelled.", review.Scene.Status.Content);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Stale_review_apply_fails_closed_and_keeps_newer_user_edit()
    {
        var root = TempBoardDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var board = await HavenBoardsHuiSession.OpenAsync(store);
            await using var review = new HavenBoardGenerativeReviewFlow(board);
            review.PreparePlan(
                [new CreateCardCommand("todo", "stale-review", "Must remain unapplied")]);

            await board.ExecuteAsync(new RenameGroupCommand("todo", "User edit wins"));

            var enter = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
            Assert.True(review.Scene.ApplyButton.KeyDown(enter));
            Assert.True(review.Scene.ApplyButton.KeyUp(enter));
            await Assert.ThrowsAsync<InvalidOperationException>(() => review.FlushAsync());

            Assert.Equal("User edit wins", board.Snapshot.Groups[0].Title);
            Assert.DoesNotContain(board.Snapshot.Groups[0].Cards, card => card.Id == "stale-review");
            Assert.Equal("Generated changes could not be applied; refresh the preview.", review.Scene.Status.Content);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempBoardDirectory() =>
        Path.Combine(Path.GetTempPath(), "cakeos-boards-review-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
