using HavenOS.Apps.Assistants.Canonical;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

public sealed class AssistantPresentationOriginalsTests
{
    [Fact]
    public async Task Mixed_raw_refusal_and_io_failure_cannot_be_hidden_by_successful_command()
    {
        var scope = new AssistantPresentationOriginals();
        var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refusal = new DenException(DenErrorCode.Conflict, "expected CAS refusal");
        var io = new IOException("actual source failure");
        var command = scope.Admit(async () =>
        {
            try { await scope.Source(() => raw.Task); } catch (DenException) { }
            return true;
        });
        raw.SetException([refusal, io]);
        Assert.True(await command);
        var originalClose = scope.CloseAndDrainAsync();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => originalClose);
        Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, io));
        Assert.Same(originalClose, scope.CloseAndDrainAsync());
        Assert.Throws<ObjectDisposedException>(() => { _ = scope.Admit(() => Task.FromResult(true)); });
    }

    [Fact]
    public async Task Known_den_cas_refusal_alone_does_not_poison_presentation_close()
    {
        var scope = new AssistantPresentationOriginals();
        var command = scope.Admit(() => scope.Source(() => Task.FromException<int>(new DenException(DenErrorCode.Conflict, "changed"))));
        await Assert.ThrowsAsync<DenException>(() => command);
        await scope.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Foreign_empty_aggregate_is_retained_as_unknown_original_cause()
    {
        var scope = new AssistantPresentationOriginals();
        var foreign = new AggregateException("foreign empty cause");
        var command = scope.Admit(() => scope.Source(() => Task.FromException<int>(foreign)));
        Assert.Same(foreign, await Assert.ThrowsAsync<AggregateException>(() => command));
        var failedClose = await Assert.ThrowsAsync<AggregateException>(() => scope.CloseAndDrainAsync());
        Assert.Contains(failedClose.InnerExceptions, error => ReferenceEquals(error, foreign));
    }

    [Fact]
    public async Task Retirement_joins_actual_held_source_even_if_encompassing_command_finished()
    {
        var scope = new AssistantPresentationOriginals();
        var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = scope.Admit(() => { scope.Source(() => raw.Task); return Task.FromResult(true); });
        Assert.True(await command);
        var originalClose = scope.CloseAndDrainAsync();
        Assert.False(originalClose.IsCompleted);
        raw.SetResult(42);
        await originalClose;
        Assert.Equal(42, await raw.Task);
    }

    [Fact]
    public async Task Suppressed_execution_context_cannot_bypass_actual_synchronous_source_join_guard()
    {
        var scope = new AssistantPresentationOriginals();
        await scope.Admit(() => scope.Source(() =>
        {
            using (ExecutionContext.SuppressFlow())
                Assert.Throws<InvalidOperationException>(() => { _ = scope.CloseAndDrainAsync(); });
            return Task.FromResult(true);
        }));
        await scope.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Joined_healthy_originals_do_not_exhaust_long_lived_scope()
    {
        var scope = new AssistantPresentationOriginals();
        for (var index = 0; index < 400; index++)
            Assert.Equal(index, await scope.Admit(() => scope.Source(() => Task.FromResult(index))));
        await scope.CloseAndDrainAsync();
    }
}
