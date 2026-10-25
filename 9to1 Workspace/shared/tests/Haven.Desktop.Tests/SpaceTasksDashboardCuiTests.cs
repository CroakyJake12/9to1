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
        Assert.Contains("Activity unconfirmed", initial, StringComparison.Ordinal);
        Assert.Contains("Original objective", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("Working", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("Verified", initial, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Snapshot.TaskId.ToString("D"), initial, StringComparison.Ordinal);
        Assert.DoesNotContain("original owner", initial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("custody", initial, StringComparison.OrdinalIgnoreCase);

        rig.Tasks.Current = rig.Snapshot with { State = TaskExecutionLifecycle.Completed, PersistenceRevision = rig.Snapshot.PersistenceRevision + 1 };
        await rig.Page.RefreshOriginalAsync(TestContext.Current.CancellationToken);

        var completed = string.Join("\n", rig.Page.OriginalScene.RecentRows.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        Assert.Contains("Completed · not verified", completed, StringComparison.Ordinal);
        Assert.DoesNotContain("Verified", completed, StringComparison.Ordinal);
        Assert.Equal(rig.Snapshot.TaskId, Assert.Single(rig.Page.OriginalObservations).Snapshot!.TaskId);
        Assert.Equal(0, rig.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Actual_delegate_acknowledged_at_view_retirement_joins_source_without_publishing_optional_ui()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = await Rig.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Mount(_ => Task.CompletedTask, async _ =>
        {
            entered.TrySetResult(); await accepted.Task;
            rig.Page!.RequestRetirement(); // Request only from the actual callback; never self-join.
        });
        Task? original = null;
        try
        {
            await rig.Page!.ActivateAsync(token);
            rig.Page.Scene.Instruction.Text = "accepted actual new work";
            Click(rig, rig.Page.Scene.DelegateTask);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            original = Assert.IsAssignableFrom<Task>(rig.Page.ActualNewWork);
            accepted.SetResult();
            await original.WaitAsync(TimeSpan.FromSeconds(5), token);
            var close = rig.Page.CloseAndDrainAsync();
            await close.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.True(original.IsCompletedSuccessfully); Assert.True(close.IsCompletedSuccessfully);
            Assert.Null(rig.Page.Scene.Root); Assert.Equal(0, rig.Tasks.Writes);
        }
        finally
        {
            accepted.TrySetResult();
            if (original is not null) await original.WaitAsync(TimeSpan.FromSeconds(5), token);
        }
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
        Task? actual = null;
        try
        {
            Assert.Equal("Original explicit instruction", await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            actual = Assert.IsAssignableFrom<Task>(rig.Page.ActualNewWork);
            Assert.False(actual.IsCompleted);
            Assert.Equal("Original explicit instruction", rig.Page.OriginalScene.Instruction.Text);
            if (newerTyping)
            {
                var version = rig.Page.OriginalScene.InstructionVersion;
                rig.Page.OriginalScene.Instruction.Text = "Newer retained instruction";
                Assert.True(rig.Page.OriginalScene.TryGetValue("Instruction", out var liveDraft));
                Assert.Equal("Newer retained instruction", liveDraft);
                Assert.True(rig.Page.OriginalScene.InstructionVersion > version, "The actual newer edit must advance the admitted draft version before an ACK can clear it.");
            }

            accepted.SetResult();
            await actual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(newerTyping ? "Newer retained instruction" : "", rig.Page.OriginalScene.Instruction.Text);
            Assert.Equal(0, rig.Tasks.Writes);
        }
        finally
        {
            accepted.TrySetResult();
            // An earlier assertion must still join this SAME acquired source driver
            // before Rig.Dispose retires its UI. Otherwise retirement masks the actual
            // earlier failure with a canceled optional ACK projection.
            actual ??= rig.Page.ActualNewWork;
            if (actual is not null) await actual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await rig.Page.OriginalScene.WhenActionsIdleAsync();
        }
    }
}
