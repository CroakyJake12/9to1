using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Genuine Home/profile/guarded CAS journal; synthetic source and exact-reference
/// outcome issuer prove final acknowledgment custody only, not physical project setup/Home
/// high-risk review, private source bytes, Dev command success or native readiness.</summary>
public sealed partial class HomeDeveloperProjectSetupJournalTests
{
    [Fact]
    public async Task Incomplete_same_original_plan_has_no_private_completion_and_does_not_issue_effect_or_replay()
    {
        await using var rig = await Rig.Create(); var issuer = new CompletionOutcomes(); var owner = CompletionOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); var before = rig.Store.SetupWrites;
        Assert.Null(await owner.GetOriginalCompletionAsync(prepared.Intent, rig.Source, TestContext.Current.CancellationToken));
        Assert.Equal(before, rig.Store.SetupWrites);
        var admission = await owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[0]);
        var actual = Task.FromResult(new object()); var result = await actual; issuer.Issue(prepared, rig.Source, admission.Step, actual, result);
        await owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actual, result);
        Assert.Null(await owner.GetOriginalCompletionAsync(prepared.Intent, rig.Source, TestContext.Current.CancellationToken));
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Exact_full_original_ack_drivers_writes_and_results_issue_one_private_completion_with_fresh_validation()
    {
        await using var rig = await Rig.Create(); var issuer = new CompletionOutcomes(); var owner = CompletionOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); var originals = await AcknowledgeCompletePlan(owner, prepared, rig.Source, issuer);
        var writes = rig.Store.SetupWrites;
        var completion = await owner.GetOriginalCompletionAsync(prepared.Intent, rig.Source, TestContext.Current.CancellationToken);
        Assert.NotNull(completion); Assert.True(owner.IsIssuedOriginalCompletion(prepared.Intent, rig.Source, completion));
        Assert.Same(completion, await owner.GetOriginalCompletionAsync(prepared.Intent, rig.Source, TestContext.Current.CancellationToken));
        Assert.False(owner.IsIssuedOriginalCompletion(prepared.Intent with { }, rig.Source, completion));
        Assert.False(owner.IsIssuedOriginalCompletion(prepared.Intent, rig.Source, new PublicCompletion()));
        await owner.ValidateOriginalCompletionAsync(prepared.Intent, rig.Source, completion, TestContext.Current.CancellationToken);
        Assert.Equal(writes, rig.Store.SetupWrites); Assert.All(originals, task => Assert.True(task.IsCompletedSuccessfully));
        Assert.True(issuer.Validations >= prepared.Intent.Steps.Length * 4);
        Assert.Equal(0, rig.Captures.ResourceEffects);
        var close = owner.CloseAndDrainAsync(); Assert.Same(close, owner.CloseAndDrainAsync()); await close;
        Assert.False(owner.IsIssuedOriginalCompletion(prepared.Intent, rig.Source, completion));
    }

    [Fact]
    public async Task Reopened_all_ack_rows_cannot_recreate_original_completion_custody_or_copy_private_receipt()
    {
        await using var rig = await Rig.Create(); var issuer = new CompletionOutcomes(); var owner = CompletionOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); await AcknowledgeCompletePlan(owner, prepared, rig.Source, issuer);
        var completion = await owner.GetOriginalCompletionAsync(prepared.Intent, rig.Source, TestContext.Current.CancellationToken);
        Assert.NotNull(completion);
        var reopened = CompletionOwner(rig, issuer); var adopted = await rig.Prepare(reopened); var writes = rig.Store.SetupWrites; rig.ExpectFault = true;
        var record = (await rig.Store.Inner.ReadAsync()).State!.Records.Single(row => row.RecordType == "home.dev-project-setup");
        Assert.All(record.Payload.Deserialize<DeveloperProjectSetupCheckpoint>()!.Observations,
            observation => Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, observation.State));
        await AssertOriginalCause<UnauthorizedAccessException>(() => reopened.GetOriginalCompletionAsync(adopted.Intent, rig.Source, TestContext.Current.CancellationToken));
        Assert.False(reopened.IsIssuedOriginalCompletion(adopted.Intent, rig.Source, completion));
        Assert.Equal(writes, rig.Store.SetupWrites); Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Actual_final_ack_persist_then_compound_fault_keeps_complete_rows_unacknowledged_and_cannot_mint_completion()
    {
        await using var rig = await Rig.Create(); var issuer = new CompletionOutcomes(); var owner = CompletionOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); var token = TestContext.Current.CancellationToken;
        foreach (var step in prepared.Intent.Steps.Take(prepared.Intent.Steps.Length - 1))
        {
            var admission = await owner.AdmitOriginalStepAsync(prepared, step, token); var actual = Task.FromResult(new object());
            var result = await actual; issuer.Issue(prepared, rig.Source, step, actual, result);
            await owner.AcknowledgeOriginalStepAsync(admission, rig.Source, actual, result, token);
        }
        var last = prepared.Intent.Steps[^1]; var finalAdmission = await owner.AdmitOriginalStepAsync(prepared, last, token);
        var stepTask = Task.FromResult(new object()); var stepResult = await stepTask; issuer.Issue(prepared, rig.Source, last, stepTask, stepResult);
        var first = new OperationCanceledException("Faulted original final ACK observer; no actual cancellation.");
        var second = new IOException("Original final ACK observer sibling.");
        rig.Store.AfterSetupWrite = _ => throw new AggregateException(first, second); rig.ExpectFault = true;
        var actualAck = owner.AcknowledgeOriginalStepAsync(finalAdmission, rig.Source, stepTask, stepResult, token);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actualAck);
        Assert.True(actualAck.IsFaulted); Assert.Contains(AllCauses(error), value => ReferenceEquals(first, value));
        Assert.Contains(AllCauses(error), value => ReferenceEquals(second, value));
        var actualStored = (await rig.Store.Inner.ReadAsync()).State!.Records.Single(value => value.RecordType == "home.dev-project-setup")
            .Payload.Deserialize<DeveloperProjectSetupCheckpoint>()!;
        Assert.All(actualStored.Observations, value => Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, value.State));
        Assert.Equal(DeveloperProjectSetupStepState.Admitted, finalAdmission.AcknowledgedCheckpoint.Observations[^1].State);
        rig.Store.AfterSetupWrite = null; var writes = rig.Store.SetupWrites;
        await AssertOriginalCause<InvalidOperationException>(() => owner.GetOriginalCompletionAsync(prepared.Intent, rig.Source, token));
        await AssertOriginalCause<InvalidOperationException>(() => owner.AcknowledgeOriginalStepAsync(finalAdmission, rig.Source, stepTask, stepResult, token));
        Assert.Equal(writes, rig.Store.SetupWrites); Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Retirement_during_actual_original_outcome_validation_denies_final_receipt_publication_and_joins_held_original()
    {
        await using var rig = await Rig.Create(); var issuer = new CompletionOutcomes(); var owner = CompletionOwner(rig, issuer);
        var prepared = await rig.Prepare(owner); await AcknowledgeCompletePlan(owner, prepared, rig.Source, issuer);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Releases.Add(() => release.TrySetResult()); rig.ExpectFault = true;
        issuer.BeforeValidation = () => { entered.TrySetResult(); return release.Task; };
        Task<IDeveloperProjectOriginalSetupCompletion?>? observation = null; Task? close = null;
        var errors = new List<Exception>();
        try
        {
            observation = owner.GetOriginalCompletionAsync(prepared.Intent, rig.Source, TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            owner.RequestRetirement(); close = owner.CloseAndDrainAsync();
            Assert.Same(close, owner.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
            release.TrySetResult();
            await AssertOriginalCause<UnauthorizedAccessException>(() => observation);
            var retained = (System.Collections.IDictionary)typeof(HomeDeveloperProjectSetupJournal)
                .GetField("_completions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
            Assert.Empty(retained); Assert.Equal(0, rig.Captures.ResourceEffects);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (observation is not null)
                try { await observation; } catch (Exception error) { if (AllCauses(error).Where(value => value is not AggregateException).Any(value => value is not UnauthorizedAccessException)) errors.Add(error); }
            if (close is not null)
                try { await close; } catch (Exception error) { if (AllCauses(error).Where(value => value is not AggregateException).Any(value => value is not UnauthorizedAccessException)) errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual final-ACK retirement control and independently joined originals failed.", errors);
    }

    private sealed class PublicCompletion : IDeveloperProjectOriginalSetupCompletion { }
    private static HomeDeveloperProjectSetupJournal CompletionOwner(Rig rig, CompletionOutcomes issuer)
    { var result = new HomeDeveloperProjectSetupJournal(rig.Store, rig.Profiles, rig.Captures, () => issuer); rig.Owners.Add(result); return result; }
    private static async Task<IReadOnlyList<Task>> AcknowledgeCompletePlan(HomeDeveloperProjectSetupJournal owner,
        HomeDeveloperProjectSetupJournal.Prepared prepared, IDeveloperProjectOriginalSourceCapture capture, CompletionOutcomes issuer)
    {
        var originals = new List<Task>(); var token = TestContext.Current.CancellationToken;
        foreach (var step in prepared.Intent.Steps)
        {
            var admission = await owner.AdmitOriginalStepAsync(prepared, step, token);
            var actual = Task.FromResult(new object()); var result = await actual; issuer.Issue(prepared, capture, step, actual, result);
            var ack = owner.AcknowledgeOriginalStepAsync(admission, capture, actual, result, token); originals.Add(ack); await ack;
        }
        return originals;
    }
    private sealed class CompletionOutcomes : IDeveloperProjectOriginalSetupStepOutcomeSource
    {
        private readonly Dictionary<DeveloperProjectSetupStep, (DeveloperProjectSetupIntent Intent,
            IDeveloperProjectOriginalSourceCapture Capture, Task Actual, object Result)> _issued = new(ReferenceEqualityComparer.Instance);
        internal int Validations;
        internal Func<Task>? BeforeValidation;
        internal void Issue(HomeDeveloperProjectSetupJournal.Prepared prepared, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task actual, object result) => _issued.Add(step, (prepared.Intent, capture, actual, result));
        public bool IsIssuedOriginalStepOutcome(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task actual, object? result) => _issued.TryGetValue(step, out var source) &&
            ReferenceEquals(source.Intent, intent) && ReferenceEquals(source.Capture, capture) &&
            ReferenceEquals(source.Actual, actual) && ReferenceEquals(source.Result, result);
        public Task ValidateOriginalStepOutcomeAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task actual, object? result, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (!IsIssuedOriginalStepOutcome(intent, capture, step, actual, result)) throw new UnauthorizedAccessException(); Validations++; return BeforeValidation?.Invoke() ?? Task.CompletedTask; }
        public DeveloperProjectOriginalStepOutcomeObservation GetOriginalStepOutcomeObservation(DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task actual, object? result)
        { if (!IsIssuedOriginalStepOutcome(intent, capture, step, actual, result)) throw new UnauthorizedAccessException(); return new("synthetic-retained-step:" + step.StepId.ToString("D"), new('d', 64)); }
        public void DemandExternalOriginalSetupStepOutcomeJoin() { }
    }
}
