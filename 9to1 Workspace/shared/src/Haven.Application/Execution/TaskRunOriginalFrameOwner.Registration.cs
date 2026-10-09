using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Haven.Application;

public enum TaskRunOriginalAttemptRegistrationKind
{
    Registered = 0,
    RefusedBeforeOwnership = 1
}

/// <summary>Opaque observation issued atomically by the actual frame registry. A registered
/// original still requires its actual registration and settlement; a refusal grants no provider
/// use. The exact refused admission is sealed against all later registration by this owner.</summary>
public sealed class TaskRunOriginalAttemptRegistrationDisposition
{
    internal TaskRunOriginalAttemptRegistrationDisposition(TaskRunOriginalAttemptRegistrationKind kind,
        TaskRunAttemptAdmission admission, Task? registration, Exception? refusal)
    { Kind = kind; OriginalAdmission = admission; OriginalRegistration = registration; OriginalRefusal = refusal; }
    public TaskRunOriginalAttemptRegistrationKind Kind { get; }
    public TaskRunAttemptAdmission OriginalAdmission { get; }
    public Task? OriginalRegistration { get; }
    public Exception? OriginalRefusal { get; }
    /// <summary>Exact terminal originals observed at issuance; null conveys no settlement.</summary>
    public Task? OriginalSettlement { get; internal init; }
    public Task? OriginalLeaseClose { get; internal init; }
    public TaskRunOriginalRetirementAcknowledgment? OriginalRetirementAcknowledgment { get; internal init; }
}

/// <summary>Implemented by the SAME configured frame/settlement owner. IDs, copied receipts,
/// canceled tokens and lookup errors cannot establish absence of runtime lease ownership.</summary>
public interface ITaskRunOriginalAttemptRegistrationSource
{
    TaskRunOriginalAttemptRegistrationDisposition RegisterOriginalAttemptDisposition(
        TaskRunAttemptAdmission sameOriginalAdmission, CancellationToken cancellationToken);
    bool IsIssuedOriginalAttemptRegistrationDisposition(
        TaskRunOriginalAttemptRegistrationDisposition disposition, TaskRunAttemptAdmission sameOriginalAdmission);
}

public sealed partial class TaskRunOriginalFrameOwner : ITaskRunOriginalAttemptRegistrationSource
{
    private readonly ConditionalWeakTable<TaskRunAttemptAdmission, Attempt> _originalRetiredRegistrations = new();
    private readonly ConditionalWeakTable<TaskRunAttemptAdmission, RegistrationRefusal> _originalRegistrationRefusals = new();
    private readonly ConditionalWeakTable<TaskRunOriginalAttemptRegistrationDisposition, RegistrationDisposition> _originalRegistrationDispositions = new();

    public TaskRunOriginalAttemptRegistrationDisposition RegisterOriginalAttemptDisposition(
        TaskRunAttemptAdmission sameOriginalAdmission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sameOriginalAdmission);
        TaskCompletionSource? gate = null;
        TaskRunOriginalAttemptRegistrationDisposition issued;
        lock (_sync)
        {
            // Existing ownership is inspected BEFORE cancellation/close refusal. The same lease
            // cannot become never-admitted because this particular caller's wait was canceled.
            var key = Key(sameOriginalAdmission);
            if (_attempts.TryGetValue(key, out var existing))
            {
                RequireSame(existing, sameOriginalAdmission); // Collision is unknown, not absence.
                issued = IssueOriginalRegistration(existing);
            }
            else if (_originalRetiredRegistrations.TryGetValue(sameOriginalAdmission, out var retired))
                issued = IssueOriginalRegistration(retired);
            else if (_originalRegistrationRefusals.TryGetValue(sameOriginalAdmission, out var refused))
                issued = IssueOriginalRefusal(sameOriginalAdmission, refused);
            else
            {
                Exception? cause = cancellationToken.IsCancellationRequested ? new OperationCanceledException(cancellationToken)
                    : _closing ? new ObjectDisposedException(nameof(TaskRunOriginalFrameOwner))
                    : _capacityRefusal is not null || _attempts.Count >= Capacity
                        ? _capacityRefusal ??= new InvalidOperationException("Original attempt custody is full; a new owner is required.")
                        : null;
                if (cause is not null)
                {
                    // This exact object can never be adopted later, even if the token is replaced.
                    // Weak keys preserve the refusal while its admission is held by its actual issuer.
                    var refusal = new RegistrationRefusal(cause);
                    _originalRegistrationRefusals.Add(sameOriginalAdmission, refusal);
                    issued = IssueOriginalRefusal(sameOriginalAdmission, refusal);
                }
                else
                {
                    var attempt = new Attempt(sameOriginalAdmission);
                    gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    attempt.Registration = RegisterCoreAsync(attempt, gate.Task);
                    _attempts.Add(key, attempt);
                    issued = IssueOriginalRegistration(attempt);
                }
            }
        }
        gate?.SetResult(); // Receipt, original Task and registry entry precede issuer callbacks.
        return issued;
    }

    public bool IsIssuedOriginalAttemptRegistrationDisposition(
        TaskRunOriginalAttemptRegistrationDisposition disposition, TaskRunAttemptAdmission sameOriginalAdmission)
    {
        if (disposition is null || sameOriginalAdmission is null) return false;
        lock (_sync)
        {
            if (!_originalRegistrationDispositions.TryGetValue(disposition, out var original)
                || !ReferenceEquals(disposition.OriginalAdmission, sameOriginalAdmission)
                || !ReferenceEquals(original.Admission, sameOriginalAdmission)) return false;
            if (original.Attempt is { } attempt)
                return disposition.Kind == TaskRunOriginalAttemptRegistrationKind.Registered
                    && ReferenceEquals(attempt.Admission, sameOriginalAdmission)
                    && ReferenceEquals(disposition.OriginalRegistration, attempt.Registration)
                    && disposition.OriginalRefusal is null
                    && ReferenceEquals(disposition.OriginalSettlement, original.Settlement)
                    && ReferenceEquals(disposition.OriginalLeaseClose, original.LeaseClose)
                    && ReferenceEquals(disposition.OriginalRetirementAcknowledgment, original.Retirement)
                    && (original.Settlement is null || ReferenceEquals(original.Settlement, attempt.Settlement))
                    && (original.LeaseClose is null || ReferenceEquals(original.LeaseClose, attempt.OriginalLeaseClose))
                    && (original.Retirement is null || ReferenceEquals(original.Retirement, attempt.RetirementReceipt));
            return disposition.Kind == TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership
                && disposition.OriginalRegistration is null
                && _originalRegistrationRefusals.TryGetValue(sameOriginalAdmission, out var refused)
                && ReferenceEquals(refused, original.Refusal)
                && ReferenceEquals(disposition.OriginalRefusal, refused.Cause);
        }
    }

    private TaskRunOriginalAttemptRegistrationDisposition IssueOriginalRegistration(Attempt attempt)
    {
        var receipt = new TaskRunOriginalAttemptRegistrationDisposition(TaskRunOriginalAttemptRegistrationKind.Registered,
            attempt.Admission, attempt.Registration!, null)
        {
            OriginalSettlement = attempt.Settlement,
            OriginalLeaseClose = attempt.OriginalLeaseClose,
            OriginalRetirementAcknowledgment = attempt.RetirementReceipt
        };
        _originalRegistrationDispositions.Add(receipt, new(attempt.Admission, attempt, null,
            attempt.Settlement, attempt.OriginalLeaseClose, attempt.RetirementReceipt));
        return receipt;
    }
    private TaskRunOriginalAttemptRegistrationDisposition IssueOriginalRefusal(TaskRunAttemptAdmission admission, RegistrationRefusal refusal)
    {
        var receipt = new TaskRunOriginalAttemptRegistrationDisposition(TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership,
            admission, null, refusal.Cause);
        _originalRegistrationDispositions.Add(receipt, new(admission, null, refusal));
        return receipt;
    }
    private void PreserveOriginalRetiredRegistration(Attempt attempt)
    {
        // SAME private historical owner persists weakly while the real issuer retains its admission.
        // Pruning healthy capacity must never manufacture a never-owned lease observation.
        _originalRetiredRegistrations.Add(attempt.Admission, attempt);
    }
    private void DemandNoOriginalRegistrationRefusal(TaskRunAttemptAdmission admission)
    {
        if (_originalRetiredRegistrations.TryGetValue(admission, out _))
            throw new InvalidOperationException("This exact original admission was already registered and retired; it cannot be adopted again.");
        if (_originalRegistrationRefusals.TryGetValue(admission, out var refusal))
            ExceptionDispatchInfo.Capture(refusal.Cause).Throw();
    }
    private sealed record RegistrationRefusal(Exception Cause);
    private sealed record RegistrationDisposition(TaskRunAttemptAdmission Admission, Attempt? Attempt,
        RegistrationRefusal? Refusal, Task? Settlement = null, Task? LeaseClose = null,
        TaskRunOriginalRetirementAcknowledgment? Retirement = null);
}
