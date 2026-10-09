using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperProjectSetupJournal
{
    private readonly Func<IDeveloperProjectOriginalSetupStepOutcomeSource>? _originalStepOutcomeSource = originalStepOutcomes;
    private readonly HashSet<OriginalStepAdmission> _originalOutcomeAdmissions = [];

    /// <summary>Current observation of the SAME private preparation/capture. No pending row,
    /// copied intent or deserialized checkpoint can recreate this preparation or any effect.</summary>
    public Task ValidateOriginalPreparationAsync(Prepared samePrepared,
        IDeveloperProjectOriginalSourceCapture sameCapture, CancellationToken ct = default) => Run(async original =>
    {
        var before = RequireOriginalPreparation(original, samePrepared, sameCapture);
        await RequireCurrentOriginalPreparationAsync(original, samePrepared, before, ct).ConfigureAwait(false);
        return true;
    });

    /// <summary>Records only a genuine configured producer's SAME actual guarded step Task,
    /// private result and complete outcome. The producer and Home permission separately retain
    /// original effect/entry-close custody; a pending journal or public receipt never grants it.
    /// A failed/uncertain acknowledgement retains the SAME step and refuses automatic redo.</summary>
    public Task<DeveloperProjectSetupCheckpoint> AcknowledgeOriginalStepAsync(
        OriginalStepAdmission sameAdmission, IDeveloperProjectOriginalSourceCapture sameCapture,
        Task sameActualStepTask, object? sameActualResult, CancellationToken ct = default) => Run(async original =>
    {
        ArgumentNullException.ThrowIfNull(sameAdmission); ArgumentNullException.ThrowIfNull(sameActualStepTask);
        var prepared = sameAdmission.Original;
        var before = RequireOriginalPreparation(original, prepared, sameCapture);
        lock (_gate)
        {
            var index = before.Intent.Steps.IndexOf(sameAdmission.Step);
            if (index < 0 || !ReferenceEquals(before, sameAdmission.AcknowledgedCheckpoint) ||
                before.Observations[index].State != DeveloperProjectSetupStepState.Admitted ||
                !prepared.OriginalAdmissions.Contains(sameAdmission.Step.StepId) ||
                !_originalOutcomeAdmissions.Add(sameAdmission))
                throw new InvalidOperationException("Retain the SAME current admitted step; an unknown/acknowledged outcome cannot be replayed.");
        }
        var issuer = Acquire(original, () => _originalStepOutcomeSource?.Invoke()) ??
            throw new InvalidOperationException("The genuine guarded Files/kernel step outcome owner is unavailable.");
        if (!Acquire(original, () => issuer.IsIssuedOriginalStepOutcome(before.Intent, sameCapture,
            sameAdmission.Step, sameActualStepTask, sameActualResult)))
            throw new UnauthorizedAccessException("A public completed Task/result or copied receipt is not the original physical step.");
        // Join the exact owner Task; never reconstruct its result from Task.Result/status.
        await Await(original, () => sameActualStepTask).ConfigureAwait(false);
        await Await(original, () => issuer.ValidateOriginalStepOutcomeAsync(before.Intent, sameCapture,
            sameAdmission.Step, sameActualStepTask, sameActualResult, ct)).ConfigureAwait(false);
        await RequireCurrentOriginalPreparationAsync(original, prepared, before, ct).ConfigureAwait(false);
        var observation = Acquire(original, () => issuer.GetOriginalStepOutcomeObservation(before.Intent,
            sameCapture, sameAdmission.Step, sameActualStepTask, sameActualResult));
        if (observation is null || string.IsNullOrWhiteSpace(observation.OriginalReceiptReference) ||
            observation.OriginalReceiptReference.Length > 256 || observation.OriginalOutcomeDigest is not { Length: 64 } digest ||
            !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("The original source supplied no bounded recovery observation.");
        var stepIndex = before.Intent.Steps.IndexOf(sameAdmission.Step);
        var next = before with { Revision = checked(before.Revision + 1), Observations = before.Observations.SetItem(stepIndex,
            new(sameAdmission.Step.StepId, DeveloperProjectSetupStepState.Acknowledged,
                observation.OriginalReceiptReference, observation.OriginalOutcomeDigest.ToLowerInvariant(), null)) };
        var record = new HomeCoreStateRecord(RecordId(before.Intent.OriginalActor, before.Intent.SetupId), RecordType, 1,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, next.Revision, JsonSerializer.SerializeToElement(next));
        Task<HomeStateWriteResult>? originalWrite = null;
        var write = await Await(original, () => originalWrite = store.WriteGuardedAsync(record, before.Revision, before.Intent.OriginalActor,
            new OriginalOutcomeGuard(this, original, prepared, before, sameAdmission.Step, issuer, sameActualStepTask, sameActualResult), ct)).ConfigureAwait(false);
        if (!write.IsSuccess) throw new StorageFailure(write.Failure!);
        var acknowledged = Decode(write.State!.Records.Single(value => value.RecordId == record.RecordId));
        if (JsonSerializer.Serialize(acknowledged) != JsonSerializer.Serialize(next))
            throw new InvalidDataException("The actual acknowledged outcome journal differs from its original observation.");
        acknowledged = acknowledged with { Intent = before.Intent };
        lock (_gate)
        {
            if (!ReferenceEquals(prepared.Checkpoint, before)) throw new InvalidOperationException("The original journal changed during outcome acknowledgement.");
            RetainOriginalAcknowledgement(original, prepared, sameAdmission, sameCapture, issuer,
                sameActualStepTask, sameActualResult, originalWrite!, write, acknowledged);
            prepared.Checkpoint = acknowledged;
        }
        return acknowledged;
    });

    private DeveloperProjectSetupCheckpoint RequireOriginalPreparation(Original original, Prepared prepared,
        IDeveloperProjectOriginalSourceCapture capture) => Acquire(original, () =>
    {
        lock (_gate)
        {
            if (prepared is null || !ReferenceEquals(prepared.Owner, this) || !ReferenceEquals(prepared.Source, capture) ||
                !_prepared.TryGetValue(prepared.Intent.SetupId, out var current) || !ReferenceEquals(current, prepared))
                throw new UnauthorizedAccessException("Retain the SAME privately issued journal preparation and source capture.");
            return prepared.Checkpoint;
        }
    });
    private async Task RequireCurrentOriginalPreparationAsync(Original original, Prepared prepared,
        DeveloperProjectSetupCheckpoint expected, CancellationToken ct)
    {
        await RequireActorAsync(original, expected.Intent.OriginalActor, ct).ConfigureAwait(false);
        await Await(original, () => _captureAuthority.RevalidateOriginalAsync(prepared.Source, expected.Intent.OriginalActor, ct)).ConfigureAwait(false);
        var read = await Await(original, () => store.ReadAsync(ct)).ConfigureAwait(false);
        if (!read.IsSuccess) throw new StorageFailure(read.Failure!);
        var record = read.State!.Records.SingleOrDefault(value => value.RecordId == RecordId(expected.Intent.OriginalActor, expected.Intent.SetupId));
        if (record is null || JsonSerializer.Serialize(Decode(record)) != JsonSerializer.Serialize(expected))
            throw new InvalidOperationException("The actual retained journal changed; inspect its SAME outcome without repeating effects.");
        await RequireActorAsync(original, expected.Intent.OriginalActor, ct).ConfigureAwait(false);
        lock (_gate) if (!ReferenceEquals(prepared.Checkpoint, expected)) throw new InvalidOperationException("The original preparation changed during observation.");
    }
    private sealed class OriginalOutcomeGuard(HomeDeveloperProjectSetupJournal owner, Original original,
        Prepared prepared, DeveloperProjectSetupCheckpoint expected, DeveloperProjectSetupStep actualDeclaredStep, IDeveloperProjectOriginalSetupStepOutcomeSource issuer,
        Task actualStep, object? actualResult) : IHomeStateCommitActorGuard
    {
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actualActor,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            if (actualActor != expected.Intent.OriginalActor ||
                !await owner.Await(original, () => owner._profileAuthority.CheckAsync(state, actualActor, phase, ct).AsTask()).ConfigureAwait(false)) return false;
            var current = state.Records.SingleOrDefault(value => value.RecordId == RecordId(actualActor, expected.Intent.SetupId));
            // Finite private issuer evidence only under the Home store lease: never reacquire
            // Home/Files/resources/permissions or invoke asynchronous producer validation here.
            return current is not null && JsonSerializer.Serialize(Decode(current)) == JsonSerializer.Serialize(expected) &&
                ReferenceEquals(prepared.Checkpoint, expected) && owner.Acquire(original, () =>
                    owner._captureAuthority.IsIssuedOriginal(prepared.Source) && issuer.IsIssuedOriginalStepOutcome(expected.Intent, prepared.Source,
                        actualDeclaredStep, actualStep, actualResult));
        }
    }
}
