using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace NineToOne.Web.Spaces.Chat;

/// <summary>Consumes the account-owned same-origin JS stream. Provider protocol and
/// usage parsing remain the original OllamaClient. This layer owns no credential.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserDulcheHttpHandler(Action demandCurrent, Func<bool>? canDispatch = null) : HttpMessageHandler, IAsyncDisposable
{
    internal sealed record Operations(Func<string,string,string,string?,Func<bool>,Task<string>> Begin,
        Func<string,Task<string>> Read, Func<string,Task> Cancel, Func<string,Task> Dispose);
    private readonly Operations _operations = new(Begin, Read, Cancel, DisposeOriginal);
    internal BrowserDulcheHttpHandler(Action demandCurrent, Operations operations, Func<bool>? canDispatch = null) : this(demandCurrent, canDispatch)
    { _operations = operations ?? throw new ArgumentNullException(nameof(operations)); }
    private readonly object _gate = new();
    private readonly HashSet<Exchange> _exchanges = [];
    private bool _closed;
    private Task? _close;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); demandCurrent();
        var path = request.RequestUri?.AbsolutePath;
        var relative = path switch { "/api/dulche/api/tags" => "api/tags", "/api/dulche/api/chat" => "api/chat", _ => throw new NotSupportedException("This provider operation is unavailable in the browser.") };
        if (request.RequestUri!.Query.Length != 0 || request.RequestUri.Fragment.Length != 0 ||
            relative == "api/tags" && request.Method != HttpMethod.Get || relative == "api/chat" && request.Method != HttpMethod.Post)
            throw new NotSupportedException("The browser provider accepts only original catalogue and chat operations.");
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
        demandCurrent(); token.ThrowIfCancellationRequested();
        Exchange exchange;
        lock (_gate)
        {
            if (_closed) throw new ObjectDisposedException(nameof(BrowserDulcheHttpHandler));
            exchange = new(this, demandCurrent); _exchanges.Add(exchange);
        }
        try
        {
            var header = await exchange.BeginAsync(request.Method.Method, relative, body, token);
            var response = new HttpResponseMessage((HttpStatusCode)header.Status)
            { RequestMessage = request, Content = new StreamContent(exchange) };
            if (!string.IsNullOrWhiteSpace(header.ContentType))
                response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.ContentType);
            return response;
        }
        catch (Exception cause)
        {
            try { await exchange.CloseAsync(); }
            catch (Exception close) { throw new AggregateException("Provider dispatch and original stream cleanup failed.", cause, close); }
            throw;
        }
    }
    private bool CanDispatchOriginal() => canDispatch?.Invoke() ?? true;
    public void Revoke()
    {
        lock (_gate) _closed = true; // Admission fence; DisposeAsync owns cancellation/drain.
    }
    public ValueTask DisposeAsync()
    {
        Task result; TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            _closed = true; result = CloseOriginalAsync(start.Task, _exchanges.ToArray()); _close = result;
        }
        start.SetResult(); return new(result);
    }
    private static async Task CloseOriginalAsync(Task start, Exchange[] exchanges)
    {
        await start;
        var errors = new List<Exception>();
        var originals = new List<Task>();
        foreach (var exchange in exchanges)
            try { originals.Add(exchange.CloseAsync()); }
            catch (Exception error) { Capture(errors, null, error); }
        foreach (var original in originals)
            try { await original; }
            catch (Exception error) { Capture(errors, original, error); }
        if (errors.Count != 0) throw new AggregateException("The original browser provider streams did not close cleanly.", errors);
    }
    private static void Capture(List<Exception> errors, Task? actual, Exception error)
    {
        IEnumerable<Exception> causes = actual?.Exception is { } group ? group.InnerExceptions : new[] { error };
        lock (errors) foreach (var cause in causes)
            if (!errors.Any(old => ReferenceEquals(old, cause))) errors.Add(cause);
    }
    private static void ThrowOriginals(IReadOnlyList<Exception> errors)
    {
        Exception[] snapshot; lock (errors) snapshot = errors.ToArray();
        if (snapshot.Length == 1 && snapshot[0] is OperationCanceledException)
            throw new AggregateException("An original browser provider source faulted with cancellation.", snapshot);
        if (snapshot.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(snapshot[0]).Throw();
        if (snapshot.Length > 1) throw new AggregateException("Original browser provider sources failed.", snapshot);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) Revoke(); base.Dispose(disposing); }
    private void Finished(Exchange exchange) { lock (_gate) _exchanges.Remove(exchange); }

    private sealed record Header(int Status, string? ContentType, bool Retired = false);
    private sealed record Chunk(bool Done, string? Base64, bool Retired = false);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Exchange(BrowserDulcheHttpHandler owner, Action demand) : Stream
    {
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly object _sync = new();
        private readonly SemaphoreSlim _readGate = new(1, 1);
        private Task? _cancel, _dispose;
        private Task<string>? _begin, _read;
        private Task<Header>? _beginDriver;
        private readonly List<Exception> _errors = [];
        private readonly List<Task<int>> _readers = [];
        private readonly HashSet<Exception> _retiredRefusals = new(ReferenceEqualityComparer.Instance);
        private ObjectDisposedException Retired()
        {
            var error = new ObjectDisposedException("Original provider stream");
            _retiredRefusals.Add(error); return error;
        }
        private bool _closed, _started, _done;
        private byte[] _remaining = [];
        private int _offset;
        private void DemandToken(CancellationToken token)
        {
            if (!token.IsCancellationRequested) return;
            var original = new OperationCanceledException(token);
            lock (_sync) _retiredRefusals.Add(original);
            throw original;
        }
        private bool IsOwnedRetirement(Exception error)
        { lock (_sync) return _retiredRefusals.Contains(error); }
        internal Task<Header> BeginAsync(string method, string path, string? body, CancellationToken token)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Header> original;
            lock (_sync)
            {
                if (_beginDriver is not null) throw new InvalidOperationException("The original provider stream already began.");
                original = BeginOriginalAsync(start.Task, method, path, body, token); _beginDriver = original;
            }
            start.SetResult(); return original;
        }
        private async Task<Header> BeginOriginalAsync(Task start, string method, string path, string? body, CancellationToken token)
        {
            await start;
            using var cancellation = token.Register(RequestCancellation);
            DemandToken(token); demand();
            lock (_sync)
            {
                if (_closed || _cancel is not null) throw Retired();
                _started = true;
                _begin = owner._operations.Begin(_id, method, path, body, MayDispatchOriginal);
            }
            try
            {
                var json = await _begin;
                var header = JsonSerializer.Deserialize<Header>(json, Json) ?? throw new InvalidDataException("Missing provider response headers.");
                bool retired;
                lock (_sync) { retired = header.Retired || _closed; if (retired) _closed = true; }
                if (retired) throw await RetireAfterCancellationAsync(token);
                DemandToken(token); demand();
                if (header.Status is < 100 or > 599) throw new InvalidDataException("Invalid provider response status.");
                return header;
            }
            catch (Exception cause)
            {
                if (!IsOwnedRetirement(cause)) Capture(_errors, _begin, cause);
                try { await JoinCancellationAsync(); } catch (Exception cancel) { Capture(_errors, _cancel, cancel); }
                ThrowOriginals(_errors); throw;
            }
        }
        private bool MayDispatchOriginal()
        {
            // JS calls this exact captured owner synchronously after identity awaits,
            // immediately before its account-owned fetch. It grants no account rights.
            lock (owner._gate) if (owner._closed) return false;
            lock (_sync) if (_closed || !_started) return false;
            demand();
            return owner.CanDispatchOriginal();
        }
        internal void RequestCancellation()
        {
            TaskCompletionSource? start = null;
            lock (_sync)
            {
                if (_cancel is not null || _dispose is not null || !_started) { _closed = true; return; }
                _closed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _cancel = CancelOriginalAsync(start.Task);
            }
            start.SetResult();
        }
        private async Task CancelOriginalAsync(Task start) { await start; await owner._operations.Cancel(_id); }
        private async Task JoinCancellationAsync() { Task? cancel; lock (_sync) cancel = _cancel; if (cancel is not null) await cancel; }
        private async Task<Exception> RetireAfterCancellationAsync(CancellationToken token)
        {
            // A privately issued retirement result is not a failed transport, but
            // the actual cancellation owner must settle before its reader does.
            // Never await _dispose here: that original close joins this reader.
            try { await JoinCancellationAsync(); }
            catch (Exception failure) { Capture(_errors, _cancel, failure); }
            ThrowOriginals(_errors);
            lock (_sync)
            {
                if (!token.IsCancellationRequested) return Retired();
                var original = new OperationCanceledException(token); _retiredRefusals.Add(original); return original;
            }
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (buffer.Length == 0) return ValueTask.FromResult(0);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<int> original;
            lock (_sync)
            {
                if (_closed) return ValueTask.FromException<int>(Retired());
                _readers.RemoveAll(task => task.IsCompletedSuccessfully);
                original = ReadOriginalAsync(start.Task, buffer, token); _readers.Add(original);
            }
            start.SetResult(); return new(original);
        }
        private async Task<int> ReadOriginalAsync(Task start, Memory<byte> buffer, CancellationToken token)
        {
            await start;
            var admission = _readGate.WaitAsync(token);
            try { await admission; }
            catch (OperationCanceledException original) when (admission.IsCanceled && token.IsCancellationRequested && original.CancellationToken == token)
            { lock (_sync) _retiredRefusals.Add(original); throw; }
            try
            {
                using var cancellation = token.Register(RequestCancellation);
                DemandToken(token); demand();
                lock (_sync)
                {
                    if (_closed) throw Retired();
                    if (_offset < _remaining.Length) return Copy(buffer);
                    if (_done) return 0;
                    _read = owner._operations.Read(_id);
                }
                string json;
                try { json = await _read; }
                catch (Exception cause)
                {
                    lock (_sync) _closed = true;
                    Capture(_errors, _read, cause);
                    try { await JoinCancellationAsync(); } catch (Exception cancel) { Capture(_errors, _cancel, cancel); }
                    ThrowOriginals(_errors); throw;
                }
                var chunk = JsonSerializer.Deserialize<Chunk>(json, Json) ?? throw new InvalidDataException("Missing provider stream chunk.");
                bool retired;
                lock (_sync) { retired = chunk.Retired || _closed; if (retired) _closed = true; }
                if (retired) throw await RetireAfterCancellationAsync(token);
                DemandToken(token); demand();
                lock (_sync)
                {
                    if (_closed) throw Retired();
                    _done = chunk.Done;
                    _remaining = string.IsNullOrEmpty(chunk.Base64) ? [] : Convert.FromBase64String(chunk.Base64); _offset = 0;
                    if (!_done && _remaining.Length == 0) throw new InvalidDataException("An open provider stream returned no bytes.");
                    return Copy(buffer);
                }
            }
            finally { _readGate.Release(); }
        }
        private int Copy(Memory<byte> buffer)
        {
            var count = Math.Min(buffer.Length, _remaining.Length - _offset);
            _remaining.AsMemory(_offset, count).CopyTo(buffer); _offset += count;
            if (_offset == _remaining.Length) { _remaining = []; _offset = 0; }
            return count;
        }
        internal Task CloseAsync()
        {
            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously); Task original;
            lock (_sync)
            {
                if (_dispose is not null) return _dispose;
                _closed = true; original = CloseOriginalAsync(start.Task); _dispose = original;
            }
            start.SetResult(); return original;
        }
        private async Task CloseOriginalAsync(Task start)
        {
            await start;
            Task? disposal = null;
            try { if (_started) disposal = owner._operations.Dispose(_id); }
            catch (Exception error) { Capture(_errors, null, error); }
            // The JS close starts cancellation immediately. Join every original .NET
            // import as well; no cancellation failure can erase its source cause.
            Task?[] originals; Task<int>[] readers;
            lock (_sync) { originals = [_beginDriver, _begin, _read, _cancel, disposal]; readers = _readers.ToArray(); }
            foreach (var original in originals.Where(task => task is not null).Distinct())
                try { await original!; } catch (Exception error) { if (!IsOwnedRetirement(error)) Capture(_errors, original, error); }
            foreach (var reader in readers)
            {
                try { await reader; }
                catch (Exception error)
                {
                    lock (_sync)
                    {
                        IEnumerable<Exception> causes = reader.Exception is { } group ? group.InnerExceptions : new[] { error };
                        foreach (var cause in causes)
                            if (!_retiredRefusals.Contains(cause)) Capture(_errors, null, cause);
                    }
                }
            }
            _remaining = [];
            ThrowOriginals(_errors);
            owner.Finished(this);
        }
        public override ValueTask DisposeAsync() => new(CloseAsync());
        protected override void Dispose(bool disposing) { if (disposing) _ = CloseAsync(); base.Dispose(disposing); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Browser provider reads are asynchronous.");
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [JSImport("begin", "nineToOneDulche")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Begin(string id, string method, string relativePath, string? bodyJson,
        [JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> canDispatchOriginal);
    [JSImport("read", "nineToOneDulche")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Read(string id);
    [JSImport("cancel", "nineToOneDulche")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task Cancel(string id);
    [JSImport("dispose", "nineToOneDulche")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task DisposeOriginal(string id);
}
