using Haven.Core;

namespace Dulche.Runtime;

public enum DulcheActionOutcome { Requested, Allowed, Denied, Succeeded, Failed }

public sealed record DulcheCaller(string CallerId, string Origin, string? DisplayName = null);

public sealed record DulcheActionContract(
    string Action,
    string PermissionScope,
    CapabilityRiskClass Risk,
    bool Reversible,
    bool ExternalSideEffects,
    string TargetKind);

public sealed record DulchePermissionRequest(
    DulcheCaller Caller,
    DulcheActionContract Contract,
    IReadOnlyList<string> TargetIds,
    string OperationId,
    string? IdempotencyKey = null);

public sealed record DulchePermissionDecision(bool Allowed, bool RequiresUserApproval, string Reason, string? ApprovalId = null);

public sealed record DulcheAuditEvent(
    string EventId,
    string OperationId,
    string Action,
    string PermissionScope,
    string CallerId,
    string Origin,
    IReadOnlyList<string> TargetIds,
    DulcheActionOutcome Outcome,
    DateTimeOffset At,
    string? ErrorCode = null,
    string? IdempotencyKey = null,
    string? ApprovalId = null);

public interface IDulchePermissionBroker
{
    Task<OperationResult<DulchePermissionDecision>> AuthorizeAsync(DulchePermissionRequest request, CancellationToken cancellationToken);
}

public interface IDulcheAuditSink
{
    Task RecordAsync(DulcheAuditEvent auditEvent, CancellationToken cancellationToken);
}

public interface IDulcheActionCatalogue
{
    IReadOnlyList<DulcheActionContract> Actions { get; }
    DulcheActionContract GetRequired(string action);
}

public sealed class DulcheActionCatalogue : IDulcheActionCatalogue
{
    private readonly IReadOnlyDictionary<string, DulcheActionContract> _actions = Build();

    public IReadOnlyList<DulcheActionContract> Actions => _actions.Values.OrderBy(action => action.Action, StringComparer.Ordinal).ToArray();

    public DulcheActionContract GetRequired(string action) => _actions.TryGetValue(action, out var contract)
        ? contract
        : throw new KeyNotFoundException($"No permission contract is registered for Dulche action '{action}'.");

    private static IReadOnlyDictionary<string, DulcheActionContract> Build()
    {
        static DulcheActionContract A(string action, string scope, CapabilityRiskClass risk, bool reversible, bool sideEffect, string target)
            => new(action, scope, risk, reversible, sideEffect, target);

        var actions = new[]
        {
            A("Dulche.Translate.DetectLanguage", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "input"),
            A("Dulche.Translate.Text", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-set"),
            A("Dulche.Translate.CreateSet", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-set"),
            A("Dulche.Translate.GetSet", "Dulche.Translate.Read", CapabilityRiskClass.ReadOnly, true, false, "translation-set"),
            A("Dulche.Translate.AddLanguages", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-set"),
            A("Dulche.Translate.RemoveLanguage", "Dulche.Translate.Write", CapabilityRiskClass.Low, true, false, "translation-variant"),
            A("Dulche.Translate.RefineVariant", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-variant"),
            A("Dulche.Translate.RegenerateVariant", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-variant"),
            A("Dulche.Translate.EditVariant", "Dulche.Translate.Write", CapabilityRiskClass.Low, true, false, "translation-variant"),
            A("Dulche.Translate.ApplyVariant", "Dulche.Translate.Apply", CapabilityRiskClass.Consequential, true, true, "owning-app-artifact"),
            A("Dulche.Translate.TranslateArtifact", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "artifact-reference"),
            A("Dulche.Translate.TranslateImage", "Dulche.Translate.Media", CapabilityRiskClass.Low, true, true, "media-reference"),
            A("Dulche.Translate.TranslateAudio", "Dulche.Translate.Media", CapabilityRiskClass.Low, true, true, "media-reference"),
            A("Dulche.Translate.CreateJob", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-job"),
            A("Dulche.Translate.GetJob", "Dulche.Translate.Read", CapabilityRiskClass.ReadOnly, true, false, "translation-job"),
            A("Dulche.Translate.PauseJob", "Dulche.Translate.Write", CapabilityRiskClass.Low, true, false, "translation-job"),
            A("Dulche.Translate.ResumeJob", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-job"),
            A("Dulche.Translate.CancelJob", "Dulche.Translate.Write", CapabilityRiskClass.Low, true, false, "translation-job"),
            A("Dulche.Translate.Retry", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-job"),
            A("Dulche.Translate.Glossary.List", "Dulche.Translate.Read", CapabilityRiskClass.ReadOnly, true, false, "glossary"),
            A("Dulche.Translate.Glossary.Get", "Dulche.Translate.Read", CapabilityRiskClass.ReadOnly, true, false, "glossary"),
            A("Dulche.Translate.Glossary.Create", "Dulche.Translate.Glossary.Write", CapabilityRiskClass.Low, true, false, "glossary"),
            A("Dulche.Translate.Glossary.Update", "Dulche.Translate.Glossary.Write", CapabilityRiskClass.Low, true, false, "glossary"),
            A("Dulche.Translate.GetSegmentMap", "Dulche.Translate.Read", CapabilityRiskClass.ReadOnly, true, false, "translation-variant"),
            A("Dulche.Translate.CheckSourceRevision", "Dulche.Translate.Read", CapabilityRiskClass.ReadOnly, true, false, "translation-set"),
            A("Dulche.Translate.UpdateStaleSet", "Dulche.Translate.Text", CapabilityRiskClass.Low, true, true, "translation-set"),
            A("Dulche.Voice.StartSession", "Dulche.Voice.Start", CapabilityRiskClass.Consequential, true, true, "voice-session"),
            A("Dulche.Voice.StopSession", "Dulche.Voice.Control", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.PauseSession", "Dulche.Voice.Control", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.ResumeSession", "Dulche.Voice.Control", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.Interrupt", "Dulche.Voice.Control", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.SetInputDevice", "Dulche.Voice.Device", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.SetOutputDevice", "Dulche.Voice.Device", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.SetVoice", "Dulche.Voice.Configure", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.SetModel", "Dulche.Voice.Configure", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.SendAudio", "Dulche.Voice.Audio", CapabilityRiskClass.Consequential, true, true, "voice-session"),
            A("Dulche.Voice.SendText", "Dulche.Voice.Text", CapabilityRiskClass.Low, true, true, "voice-session"),
            A("Dulche.Voice.SetLanguages", "Dulche.Voice.Configure", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.SetCaptions", "Dulche.Voice.Configure", CapabilityRiskClass.Low, true, false, "voice-session"),
            A("Dulche.Voice.SetTranscriptRetention", "Dulche.Voice.Retention", CapabilityRiskClass.Consequential, true, true, "voice-session"),
            A("Dulche.Voice.GetState", "Dulche.Voice.Read", CapabilityRiskClass.ReadOnly, true, false, "voice-session")
        };

        return actions.ToDictionary(action => action.Action, StringComparer.Ordinal);
    }
}

public sealed class DulcheSecurityGate(IDulchePermissionBroker permissions, IDulcheAuditSink audit, IDulcheActionCatalogue catalogue)
{
    public async Task<(DulcheError? Error, string OperationId, string? ApprovalId)> AuthorizeAsync(
        string action,
        DulcheCaller caller,
        IEnumerable<string>? targets,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrWhiteSpace(caller.CallerId) || string.IsNullOrWhiteSpace(caller.Origin))
            return (new(DulcheErrorCode.PermissionDenied, "A verified caller identity and origin are required.", action, false), string.Empty, null);

        var contract = catalogue.GetRequired(action);
        var targetIds = targets?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var operationId = Guid.NewGuid().ToString("N");
        OperationResult<DulchePermissionDecision> decision;
        try
        {
            decision = await permissions.AuthorizeAsync(new(caller, contract, targetIds, operationId, idempotencyKey), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException)
        {
            await RecordSafelyAsync(action, contract, caller, targetIds, operationId, DulcheActionOutcome.Failed, idempotencyKey, null, DulcheErrorCode.ProviderUnavailable, cancellationToken).ConfigureAwait(false);
            return (new(DulcheErrorCode.ProviderUnavailable, "The Home permission broker could not authorize this action.", action, true, Details: new Dictionary<string, string> { ["cause"] = ex.Message }), operationId, null);
        }

        if (!decision.Succeeded || decision.Value is null)
        {
            var error = decision.Error ?? new DulcheError(DulcheErrorCode.PermissionDenied, "The permission broker returned no decision.", action, true);
            await RecordSafelyAsync(action, contract, caller, targetIds, operationId, DulcheActionOutcome.Failed, idempotencyKey, null, error.Code, cancellationToken).ConfigureAwait(false);
            return (error, operationId, null);
        }

        var approvalId = decision.Value.ApprovalId;
        var outcome = decision.Value.Allowed ? DulcheActionOutcome.Allowed : DulcheActionOutcome.Denied;
        var auditError = await RecordSafelyAsync(action, contract, caller, targetIds, operationId, outcome, idempotencyKey, approvalId,
            decision.Value.Allowed ? null : DulcheErrorCode.PermissionDenied, cancellationToken).ConfigureAwait(false);
        if (auditError is not null)
            return (new(DulcheErrorCode.AuditUnavailable, "The action was not started because its permission decision could not be audited.", action, true), operationId, approvalId);
        if (!decision.Value.Allowed)
        {
            var code = decision.Value.RequiresUserApproval ? DulcheErrorCode.PermissionDenied : DulcheErrorCode.PermissionDenied;
            return (new(code, decision.Value.Reason, action, decision.Value.RequiresUserApproval, Details: approvalId is null ? null : new Dictionary<string, string> { ["approvalId"] = approvalId }), operationId, approvalId);
        }

        return (null, operationId, approvalId);
    }

    public async Task RecordCompletionAsync(
        string action,
        DulcheCaller caller,
        IEnumerable<string>? targets,
        string operationId,
        string? idempotencyKey,
        DulcheError? error,
        CancellationToken cancellationToken)
    {
        var contract = catalogue.GetRequired(action);
        var targetIds = targets?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var auditError = await RecordSafelyAsync(action, contract, caller, targetIds, operationId,
            error is null ? DulcheActionOutcome.Succeeded : DulcheActionOutcome.Failed,
            idempotencyKey, null, error?.Code, cancellationToken).ConfigureAwait(false);
        if (auditError is not null)
            throw new InvalidOperationException("Dulche action outcome could not be written to the audit log.", auditError);
    }

    public Task RecordIntermediateAsync(
        string action,
        DulcheCaller caller,
        IEnumerable<string>? targets,
        string operationId,
        string? idempotencyKey,
        DulcheActionOutcome outcome,
        DulcheError? error,
        CancellationToken cancellationToken)
    {
        var contract = catalogue.GetRequired(action);
        var targetIds = targets?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        return audit.RecordAsync(new($"audit_{Guid.NewGuid():N}", operationId, action, contract.PermissionScope, caller.CallerId, caller.Origin,
            targetIds, outcome, DateTimeOffset.UtcNow, error?.Code.ToString(), idempotencyKey), cancellationToken);
    }

    private async Task<Exception?> RecordSafelyAsync(
        string action,
        DulcheActionContract contract,
        DulcheCaller caller,
        IReadOnlyList<string> targets,
        string operationId,
        DulcheActionOutcome outcome,
        string? idempotencyKey,
        string? approvalId,
        DulcheErrorCode? errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await RecordAsync(action, contract, caller, targets, operationId, outcome, idempotencyKey, approvalId, errorCode, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException)
        {
            return ex;
        }
    }

    private Task RecordAsync(
        string action,
        DulcheActionContract contract,
        DulcheCaller caller,
        IReadOnlyList<string> targets,
        string operationId,
        DulcheActionOutcome outcome,
        string? idempotencyKey,
        string? approvalId,
        DulcheErrorCode? errorCode,
        CancellationToken cancellationToken)
    {
        var eventId = $"audit_{Guid.NewGuid():N}";
        return audit.RecordAsync(new(eventId, operationId, action, contract.PermissionScope, caller.CallerId, caller.Origin,
            targets, outcome, DateTimeOffset.UtcNow, errorCode?.ToString(), idempotencyKey, approvalId), cancellationToken);
    }
}
