using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real Home/profile/CAS and original ACK custody, with an exact-reference
/// synthetic outcome dependency. These controls distinguish external dependency
/// preflight from an actual lazy source callback or live original; no effect grant.</summary>
public sealed partial class HomeDeveloperProjectSetupJournalTests
{
    [Fact]
    public async Task External_completion_dependency_preflight_preserves_same_partial_ack_and_does_not_replay()
    {
        await using var rig = await Rig.Create();
        var issuer = new JoinGuardOutcomes();
        var factoryCalls = 0;
        var owner = new HomeDeveloperProjectSetupJournal(rig.Store, rig.Profiles, rig.Captures,
            () => { factoryCalls++; return issuer; });
        rig.Owners.Add(owner);
        issuer.BeforeJoin = owner.DemandExternalOriginalSetupCompletionJoin;
        var prepared = await rig.Prepare(owner);
        var step = prepared.Intent.Steps[0];
        var admission = await owner.AdmitOriginalStepAsync(prepared, step);
        var actual = Task.FromResult(new object());
        var result = await actual;
        issuer.Outcomes.Issue(prepared, rig.Source, step, actual, result);
        var acknowledged = await owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actual, result);
        var before = File.ReadAllBytes(Path.Combine(rig.Directory, "home.json"));
        var writes = rig.Store.SetupWrites;
        var factoriesBefore = factoryCalls;

        // The real Files -> permission -> same journal completion dependency revisits
        // this local fence during external preflight. It is not a source operation.
        owner.DemandExternalOriginalRetirementJoin();
        owner.DemandExternalOriginalRetirementJoin();

        Assert.Equal(factoriesBefore + 2, factoryCalls);
        Assert.Equal(2, issuer.JoinCalls);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(rig.Directory, "home.json")));
        Assert.Equal(writes, rig.Store.SetupWrites);
        var observed = await owner.ReadCheckpointAsync(rig.SetupId, rig.Actor);
        Assert.Equal(JsonSerializer.Serialize(acknowledged), JsonSerializer.Serialize(observed));
        Assert.Equal(rig.SetupId, observed!.Intent.SetupId);
        Assert.Equal(prepared.Intent.WorkspaceId, observed.Intent.WorkspaceId);
        Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, observed.Observations[0].State);
        Assert.All(observed.Observations.Skip(1), value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
        Assert.Equal(0, rig.Captures.ResourceEffects);
        var close = owner.CloseAndDrainAsync();
        Assert.Same(close, owner.CloseAndDrainAsync());
        await close.WaitAsync(Bound);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(rig.Directory, "home.json")));
    }

    [Fact]
    public async Task Genuine_lazy_factory_self_join_remains_rejected_and_its_marker_unwinds()
    {
        await using var rig = await Rig.Create();
        var issuer = new JoinGuardOutcomes();
        HomeDeveloperProjectSetupJournal? owner = null;
        var attempted = false;
        var armed = false;
        owner = new HomeDeveloperProjectSetupJournal(rig.Store, rig.Profiles, rig.Captures, () =>
        {
            if (armed)
            {
                attempted = true;
                owner!.DemandExternalOriginalSetupCompletionJoin();
            }
            return issuer;
        });
        rig.Owners.Add(owner);
        var prepared = await rig.Prepare(owner);
        var before = File.ReadAllBytes(Path.Combine(rig.Directory, "home.json"));
        armed = true;

        var rejected = Assert.Throws<InvalidOperationException>(owner.DemandExternalOriginalRetirementJoin);

        Assert.True(attempted);
        Assert.Equal("An actual journal source callback cannot join its own original ACK observation.", rejected.Message);
        Assert.Equal(0, issuer.JoinCalls);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(rig.Directory, "home.json")));
        armed = false;
        owner.DemandExternalOriginalSetupCompletionJoin();
        owner.DemandExternalOriginalRetirementJoin();
        Assert.Equal(1, issuer.JoinCalls);
        Assert.All((await owner.ReadCheckpointAsync(prepared.Intent.SetupId, rig.Actor))!.Observations,
            value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Live_original_after_async_source_continuation_still_refuses_completion_self_join()
    {
        await using var rig = await Rig.Create();
        var owner = rig.Owner;
        var checkedLiveOriginal = false;
        rig.Store.BeforeSetupWrite = async (row, expected, actor, guard, token) =>
        {
            await Task.Yield();
            var rejected = Assert.Throws<InvalidOperationException>(owner.DemandExternalOriginalSetupCompletionJoin);
            Assert.Equal("A live journal original cannot join its own completion observation.", rejected.Message);
            checkedLiveOriginal = true;
            return await rig.Store.Inner.WriteGuardedAsync(row, expected, actor, guard, token);
        };

        var prepared = await rig.Prepare(owner).WaitAsync(Bound);

        Assert.True(checkedLiveOriginal);
        owner.DemandExternalOriginalSetupCompletionJoin();
        Assert.Equal(1, rig.Store.SetupWrites);
        Assert.Equal(rig.SetupId, prepared.Intent.SetupId);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    private sealed class JoinGuardOutcomes : IDeveloperProjectOriginalSetupStepOutcomeSource
    {
        internal readonly CompletionOutcomes Outcomes = new();
        internal Action? BeforeJoin;
        internal int JoinCalls;
        public bool IsIssuedOriginalStepOutcome(DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task actual, object? result) =>
            Outcomes.IsIssuedOriginalStepOutcome(intent, capture, step, actual, result);
        public Task ValidateOriginalStepOutcomeAsync(DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task actual, object? result,
            CancellationToken token) => Outcomes.ValidateOriginalStepOutcomeAsync(intent, capture, step, actual, result, token);
        public DeveloperProjectOriginalStepOutcomeObservation GetOriginalStepOutcomeObservation(DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task actual, object? result) =>
            Outcomes.GetOriginalStepOutcomeObservation(intent, capture, step, actual, result);
        public void DemandExternalOriginalSetupStepOutcomeJoin()
        {
            JoinCalls++;
            BeforeJoin?.Invoke();
        }
    }
}
