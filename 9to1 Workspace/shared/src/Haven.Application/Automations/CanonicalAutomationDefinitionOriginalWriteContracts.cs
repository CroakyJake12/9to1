using Haven.Core;

namespace Haven.Application.Automations;

public enum CanonicalAutomationOriginalChangeKind { RecoverLegacy, Disable, EditDisabled }

/// <summary>Detached editable values, never a store selection, approval or run grant.
/// The protected owner must revalidate the actual recovered, disabled row separately.</summary>
public sealed record CanonicalAutomationOriginalEdit(string Name, string Instruction, string ScheduleJson)
{
    public AutomationDefinition PrepareDisabledProposal(AutomationDefinition original, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.IsEnabled || original.ArchivedAt is not null)
            throw new InvalidOperationException("Disable and recover this automation before editing its saved settings.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || Instruction is null || ScheduleJson is null)
            throw new ArgumentException("Use a name of up to 256 characters and bounded instruction and schedule values.");
        if (original.GraphBinding is not null || ScheduledGraphAutomationPayloadCodec.IsPayload(original.Instruction))
        {
            if (Instruction != original.Instruction || ScheduleJson != original.ScheduleJson)
                throw new InvalidOperationException("Graph instructions and schedules must remain attached to their original graph.");
        }
        else
        {
            if (Instruction.Length > 16000 || System.Text.Encoding.UTF8.GetByteCount(ScheduleJson) > 16384)
                throw new ArgumentException("Use an instruction of up to 16000 characters and a bounded saved schedule.");
            if (ScheduledGraphAutomationPayloadCodec.IsPayload(Instruction))
                throw new InvalidOperationException("Use the graph editor to change graph-backed instructions; this form edits plain instructions only.");
            using var before = System.Text.Json.JsonDocument.Parse(original.ScheduleJson);
            using var after = System.Text.Json.JsonDocument.Parse(ScheduleJson);
            var old = before.RootElement; var next = after.RootElement;
            if (old.ValueKind != System.Text.Json.JsonValueKind.Object || next.ValueKind != System.Text.Json.JsonValueKind.Object ||
                old.EnumerateObject().Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != old.EnumerateObject().Count() ||
                next.EnumerateObject().Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != next.EnumerateObject().Count())
                throw new ArgumentException("The saved schedule must be one unambiguous object.");
            string[] editable = original.ScheduleKind switch
            {
                AutomationScheduleKind.Once => ["at"], AutomationScheduleKind.Hourly => ["intervalHours"],
                AutomationScheduleKind.Daily => ["time"], AutomationScheduleKind.Weekly => ["dayOfWeek", "time"],
                AutomationScheduleKind.ConditionWatch => ["intervalMinutes"],
                _ => throw new ArgumentException("This schedule kind cannot be edited here.")
            };
            foreach (var name in old.EnumerateObject().Select(value => value.Name).Concat(next.EnumerateObject().Select(value => value.Name)).Distinct(StringComparer.Ordinal))
                if (!editable.Contains(name, StringComparer.Ordinal) &&
                    (!old.TryGetProperty(name, out var left) || !next.TryGetProperty(name, out var right) ||
                     !System.Text.Json.JsonElement.DeepEquals(left, right)))
                    throw new ArgumentException("Other saved schedule properties must remain unchanged.");
            string Text(string key) => next.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()! : throw new ArgumentException("A schedule value is missing: " + key);
            void Number(string key, int minimum, int maximum)
            {
                if (!next.TryGetProperty(key, out var value) || value.ValueKind != System.Text.Json.JsonValueKind.Number ||
                    !value.TryGetInt32(out var number) || number < minimum || number > maximum)
                    throw new ArgumentException($"Use {minimum}–{maximum} for {key}.");
            }
            if (original.ScheduleKind == AutomationScheduleKind.Once)
            {
                var at = Text("at");
                if (!System.Text.RegularExpressions.Regex.IsMatch(at, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?(?:Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
                    !DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                    throw new ArgumentException("Enter a complete date and time with its UTC offset.");
            }
            if ((original.ScheduleKind is AutomationScheduleKind.Daily or AutomationScheduleKind.Weekly) &&
                !TimeOnly.TryParseExact(Text("time"), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                throw new ArgumentException("Enter the time as HH:mm.");
            if (original.ScheduleKind == AutomationScheduleKind.Weekly &&
                !Enum.GetNames<DayOfWeek>().Contains(Text("dayOfWeek"), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Enter a day from Monday to Sunday.");
            if (original.ScheduleKind == AutomationScheduleKind.Hourly) Number("intervalHours", 1, 168);
            if (original.ScheduleKind == AutomationScheduleKind.ConditionWatch) Number("intervalMinutes", 60, 10080);
        }
        return original with { Name = Name.Trim(), Instruction = Instruction, ScheduleJson = ScheduleJson,
            IsEnabled = false, NextRunAt = null, UpdatedAt = updatedAt, OperationalState = AutomationOperationalState.Disabled };
    }
}

/// <summary>Privately issued by the SAME protected row owner. This detached observation
/// grants no WRITE, scheduling, Task admission or execution permission.</summary>
public interface ICanonicalAutomationDefinitionOriginalChangeIntent
{
    AuthenticatedResourceActor Actor { get; }
    ResourceStoreIdentity OriginalStoreIdentity { get; }
    VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
    AutomationDefinition OriginalDefinition { get; }
    AutomationDefinition ProposedDefinition => OriginalDefinition;
    CanonicalAutomationOriginalChangeKind ChangeKind { get; }
    string ActionId { get; }
    Guid OperationId { get; }
    long ExpectedRevision { get; }
    string OriginalRowSha256 { get; }
    bool IsRecoveredOperation { get; }
}
public interface ICanonicalAutomationDefinitionOriginalChangeAcknowledgment
{
    ICanonicalAutomationDefinitionOriginalChangeIntent OriginalIntent { get; }
    Guid OperationId { get; }
    AutomationDefinition Definition { get; }
    AutomationOwnerCommitReceipt OriginalReceipt { get; }
    bool IsRecoveredOperation { get; }
}
public interface ICanonicalAutomationDefinitionOriginalWriteSource
{
    Task<ICanonicalAutomationDefinitionOriginalChangeIntent> PrepareOriginalChangeWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<AutomationDefinition> sameSelectedRow, CanonicalAutomationOriginalChangeKind kind,
        Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalChangeIntent(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    string GetOriginalChangeIntentDigest(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    Task RevalidateOriginalChangeIntentWithinSourceAsync(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalChangeCommit(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> CommitOriginalChangeWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent, Action<Action> scope,
        Action<Task> retain, CancellationToken token);
    bool IsOriginalAtomicChangeTask(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic);
    bool IsOwnedOriginalChangeAcknowledgment(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        ICanonicalAutomationDefinitionOriginalChangeAcknowledgment sameAcknowledgment,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic);
    // Pure private dispatch + independently joined SAME native pin close proof.
    // This neither erases a business failure nor infers no SQL from a null field.
    bool IsOwnedOriginalChangeNativeRelease(ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? sameAtomic);
    bool IsAcknowledgedOriginalChangeSourceRefusal(Task sameActualSource) => false;
}
public interface ICanonicalAutomationDefinitionOriginalHomeWriteClaim : IAsyncDisposable
{
    ICanonicalAutomationDefinitionOriginalChangeIntent OriginalIntent { get; }
    string? OriginalApprovalRequestId { get; }
    Task OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalAutomationDefinitionOriginalHomeWriteSource
{
    Task<ICanonicalAutomationDefinitionOriginalHomeWriteClaim> AcquireOriginalWriteWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent, Action<Action> scope, Action<Task> retain,
        Action<ICanonicalAutomationDefinitionOriginalHomeWriteClaim>? capture, CancellationToken token);
    bool IsAcknowledgedOriginalWriteRefusal(Task sameAcquisition);
    bool IsIssuedOriginalWriteClaim(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    Task AcquireOriginalCommitEntryWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ValidateOriginalCommitWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalCommit(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent);
    void RetainOriginalSqlCommit(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic);
    Task CompleteOriginalWriteWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameAtomic,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
public interface ICanonicalAutomationDefinitionOriginalHomeReviewWithdrawalSource
{
    Task WithdrawOriginalPendingWriteWithinSourceAsync(ICanonicalAutomationDefinitionOriginalHomeWriteClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
