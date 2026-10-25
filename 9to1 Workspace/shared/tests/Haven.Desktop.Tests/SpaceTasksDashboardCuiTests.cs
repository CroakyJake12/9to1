using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed partial class SpaceTasksDashboardOriginalWorkTests
{
    [AvaloniaFact]
    public async Task Authored_task_tiles_show_saved_status_and_completed_without_inventing_verification_or_activity()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Mount(_ => Task.CompletedTask);
        await rig.Page!.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(rig.Page.OriginalScene.Root);
        Assert.Single(rig.Page.OriginalScene.RecentRows.GetVisualDescendants().OfType<Button>());
        var initial = string.Join("\n", rig.Page.OriginalScene.RecentRows.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        Assert.Contains("Saved Running", initial, StringComparison.Ordinal);
        Assert.Contains("Original objective", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("Working", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("Verified", initial, StringComparison.Ordinal);

        rig.Tasks.Current = rig.Snapshot with { State = TaskExecutionLifecycle.Completed, PersistenceRevision = rig.Snapshot.PersistenceRevision + 1 };
        await rig.Page.RefreshOriginalAsync(TestContext.Current.CancellationToken);

        var completed = string.Join("\n", rig.Page.OriginalScene.RecentRows.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        Assert.Contains("Completed · not verified", completed, StringComparison.Ordinal);
        Assert.DoesNotContain("Verified", completed, StringComparison.Ordinal);
        Assert.Equal(rig.Snapshot.TaskId, Assert.Single(rig.Page.OriginalObservations).Snapshot!.TaskId);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authored_delegate_clears_only_the_exact_instruction_revision_after_its_actual_callback(bool newerTyping)
    {
        await using var rig = await Rig.CreateAsync();
        var entered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Mount(_ => Task.CompletedTask, instruction => { entered.TrySetResult(instruction); return accepted.Task; });
        await rig.Page!.ActivateAsync(TestContext.Current.CancellationToken);
        rig.Page.OriginalScene.Instruction.Text = "Original explicit instruction";
        Click(rig, rig.Page.OriginalScene.DelegateTask);
        try
        {
            Assert.Equal("Original explicit instruction", await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var actual = Assert.IsAssignableFrom<Task>(rig.Page.ActualNewWork);
            Assert.False(actual.IsCompleted);
            Assert.Equal("Original explicit instruction", rig.Page.OriginalScene.Instruction.Text);
            if (newerTyping) rig.Page.OriginalScene.Instruction.Text = "Newer retained instruction";

            accepted.SetResult();
            await actual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(newerTyping ? "Newer retained instruction" : "", rig.Page.OriginalScene.Instruction.Text);
            Assert.Equal(0, rig.Tasks.Writes);
        }
        finally { accepted.TrySetResult(); }
    }
}
