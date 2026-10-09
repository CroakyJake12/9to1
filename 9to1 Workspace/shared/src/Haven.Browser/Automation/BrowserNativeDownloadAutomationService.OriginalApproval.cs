using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed partial class BrowserNativeDownloadAutomationService
{
    private readonly object _originalGate = new();
    private readonly ConditionalWeakTable<IBrowserOriginalNativeDownloadApproval, OriginalApproval> _originalApprovals = new();
    private readonly List<OriginalApproval> _originalPending = [];
    private readonly AsyncLocal<OriginalApproval?> _originalExecuting = new();
    [ThreadStatic] private static Dictionary<BrowserNativeDownloadAutomationService, int>? _originalPhysical;
    private Task? _originalClose;
    private readonly List<OriginalCancellation> _originalCancellations = [];
    private readonly ConditionalWeakTable<IBrowserNativeDownloadExecution, OriginalCancellation> _originalCancellationByExecution = new();
    private readonly AsyncLocal<int> _originalClosing = new();
    private sealed class OriginalCancellation(Guid actionId, IBrowserNativeDownloadExecution execution)
    {
        internal readonly Guid ActionId = actionId;
        internal readonly IBrowserNativeDownloadExecution Execution = execution;
        internal Task? Raw;
        internal Task Driver = null!;
        internal readonly List<Exception> Errors = [];
    }

    private sealed class OriginalApproval(BrowserPendingAction approved,
        bool isPrivate, IBrowserOriginalApprovedNativeDownloadExecution execution) : IBrowserOriginalNativeDownloadApproval
    {
        public Guid ActionId => approved.Id;
        public bool IsPrivate => isPrivate;
        internal readonly IBrowserOriginalApprovedNativeDownloadExecution Execution = execution;
        internal readonly TaskCompletionSource Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<Exception> Errors = [];
        internal Task<BrowserDownloadRecord>? Execute;
        internal BrowserDownloadRecord? Record;
        internal bool Completed;
    }

    private OriginalApproval CaptureOriginalApproval(BrowserPendingAction actualApproved,
        bool isPrivate, IBrowserOriginalApprovedNativeDownloadExecution sameExecution)
    {
        lock (_originalGate)
        {
            ThrowIfDisposed();
            if (_originalPending.Count >= 128)
                throw new InvalidOperationException("Join unresolved original browser approvals before admitting another transfer.");
            var receipt = new OriginalApproval(actualApproved, isPrivate, sameExecution);
            _originalApprovals.Add(receipt, receipt); _originalPending.Add(receipt);
            return receipt;
        }
    }

    private void InvokeOriginalApproval(OriginalApproval receipt, Action actual)
    {
        var physical = _originalPhysical ??= new(ReferenceEqualityComparer.Instance);
        physical.TryGetValue(this, out var depth); physical[this] = depth + 1;
        try { actual(); }
        catch (Exception cause) { CaptureOriginalApprovalFailure(receipt, cause); throw; }
        finally { if (depth == 0) physical.Remove(this); else physical[this] = depth; }
    }

    private void CompleteOriginalApproval(OriginalApproval receipt, BrowserDownloadRecord sameRecord)
    {
        lock (_originalGate)
        {
            if (receipt.Execute is not { IsCompletedSuccessfully: true } actual ||
                !ReferenceEquals(actual.Result, sameRecord) || sameRecord.ActionId != receipt.ActionId)
                throw new UnauthorizedAccessException("The SAME actual approved native execution result is required.");
            receipt.Record = sameRecord; receipt.Completed = true;
        }
    }

    private void CaptureOriginalApprovalFailure(OriginalApproval receipt, Exception observed)
    {
        lock (_originalGate)
        {
            if (receipt.Execute?.Exception is { } originalClrContainer)
                foreach (var actual in originalClrContainer.InnerExceptions) Add(actual);
            Add(observed);
            receipt.Completed = false;
            if (!_originalPending.Any(prior => ReferenceEquals(prior, receipt))) _originalPending.Add(receipt);
            void Add(Exception cause)
            {
                if (!receipt.Errors.Any(prior => ReferenceEquals(prior, cause))) receipt.Errors.Add(cause);
            }
        }
    }

    private void SettleOriginalApproval(OriginalApproval receipt)
    {
        lock (_originalGate)
        {
            receipt.Settled.TrySetResult();
            // Execute was independently awaited by the SAME approval driver. Healthy
            // terminal metadata stays weakly issued while its actual native owner lives.
            if (receipt.Completed && receipt.Errors.Count == 0)
                _originalPending.RemoveAll(prior => ReferenceEquals(prior, receipt));
        }
    }

    private OriginalApproval RequireOriginalApproval(IBrowserOriginalNativeDownloadApproval receipt) =>
        _originalApprovals.TryGetValue(receipt, out var actual) && ReferenceEquals(receipt, actual)
            ? actual : throw new UnauthorizedAccessException("The SAME original browser approval source is required.");

    public bool IsIssuedOriginalApproval(IBrowserOriginalNativeDownloadApproval originalApproval,
        IBrowserNativeDownloadExecution sameExecution) =>
        _originalApprovals.TryGetValue(originalApproval, out var actual) && ReferenceEquals(originalApproval, actual) &&
        ReferenceEquals(actual.Execution, sameExecution);

    public bool IsOriginalExecutionAdmission(IBrowserOriginalNativeDownloadApproval originalApproval,
        IBrowserNativeDownloadExecution sameExecution)
    {
        if (!IsIssuedOriginalApproval(originalApproval, sameExecution)) return false;
        lock (_originalGate)
        {
            var actual = RequireOriginalApproval(originalApproval);
            return Volatile.Read(ref _disposed) == 0 && actual.Errors.Count == 0 &&
                (!actual.Settled.Task.IsCompleted || actual.Completed);
        }
    }

    public Task<BrowserDownloadRecord>? GetOriginalExecutionTask(IBrowserOriginalNativeDownloadApproval originalApproval)
    { lock (_originalGate) return RequireOriginalApproval(originalApproval).Execute; }

    public bool IsOriginalApprovedCompletion(IBrowserOriginalNativeDownloadApproval originalApproval,
        IBrowserNativeDownloadExecution sameExecution, Task<BrowserDownloadRecord> sameExecutionTask,
        BrowserDownloadRecord sameRecord)
    {
        if (!IsIssuedOriginalApproval(originalApproval, sameExecution)) return false;
        lock (_originalGate)
        {
            var actual = RequireOriginalApproval(originalApproval);
            return Volatile.Read(ref _disposed) == 0 && actual.Settled.Task.IsCompletedSuccessfully &&
                actual.Completed && actual.Errors.Count == 0 &&
                ReferenceEquals(actual.Execute, sameExecutionTask) && sameExecutionTask.IsCompletedSuccessfully &&
                ReferenceEquals(sameExecutionTask.Result, sameRecord) && ReferenceEquals(actual.Record, sameRecord);
        }
    }

    public async Task WaitOriginalApprovalSettlementWithinSourceAsync(
        IBrowserOriginalNativeDownloadApproval originalApproval, Action<Action> scope,
        Action<Task> retain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var actual = RequireOriginalApproval(originalApproval);
        if (ReferenceEquals(_originalExecuting.Value, actual) || _originalPhysical?.ContainsKey(this) == true)
            throw new InvalidOperationException("The original execution cannot await its own browser approval settlement.");
        Task? raw = null; Exception? acquisition = null;
        try
        {
            InvokeOriginalApproval(actual, () =>
            {
                var used = 0; var active = 1; var thread = Environment.CurrentManagedThreadId;
                try
                {
                    Exception? protocol = null;
                    try
                    {
                        scope(() =>
                        {
                            try
                            {
                                if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId ||
                                    Interlocked.Exchange(ref used, 1) != 0)
                                    throw new InvalidOperationException("The original approval callback was inactive, repeated or foreign-thread.");
                                raw = actual.Settled.Task; retain(raw);
                            }
                            catch (Exception cause)
                            {
                                protocol ??= cause; CaptureOriginalApprovalFailure(actual, cause); throw;
                            }
                        });
                    }
                    catch (Exception cause)
                    {
                        if (protocol is not null && !ReferenceEquals(protocol, cause))
                            throw new AggregateException("Original callback and caller scope both failed.", protocol, cause);
                        throw;
                    }
                    if (protocol is not null) ExceptionDispatchInfo.Capture(protocol).Throw();
                    if (used != 1) throw new InvalidOperationException("The original approval callback was not invoked.");
                }
                finally { Volatile.Write(ref active, 0); }
            });
        }
        catch (Exception cause) { acquisition = cause; }
        if (raw is not null) await raw.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (acquisition is not null) ExceptionDispatchInfo.Capture(acquisition).Throw();
        if (raw is null) throw new InvalidOperationException("No original approval settlement Task was captured.");
    }

    private Task CancelOriginalExecutionAsync(Guid actionId, IBrowserNativeDownloadExecution sameExecution)
    {
        OriginalCancellation original; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_originalGate)
        {
            if (_originalCancellationByExecution.TryGetValue(sameExecution, out original!)) return original.Driver;
            original = new(actionId, sameExecution); original.Driver = Drive();
            _originalCancellations.Add(original); _originalCancellationByExecution.Add(sameExecution, original);
        }
        start.SetResult(); return original.Driver;
        async Task Drive()
        {
            await start.Task.ConfigureAwait(false); _originalClosing.Value++;
            try
            {
                var physical = _originalPhysical ??= new(ReferenceEqualityComparer.Instance);
                physical.TryGetValue(this, out var depth); physical[this] = depth + 1;
                try { original.Raw = sameExecution.CancelAsync(CancellationToken.None)
                    ?? throw new InvalidOperationException("The original native cancellation returned no Task."); }
                catch (Exception cause) { lock (_originalGate) original.Errors.Add(cause); }
                finally { if (depth == 0) physical.Remove(this); else physical[this] = depth; }
                if (original.Raw is { } raw)
                    try { await raw.ConfigureAwait(false); }
                    catch (Exception observed)
                    {
                        IEnumerable<Exception> causes = raw.Exception is { } clrContainer ? clrContainer.InnerExceptions : [observed];
                        lock (_originalGate) foreach (var cause in causes)
                            if (!original.Errors.Any(prior => ReferenceEquals(prior, cause))) original.Errors.Add(cause);
                    }
                Exception[] failures; lock (_originalGate) failures = original.Errors.ToArray();
                if (failures.Length == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
                if (failures.Length != 0) throw new AggregateException("Actual native cancellation did not settle.", failures);
            }
            finally { _originalClosing.Value--; }
        }
    }

    private async Task JoinOriginalApprovalsAsync()
    {
        OriginalApproval[] originals; lock (_originalGate) originals = _originalPending.ToArray();
        var errors = new List<Exception>();
        foreach (var actual in originals)
        {
            await actual.Settled.Task.ConfigureAwait(false);
            if (actual.Execute is { } raw)
                try { await raw.ConfigureAwait(false); }
                catch (Exception cause) { CaptureOriginalApprovalFailure(actual, cause); }
            lock (_originalGate)
                foreach (var cause in actual.Errors)
                    if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
        }
        OriginalCancellation[] cancellations; lock (_originalGate) cancellations = _originalCancellations.ToArray();
        foreach (var original in cancellations)
        {
            try { await original.Driver.ConfigureAwait(false); } catch { /* exact raw causes are retained below */ }
            lock (_originalGate) foreach (var cause in original.Errors)
                if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Original browser approval/cancellation sources failed; preserve their actual causes.", errors);
    }

    public ValueTask DisposeAsync()
    {
        if (_originalCommand.Value is not null || _originalExecuting.Value is not null || _originalClosing.Value != 0 || _originalPhysical?.ContainsKey(this) == true)
            throw new InvalidOperationException("An original browser callback cannot join its own service close.");
        lock (_originalGate)
        {
            if (_originalClose is not null) return new(_originalClose);
            Interlocked.Exchange(ref _disposed, 1);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = DisposeOriginalDriverAsync(start.Task); start.SetResult();
            return new(_originalClose);
        }
    }
}
