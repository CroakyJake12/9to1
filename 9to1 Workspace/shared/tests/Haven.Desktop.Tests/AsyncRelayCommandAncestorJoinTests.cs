using Haven.Desktop.ViewModels;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class AsyncRelayCommandAncestorJoinTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_nested_Raise_child_refuses_live_ancestor_join_but_retired_chain_allows_it(bool parentAwaitsChild)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? child = null;
        AsyncRelayCommand? command = null;
        var callbacks = 0;
        command = new AsyncRelayCommand(() =>
        {
            // This is real nested command RunSynchronous B inside admitted A.
            command!.RaiseCanExecuteChanged();
            return parentAwaitsChild ? child! : Task.CompletedTask;
        });
        command.CanExecuteChanged += (_, _) =>
        {
            if (++callbacks != 2) return;
            // Child inherits B's actual EC, then B returns and becomes terminal.
            child = Task.Run(async () =>
            {
                await release.Task;
                if (parentAwaitsChild)
                {
                    Assert.Throws<InvalidOperationException>(() => command.DemandExternalOriginalRetirementJoin());
                    Assert.Throws<InvalidOperationException>(() => { _ = command.CloseAndDrainAsync(); });
                    Assert.Null(command.OriginalClose);
                }
                else
                {
                    command.DemandExternalOriginalRetirementJoin();
                    var close = command.CloseAndDrainAsync();
                    Assert.Same(close, command.CloseAndDrainAsync());
                    await close;
                    Assert.True(close.IsCompletedSuccessfully);
                }
            });
        };
        var actual = command.ExecuteAsync();
        try
        {
            var actualChild = Assert.IsAssignableFrom<Task>(child);
            Assert.Same(actual, command.OriginalExecution);
            Assert.False(actualChild.IsCompleted);
            if (parentAwaitsChild) Assert.False(actual.IsCompleted);
            else { await actual; Assert.True(actual.IsCompletedSuccessfully); }
            release.SetResult();
            await actualChild;
            await actual;
            var close = command.CloseAndDrainAsync();
            await close;
            Assert.Same(close, command.OriginalClose);
            Assert.True(actual.IsCompletedSuccessfully);
        }
        finally
        {
            release.TrySetResult();
            if (child is not null) _ = await Record.ExceptionAsync(() => child);
            _ = await Record.ExceptionAsync(() => actual);
            _ = await Record.ExceptionAsync(() => command.CloseAndDrainAsync());
        }
    }
}
