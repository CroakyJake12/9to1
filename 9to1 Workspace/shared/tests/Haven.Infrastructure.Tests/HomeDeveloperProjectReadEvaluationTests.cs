using Haven.Application;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class HomeDeveloperProjectReadAdmissionTests
{
    [Fact] public Task Held_public_resource_evaluation_is_owned_before_retirement_and_conserves_its_late_faults() => Run(async rig =>
    {
        var read = await rig.Accept(); var actor = await rig.Own(rig.Profiles.GetCurrentAsync(default)); Assert.NotNull(actor);
        var scope = Assert.Single(rig.Selections.GetOriginalReadScopes(rig.Selections.Original));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Selections.OriginalValidation = held.Task; rig.Selections.OnValidate = () => entered.TrySetResult();
        var first = new OperationCanceledException("actual resource source validation Task is faulted"); var second = new IOException("late original evaluation fault");
        var evaluation = rig.Own(rig.Reads.EvaluateAsync(actor!, HomeDeveloperProjectReadAdmissionSource.ReadAction, scope, default).AsTask()); Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(evaluation.IsCompleted);
            rig.Reads.RequestOriginalReadRetirement(); close = rig.Own(rig.Reads.CloseAndDrainOriginalReadsAsync()); Assert.False(close.IsCompleted);
        }
        finally
        {
            held.TrySetException([first, second]); rig.Expected.Add(evaluation); rig.ExpectedClose = true;
            try { await evaluation; } catch { _ = evaluation.Exception; }
            if (close is not null)
            {
                var error = await Assert.ThrowsAsync<AggregateException>(() => close); rig.Expected.Add(close);
                Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error));
            }
        }
        Assert.True(evaluation.IsFaulted); Assert.NotNull(close); Assert.True(close.IsFaulted); Assert.Equal(0, rig.FiniteReads);
        var refused = await rig.Own(rig.Reads.EvaluateAsync(actor!, HomeDeveloperProjectReadAdmissionSource.ReadAction, scope, default).AsTask()); Assert.False(refused.Allowed);
    });
}
