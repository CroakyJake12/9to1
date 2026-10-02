using Haven.Application;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

public sealed record FormDataHomeWriteResult(DataRecordMutationResult Data, FormDataWriteJournalResult? Journal,
    string? ReconciliationCode);

/// <summary>Dispatches the exact owner operation first, consuming its Home capability before any
/// response reread. A later journal failure preserves the actual Data result and can be reconciled
/// through the retained operation receipt; this coordinator never repeats an owning mutation.</summary>
public sealed class FormDataHomeWriteOperation(DataHomeRecordUpdateOperation data, FormDataResponseWriteService responses)
{
    private readonly ConditionalWeakTable<HomeResourceExecutionCapability, DataRecordUpdateIntent> _intents = new();

    public async Task<FormDataHomeWriteResult> FinishAsync(
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        if (!_intents.TryGetValue(capability, out var intent))
            throw new UnauthorizedAccessException("This Forms coordinator did not retain the original Data intent.");
        var result = await data.FinishAsync(capability, cancellationToken).ConfigureAwait(false);
        return await ReconcileKnownAsync(intent, result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FormDataHomeWriteResult> ExecuteAsync(DataRecordUpdateIntent intent,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken = default)
    {
        // Do not insert a Forms read before dispatch: revocation after approval must still reach
        // the canonical one-use claim/audit boundary rather than leave an unconsumed capability.
        _intents.Add(capability, intent); // Retain exact original source before owner awaits; never replace it.
        var result = await data.ExecuteAsync(intent, capability, cancellationToken).ConfigureAwait(false);
        return await ReconcileKnownAsync(intent, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FormDataHomeWriteResult> ReconcileKnownAsync(DataRecordUpdateIntent intent,
        DataRecordMutationResult result, CancellationToken cancellationToken)
    {
        if (!result.OutcomeKnown) return new(result, null, "DataOutcomeUnconfirmed");
        if (intent.Origin is not { } source) return new(result, null, "NoFormSource");
        try
        {
            var journal = await responses.ReconcileAsync(source.FormID, source.ResponseID, intent.OperationID,
                cancellationToken).ConfigureAwait(false);
            return new(result, journal, journal.Success ? journal.Code : "FormAcknowledgementPending");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException
            or InvalidOperationException or ArgumentException or KeyNotFoundException or NotSupportedException or JsonException)
        {
            // This is a failure to acknowledge, not a rollback or another attempt. Home retains
            // its own execution audit and the target retains any committed receipt.
            return new(result, null, error is OperationCanceledException ? "FormAcknowledgementCancelled" : "FormAcknowledgementPending");
        }
    }
}
