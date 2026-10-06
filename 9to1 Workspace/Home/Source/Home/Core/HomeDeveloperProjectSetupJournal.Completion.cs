using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperProjectSetupJournal : IDeveloperProjectOriginalSetupCompletionSource
{
    private readonly Dictionary<Prepared, Dictionary<DeveloperProjectSetupStep, Acknowledgement>> _acknowledgements =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Prepared, Completion> _completions = new(ReferenceEqualityComparer.Instance);

    private sealed class Acknowledgement(Original original, Prepared prepared, OriginalStepAdmission admission,
        IDeveloperProjectOriginalSourceCapture capture, IDeveloperProjectOriginalSetupStepOutcomeSource issuer,
        Task actualStep, object? actualResult, Task<HomeStateWriteResult> actualWrite,
        HomeStateWriteResult actualWriteResult, DeveloperProjectSetupCheckpoint actualAcknowledged)
    {
        internal Original Original => original;
        internal Prepared Prepared => prepared;
        internal OriginalStepAdmission Admission => admission;
        internal IDeveloperProjectOriginalSourceCapture Capture => capture;
        internal IDeveloperProjectOriginalSetupStepOutcomeSource Issuer => issuer;
        internal Task ActualStep => actualStep;
        internal object? ActualResult => actualResult;
        internal Task<HomeStateWriteResult> ActualWrite => actualWrite;
        internal HomeStateWriteResult ActualWriteResult => actualWriteResult;
        internal DeveloperProjectSetupCheckpoint Acknowledged => actualAcknowledged;
    }
    private sealed class Completion(HomeDeveloperProjectSetupJournal owner, Prepared prepared,
        DeveloperProjectSetupCheckpoint expected, Acknowledgement[] originals) : IDeveloperProjectOriginalSetupCompletion
    {
        internal HomeDeveloperProjectSetupJournal Owner => owner;
        internal Prepared Prepared => prepared;
        internal DeveloperProjectSetupCheckpoint Expected => expected;
        internal Acknowledgement[] Originals => originals;
    }

    private void RetainOriginalAcknowledgement(Original original, Prepared prepared, OriginalStepAdmission admission,
        IDeveloperProjectOriginalSourceCapture capture, IDeveloperProjectOriginalSetupStepOutcomeSource issuer,
        Task actualStep, object? actualResult, Task<HomeStateWriteResult> actualWrite,
        HomeStateWriteResult actualWriteResult, DeveloperProjectSetupCheckpoint actualAcknowledged)
    {
        // Called under the SAME metadata gate only after the original guarded write
        // returned and its entire acknowledged serialized checkpoint was checked.
        if (!_acknowledgements.TryGetValue(prepared, out var steps))
            _acknowledgements.Add(prepared, steps = new(ReferenceEqualityComparer.Instance));
        if (!steps.TryAdd(admission.Step, new(original, prepared, admission, capture, issuer,
            actualStep, actualResult, actualWrite, actualWriteResult, actualAcknowledged)))
            throw new InvalidOperationException("The SAME privately issued original ACK is already retained; no replay was issued.");
    }

    private Original[] OriginalAcknowledgementOwners() => _acknowledgements.Values.SelectMany(value => value.Values)
        .Select(value => value.Original).Distinct().ToArray();

    public void DemandExternalOriginalSetupCompletionJoin()
    {
        // This is only the local actual journal dependency guard. Whole journal close
        // retains its existing source/outcome dependency preflight separately.
        if (_callbacks?.Any(value => ReferenceEquals(value, this)) == true)
            throw new InvalidOperationException("An actual journal source callback cannot join its own original ACK observation.");
        for (var original = _executing.Value; original is not null; original = original.Parent)
            if (Volatile.Read(ref original.Live))
                throw new InvalidOperationException("A live journal original cannot join its own completion observation.");
    }

    private Prepared RequireCompletionPreparation(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture)
    {
        lock (_gate)
            if (!_retiring && _prepared.TryGetValue(sameIntent.SetupId, out var prepared) &&
                ReferenceEquals(prepared.Owner, this) && ReferenceEquals(prepared.Intent, sameIntent) &&
                ReferenceEquals(prepared.Source, sameCapture)) return prepared;
        throw new UnauthorizedAccessException("Retain the SAME private original preparation/intent/capture; stored rows cannot issue a completion.");
    }

    public Task<IDeveloperProjectOriginalSetupCompletion?> GetOriginalCompletionAsync(
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture, CancellationToken ct)
    {
        DemandExternalOriginalSetupCompletionJoin();
        return Run<IDeveloperProjectOriginalSetupCompletion?>(async original =>
        {
            var prepared = RequireCompletionPreparation(sameIntent, sameCapture);
            var expected = prepared.Checkpoint;
            await RequireCurrentOriginalPreparationAsync(original, prepared, expected, ct).ConfigureAwait(false);
            Acknowledgement[] acknowledgements;
            lock (_gate)
            {
                if (!ReferenceEquals(RequireCompletionPreparation(sameIntent, sameCapture), prepared))
                    throw new UnauthorizedAccessException("The SAME original preparation retired or changed before incomplete observation disclosure.");
                if (expected.Observations.Any(value => value.State != DeveloperProjectSetupStepState.Acknowledged)) return null;
                if (!_acknowledgements.TryGetValue(prepared, out var retained) || retained.Count != sameIntent.Steps.Length ||
                    sameIntent.Steps.Any(value => !retained.ContainsKey(value)))
                    throw new UnauthorizedAccessException("Complete stored rows lack SAME original guarded ACK task/result custody.");
                acknowledgements = sameIntent.Steps.Select(value => retained[value]).ToArray();
            }
            await ValidateOriginalAcknowledgementsAsync(original, prepared, expected, acknowledgements, ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (!ReferenceEquals(RequireCompletionPreparation(sameIntent, sameCapture), prepared))
                    throw new UnauthorizedAccessException("The SAME original preparation retired or changed before final completion disclosure.");
                if (!ReferenceEquals(prepared.Checkpoint, expected)) throw new InvalidOperationException("The original journal changed during completion observation.");
                if (_completions.TryGetValue(prepared, out var existing)) return existing;
                var receipt = new Completion(this, prepared, expected, acknowledgements);
                _completions.Add(prepared, receipt); return receipt;
            }
        });
    }

    private async Task ValidateOriginalAcknowledgementsAsync(Original original, Prepared prepared,
        DeveloperProjectSetupCheckpoint expected, Acknowledgement[] acknowledgements, CancellationToken ct)
    {
        if (acknowledgements.Length != expected.Intent.Steps.Length) throw new UnauthorizedAccessException("The original ACK cohort is incomplete.");
        var errors = new List<Exception>();
        async Task Join(Func<Task> source)
        {
            try { await Await(original, source).ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }
        }
        for (var index = 0; index < acknowledgements.Length; index++)
        {
            var acknowledgement = acknowledgements[index]; var step = expected.Intent.Steps[index];
            if (!ReferenceEquals(acknowledgement.Prepared, prepared) || !ReferenceEquals(acknowledgement.Capture, prepared.Source) ||
                !ReferenceEquals(acknowledgement.Admission.Step, step) ||
                !ReferenceEquals(acknowledgement.Acknowledged.Intent, expected.Intent) ||
                acknowledgement.Acknowledged.Observations[index].State != DeveloperProjectSetupStepState.Acknowledged)
                throw new UnauthorizedAccessException("An ACK metadata copy does not substitute the original declared-step custody.");
            // Actual whole owning ACK driver/finally, actual raw physical step and guarded
            // Home write tasks are joined independently; no Task.Result reconstruction.
            await Join(() => acknowledgement.Original.Task).ConfigureAwait(false);
            await Join(() => acknowledgement.ActualStep).ConfigureAwait(false);
            try
            {
                var actualWriteResult = await Await(original, () => acknowledgement.ActualWrite).ConfigureAwait(false);
                if (!ReferenceEquals(actualWriteResult, acknowledgement.ActualWriteResult) || !actualWriteResult.IsSuccess)
                    throw new UnauthorizedAccessException("No SAME actual successful guarded Home write result owns this ACK.");
            }
            catch (Exception error) { errors.Add(error); }
            Task[] sources; lock (_gate) sources = acknowledgement.Original.Sources.ToArray();
            foreach (var source in sources) await Join(() => source).ConfigureAwait(false);
            try
            {
                if (!Acquire(original, () => acknowledgement.Issuer.IsIssuedOriginalStepOutcome(expected.Intent, prepared.Source,
                    step, acknowledgement.ActualStep, acknowledgement.ActualResult)))
                    throw new UnauthorizedAccessException("The genuine original physical outcome issuer no longer recognizes this ACK.");
                await Await(original, () => acknowledgement.Issuer.ValidateOriginalStepOutcomeAsync(expected.Intent, prepared.Source,
                    step, acknowledgement.ActualStep, acknowledgement.ActualResult, ct)).ConfigureAwait(false);
            }
            catch (Exception error) { errors.Add(error); }
        }
        // Entire current actor/source/store is observed again AFTER all awaited work.
        try { await RequireCurrentOriginalPreparationAsync(original, prepared, expected, ct).ConfigureAwait(false); }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("The original complete ACK cohort/current observation did not settle; every retained actual ACK task was independently joined.", errors);
    }

    private Completion RequireCompletion(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, IDeveloperProjectOriginalSetupCompletion sameCompletion)
    {
        var prepared = RequireCompletionPreparation(sameIntent, sameCapture);
        lock (_gate)
            if (sameCompletion is Completion receipt && ReferenceEquals(receipt.Owner, this) &&
                ReferenceEquals(receipt.Prepared, prepared) && ReferenceEquals(receipt.Expected, prepared.Checkpoint) &&
                _completions.TryGetValue(prepared, out var original) && ReferenceEquals(receipt, original)) return receipt;
        throw new UnauthorizedAccessException("No SAME privately issued final original completion exists.");
    }

    public bool IsIssuedOriginalCompletion(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, IDeveloperProjectOriginalSetupCompletion sameCompletion)
    { try { RequireCompletion(sameIntent, sameCapture, sameCompletion); return true; } catch (UnauthorizedAccessException) { return false; } }

    public Task ValidateOriginalCompletionAsync(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, IDeveloperProjectOriginalSetupCompletion sameCompletion, CancellationToken ct)
    {
        DemandExternalOriginalSetupCompletionJoin();
        return Run(async original =>
        {
            var receipt = RequireCompletion(sameIntent, sameCapture, sameCompletion);
            await ValidateOriginalAcknowledgementsAsync(original, receipt.Prepared, receipt.Expected, receipt.Originals, ct).ConfigureAwait(false);
            RequireCompletion(sameIntent, sameCapture, sameCompletion); return true;
        });
    }
}
