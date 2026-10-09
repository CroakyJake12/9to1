using System.Collections.Concurrent;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

/// <summary>
/// Decorates the existing browser automation service with approval-gated native WebView downloads.
/// Normal automation remains owned by the wrapped service; only native download actions are intercepted here.
/// </summary>
public sealed partial class BrowserNativeDownloadAutomationService : IBrowserAutomationService, IBrowserNativeDownloadService, IBrowserOriginalNativeDownloadApprovalSource, IAsyncDisposable
{
    private readonly IBrowserAutomationService _inner;
    private readonly IBrowserNavigationPolicy _policy;
    private readonly IBrowserAutomationStore _store;
    private readonly ConcurrentDictionary<Guid, IBrowserNativeDownloadExecution> _executions = new();
    private readonly ConcurrentDictionary<Guid, BrowserPendingAction> _privatePending = new();
    private readonly SemaphoreSlim _nativeActionGate = new(1, 1);
    private int _disposed;

    public BrowserNativeDownloadAutomationService(
        IBrowserAutomationService inner,
        IBrowserNavigationPolicy policy,
        IBrowserAutomationStore store)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken) =>
        _inner.CapturePageAsync(cancellationToken);

    public Task<string> NavigateAsync(string address, CancellationToken cancellationToken) =>
        _inner.NavigateAsync(address, cancellationToken);

    public Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken) =>
        _inner.ClickReferenceAsync(reference, cancellationToken);

    public Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken) =>
        _inner.FillReferenceAsync(reference, value, cancellationToken);

    public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken) =>
        _inner.RequestDownloadAsync(address, suggestedFileName, cancellationToken);

    public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) =>
        _inner.GetAuditAsync(limit, cancellationToken);

    public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) =>
        _inner.GetDownloadsAsync(limit, cancellationToken);

    public Task<BrowserPendingAction> RequestNativeDownloadAsync(
        BrowserNativeDownloadRequest request, IBrowserNativeDownloadExecution execution, CancellationToken cancellationToken) =>
        RunOriginalCommandAsync(execution, () => RequestNativeDownloadCoreAsync(request, execution, cancellationToken));

    private async Task<BrowserPendingAction> RequestNativeDownloadCoreAsync(
        BrowserNativeDownloadRequest request, IBrowserNativeDownloadExecution execution, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureNativeAutomationAllowed();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(execution);
        if (request.ActionId == Guid.Empty)
            throw new ArgumentException("A native download requires a non-empty action id.", nameof(request));
        if (!request.ApprovalAddress.IsAbsoluteUri
            || request.ApprovalAddress.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(request.ApprovalAddress.UserInfo))
            throw new UnauthorizedAccessException("Native downloads require an HTTP or HTTPS page origin without embedded credentials.");

        var assessment = await ReadOriginalCommandSourceAsync(() => _policy.AssessAsync(request.ApprovalAddress, cancellationToken)).ConfigureAwait(false);
        ThrowIfDisposed();
        if (!assessment.IsAllowed)
            throw new UnauthorizedAccessException("Download blocked: " + assessment.Reason);

        var now = DateTimeOffset.UtcNow;
        var fileName = BrowserDownloadFilePolicy.SanitizeFileName(request.SuggestedFileName) ?? "download";
        var redactedAddress = request.ApprovalAddress.GetLeftPart(UriPartial.Path);
        var action = new BrowserPendingAction(
            request.ActionId,
            BrowserActionKind.Download,
            request.IsPrivate ? "private" : request.ApprovalAddress.GetLeftPart(UriPartial.Authority),
            request.IsPrivate ? $"Private download {fileName}" : $"Download {redactedAddress}",
            request.IsPrivate ? $"private-native:{request.ActionId:N}" : $"native:{request.ActionId:N}",
            fileName,
            BrowserActionState.Pending,
            now,
            now.AddMinutes(10),
            now,
            null);

        if (!_executions.TryAdd(action.Id, execution))
            throw new InvalidOperationException("A native download with this action id is already pending.");
        try
        {
            if (request.IsPrivate)
            {
                if (!_privatePending.TryAdd(action.Id, action))
                    throw new InvalidOperationException("A private native download with this action id is already pending.");
            }
            else
            {
                await ReadOriginalCommandSourceAsync(() => _store.AddPendingAsync(action, cancellationToken)).ConfigureAwait(false);
                await TryAuditAsync(action.Kind, "approval-requested", action.Origin, action.Summary, true).ConfigureAwait(false);
            }
            return action;
        }
        catch
        {
            _executions.TryRemove(action.Id, out _);
            _privatePending.TryRemove(action.Id, out _);
            try { await CancelOriginalExecutionAsync(request.ActionId, execution).ConfigureAwait(false); } catch { /* SAME cancellation originals remain owned */ }
            throw;
        }
    }

    public Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken) =>
        RunOriginalCommandAsync(_executions.TryGetValue(actionId, out var execution) ? execution : null,
            () => ApproveCoreAsync(actionId, cancellationToken));

    private async Task<BrowserActionExecutionResult> ApproveCoreAsync(Guid actionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_executions.ContainsKey(actionId))
            return await _inner.ApproveAsync(actionId, cancellationToken).ConfigureAwait(false);

        EnsureNativeAutomationAllowed();
        await ReadOriginalCommandSourceAsync(() => _nativeActionGate.WaitAsync(cancellationToken)).ConfigureAwait(false);
        try
        {
            if (!_executions.TryGetValue(actionId, out var execution))
                return await _inner.ApproveAsync(actionId, cancellationToken).ConfigureAwait(false);

            var isPrivate = _privatePending.TryGetValue(actionId, out var action);
            action ??= await ReadOriginalCommandSourceAsync(() => _store.GetActionAsync(actionId, cancellationToken)).ConfigureAwait(false);
            if (action is null)
            {
                await CancelAndForgetAsync(actionId, execution).ConfigureAwait(false);
                throw new KeyNotFoundException("The native browser download approval no longer exists.");
            }
            if (action.State != BrowserActionState.Pending)
                return new BrowserActionExecutionResult(action.Id, action.State, $"The action is already {action.State.ToString().ToLowerInvariant()}.");
            if (action.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                await CancelAndForgetAsync(actionId, execution).ConfigureAwait(false);
                var expired = action with { State = BrowserActionState.Expired, UpdatedAt = DateTimeOffset.UtcNow, Failure = "The approval expired." };
                if (!isPrivate) await ReadOriginalCommandSourceAsync(() => _store.UpdateActionAsync(expired, CancellationToken.None)).ConfigureAwait(false);
                return new BrowserActionExecutionResult(action.Id, expired.State, "The approval expired; request the download again.");
            }

            var approved = action with { State = BrowserActionState.Approved, UpdatedAt = DateTimeOffset.UtcNow, Failure = null };
            if (isPrivate) _privatePending[actionId] = approved;
            else await ReadOriginalCommandSourceAsync(() => _store.UpdateActionAsync(approved, cancellationToken)).ConfigureAwait(false);
            ThrowIfDisposed();

            BrowserDownloadRecord? download = null;
            OriginalApproval? originalApproval = null;
            var previousOriginal = _originalExecuting.Value;
            try
            {
                if (execution is IBrowserOriginalApprovedNativeDownloadExecution originalExecution)
                {
                    originalApproval = CaptureOriginalApproval(approved, isPrivate, originalExecution);
                    _originalExecuting.Value = originalApproval;
                    InvokeOriginalApproval(originalApproval, () => originalExecution.BindOriginalDownloadApproval(this, originalApproval));
                    InvokeOriginalApproval(originalApproval, () => originalApproval.Execute = execution.ExecuteAsync(cancellationToken)
                        ?? throw new InvalidOperationException("The original native execution returned no Task."));
                    download = await originalApproval.Execute!.ConfigureAwait(false);
                }
                else download = await ReadOriginalCommandSourceAsync(() => execution.ExecuteAsync(cancellationToken)).ConfigureAwait(false);
                if (!isPrivate)
                {
                    await ReadOriginalCommandSourceAsync(() => _store.AddDownloadAsync(download, CancellationToken.None)).ConfigureAwait(false);
                    var executed = approved with { State = BrowserActionState.Executed, UpdatedAt = DateTimeOffset.UtcNow, Failure = null };
                    await ReadOriginalCommandSourceAsync(() => _store.UpdateActionAsync(executed, CancellationToken.None)).ConfigureAwait(false);
                    await TryAuditAsync(action.Kind, "executed", action.Origin, $"Downloaded {download.FileName} ({download.SizeBytes:N0} bytes).", true).ConfigureAwait(false);
                }
                if (originalApproval is not null) CompleteOriginalApproval(originalApproval, download);
                Forget(actionId);
                return new BrowserActionExecutionResult(action.Id, BrowserActionState.Executed,
                    $"Downloaded {download.FileName} ({download.SizeBytes:N0} bytes) to Haven's Downloads folder.", download);
            }
            catch (Exception exception)
            {
                if (originalApproval is not null) CaptureOriginalApprovalFailure(originalApproval, exception);
                Forget(actionId);
                var failure = Bound(exception.Message, 1_000);
                if (!isPrivate)
                {
                    var failed = approved with { State = BrowserActionState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Failure = failure };
                    await TryUpdateActionAsync(failed).ConfigureAwait(false);
                    await TryAuditAsync(action.Kind, "execution-failed", action.Origin, failure, false).ConfigureAwait(false);
                }
                return new BrowserActionExecutionResult(action.Id, BrowserActionState.Failed, "Browser download failed: " + exception.Message, download);
            }
            finally
            {
                _originalExecuting.Value = previousOriginal;
                if (originalApproval is not null) SettleOriginalApproval(originalApproval);
            }
        }
        finally
        {
            _nativeActionGate.Release();
        }
    }

    public Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken) =>
        RunOriginalCommandAsync(_executions.TryGetValue(actionId, out var execution) ? execution : null,
            () => RejectCoreAsync(actionId, cancellationToken));

    private async Task<BrowserActionExecutionResult> RejectCoreAsync(Guid actionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_executions.ContainsKey(actionId))
            return await _inner.RejectAsync(actionId, cancellationToken).ConfigureAwait(false);

        await ReadOriginalCommandSourceAsync(() => _nativeActionGate.WaitAsync(cancellationToken)).ConfigureAwait(false);
        try
        {
            if (!_executions.TryGetValue(actionId, out var execution))
                return await _inner.RejectAsync(actionId, cancellationToken).ConfigureAwait(false);

            var isPrivate = _privatePending.TryGetValue(actionId, out var action);
            action ??= await ReadOriginalCommandSourceAsync(() => _store.GetActionAsync(actionId, cancellationToken)).ConfigureAwait(false);
            if (action is null)
            {
                await CancelAndForgetAsync(actionId, execution).ConfigureAwait(false);
                throw new KeyNotFoundException("The native browser download approval no longer exists.");
            }

            await CancelOriginalExecutionAsync(actionId, execution).ConfigureAwait(false);
            Forget(actionId);
            var rejected = action with { State = BrowserActionState.Rejected, UpdatedAt = DateTimeOffset.UtcNow, Failure = "Rejected by the user." };
            if (!isPrivate)
            {
                await ReadOriginalCommandSourceAsync(() => _store.UpdateActionAsync(rejected, CancellationToken.None)).ConfigureAwait(false);
                await TryAuditAsync(action.Kind, "rejected", action.Origin, action.Summary, true).ConfigureAwait(false);
            }
            return new BrowserActionExecutionResult(action.Id, rejected.State, "Browser download rejected.");
        }
        finally
        {
            _nativeActionGate.Release();
        }
    }

    public async Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var persisted = await _inner.GetPendingAsync(cancellationToken).ConfigureAwait(false);
        var privateActions = _privatePending.Values
            .Where(item => item.State == BrowserActionState.Pending)
            .OrderBy(item => item.CreatedAt)
            .ToArray();
        if (privateActions.Length == 0) return persisted;
        return persisted.Concat(privateActions).OrderBy(item => item.CreatedAt).ToArray();
    }

    private static void EnsureNativeAutomationAllowed()
    {
        if (RuntimeSafetyState.IsSafeMode)
            throw new InvalidOperationException($"Browser automation is disabled in crash-loop recovery safe mode. {RuntimeSafetyState.Reason}");
    }

    private async Task CancelAndForgetAsync(Guid actionId, IBrowserNativeDownloadExecution execution)
    {
        try { await CancelOriginalExecutionAsync(actionId, execution).ConfigureAwait(false); } catch { }
        Forget(actionId);
    }

    private void Forget(Guid actionId)
    {
        _executions.TryRemove(actionId, out _);
        _privatePending.TryRemove(actionId, out _);
    }

    private async Task TryUpdateActionAsync(BrowserPendingAction action)
    {
        try { await ReadOriginalCommandSourceAsync(() => _store.UpdateActionAsync(action, CancellationToken.None)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine("Native browser action state persistence failed: " + exception.Message);
        }
    }

    private async Task TryAuditAsync(BrowserActionKind kind, string operation, string origin, string detail, bool succeeded)
    {
        try
        {
            await ReadOriginalCommandSourceAsync(() => _store.AddAuditAsync(new BrowserAuditEntry(
                Guid.NewGuid(), kind, operation, origin, Bound(detail, 2_000), succeeded, DateTimeOffset.UtcNow), CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("Native browser audit persistence failed: " + exception.Message);
        }
    }

    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum] + "…";

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

    private async Task DisposeOriginalDriverAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Interlocked.Exchange(ref _disposed, 1);
        // Cancel accepted native owners before joining a driver waiting for native
        // completion or the action gate. Admission was sealed before this snapshot.
        OriginalCommand[] commands; lock (_originalGate) commands = _originalCommands.ToArray();
        var executions = _executions.Values.Concat(commands.Select(command => command.Execution).OfType<IBrowserNativeDownloadExecution>())
            .Distinct<IBrowserNativeDownloadExecution>(ReferenceEqualityComparer.Instance).ToArray();
        var failures = new List<Exception>();
        foreach (var execution in executions)
            try { await CancelOriginalExecutionAsync(Guid.Empty, execution).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        try { await JoinOriginalCommandsAsync(commands).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        try { await JoinOriginalApprovalsAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Original native download command and transfer custody did not settle.", failures);
        _executions.Clear(); _privatePending.Clear();
        // App-lifetime service: do not dispose the semaphore while an in-flight approval may still release it.
    }
}
