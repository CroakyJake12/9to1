using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskRunConfiguredCloudAdmissionSource
{
    private ITaskOriginalAttachmentEgressSource? _attachmentEgress;
    private readonly ConditionalWeakTable<TaskOriginalAttachmentEgressRequest, AttachmentFrame> _attachmentRequests = new();
    private readonly List<AttachmentFrame> _attachmentFrames = [];
    private readonly object _attachmentErrorGate = new();
    private readonly List<Exception> _attachmentUnexpected = [];
    private void DemandOriginalAttachmentSourceHealthy()
    {
        lock (_attachmentErrorGate) if (_attachmentUnexpected.Count != 0)
            throw new AggregateException("The original attachment source retained an unexpected callback failure.", _attachmentUnexpected.ToArray());
    }
    private void RememberUnexpectedAttachmentCallback(AttachmentFrame frame, Exception cause)
    {
        lock (_attachmentErrorGate) if (!_attachmentUnexpected.Any(value => ReferenceEquals(value, cause))) _attachmentUnexpected.Add(cause);
        // An expired callback cannot disappear when an independently joined healthy
        // frame was pruned. Re-retain its original ledger for cleanup only.
        lock (_sync) if (!_attachmentFrames.Any(value => ReferenceEquals(value, frame))) _attachmentFrames.Add(frame);
    }
    private bool _attachmentFramesRetiring;
    private Task? _attachmentFramesClose;
    public void BindOriginalAttachmentEgressSource(ITaskOriginalAttachmentEgressSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_attachmentFramesRetiring, this);
            if (_attachmentEgress is not null && !ReferenceEquals(_attachmentEgress, source))
                throw new InvalidOperationException("The original attachment disclosure source cannot be replaced.");
            _attachmentEgress = source;
        }
    }
    public bool HasOriginalAttachmentEgressSource(ITaskOriginalAttachmentEgressSource source)
    { lock (_sync) return !_attachmentFramesRetiring && ReferenceEquals(_attachmentEgress, source); }
    public bool IsIssuedOriginalAttachmentEgressRequest(TaskOriginalAttachmentEgressRequest request,
        ITaskOriginalAttachmentEgressSource source) => ReferenceEquals(request.Source, this) &&
        ReferenceEquals(_attachmentEgress, source) && _attachmentRequests.TryGetValue(request, out var frame) &&
        ReferenceEquals(frame.Request, request) && frame.IsLive;
    private bool HasCapturedOriginalAttachmentDomain(TaskRunContextInventory inventory) =>
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
    {
        DemandOriginalAttachmentSourceHealthy();
        if (inventory.OriginalAttachmentInvocation is not { } invocation || inventory.OriginalAttachmentLineage is not { } lineage ||
            _attachmentEgress is not IChatOriginalAttachmentInputSource input ||
            !_attachmentEgress.HasOriginalEgressComposition(this) ||
            !invocation.Chat.IsIssuedOriginalAttachmentInvocation(invocation, input) ||
            !input.IsIssuedOriginalAttachmentInput(invocation.Input)) return false;
        return JsonSerializer.Serialize(input.ObserveOriginalAttachmentLineage(invocation.Input)) == JsonSerializer.Serialize(lineage);
    });
    private AttachmentFrame CreateOriginalAttachmentFrame(TaskRunAttemptAdmission admission,
        TaskExecutionSnapshot snapshot, ProviderExecutionContext context, Capture capture)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_attachmentFramesRetiring, this);
            DemandOriginalAttachmentSourceHealthy();
            var source = _attachmentEgress ?? throw new NotSupportedException("Selected attachments need separate model disclosure approval.");
            _attachmentFrames.RemoveAll(value => value.ObserveHealthyClose());
            if (_attachmentFrames.Count >= 128) throw new InvalidOperationException("Unsettled attachment disclosure frames require inspection.");
            var request = new TaskOriginalAttachmentEgressRequest(this, admission, snapshot, context,
                capture.AttachmentInvocation ?? throw new UnauthorizedAccessException("The live original attachment invocation is unavailable."), capture.PayloadFingerprint);
            var frame = new AttachmentFrame(this, source, request); _attachmentRequests.Add(request, frame); _attachmentFrames.Add(frame); return frame;
        }
    }
    public Task? OriginalAttachmentFramesClose { get { lock (_sync) return _attachmentFramesClose; } }
    public void DemandExternalOriginalAttachmentFramesJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        AttachmentFrame[] frames; lock (_sync) frames = _attachmentFrames.ToArray();
        foreach (var frame in frames) frame.DemandExternalJoin();
    }
    public Task CloseOriginalAttachmentFramesAndDrainAsync()
    {
        DemandExternalOriginalAttachmentFramesJoin(); TaskCompletionSource? start = null; Task raw;
        lock (_sync)
        {
            _attachmentFramesRetiring = true;
            if (_attachmentFramesClose?.IsCompletedSuccessfully == true) DemandOriginalAttachmentSourceHealthy();
            if (_attachmentFramesClose is null)
            { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _attachmentFramesClose = CloseFrames(start.Task, _attachmentFrames.ToArray()); }
            raw = _attachmentFramesClose;
        }
        start?.SetResult(); return raw;
    }
    private async Task CloseFrames(Task start, AttachmentFrame[] frames)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        foreach (var frame in frames)
        {
            Task? raw = null;
            try { raw = frame.CloseAndDrainAsync(); await raw.ConfigureAwait(false); }
            catch (Exception cause) { AddTask(failures, raw, cause); }
        }
        lock (_attachmentErrorGate) foreach (var cause in _attachmentUnexpected)
            if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause);
        Throw(failures);
    }
    private sealed class AttachmentFrame
    {
        private readonly TaskRunConfiguredCloudAdmissionSource _owner;
        private readonly ITaskOriginalAttachmentEgressSource _source;
        internal TaskOriginalAttachmentEgressRequest Request { get; }
        private readonly object _gate = new();
        private readonly CloudflareOriginalTaskLedger _originals = new();
        private readonly List<Task> _commands = [];
        private Task<ITaskOriginalAttachmentEgressLease>? _acquisition;
        private ITaskOriginalAttachmentEgressLease? _lease;
        private Task? _close, _leaseClose;
        private bool _closed, _invoked;
        private Exception? _knownRefusal;
        internal AttachmentFrame(TaskRunConfiguredCloudAdmissionSource owner, ITaskOriginalAttachmentEgressSource source,
            TaskOriginalAttachmentEgressRequest request)
        { _owner = owner; _source = source; Request = request; _originals.BindOriginalOwner(this); }
        internal bool IsLive { get { lock (_gate) return !_closed && !_invoked && _originals.OriginalErrors.Count == 0; } }
        internal bool ObserveHealthyClose()
        {
            Task? raw; lock (_gate) raw = _close;
            if (raw?.IsCompletedSuccessfully != true || _originals.OriginalErrors.Any(cause => !OnlyKnownRefusal(cause))) return false;
            raw.GetAwaiter().GetResult(); return true;
        }
        internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        private void DemandProductive()
        {
            _owner.DemandOriginalAttachmentSourceHealthy();
            lock (_gate) ObjectDisposedException.ThrowIf(_closed || _invoked, this);
            if (_originals.OriginalErrors.Count != 0) throw new AggregateException("The original disclosure frame has retained source failures.", _originals.OriginalErrors);
        }
        internal Task RevalidateAsync(CancellationToken token)
        {
            TaskCompletionSource start; Task raw;
            lock (_gate)
            {
                DemandProductive(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                raw = Validate(start.Task, token); _commands.Add(raw);
            }
            start.SetResult(); return raw;
        }
        private async Task Validate(Task start, CancellationToken token)
        {
            await start.ConfigureAwait(false); using var active = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var phase = new Phase(this);
            try
            {
                DemandProductive();
                if (_lease is null)
                {
                    await _originals.CaptureOriginalAcquisitionAsync(() =>
                    {
                        _acquisition = _source.AcquireOriginalEgressWithinSourceAsync(Request, phase.Run, phase.Retain, token);
                        return _acquisition;
                    }, actual => _lease = actual).ConfigureAwait(false);
                    var issued = _originals.Invoke(() => _source.IsIssuedOriginalEgressLease(Request, _lease!));
                    if (!issued) throw new UnauthorizedAccessException("The actual attachment owner did not issue this disclosure lease.");
                }
                await _originals.AwaitAsync(_originals.Invoke(() => _lease!.ValidateOriginalWithinSourceAsync(phase.Run, phase.Retain, token))).ConfigureAwait(false);
                DemandProductive();
            }
            catch (Exception cause)
            {
                _originals.Retain(cause);
                if (_acquisition is not null && _originals.Invoke(() => _source.IsAcknowledgedOriginalEgressRefusal(_acquisition)) &&
                    _acquisition.Exception is { InnerExceptions.Count: 1 } actual)
                    _knownRefusal = actual.InnerExceptions[0];
                throw;
            }
            finally { phase.Seal(); }
        }
        internal T RunOriginalInvocation<T>(Func<T> originalRawStart)
        {
            DemandProductive();
            return _originals.Invoke(() =>
            {
                lock (_gate)
                {
                    DemandProductive();
                    if (_lease is null || _commands.Count == 0 || _commands.Any(raw => !raw.IsCompletedSuccessfully))
                        throw new UnauthorizedAccessException("The actual attachment disclosure frame has no healthy current validation.");
                    // The domain owner checks its still-live request before it invokes
                    // this exact nested model-use fence. Seal before the raw factory.
                    return _lease.RunOriginalInvocation(() =>
                    { _invoked = true; return originalRawStart(); });
                }
            });
        }
        internal Task CloseAndDrainAsync()
        {
            DemandExternalJoin(); TaskCompletionSource? start = null; Task raw;
            lock (_gate)
            {
                if (_close is null)
                { _closed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task, _commands.ToArray()); }
                raw = _close;
            }
            start?.SetResult(); return raw;
        }
        private async Task Close(Task start, Task[] commands)
        {
            await start.ConfigureAwait(false); using var active = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            foreach (var command in commands)
                try { await command.ConfigureAwait(false); } catch (Exception cause) { _originals.Capture(command, cause); }
            if (_acquisition is { IsCompletedSuccessfully: true }) _lease ??= _acquisition.GetAwaiter().GetResult();
            if (_lease is not null)
                try
                {
                    await _originals.ObserveOriginalCloseAsync(() =>
                        new ValueTask(_leaseClose = _lease.CloseAndDrainOriginalAsync())).ConfigureAwait(false);
                }
                catch (Exception cause) { _originals.Capture(_leaseClose, cause); }
            // Capture the SAME raw close itself before independent final observation.
            if (_leaseClose is not null) _ = _originals.Track(_leaseClose);
            await _originals.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            var failures = _originals.OriginalErrors.Where(cause => !OnlyKnownRefusal(cause)).ToArray();
            if (failures.Length != 0) throw new AggregateException("Original attachment disclosure frame sources did not close healthy.", failures);
        }
        private bool OnlyKnownRefusal(Exception cause)
        {
            if (_knownRefusal is not { } original) return false;
            var pending = new Stack<Exception>(); var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            var enqueued = 1; pending.Push(cause);
            while (pending.TryPop(out var same))
            {
                if (ReferenceEquals(same, original)) continue;
                if (!seen.Add(same)) continue;
                if (same.GetType() != typeof(AggregateException) || same is not AggregateException { InnerExceptions.Count: > 0 } group)
                    return false;
                foreach (var child in group.InnerExceptions)
                {
                    if (++enqueued > 4096) return false;
                    pending.Push(child);
                }
            }
            return true;
        }
        private sealed class Phase(AttachmentFrame owner)
        {
            private int _active = 1;
            internal void Seal() => Interlocked.Exchange(ref _active, 0);
            internal void Run(Action body) => owner._originals.Invoke(() =>
            {
                if (Volatile.Read(ref _active) != 1) throw Expired();
                owner.DemandProductive(); body(); return true;
            });
            private Exception Expired()
            {
                var cause = new InvalidOperationException("The original attachment disclosure source callback expired.");
                owner._originals.Retain(cause); owner._owner.RememberUnexpectedAttachmentCallback(owner, cause); return cause;
            }
            internal void Retain(Task raw)
            {
                _ = owner._originals.Track(raw); // SAME late raw remains cleanup custody.
                if (Volatile.Read(ref _active) != 1) throw Expired();
            }
        }
    }
}
