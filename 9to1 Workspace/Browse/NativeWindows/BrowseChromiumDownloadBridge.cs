using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Microsoft.Web.WebView2.Core;

namespace HavenOS.Apps.Browse;

/// <summary>The original page transfer enters the canonical Browser approval
/// service and transport. No URL replay, copied cookies or private ledger.</summary>
internal sealed class BrowseChromiumDownloadBridge : IAsyncDisposable
{
    private readonly CoreWebView2 _core;
    private readonly bool _private;
    private readonly BrowserDownloadTransport? _transport;
    private readonly IBrowserNativeDownloadService? _downloads;
    private readonly List<Task> _originalSources = [];
    private readonly List<NativeExecution> _originalExecutions = [];
    private Task? _originalClose;
    private bool _retiring;
    internal bool CanRetireOriginal => _originalExecutions.All(execution => execution.CanRetireOriginal);
    internal BrowseChromiumDownloadBridge(CoreWebView2 originalCore, bool isPrivate,
        BrowserDownloadTransport? originalTransport, IBrowserNativeDownloadService? originalDownloads)
    {
        _core = originalCore; _private = isPrivate;
        _transport = originalTransport; _downloads = originalDownloads;
        _core.DownloadStarting += OnDownloadStarting;
    }
    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
        args.Handled = true;
        if (_retiring || _transport is null || _downloads is null) { args.Cancel = true; return; }
        _originalSources.RemoveAll(source => source.IsCompletedSuccessfully);
        if (_originalSources.Count >= 128 || _originalExecutions.Count >= 128) { args.Cancel = true; return; }
        var originalDeferral = args.GetDeferral(); var originalOperation = args.DownloadOperation;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = AdmitAsync(start.Task, args, originalOperation, originalDeferral);
        _originalSources.Add(original);
        try { originalOperation.Pause(); start.SetResult(); }
        catch (Exception failure) { start.SetException(failure); }
        var originalObserver = ObserveAdmissionAsync(original); _originalSources.Add(originalObserver);
    }
    private static async Task ObserveAdmissionAsync(Task original)
    {
        try { await original; }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retained its original native download admission failure: {0}", failure); }
    }
    private async Task AdmitAsync(Task start, CoreWebView2DownloadStartingEventArgs args,
        CoreWebView2DownloadOperation operation, CoreWebView2Deferral deferral)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        var failures = new List<Exception>();
        try
        {
            await start;
            var sameTransport = _transport ?? throw new InvalidOperationException("The original native download transport is unavailable.");
            if (operation.TotalBytesToReceive is { } total && total > (ulong)BrowserDownloadTransport.MaximumDownloadBytes)
                throw new InvalidOperationException("The page download exceeds the canonical Browser size limit.");
            if (!Uri.TryCreate(operation.Uri, UriKind.Absolute, out var source)) throw new InvalidDataException("The original page download has no valid URI.");
            var initiator = Uri.TryCreate(_core.Source, UriKind.Absolute, out var actualInitiator) ? actualInitiator : null;
            var approval = source.Scheme is "http" or "https" ? source : initiator is { Scheme: "http" or "https" }
                ? initiator : throw new UnauthorizedAccessException("Browser-local downloads need an actual HTTP or HTTPS page origin.");
            var actionId = Guid.NewGuid();
            var originalPlan = OriginalNativeInvocation.Acquire(this, () => sameTransport.PrepareNativeDownloadAsync(actionId, source, initiator,
                string.IsNullOrWhiteSpace(args.ResultFilePath) ? null : Path.GetFileName(args.ResultFilePath), operation.ContentDisposition, CancellationToken.None));
            _originalSources.Add(originalPlan);
            var plan = await originalPlan;
            args.ResultFilePath = plan.PartialPath;
            var execution = new NativeExecution(operation, plan, sameTransport, _private); _originalExecutions.Add(execution);
            var originalAdmission = OriginalNativeInvocation.Acquire(this, () => _downloads!.RequestNativeDownloadAsync(new(actionId, approval, plan.FileName, _private), execution, CancellationToken.None));
            _originalSources.Add(originalAdmission); await originalAdmission;
        }
        catch (Exception failure)
        {
            failures.Add(failure); args.Cancel = true;
            try { using var invocation = OriginalNativeInvocation.EnterExternal(this); operation.Cancel(); } catch (Exception retirementFailure) { failures.Add(retirementFailure); }
        }
        finally
        {
            try { deferral.Complete(); } catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("Native download admission retained its original failures.", failures);
    }
    internal IBrowserOriginalDownloadContent? ObserveOriginalDownloadContent(BrowserDownloadRecord currentCanonicalRow)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var invocation = OriginalNativeInvocation.EnterExternal(this);
        if (_retiring) return null;
        var candidates = _originalExecutions.Select(execution => execution.ObserveOriginalContent(currentCanonicalRow))
            .Where(content => content is not null).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
    internal void DemandExternalJoin()
    {
        OriginalNativeInvocation.DemandExternalJoin(this);
        using var guard = OriginalNativeInvocation.EnterExternal(this);
        foreach (var execution in _originalExecutions) execution.DemandExternalJoin();
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalJoin();
        if (_originalClose is not null) return new(_originalClose);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retiring = true; _originalClose = CloseAsync(start.Task); start.SetResult(); return new(_originalClose);
    }
    private async Task CloseAsync(Task start)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        await start; var failures = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            var originals = _originalSources.Where(source => !joined.Contains(source)).Distinct().ToArray();
            if (originals.Length == 0) break;
            foreach (var source in originals)
            { joined.Add(source); try { await source; } catch (Exception failure) { failures.Add(failure); } }
        }
        foreach (var execution in _originalExecutions.ToArray())
            try { await execution.CloseOriginalAsync(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Native download owner retained actual failed sources.", failures);
        var originalDetach = Dispatcher.UIThread.InvokeAsync(() => { using var invocation = OriginalNativeInvocation.EnterExternal(this); _core.DownloadStarting -= OnDownloadStarting; }).GetTask();
        _originalSources.Add(originalDetach); await originalDetach;
    }
    private sealed class NativeExecution : IBrowserOriginalApprovedNativeDownloadExecution, IBrowserOriginalNativeDownloadCompletionSource
    {
        private readonly CoreWebView2DownloadOperation _operation;
        private readonly BrowserDownloadTransport.NativeDownloadPlan _plan;
        private readonly BrowserDownloadTransport _transport;
        private readonly bool _isPrivate;
        private readonly IBrowserOriginalNativeDownloadTransportPlan? _originalTransportPlan;
        private IBrowserOriginalNativeDownloadApprovalSource? _originalApprovalSource;
        private IBrowserOriginalNativeDownloadApproval? _originalApproval;
        private CompletionObservation? _originalCompletedTransfer;
        private IBrowserOriginalDownloadContent? _originalContent;
        private Task? _originalContentClose;
        private bool _originalContentOwned;
        private NativeCompletionObservation? _originalNativeObservation;
        private readonly TaskCompletionSource<BrowserDownloadRecord> _originalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Task> _originalSources = [];
        private readonly object _sourceGate = new();
        private void Retain(Task original) { lock (_sourceGate) _originalSources.Add(original); }
        private Task[] SnapshotSources() { lock (_sourceGate) return _originalSources.ToArray(); }
        private Task? _originalExecute, _originalCancel, _originalTerminal, _originalClose;
        private bool _started, _ownWithdrawalRequested, _ownWithdrawalObserved;
        private volatile bool _terminal;
        private readonly CancellationTokenSource _ownWithdrawal = new();
        internal bool CanRetireOriginal => _terminal;
        internal void DemandExternalJoin()
        {
            OriginalNativeInvocation.DemandExternalJoin(this);
            using var guard = OriginalNativeInvocation.EnterExternal(this);
            if (_originalContentOwned) _originalContent?.DemandExternalOriginalRetirementJoin();
            _transport.OriginalPhysicalOwner?.DemandExternalOriginalRetirementJoin();
        }
        internal NativeExecution(CoreWebView2DownloadOperation originalOperation,
            BrowserDownloadTransport.NativeDownloadPlan originalPlan, BrowserDownloadTransport originalTransport, bool isPrivate)
        {
            _operation = originalOperation; _plan = originalPlan; _transport = originalTransport; _isPrivate = isPrivate;
            if (originalTransport.OriginalPhysicalOwner is not null)
                _originalTransportPlan = originalTransport.GetOriginalTransportPlan(originalPlan);
            _operation.StateChanged += OnState; _operation.BytesReceivedChanged += OnBytes;
        }
        internal IBrowserOriginalDownloadContent? ObserveOriginalContent(BrowserDownloadRecord currentCanonicalRow)
        {
            using var invocation = OriginalNativeInvocation.EnterExternal(this);
            if (_originalClose is not null || !_originalContentOwned || _originalContent is not { } content ||
                _originalExecute is not Task<BrowserDownloadRecord> { IsCompletedSuccessfully: true } execute ||
                !ReferenceEquals(execute.Result, content.OriginalRecord) || _originalApprovalSource is not { } source ||
                _originalApproval is not { } receipt || !source.IsIssuedOriginalApproval(receipt, this) ||
                !ReferenceEquals(source.GetOriginalExecutionTask(receipt), execute) ||
                _transport.OriginalPhysicalOwner?.IsIssuedOriginalContent(content) != true) return null;
            var actual = content.OriginalRecord;
            // Current ledger selection is descriptive; only the SAME physical
            // owner/approval/native source can issue actual content for Files.
            return actual.Id == currentCanonicalRow.Id && actual.ActionId == currentCanonicalRow.ActionId &&
                actual.SizeBytes == currentCanonicalRow.SizeBytes && actual.FileName == currentCanonicalRow.FileName &&
                actual.Address == currentCanonicalRow.Address && actual.Sha256 == currentCanonicalRow.Sha256 ? content : null;
        }
        public void BindOriginalDownloadApproval(IBrowserOriginalNativeDownloadApprovalSource source,
            IBrowserOriginalNativeDownloadApproval originalApproval)
        {
            ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(originalApproval);
            using var invocation = OriginalNativeInvocation.EnterExternal(this);
            lock (_sourceGate)
            {
                if (_originalClose is not null || _started || _originalExecute is not null)
                    throw new InvalidOperationException("The original native transfer can only bind its approval before execution.");
                if (_originalApproval is not null)
                {
                    if (ReferenceEquals(_originalApproval, originalApproval) && ReferenceEquals(_originalApprovalSource, source)) return;
                    throw new InvalidOperationException("The original native transfer already has a different approval owner.");
                }
                if (originalApproval.IsPrivate != _isPrivate || originalApproval.ActionId != _plan.ActionId || !source.IsIssuedOriginalApproval(originalApproval, this))
                    throw new UnauthorizedAccessException("The actual approval service did not issue this receipt for the SAME native transfer.");
                _originalApprovalSource = source; _originalApproval = originalApproval;
            }
        }
        private void DemandOriginalApprovalAdmission()
        {
            using var invocation = OriginalNativeInvocation.EnterExternal(this);
            var source = _originalApprovalSource ?? throw new UnauthorizedAccessException("The original native transfer has no actual Browser approval source.");
            var receipt = _originalApproval ?? throw new UnauthorizedAccessException("The original native transfer has no actual Browser approval receipt.");
            if (!source.IsIssuedOriginalApproval(receipt, this) || !source.IsOriginalExecutionAdmission(receipt, this))
                throw new UnauthorizedAccessException("This native execution is outside its SAME approval owner's actual admission.");
        }
        public Task<BrowserDownloadRecord> ExecuteAsync(CancellationToken token)
        {
            lock (_sourceGate)
            {
                if (_originalExecute is not null) return (Task<BrowserDownloadRecord>)_originalExecute;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = ExecuteOriginalAsync(start.Task, token); _originalExecute = original; Retain(original); start.SetResult(); return original;
            }
        }
        private async Task<BrowserDownloadRecord> ExecuteOriginalAsync(Task start, CancellationToken token)
        {
            using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
            await start; token.ThrowIfCancellationRequested(); DemandOriginalApprovalAdmission(); _started = true;
            var originalUi = Dispatcher.UIThread.InvokeAsync(() => { using var invocation = OriginalNativeInvocation.EnterExternal(this); token.ThrowIfCancellationRequested(); _operation.Resume(); CheckState(); }).GetTask();
            Retain(originalUi); await originalUi;
            // Completion belongs to this SAME native transfer; no waiter proxy.
            return await _originalCompletion.Task;
        }
        private void OnState(object? sender, object args) { using var invocation = OriginalNativeInvocation.EnterExternal(this); if (_started && !_terminal) CheckState(); }
        private void OnBytes(object? sender, object args)
        { using var invocation = OriginalNativeInvocation.EnterExternal(this); if (!_terminal && _operation.BytesReceived > BrowserDownloadTransport.MaximumDownloadBytes) IssueTerminal(new InvalidOperationException("The actual native transfer exceeded the canonical Browser size limit.")); }
        private void CheckState()
        {
            if (_terminal) return;
            if (_ownWithdrawalRequested && _operation.State == CoreWebView2DownloadState.Interrupted &&
                _operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled)
            {
                _terminal = _ownWithdrawalObserved = true; Detach(); _ownWithdrawal.Cancel();
                _originalCompletion.TrySetCanceled(_ownWithdrawal.Token); return;
            }
            if (_operation.State == CoreWebView2DownloadState.Completed) IssueTerminal(null);
            else if (_operation.State == CoreWebView2DownloadState.Interrupted && !_operation.CanResume)
                IssueTerminal(new IOException("The actual native transfer was interrupted: " + _operation.InterruptReason));
        }
        private void IssueTerminal(Exception? failure)
        {
            if (_terminal) return; _terminal = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalTerminal = TerminalAsync(start.Task, failure); Retain(_originalTerminal); start.SetResult();
        }
        private async Task TerminalAsync(Task start, Exception? failure)
        {
            using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
            await start;
            try
            {
                var originalUi = Dispatcher.UIThread.InvokeAsync(() => { using var invocation = OriginalNativeInvocation.EnterExternal(this); Detach(); }).GetTask(); Retain(originalUi); await originalUi;
                if (failure is not null) { _originalCompletion.TrySetException(failure); return; }
                if (_originalTransportPlan is { } actualPlan)
                {
                    var receipt = new CompletionObservation(this, actualPlan,
                        _originalApprovalSource ?? throw new UnauthorizedAccessException("The completion has no original approval source."),
                        _originalApproval ?? throw new UnauthorizedAccessException("The completion has no original approval receipt."));
                    _originalCompletedTransfer = receipt;
                    var originalRevalidation = RevalidateOriginalCompletionWithinSourceAsync(receipt,
                        ScopeOriginalNativeObservation, Retain, CancellationToken.None);
                    Retain(originalRevalidation); await originalRevalidation;
                    var originalFinalize = OriginalNativeInvocation.Acquire(this, () => _transport.FinalizeOriginalNativeDownloadWithinSourceAsync(
                        _plan, this, receipt, _originalNativeObservation!.MimeType, ScopeOriginalNativeObservation, Retain, CancellationToken.None));
                    Retain(originalFinalize); _originalContent = await originalFinalize;
                    using var completionGuard = OriginalNativeInvocation.EnterExternal(this);
                    if (!ReferenceEquals(_originalContent.OriginalPlan, actualPlan) ||
                        _transport.OriginalPhysicalOwner?.IsIssuedOriginalContent(_originalContent) != true)
                        throw new InvalidOperationException("The physical transport owner returned foreign native content.");
                    _originalContentOwned = true;
                    _originalCompletion.TrySetResult(_originalContent.OriginalRecord);
                }
                else
                {
                    // Existing native Browser transfer remains available without
                    // an installed NT/Files content supplier. This legacy result
                    // is never offered as a canonical Files registration grant.
                    var originalFinalize = OriginalNativeInvocation.Acquire(this, () => _transport.FinalizeNativeDownloadAsync(_plan, _operation.MimeType, CancellationToken.None));
                    Retain(originalFinalize); _originalCompletion.TrySetResult(await originalFinalize);
                }
            }
            catch (Exception error) { _originalCompletion.TrySetException(error); throw; }
        }
        private sealed class CompletionObservation(NativeExecution owner,
            IBrowserOriginalNativeDownloadTransportPlan plan,
            IBrowserOriginalNativeDownloadApprovalSource approvalSource,
            IBrowserOriginalNativeDownloadApproval approval) : IBrowserOriginalNativeDownloadCompletion
        {
            internal NativeExecution Owner { get; } = owner;
            public IBrowserOriginalNativeDownloadTransportPlan OriginalPlan { get; } = plan;
            public IBrowserOriginalNativeDownloadApprovalSource OriginalApprovalSource { get; } = approvalSource;
            public IBrowserOriginalNativeDownloadApproval OriginalApproval { get; } = approval;
            public IBrowserNativeDownloadExecution OriginalExecution => Owner;
        }
        public bool IsIssuedOriginalCompletion(IBrowserOriginalNativeDownloadCompletion completion,
            IBrowserOriginalNativeDownloadTransportPlan samePlan) =>
            ReferenceEquals(completion, _originalCompletedTransfer) &&
            completion is CompletionObservation actual && ReferenceEquals(actual.Owner, this) &&
            ReferenceEquals(actual.OriginalPlan, samePlan) && ReferenceEquals(samePlan, _originalTransportPlan) &&
            ReferenceEquals(actual.OriginalApprovalSource, _originalApprovalSource) && ReferenceEquals(actual.OriginalApproval, _originalApproval);
        private sealed record NativeCompletionObservation(CoreWebView2DownloadOperation Operation,
            IBrowserOriginalNativeDownloadCompletion Completion, CoreWebView2DownloadState State,
            string ResultFilePath, long BytesReceived, string? MimeType);
        public Task RevalidateOriginalCompletionWithinSourceAsync(IBrowserOriginalNativeDownloadCompletion completion,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            lock (_sourceGate)
            {
            if (_originalClose is not null && _originalTerminal is not { IsCompleted: false })
                throw new InvalidOperationException("The original native transfer no longer admits completion observations.");
            _originalSources.RemoveAll(source => source.IsCompletedSuccessfully);
            if (_originalSources.Count >= 128) throw new InvalidOperationException("Recover retained native completion observations before admitting another.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RevalidateCoreAsync(start.Task, completion, scope, retain, token);
            Retain(original); start.SetResult(); return original;
            }
        }
        private async Task RevalidateCoreAsync(Task start, IBrowserOriginalNativeDownloadCompletion completion,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
            await start; token.ThrowIfCancellationRequested();
            if (_originalTransportPlan is not { } actualPlan || !IsIssuedOriginalCompletion(completion, actualPlan))
                throw new UnauthorizedAccessException("The native observation belongs to a different original transfer.");
            var originalUi = Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var invocation = OriginalNativeInvocation.EnterExternal(this);
                token.ThrowIfCancellationRequested();
                _originalNativeObservation = new(_operation, completion, _operation.State,
                    _operation.ResultFilePath, _operation.BytesReceived, _operation.MimeType);
            }).GetTask();
            Retain(originalUi);
            using (OriginalNativeInvocation.EnterExternal(this)) retain(originalUi);
            await originalUi;
            var active = 1; var entered = 0; var thread = Environment.CurrentManagedThreadId;
            Exception? observationFailure = null;
            try
            {
                try
                {
                    using var scopeGuard = OriginalNativeInvocation.EnterExternal(this);
                    scope(() =>
                    {
                        try
                        {
                            if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref entered, 1) != 0)
                                throw new InvalidOperationException("The original native completion observation callback is inactive, repeated or foreign-thread.");
                            DemandOriginalCompletedDownload(completion);
                        }
                        catch (Exception failure) { observationFailure ??= failure; throw; }
                    });
                }
                catch (Exception scopeFailure)
                {
                    if (observationFailure is not null && !ReferenceEquals(observationFailure, scopeFailure))
                        throw new AggregateException("Native completion observation and caller scope both failed.", observationFailure, scopeFailure);
                    throw;
                }
                if (observationFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(observationFailure).Throw();
                if (entered != 1) throw new InvalidOperationException("The original native completion observation callback was not invoked.");
            }
            finally { Volatile.Write(ref active, 0); }
        }
        public void DemandOriginalCompletedDownload(IBrowserOriginalNativeDownloadCompletion completion)
        {
            using var invocation = OriginalNativeInvocation.EnterExternal(this);
            var actual = _originalNativeObservation;
            if (_originalTransportPlan is not { } actualPlan || !IsIssuedOriginalCompletion(completion, actualPlan) ||
                actual is null || !ReferenceEquals(actual.Operation, _operation) || !ReferenceEquals(actual.Completion, completion) ||
                !_terminal || !_started || _ownWithdrawalRequested || _originalExecute is null ||
                actual.State != CoreWebView2DownloadState.Completed || actual.BytesReceived > BrowserDownloadTransport.MaximumDownloadBytes ||
                !string.Equals(Path.GetFullPath(actual.ResultFilePath), Path.GetFullPath(_plan.PartialPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The SAME native operation no longer proves this original completed transfer.");
            var source = _originalApprovalSource!; var receipt = _originalApproval!;
            var actualSettledCompletion = _originalContentOwned && _originalContent is { } content &&
                _originalExecute is Task<BrowserDownloadRecord> execute && source.IsOriginalApprovedCompletion(receipt, this, execute, content.OriginalRecord);
            if (!source.IsIssuedOriginalApproval(receipt, this) ||
                !(source.IsOriginalExecutionAdmission(receipt, this) || actualSettledCompletion))
                throw new UnauthorizedAccessException("The original native completion has lost its actual Browser admission.");
        }
        private void ScopeOriginalNativeObservation(Action observation)
        {
            // Finite source scope remains synchronous on the caller's thread;
            // only the separately retained revalidation driver marshals UI work.
            using var invocation = OriginalNativeInvocation.EnterExternal(this); observation();
        }
        public Task CancelAsync(CancellationToken token)
        {
            lock (_sourceGate)
            {
                if (_originalCancel is not null) return _originalCancel;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalCancel = CancelOriginalAsync(start.Task, token); Retain(_originalCancel); start.SetResult(); return _originalCancel;
            }
        }
        private async Task CancelOriginalAsync(Task start, CancellationToken token)
        {
            using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
            await start; token.ThrowIfCancellationRequested();
            var originalUi = Dispatcher.UIThread.InvokeAsync(() => { using var invocation = OriginalNativeInvocation.EnterExternal(this); _ownWithdrawalRequested = true; _operation.Cancel(); CheckState(); }).GetTask();
            Retain(originalUi); await originalUi;
        }
        public Task CloseOriginalAsync()
        {
            DemandExternalJoin();
            lock (_sourceGate)
            {
                if (_originalClose is not null) return _originalClose;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = CloseAsync(start.Task); start.SetResult(); return _originalClose;
            }
        }
        private async Task CloseAsync(Task start)
        {
            using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
            await start;
            // Do not silently cancel a canonical pending/approved transfer.
            // Its approval owner must first complete or explicitly withdraw it.
            if (!_terminal) throw new InvalidOperationException("A canonical page download still owns this native transfer. Complete or cancel it through Browser approval before closing its engine.");
            var failures = new List<Exception>();
            if (_originalTerminal is not null) try { await _originalTerminal; } catch (Exception failure) { failures.Add(failure); }
            var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            while (true)
            {
              var originals = SnapshotSources().Append(_originalCompletion.Task).Where(source => !joined.Contains(source)).Distinct().ToArray();
              if (originals.Length == 0) break;
              foreach (var source in originals)
              {
                joined.Add(source);
                try { await source; }
                catch (OperationCanceledException withdrawal) when (_ownWithdrawalObserved && _ownWithdrawalRequested &&
                    (ReferenceEquals(source, _originalExecute) || ReferenceEquals(source, _originalCompletion.Task)) &&
                    source.IsCanceled && withdrawal.CancellationToken == _ownWithdrawal.Token &&
                    _originalCancel is { IsCompletedSuccessfully: true } &&
                    _operation.State == CoreWebView2DownloadState.Interrupted &&
                    _operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled) { }
                catch (Exception failure) { failures.Add(failure); }
              }
            }
            if (_originalContentOwned && _originalContent is { } originalContent)
            {
                try
                {
                    using var guard = OriginalNativeInvocation.EnterExternal(this);
                    originalContent.RequestRetirement(); _originalContentClose = originalContent.CloseAndDrainAsync();
                }
                catch (Exception failure) { failures.Add(failure); }
                if (_originalContentClose is not null)
                    try { await _originalContentClose; } catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count != 0) throw new AggregateException("The original native transfer retained failed source references.", failures);
        }
        private void Detach() { _operation.StateChanged -= OnState; _operation.BytesReceivedChanged -= OnBytes; }
    }
}
