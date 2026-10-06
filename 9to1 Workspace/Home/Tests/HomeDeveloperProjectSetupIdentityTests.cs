using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed partial class HomeDeveloperProjectSetupJournalTests
{
    [Fact]
    public async Task Same_privately_reviewed_intent_and_steps_survive_actual_pending_and_outcome_acknowledgements()
    {
        await using var rig = await Rig.Create(); var issuer = new StepOutcomeIssuer(); var owner = OutcomeOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); var reviewed = prepared.Intent; var first = reviewed.Steps[0];
        var actual = Task.CompletedTask; var result = new object(); issuer.Issue(prepared, rig.Source, first, actual, result);
        var pending = await owner.AdmitOriginalStepAsync(prepared, first);
        Assert.Same(reviewed, prepared.Intent); Assert.Same(reviewed, pending.AcknowledgedCheckpoint.Intent); Assert.Same(first, pending.Step);
        var acknowledged = await owner.AcknowledgeOriginalStepAsync(pending, rig.Source, actual, result);
        Assert.Same(reviewed, prepared.Intent); Assert.Same(reviewed, acknowledged.Intent);
        Assert.All(Enumerable.Range(0, reviewed.Steps.Length), index => Assert.Same(reviewed.Steps[index], prepared.Intent.Steps[index]));
        var physical = (await rig.Store.Inner.ReadAsync()).State!.Records.Single(row => row.RecordType == "home.dev-project-setup").Payload.Deserialize<DeveloperProjectSetupCheckpoint>()!;
        Assert.Equal(JsonSerializer.Serialize(reviewed), JsonSerializer.Serialize(physical.Intent));
        Assert.Equal(JsonSerializer.Serialize(acknowledged), JsonSerializer.Serialize(physical));
        var next = await owner.AdmitOriginalStepAsync(prepared, reviewed.Steps[1]);
        Assert.Same(reviewed, next.AcknowledgedCheckpoint.Intent); Assert.Same(reviewed.Steps[1], next.Step);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Value_equal_other_owner_preparation_is_not_the_same_private_reviewed_preparation()
    {
        await using var rig = await Rig.Create(); var issuer = new StepOutcomeIssuer(); var owner = OutcomeOwner(rig, issuer);
        var original = await rig.Prepare(owner); var other = rig.OtherOwner(); var copied = await rig.Prepare(other);
        Assert.Equal(JsonSerializer.Serialize(original.Intent), JsonSerializer.Serialize(copied.Intent)); Assert.NotSame(original.Intent, copied.Intent);
        var writes = rig.Store.SetupWrites; rig.ExpectFault = true;
        await AssertOriginalCause<UnauthorizedAccessException>(() => owner.ValidateOriginalPreparationAsync(copied, rig.Source));
        await AssertOriginalCause<UnauthorizedAccessException>(() => owner.AdmitOriginalStepAsync(copied, copied.Intent.Steps[0]));
        Assert.Equal(writes, rig.Store.SetupWrites);
        Assert.All((await owner.ReadCheckpointAsync(rig.SetupId, rig.Actor))!.Observations,
            row => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, row.State));
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }
}
