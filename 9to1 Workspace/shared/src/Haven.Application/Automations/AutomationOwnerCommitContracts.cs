using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application.Automations;

public enum AutomationOwnerEntityKind { Automation, ReusableTask }
public enum AutomationDefinitionChangeKind { Create, Update, Disable, Archive, Restore, PublishGraph, RecoverLegacy }
public enum AutomationValueAvailability { Present, Redacted, Unavailable }

/// <summary>Present JSON null remains distinct from an absent dictionary key, redaction or unavailable data.</summary>
public sealed class AutomationObservedValue
{
    private readonly JsonElement? _value;
    private AutomationObservedValue(AutomationValueAvailability availability, JsonElement? value)
    { Availability = availability; _value = value?.Clone(); }
    public AutomationValueAvailability Availability { get; }
    public JsonElement? Value => _value?.Clone();
    public static AutomationObservedValue Present(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Present data must have a defined JSON value.");
        return new(AutomationValueAvailability.Present, value);
    }
    public static AutomationObservedValue Redacted() => new(AutomationValueAvailability.Redacted, null);
    public static AutomationObservedValue Unavailable() => new(AutomationValueAvailability.Unavailable, null);
}

/// <summary>Detached exact proposal for one owning Home review. Its store ID and protected descriptors are
/// descriptive targets: only the actual SQL identity/current actor/Home binding can issue commit admission.</summary>
public sealed class AutomationDefinitionChange
{
    private readonly byte[] _proposal;
    private readonly JsonElement _arguments;
    private AutomationDefinitionChange(Guid storeID, Guid entityID, long expectedRevision,
        AutomationOwnerEntityKind entityKind, AutomationDefinitionChangeKind changeKind, object proposal, Guid operationID,
        AuthenticatedResourceActor originalActor, string? recoveryRowSHA256 = null)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (storeID == Guid.Empty || entityID == Guid.Empty || operationID == Guid.Empty || expectedRevision < 0)
            throw new ArgumentException("An exact canonical definition target is required.");
        _proposal = JsonSerializer.SerializeToUtf8Bytes(proposal);
        if (_proposal.Length > 2 * 1024 * 1024) throw new ArgumentException("The definition exceeds the bounded review payload.");
        StoreID = storeID; EntityID = entityID; ExpectedRevision = expectedRevision; EntityKind = entityKind;
        ChangeKind = changeKind; OperationID = operationID; OriginalActor = originalActor;
        if ((changeKind == AutomationDefinitionChangeKind.RecoverLegacy) != (recoveryRowSHA256 is not null) ||
            recoveryRowSHA256 is { Length: not 64 } || recoveryRowSHA256 is not null && !recoveryRowSHA256.All(Uri.IsHexDigit))
            throw new ArgumentException("Recovery requires an exact bounded raw-row digest.");
        RecoveryRowSHA256 = recoveryRowSHA256;
        ActionID = changeKind switch
        {
            AutomationDefinitionChangeKind.Create => "automations.create",
            AutomationDefinitionChangeKind.Update => "automations.update",
            AutomationDefinitionChangeKind.Disable => "automations.disable",
            AutomationDefinitionChangeKind.Archive => "automations.delete",
            AutomationDefinitionChangeKind.Restore => "automations.restore",
            AutomationDefinitionChangeKind.PublishGraph => "automations.activate",
            AutomationDefinitionChangeKind.RecoverLegacy => "automations.recover",
            _ => throw new ArgumentOutOfRangeException(nameof(changeKind))
        };
        using var snapshot = JsonDocument.Parse(_proposal);
        _arguments = JsonSerializer.SerializeToElement(new { operationID, storeID, entityID, entityKind,
            changeKind, expectedRevision, originalActor, recoveryRowSHA256, proposal = snapshot.RootElement });
        Scopes = Array.AsReadOnly(new[] { new ResourceScope(entityKind == AutomationOwnerEntityKind.Automation
            ? "automation.definition" : "automation.reusable-task", $"{storeID:D}/{entityID:D}",
            expectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Write) });
    }
    public const string TargetAppID = "automations";
    public Guid StoreID { get; }
    public Guid EntityID { get; }
    public long ExpectedRevision { get; }
    public AutomationOwnerEntityKind EntityKind { get; }
    public AutomationDefinitionChangeKind ChangeKind { get; }
    public Guid OperationID { get; }
    public string? RecoveryRowSHA256 { get; }
    public AuthenticatedResourceActor OriginalActor { get; }
    public string ActionID { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public string PayloadSHA256 => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(_arguments)));
    public AutomationDefinition? Automation => EntityKind == AutomationOwnerEntityKind.Automation
        ? JsonSerializer.Deserialize<AutomationDefinition>(_proposal) : null;
    public ReusableTaskDefinition? ReusableTask => EntityKind == AutomationOwnerEntityKind.ReusableTask
        ? JsonSerializer.Deserialize<ReusableTaskDefinition>(_proposal) : null;
    public static AutomationDefinitionChange Capture(Guid actualStoreID, AuthenticatedResourceActor originalActor, AutomationDefinition proposal,
        long expectedRevision, AutomationDefinitionChangeKind kind, Guid? operationID = null) =>
        new(actualStoreID, proposal.Id, expectedRevision, AutomationOwnerEntityKind.Automation, kind, proposal, operationID ?? Guid.NewGuid(), originalActor);
    public static AutomationDefinitionChange Capture(Guid actualStoreID, AuthenticatedResourceActor originalActor, ReusableTaskDefinition proposal,
        long expectedRevision, AutomationDefinitionChangeKind kind, Guid? operationID = null) =>
        new(actualStoreID, proposal.Id, expectedRevision, AutomationOwnerEntityKind.ReusableTask, kind, proposal, operationID ?? Guid.NewGuid(), originalActor);
    public static AutomationDefinitionChange CaptureLegacyRecovery(Guid actualStoreID, AuthenticatedResourceActor originalActor,
        AutomationOwnerRead<AutomationDefinition> observed, Guid? operationID = null) =>
        new(actualStoreID, observed.Value.Id, observed.Value.Revision, AutomationOwnerEntityKind.Automation,
            AutomationDefinitionChangeKind.RecoverLegacy, observed.Value with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention },
            operationID ?? Guid.NewGuid(), originalActor, ComputeRawRowSHA256(observed.RetainedProtectedDescriptors));
    public static AutomationDefinitionChange CaptureLegacyRecovery(Guid actualStoreID, AuthenticatedResourceActor originalActor,
        AutomationOwnerRead<ReusableTaskDefinition> observed, Guid? operationID = null) =>
        new(actualStoreID, observed.Value.Id, observed.Value.Revision, AutomationOwnerEntityKind.ReusableTask,
            AutomationDefinitionChangeKind.RecoverLegacy, observed.Value with { IsEnabled = false, OperationalState = AutomationOperationalState.NeedsAttention },
            operationID ?? Guid.NewGuid(), originalActor, ComputeRawRowSHA256(observed.RetainedProtectedDescriptors));
    public static string ComputeRawRowSHA256(IReadOnlyDictionary<string, string?> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(row.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new { pair.Key, pair.Value }).ToArray());
        if (bytes.Length > 2 * 1024 * 1024) throw new ArgumentException("The retained recovery row exceeds the review bound.");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public sealed record AutomationDefinitionCommitContext(ResourceStoreIdentity StoreIdentity, Guid EntityID,
    AutomationOwnerEntityKind EntityKind, long ExpectedRevision, Guid OperationID, string PayloadSHA256,
    string ActionID, AutomationOwnerBinding OwnerBinding);

/// <summary>Actual owner-issued admission is checked under SQL; it reads only raw Home/actor receipts,
/// never recursively resolves this repository. A caller implementation is not sufficient: the SQL owner
/// must also verify the issuing AutomationLocalStoreAuthority instance before accepting it.</summary>
public interface IAutomationDefinitionCommitAdmission
{
    ValueTask<bool> CheckAsync(AutomationDefinitionCommitContext context, CancellationToken cancellationToken);
}
public enum AutomationCommitReceiptObservation { CurrentCommitted, OutcomeUnconfirmed }
public sealed record AutomationCommitReceiptRead(AutomationCommitReceiptObservation Observation,
    AutomationOwnerCommitReceipt? Receipt);

public sealed record AutomationDefinitionCommitResult(bool Committed, string Code, long? Revision,
    Guid OperationID, string PayloadSHA256);
public sealed record AutomationOwnerRead<T>(T Value, bool RequiresRecovery, string? RecoveryCode,
    IReadOnlyDictionary<string, string?> RetainedProtectedDescriptors);
public sealed record AutomationLibraryQuery(bool IncludeDisabled = true, bool IncludeArchived = false,
    string Search = "", int Limit = 100, string? Cursor = null);
public sealed record AutomationLibraryPage<T>(IReadOnlyList<AutomationOwnerRead<T>> Items, string? NextCursor);

/// <summary>Same existing canonical SQL rows, including disabled and recoverable archived definitions.
/// Ordinary legacy Upsert/Delete are not this protected writer and cannot manufacture ownership or pins.</summary>
public interface IAutomationOwnerRepository
{
    ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken);
    Task<AutomationCommitReceiptRead> ObserveCommitAsync(Guid automationID, Guid operationID,
        string payloadSHA256, long expectedRevision, CancellationToken cancellationToken);
    Task<AutomationOwnerRead<AutomationDefinition>?> GetOwnedAsync(Guid automationID, CancellationToken cancellationToken);
    Task<AutomationLibraryPage<AutomationDefinition>> ListOwnedAsync(AutomationLibraryQuery query, CancellationToken cancellationToken);
    Task<AutomationDefinitionCommitResult> CompareExchangeOwnedAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, CancellationToken cancellationToken);
}
public interface IReusableTaskOwnerRepository
{
    Task<AutomationCommitReceiptRead> ObserveTaskCommitAsync(Guid taskID, Guid operationID,
        string payloadSHA256, long expectedRevision, CancellationToken cancellationToken);
    Task<AutomationOwnerRead<ReusableTaskDefinition>?> GetOwnedTaskAsync(Guid taskID, CancellationToken cancellationToken);
    Task<AutomationLibraryPage<ReusableTaskDefinition>> ListOwnedTasksAsync(AutomationLibraryQuery query, CancellationToken cancellationToken);
    Task<AutomationDefinitionCommitResult> CompareExchangeOwnedTaskAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, CancellationToken cancellationToken);
}
