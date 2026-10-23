using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>Owns an intercepted attachment response, never a replay of its URL.
/// Browser download behavior remains deny. Only original Document responses with
/// Content-Disposition: attachment enter the existing Home approval/transport.</summary>
internal sealed class ChromiumResponseDownloadBridge : IAsyncDisposable
{
    private readonly IChromiumEndpoint _endpoint;
    private readonly string _session;
    private readonly Func<Uri?> _initiator;
    private readonly bool _private;
    private readonly BrowserDownloadTransport _transport;
    private readonly IBrowserNativeDownloadService _downloads;
    private readonly Action<string>? _status;
    private readonly object _gate = new();
    private readonly Dictionary<string, Transfer> _transfers = new(StringComparer.Ordinal);
    private readonly List<Task> _sources = [];
    private bool _retiring;
    private Task? _close;

    internal ChromiumResponseDownloadBridge(IChromiumEndpoint endpoint, string session, Func<Uri?> initiator,
        bool isPrivate, BrowserDownloadTransport transport, IBrowserNativeDownloadService downloads, Action<string>? status = null)
    {
        _endpoint = endpoint; _session = session; _initiator = initiator;
        _private = isPrivate; _transport = transport; _downloads = downloads; _status = status;
    }

    internal bool CanRetireOriginal { get { lock (_gate) return _transfers.Values.All(item => item.Terminal); } }
    internal IReadOnlyList<Task> OriginalSources { get { lock (_gate) return _sources.ToArray(); } }
    internal bool IsOriginalPendingDownload(BrowserPendingAction action)
    { lock (_gate) return !_retiring && _transfers.Values.Count(transfer => transfer.OwnsPending(action)) == 1; }
    private void Retain(Task task) { lock (_gate) _sources.Add(task); }
    private void NotifyStatus(string message)
    { OriginalNativeInvocation.Acquire(this, () => { _status?.Invoke(message); return true; }); }

    internal async Task WithdrawPendingAsync(CancellationToken token)
    {
        DemandExternalJoin();
        using var driver = OriginalNativeInvocation.EnterDriver(this);
        Task[] admission; lock (_gate) admission = _sources.ToArray();
        foreach (var source in admission) await source.ConfigureAwait(false);
        BrowserPendingAction[] pending;
        lock (_gate) pending = _transfers.Values.Select(transfer => transfer.Pending).OfType<BrowserPendingAction>().ToArray();
        if (pending.Length != 0 && _downloads is not IBrowserAutomationService)
            throw new NotSupportedException("The canonical pending-download rejection owner is unavailable.");
        foreach (var action in pending)
        {
            var rejected = OriginalNativeInvocation.Acquire(this, () => ((IBrowserAutomationService)_downloads).RejectAsync(action.Id, token));
            Retain(rejected); await rejected.ConfigureAwait(false);
        }
    }

    internal bool TryHandleResponse(JsonElement paused)
    {
        if (!paused.TryGetProperty("responseStatusCode", out var status) || !status.TryGetInt32(out var code) || code is < 200 or >= 300 ||
            !paused.TryGetProperty("resourceType", out var resource) || resource.GetString() != "Document" ||
            !paused.TryGetProperty("requestId", out var requestIdValue) || requestIdValue.ValueKind != JsonValueKind.String ||
            !paused.TryGetProperty("request", out var request) || !request.TryGetProperty("url", out var address) ||
            !Uri.TryCreate(address.GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 ||
            !paused.TryGetProperty("responseHeaders", out var headers) || headers.ValueKind != JsonValueKind.Array) return false;
        string? Header(string name)
        {
            var values = headers.EnumerateArray().Where(header => header.TryGetProperty("name", out var key) &&
                string.Equals(key.GetString(), name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return values.Length == 1 && values[0].TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        var disposition = Header("Content-Disposition");
        if (!ContentDispositionHeaderValue.TryParse(disposition, out var parsed) ||
            !string.Equals(parsed.DispositionType, "attachment", StringComparison.OrdinalIgnoreCase)) return false;
        long? expected = null;
        var encoding = Header("Content-Encoding");
        if (string.IsNullOrWhiteSpace(encoding) || encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
            if (long.TryParse(Header("Content-Length"), NumberStyles.None, CultureInfo.InvariantCulture, out var length)) expected = length;
        var id = requestIdValue.GetString()!;
        Task admission;
        lock (_gate)
        {
            if (_transfers.ContainsKey(id)) return true;
            _sources.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_retiring || _transfers.Count >= 64 || _sources.Count >= 256)
            {
                admission = SendAsync("Fetch.failRequest", new { requestId = id, errorReason = "Aborted" }, CancellationToken.None);
                Retain(admission);
            }
            else
            {
                var transfer = new Transfer(this, id, uri, _initiator(), disposition!, Header("Content-Type"), expected);
                _transfers.Add(id, transfer);
                admission = transfer.AdmitAsync(); Retain(admission);
            }
        }
        Retain(ObserveAdmissionAsync(admission));
        return true;
    }
    private static async Task ObserveAdmissionAsync(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch { /* The SAME failed source is retained; never log URLs, headers or payloads. */ }
    }
    private Task<JsonElement> SendAsync(string method, object parameters, CancellationToken token) =>
        OriginalNativeInvocation.Acquire(this, () => _endpoint.SendAsync(_session, method, parameters, token));

    internal IBrowserOriginalDownloadContent? ObserveOriginalDownloadContent(BrowserDownloadRecord record)
    {
        using var source = OriginalNativeInvocation.EnterExternal(this);
        lock (_gate)
        {
            if (_retiring) return null;
            var candidates = _transfers.Values.Select(transfer => transfer.Observe(record)).OfType<IBrowserOriginalDownloadContent>().ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
    }
    internal void DemandExternalJoin()
    {
        OriginalNativeInvocation.DemandExternalJoin(this);
        lock (_gate) foreach (var transfer in _transfers.Values) transfer.DemandExternalJoin();
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            if (!CanRetireOriginal) throw new InvalidOperationException("Home still owns a paused or active attachment response.");
            _retiring = true; return new(_close = CloseAsync());
        }
    }
    private async Task CloseAsync()
    {
        using var driver = OriginalNativeInvocation.EnterDriver(this);
        var failures = new List<Exception>();
        Transfer[] transfers; lock (_gate) transfers = _transfers.Values.ToArray();
        foreach (var transfer in transfers)
            try { await transfer.CloseAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        Task[] sources; lock (_gate) sources = _sources.ToArray();
        foreach (var task in sources)
            try { await task.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Chromium retained its attachment source failures.", failures);
    }

    private sealed class Transfer : IBrowserOriginalApprovedNativeDownloadExecution, IBrowserOriginalNativeDownloadCompletionSource
    {
        private readonly ChromiumResponseDownloadBridge _owner;
        private readonly string _request;
        private readonly Uri _address;
        private readonly Uri? _initiator;
        private readonly string _disposition;
        private readonly string? _contentType;
        private readonly long? _expectedLength;
        private readonly object _gate = new();
        private readonly List<Task> _sources = [];
        private readonly CancellationTokenSource _withdrawal = new(), _expiry = new();
        private BrowserDownloadTransport.NativeDownloadPlan? _plan;
        private IBrowserOriginalNativeDownloadTransportPlan? _originalPlan;
        private IBrowserOriginalNativeDownloadApprovalSource? _approvalSource;
        private IBrowserOriginalNativeDownloadApproval? _approval;
        private IBrowserOriginalDownloadContent? _content;
        private Completion? _completion;
        private BrowserPendingAction? _pending;
        private string? _stream;
        private Task<BrowserDownloadRecord>? _execute;
        private Task? _cancel, _expire, _close;
        private long _bytes;
        private bool _started, _eof, _flushed, _responseSettled, _ownWithdrawal;
        private volatile bool _terminal;
        internal bool Terminal => _terminal;

        internal Transfer(ChromiumResponseDownloadBridge owner, string request, Uri address, Uri? initiator,
            string disposition, string? contentType, long? expectedLength)
        { _owner = owner; _request = request; _address = address; _initiator = initiator; _disposition = disposition; _contentType = contentType; _expectedLength = expectedLength; }
        private void Retain(Task task)
        {
            lock (_gate) { _sources.RemoveAll(source => source.IsCompletedSuccessfully); _sources.Add(task); }
        }
        private Task<JsonElement> Send(string method, object parameters, CancellationToken token)
        {
            var actual = OriginalNativeInvocation.Acquire(this, () => _owner.SendAsync(method, parameters, token));
            Retain(actual); return actual;
        }
        internal async Task AdmitAsync()
        {
            using var driver = OriginalNativeInvocation.EnterDriver(this);
            try
            {
                if (_expectedLength is > BrowserDownloadTransport.MaximumDownloadBytes)
                    throw new InvalidOperationException("The attachment exceeds the canonical Browser size limit.");
                var plan = OriginalNativeInvocation.Acquire(this, () => _owner._transport.PrepareNativeDownloadAsync(
                    Guid.NewGuid(), _address, _initiator, null, _disposition, CancellationToken.None));
                Retain(plan); _plan = await plan.ConfigureAwait(false);
                if (_owner._transport.OriginalPhysicalOwner is not null) _originalPlan = _owner._transport.GetOriginalTransportPlan(_plan);
                var request = OriginalNativeInvocation.Acquire(this, () => _owner._downloads.RequestNativeDownloadAsync(
                    new(_plan.ActionId, _address, _plan.FileName, _owner._private), this, CancellationToken.None));
                Retain(request); var pending = await request.ConfigureAwait(false);
                if (pending.Id != _plan.ActionId || pending.Kind != BrowserActionKind.Download || pending.State != BrowserActionState.Pending)
                    throw new InvalidOperationException("The approval owner returned a different pending attachment.");
                var awaitingApproval = false;
                lock (_gate)
                    if (!_started && !_terminal) { _pending = pending; _expire = ExpireAsync(pending.ExpiresAt); Retain(_expire); awaitingApproval = true; }
                if (awaitingApproval) _owner.NotifyStatus("Attachment paused for Home approval. Open Downloads to review.");
            }
            catch (Exception failure)
            {
                try { await CancelAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception cleanup) { throw new AggregateException("Attachment admission and withdrawal failed.", failure, cleanup); }
                throw;
            }
        }
        private async Task ExpireAsync(DateTimeOffset deadline)
        {
            try
            {
                var delay = deadline - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, _expiry.Token).ConfigureAwait(false);
                lock (_gate) if (_started || _terminal) return;
                // The real store expires persisted actions. Private pending metadata
                // is in-memory and GetPending does not forget it; the exact native
                // response is still withdrawn below, and grants remain impossible.
                if (_owner._downloads is IBrowserAutomationService service)
                {
                    var expiry = OriginalNativeInvocation.Acquire(this, () => service.GetPendingAsync(CancellationToken.None));
                    Retain(expiry); await expiry.ConfigureAwait(false);
                }
                await CancelAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException cancelled) when (_expiry.IsCancellationRequested && cancelled.CancellationToken == _expiry.Token) { }
        }
        public void BindOriginalDownloadApproval(IBrowserOriginalNativeDownloadApprovalSource source, IBrowserOriginalNativeDownloadApproval approval)
        {
            using var callback = OriginalNativeInvocation.EnterExternal(this);
            lock (_gate)
            {
                if (_started || _terminal || _plan is null || _approval is not null &&
                    (!ReferenceEquals(_approval, approval) || !ReferenceEquals(_approvalSource, source)))
                    throw new InvalidOperationException("The original response cannot replace its approval owner.");
                if (approval.ActionId != _plan.ActionId || approval.IsPrivate != _owner._private || !source.IsIssuedOriginalApproval(approval, this))
                    throw new UnauthorizedAccessException("Approval must belong to this SAME original attachment response.");
                _approvalSource = source; _approval = approval;
            }
        }
        private void DemandApproval()
        {
            using var callback = OriginalNativeInvocation.EnterExternal(this);
            if (_approvalSource is not { } source || _approval is not { } approval ||
                !source.IsIssuedOriginalApproval(approval, this) || !source.IsOriginalExecutionAdmission(approval, this))
                throw new UnauthorizedAccessException("This response has no current genuine Home execution admission.");
        }
        public Task<BrowserDownloadRecord> ExecuteAsync(CancellationToken token)
        {
            lock (_gate)
            {
                if (_execute is not null) return _execute;
                if (_terminal || _plan is null) throw new InvalidOperationException("The paused response is no longer available.");
                DemandApproval(); _started = true; _expiry.Cancel();
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _execute = ExecuteCoreAsync(start.Task, token); Retain(_execute); start.SetResult(); return _execute;
            }
        }
        private async Task<BrowserDownloadRecord> ExecuteCoreAsync(Task start, CancellationToken token)
        {
            using var driver = OriginalNativeInvocation.EnterDriver(this);
            await start.ConfigureAwait(false);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _withdrawal.Token);
            try
            {
                lifetime.Token.ThrowIfCancellationRequested(); DemandApproval();
                var stream = await Send("Fetch.takeResponseBodyAsStream", new { requestId = _request }, lifetime.Token).ConfigureAwait(false);
                _stream = stream.GetProperty("stream").GetString();
                if (string.IsNullOrWhiteSpace(_stream)) throw new InvalidDataException("The original response issued no body stream.");
                await using (var output = new FileStream(_plan!.PartialPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var emptyReads = 0;
                    while (true)
                    {
                        var chunk = await Send("IO.read", new { handle = _stream, size = 64 * 1024 }, lifetime.Token).ConfigureAwait(false);
                        var data = chunk.GetProperty("data").GetString() ?? string.Empty;
                        var base64 = chunk.TryGetProperty("base64Encoded", out var encoded) && encoded.ValueKind == JsonValueKind.True;
                        if (base64 ? data.Length > ((64 * 1024 + 2) / 3) * 4 :
                            data.Length > 64 * 1024 || Encoding.UTF8.GetByteCount(data) > 64 * 1024)
                            throw new InvalidDataException("The original attachment chunk exceeded the requested read bound.");
                        var bytes = base64 ? Convert.FromBase64String(data) : Encoding.UTF8.GetBytes(data);
                        if (bytes.Length > 64 * 1024 || _bytes + bytes.Length > BrowserDownloadTransport.MaximumDownloadBytes)
                            throw new InvalidDataException("The original attachment stream exceeded the bounded Browser byte limit.");
                        if (bytes.Length > 0) { await output.WriteAsync(bytes, lifetime.Token).ConfigureAwait(false); _bytes += bytes.Length; emptyReads = 0; }
                        else if (++emptyReads > 3) throw new InvalidDataException("The original attachment stream stopped before EOF.");
                        if (chunk.TryGetProperty("eof", out var eof) && eof.ValueKind == JsonValueKind.True) { _eof = true; break; }
                    }
                    if (_expectedLength is { } expected && expected != _bytes) throw new InvalidDataException("The original attachment ended before its declared byte count.");
                    await output.FlushAsync(lifetime.Token).ConfigureAwait(false); _flushed = true;
                }
                await CloseStreamAsync().ConfigureAwait(false);
                // Taking the stream consumes the response. Finish that exact paused
                // navigation with no document body, leaving the original page in place.
                await Send("Fetch.fulfillRequest", new { requestId = _request, responseCode = 204, responseHeaders = Array.Empty<object>(), body = "" }, lifetime.Token).ConfigureAwait(false);
                _responseSettled = true; _terminal = true;
                if (_originalPlan is not null)
                {
                    _completion = new(this, _originalPlan, _approvalSource!, _approval!);
                    var final = OriginalNativeInvocation.Acquire(this, () => _owner._transport.FinalizeOriginalNativeDownloadWithinSourceAsync(
                        _plan!, this, _completion, _contentType, Scope, Retain, lifetime.Token));
                    Retain(final); _content = await final.ConfigureAwait(false);
                    _owner.NotifyStatus("Attachment bytes saved; refresh Downloads for the approved result.");
                    return _content.OriginalRecord;
                }
                var legacy = OriginalNativeInvocation.Acquire(this, () => _owner._transport.FinalizeNativeDownloadAsync(_plan!, _contentType, lifetime.Token));
                Retain(legacy); var record = await legacy.ConfigureAwait(false);
                _owner.NotifyStatus("Attachment bytes saved; refresh Downloads for the approved result.");
                return record;
            }
            catch (Exception failure)
            {
                try { await CancelAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception cleanup) { throw new AggregateException("Original attachment transfer and withdrawal failed.", failure, cleanup); }
                throw;
            }
        }
        private async Task CloseStreamAsync()
        {
            var handle = _stream; if (handle is null) return;
            await Send("IO.close", new { handle }, CancellationToken.None).ConfigureAwait(false); _stream = null;
        }
        public Task CancelAsync(CancellationToken token)
        {
            lock (_gate)
            {
                if (_cancel is not null) return _cancel;
                if (_terminal) return Task.CompletedTask;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _cancel = CancelCoreAsync(start.Task, token); Retain(_cancel); start.SetResult(); return _cancel;
            }
        }
        private async Task CancelCoreAsync(Task start, CancellationToken token)
        {
            using var driver = OriginalNativeInvocation.EnterDriver(this);
            await start.ConfigureAwait(false); token.ThrowIfCancellationRequested();
            _expiry.Cancel(); _ownWithdrawal = true; _withdrawal.Cancel();
            var failures = new List<Exception>();
            try { if (!_responseSettled) await Send("Fetch.failRequest", new { requestId = _request, errorReason = "Aborted" }, token).ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
            try { await CloseStreamAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
            if (failures.Count != 0) throw new AggregateException("The original paused response did not withdraw cleanly.", failures);
            _responseSettled = true; _terminal = true;
            if (_plan is not null && _owner._transport.OriginalPhysicalOwner is null) _owner._transport.AbortNativeDownload(_plan);
            _owner.NotifyStatus("Attachment response withdrawn.");
        }
        private sealed record Completion(Transfer Owner, IBrowserOriginalNativeDownloadTransportPlan OriginalPlan,
            IBrowserOriginalNativeDownloadApprovalSource OriginalApprovalSource,
            IBrowserOriginalNativeDownloadApproval OriginalApproval) : IBrowserOriginalNativeDownloadCompletion
        { public IBrowserNativeDownloadExecution OriginalExecution => Owner; }
        public bool IsIssuedOriginalCompletion(IBrowserOriginalNativeDownloadCompletion completion, IBrowserOriginalNativeDownloadTransportPlan plan) =>
            ReferenceEquals(completion, _completion) && completion is Completion actual && ReferenceEquals(actual.Owner, this) &&
            ReferenceEquals(actual.OriginalPlan, plan) && ReferenceEquals(plan, _originalPlan) &&
            ReferenceEquals(actual.OriginalApprovalSource, _approvalSource) && ReferenceEquals(actual.OriginalApproval, _approval);
        private void Scope(Action callback) { using var original = OriginalNativeInvocation.EnterExternal(this); callback(); }
        public Task RevalidateOriginalCompletionWithinSourceAsync(IBrowserOriginalNativeDownloadCompletion completion,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            lock (_gate)
            {
            if (_close is not null && _execute is not { IsCompleted: false })
                throw new InvalidOperationException("This response no longer admits completion observations.");
            _sources.RemoveAll(source => source.IsCompletedSuccessfully);
            if (_sources.Count >= 128) throw new InvalidOperationException("Recover retained attachment observations before admitting another.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RevalidateAsync(start.Task, completion, scope, retain, token);
            Retain(original); start.SetResult(); return original;
            }
        }
        private async Task RevalidateAsync(Task start, IBrowserOriginalNativeDownloadCompletion completion,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            using var driver = OriginalNativeInvocation.EnterDriver(this);
            await start.ConfigureAwait(false); token.ThrowIfCancellationRequested();
            var entered = 0; var active = 1; var thread = Environment.CurrentManagedThreadId; Exception? observationFailure = null;
            try
            {
                using var original = OriginalNativeInvocation.EnterExternal(this);
                scope(() =>
                {
                    try
                    {
                        if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref entered, 1) != 0)
                            throw new InvalidOperationException("The original EOF observation callback is inactive, repeated or foreign-thread.");
                        DemandOriginalCompletedDownload(completion);
                    }
                    catch (Exception failure) { observationFailure = failure; throw; }
                });
                if (observationFailure is not null) ExceptionDispatchInfo.Capture(observationFailure).Throw();
                if (entered != 1) throw new InvalidOperationException("The original EOF observation callback was not invoked.");
            }
            catch (Exception scopeFailure)
            {
                if (observationFailure is not null && !ReferenceEquals(observationFailure, scopeFailure))
                    throw new AggregateException("Original EOF observation and parent scope failed.", observationFailure, scopeFailure);
                throw;
            }
            finally { Volatile.Write(ref active, 0); }
        }
        public void DemandOriginalCompletedDownload(IBrowserOriginalNativeDownloadCompletion completion)
        {
            using var original = OriginalNativeInvocation.EnterExternal(this);
            var settled = _content is not null && _execute is not null && _approvalSource is not null && _approval is not null &&
                _approvalSource.IsOriginalApprovedCompletion(_approval, this, _execute, _content.OriginalRecord);
            if (_originalPlan is null || !IsIssuedOriginalCompletion(completion, _originalPlan) || !_terminal || !_started ||
                !_eof || !_flushed || !_responseSettled || _stream is not null || _ownWithdrawal || _bytes > BrowserDownloadTransport.MaximumDownloadBytes ||
                _approvalSource is null || _approval is null || !_approvalSource.IsIssuedOriginalApproval(_approval, this) ||
                !(_approvalSource.IsOriginalExecutionAdmission(_approval, this) || settled))
                throw new UnauthorizedAccessException("This SAME response no longer proves an approved complete EOF transfer.");
        }
        internal IBrowserOriginalDownloadContent? Observe(BrowserDownloadRecord record)
        {
            lock (_gate)
                return _content is not null && _execute is { IsCompletedSuccessfully: true } && ReferenceEquals(_content.OriginalRecord, record) &&
                    _approval is not null && _approvalSource?.IsOriginalApprovedCompletion(_approval, this, _execute, record) == true ? _content : null;
        }
        internal bool OwnsPending(BrowserPendingAction action)
        {
            lock (_gate) return !_terminal && !_started && !_ownWithdrawal && _plan is not null && _pending is not null &&
                action.Kind == BrowserActionKind.Download && action.State == BrowserActionState.Pending &&
                _pending == action && _pending.Id == _plan.ActionId;
        }
        internal BrowserPendingAction? Pending { get { lock (_gate) return _pending is { } pending && OwnsPending(pending) ? pending : null; } }
        internal void DemandExternalJoin()
        {
            OriginalNativeInvocation.DemandExternalJoin(this);
            using var guard = OriginalNativeInvocation.EnterExternal(this);
            _content?.DemandExternalOriginalRetirementJoin();
            _owner._transport.OriginalPhysicalOwner?.DemandExternalOriginalRetirementJoin();
        }
        internal Task CloseAsync()
        {
            DemandExternalJoin();
            lock (_gate)
            {
                if (_close is not null) return _close;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = JoinAsync(start.Task); start.SetResult(); return _close;
            }
        }
        private async Task JoinAsync(Task start)
        {
            using var driver = OriginalNativeInvocation.EnterDriver(this);
            await start.ConfigureAwait(false);
            if (!_terminal) throw new InvalidOperationException("The original response is still paused or executing.");
            var failures = new List<Exception>();
            var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                Task[] sources; lock (_gate) sources = _sources.Where(source => !joined.Contains(source)).ToArray();
                if (sources.Length == 0) break;
                foreach (var source in sources)
                {
                    joined.Add(source);
                    try { await source.ConfigureAwait(false); }
                    catch (Exception failure) { failures.Add(failure); }
                }
            }
            if (_approvalSource is { } issuer && _approval is { } receipt)
                try
                {
                    var settlement = OriginalNativeInvocation.Acquire(this, () => issuer.WaitOriginalApprovalSettlementWithinSourceAsync(receipt, Scope, Retain, CancellationToken.None));
                    Retain(settlement); await settlement.ConfigureAwait(false);
                }
                catch (Exception failure) { failures.Add(failure); }
            if (_content is { } content)
                try
                {
                    Task close;
                    using (OriginalNativeInvocation.EnterExternal(this)) { content.RequestRetirement(); close = content.CloseAndDrainAsync(); }
                    Retain(close); await close.ConfigureAwait(false);
                }
                catch (Exception failure) { failures.Add(failure); }
            if (failures.Count != 0) throw new AggregateException("The original attachment sources did not settle.", failures);
        }
    }
}
