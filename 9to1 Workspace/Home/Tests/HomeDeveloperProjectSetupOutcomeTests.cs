using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real Home/profile/CAS journal with a synthetic exact-reference outcome issuer.
/// These controls prove only outcome-acknowledgement custody/refusal, not physical registration,
/// Home high-risk review/entry, source capture, effect completion or native project acceptance.</summary>
public sealed partial class HomeDeveloperProjectSetupJournalTests
{
    [Fact]
    public async Task Genuine_source_step_observation_acknowledges_only_the_same_pending_step_and_ids()
    {
        await using var rig = await Rig.Create(); var issuer = new StepOutcomeIssuer();
        var owner = OutcomeOwner(rig, issuer); var prepared = await rig.Prepare(owner);
        var admission = await owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[0]);
        var raw = Task.CompletedTask; var actualResult = new object(); issuer.Issue(prepared, rig.Source, admission.Step, raw, actualResult);
        var saved = await owner.AcknowledgeOriginalStepAsync(admission, rig.Source, raw, actualResult);
        Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, saved.Observations[0].State);
        Assert.All(saved.Observations.Skip(1), row => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, row.State));
        Assert.Equal(JsonSerializer.Serialize(prepared.Intent), JsonSerializer.Serialize(saved.Intent));
        Assert.Equal(issuer.Observation.OriginalReceiptReference, saved.Observations[0].OriginalReceiptReference);
        Assert.Equal(1, issuer.Validations); Assert.Equal(0, rig.Captures.ResourceEffects);
        var next = await owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[1]);
        Assert.Same(prepared.Intent.Steps[1], next.Step);
    }

    [Fact]
    public async Task Public_completed_task_and_value_equal_observation_do_not_acknowledge_an_actual_step()
    {
        await using var rig = await Rig.Create(); var issuer = new StepOutcomeIssuer();
        var owner = OutcomeOwner(rig, issuer); var prepared = await rig.Prepare(owner);
        var admission = await owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[0]);
        var originalResult = new object(); var actual = Task.FromResult(17);
        issuer.Issue(prepared, rig.Source, admission.Step, actual, originalResult); var writes = rig.Store.SetupWrites;
        rig.ExpectFault = true;
        await AssertOriginalCause<UnauthorizedAccessException>(() => owner.AcknowledgeOriginalStepAsync(admission, rig.Source, Task.CompletedTask, originalResult));
        Assert.Equal(writes, rig.Store.SetupWrites); Assert.Equal(0, issuer.Validations);
        Assert.Equal(DeveloperProjectSetupStepState.Admitted, (await owner.ReadCheckpointAsync(rig.SetupId, rig.Actor))!.Observations[0].State);
        await AssertOriginalCause<InvalidOperationException>(() => owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actual, originalResult));
        Assert.Equal(writes, rig.Store.SetupWrites); Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Held_actual_outcome_validation_is_joined_and_preserves_raw_faulted_cancellation_siblings()
    {
        await using var rig = await Rig.Create(); var issuer = new StepOutcomeIssuer(); var owner = OutcomeOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); var admission = await owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[0]);
        var value = new object(); var actualStep = Task.CompletedTask; issuer.Issue(prepared, rig.Source, admission.Step, actualStep, value);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new OperationCanceledException("Faulted original outcome validation, no actual cancellation.");
        var second = new IOException("Actual sibling outcome validation fault.");
        issuer.Validation = () => { entered.TrySetResult(); return raw.Task; };
        rig.Releases.Add(() => raw.TrySetException([first, second])); rig.ExpectFault = true;
        var acknowledgement = owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actualStep, value);
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(Bound);
            close = owner.CloseAndDrainAsync(); Assert.Same(close, owner.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
            raw.TrySetException([first, second]);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => acknowledgement);
            Assert.True(acknowledgement.IsFaulted); Assert.Contains(AllCauses(error), cause => ReferenceEquals(cause, first));
            Assert.Contains(AllCauses(error), cause => ReferenceEquals(cause, second));
            var closeError = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(AllCauses(closeError), cause => ReferenceEquals(cause, first)); Assert.Contains(AllCauses(closeError), cause => ReferenceEquals(cause, second));
        }
        finally
        {
            raw.TrySetException([first, second]);
            try { await acknowledgement; } catch { }
            if (close is not null) try { await close; } catch { }
        }
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Persist_then_fault_keeps_actual_outcome_ids_and_refuses_a_second_acknowledgement()
    {
        await using var rig = await Rig.Create(); var issuer = new StepOutcomeIssuer(); var owner = OutcomeOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); var admission = await owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[0]);
        var result = new object(); var actualStep = Task.CompletedTask; issuer.Issue(prepared, rig.Source, admission.Step, actualStep, result);
        var fault = new IOException("Actual observer failed after original outcome journal persistence.");
        rig.Store.AfterSetupWrite = _ => throw fault; rig.ExpectFault = true;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actualStep, result));
        Assert.Contains(AllCauses(error), cause => ReferenceEquals(cause, fault));
        var retained = (await rig.Store.Inner.ReadAsync()).State!.Records.Single(row => row.RecordType == "home.dev-project-setup").Payload.Deserialize<DeveloperProjectSetupCheckpoint>()!;
        Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, retained.Observations[0].State);
        Assert.Equal(prepared.Intent.Files[0].FileId, retained.Intent.Files[0].FileId);
        var writes = rig.Store.SetupWrites;
        await AssertOriginalCause<InvalidOperationException>(() => owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actualStep, result));
        Assert.Equal(writes, rig.Store.SetupWrites); Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Retired_original_outcome_before_final_home_swap_refuses_ack_and_preserves_pending_state()
    {
        await using var rig = await Rig.Create(); var issuer = new StepOutcomeIssuer(); var owner = OutcomeOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); var admission = await owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[0]);
        var result = new object(); var actualStep = Task.CompletedTask; issuer.Issue(prepared, rig.Source, admission.Step, actualStep, result);
        rig.Store.BeforeSetupWrite = (row, revision, actor, guard, ct) =>
        { issuer.Retired = true; return rig.Store.Inner.WriteGuardedAsync(row, revision, actor, guard, ct); };
        rig.ExpectFault = true;
        await Assert.ThrowsAnyAsync<Exception>(() => owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actualStep, result));
        Assert.Equal(DeveloperProjectSetupStepState.Admitted, (await owner.ReadCheckpointAsync(rig.SetupId, rig.Actor))!.Observations[0].State);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    private static HomeDeveloperProjectSetupJournal OutcomeOwner(Rig rig, StepOutcomeIssuer issuer)
    { var actual = new HomeDeveloperProjectSetupJournal(rig.Store, rig.Profiles, rig.Captures, () => issuer); rig.Owners.Add(actual); return actual; }
    private sealed class StepOutcomeIssuer : IDeveloperProjectOriginalSetupStepOutcomeSource
    {
        private DeveloperProjectSetupIntent? _intent; private IDeveloperProjectOriginalSourceCapture? _capture;
        private DeveloperProjectSetupStep? _step; private Task? _actual; private object? _result;
        internal bool Retired; internal int Validations; internal Func<Task>? Validation;
        internal readonly DeveloperProjectOriginalStepOutcomeObservation Observation = new("synthetic-original-step-reference", new('d', 64));
        internal void Issue(HomeDeveloperProjectSetupJournal.Prepared prepared, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task actual, object result)
        { _intent = prepared.Intent; _capture = capture; _step = step; _actual = actual; _result = result; }
        public bool IsIssuedOriginalStepOutcome(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task actual, object? result) => !Retired && ReferenceEquals(intent, _intent) &&
            ReferenceEquals(capture, _capture) && ReferenceEquals(step, _step) && ReferenceEquals(actual, _actual) && ReferenceEquals(result, _result);
        public Task ValidateOriginalStepOutcomeAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task actual, object? result, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (!IsIssuedOriginalStepOutcome(intent, capture, step, actual, result)) throw new UnauthorizedAccessException();
            Validations++; return Validation?.Invoke() ?? Task.CompletedTask;
        }
        public DeveloperProjectOriginalStepOutcomeObservation GetOriginalStepOutcomeObservation(DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task actual, object? result)
        { if (!IsIssuedOriginalStepOutcome(intent, capture, step, actual, result)) throw new UnauthorizedAccessException(); return Observation; }
        public void DemandExternalOriginalSetupStepOutcomeJoin() { }
    }
}
