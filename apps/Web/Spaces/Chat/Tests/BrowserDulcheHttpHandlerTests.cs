using System.Text;
using System.Text.Json;
using NineToOne.Web.Spaces.Chat;
using Xunit;

namespace NineToOne.Web.Tests;

public sealed class BrowserDulcheHttpHandlerTests
{
    private const string Headers = "{\"status\":200,\"contentType\":\"application/x-ndjson\"}";
    private static HttpClient Client(BrowserDulcheHttpHandler handler) => new(handler, false) { BaseAddress = new("https://browser-host.invalid/api/dulche/") };
    private static Task<HttpResponseMessage> Open(HttpClient client, CancellationToken token = default) =>
        client.SendAsync(new(HttpMethod.Get, "api/tags"), HttpCompletionOption.ResponseHeadersRead, token);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group && group.InnerExceptions.Count != 0
        ? group.InnerExceptions.SelectMany(Leaves) : new[] { error };

    [Fact]
    public async Task Original_read_and_cancel_failures_both_survive_send_and_close()
    {
        var readStarted = Signal(); var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readFailure = new IOException("original read source"); var cancelFailure = new IOException("original cancel source");
        var cancelStarted = Signal();
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => { readStarted.TrySetResult(); return source.Task; },
            _ => { cancelStarted.TrySetResult(); return Task.FromException(cancelFailure); }, _ => Task.CompletedTask));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync(); using var send = new CancellationTokenSource();
        var reading = stream.ReadAsync(new byte[32], send.Token).AsTask(); await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        send.Cancel(); await cancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); source.SetException(readFailure);
        var result = await Assert.ThrowsAnyAsync<Exception>(() => reading);
        Assert.Contains(Leaves(result), error => ReferenceEquals(error, readFailure));
        Assert.Contains(Leaves(result), error => ReferenceEquals(error, cancelFailure));
        var close = await Assert.ThrowsAnyAsync<Exception>(() => handler.DisposeAsync().AsTask());
        Assert.Contains(Leaves(close), error => ReferenceEquals(error, readFailure));
        Assert.Contains(Leaves(close), error => ReferenceEquals(error, cancelFailure));
    }

    [Fact]
    public async Task Close_starts_all_original_stream_closures_before_joining_held_first()
    {
        var firstClose = Signal(); var bothStarted = Signal(); var closing = 0;
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => Task.FromResult("{\"done\":true,\"base64\":\"\"}"), _ => Task.CompletedTask,
            _ => { if (Interlocked.Increment(ref closing) == 1) return firstClose.Task; bothStarted.SetResult(); return Task.CompletedTask; }));
        using var client = Client(handler); using var first = await Open(client); using var second = await Open(client);
        var close = handler.DisposeAsync().AsTask(); await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(close.IsCompleted); Assert.Equal(2, closing);
        firstClose.SetResult(); await close;
    }

    [Fact]
    public async Task Closing_original_stream_refuses_already_buffered_bytes()
    {
        var closing = Signal(); var release = Signal();
        var chunk = JsonSerializer.Serialize(new { done = false, base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("private bytes")) });
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => Task.FromResult(chunk), _ => Task.CompletedTask,
            _ => { closing.TrySetResult(); return release.Task; }));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync(); var buffer = new byte[1];
        Assert.Equal(1, await stream.ReadAsync(buffer));
        var close = handler.DisposeAsync().AsTask(); await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync(buffer).AsTask());
        release.SetResult(); await close;
    }

    [Fact]
    public async Task Close_refuses_held_read_publication_and_waits_for_original_reader()
    {
        var readStarted = Signal(); var disposeStarted = Signal();
        var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => { readStarted.SetResult(); return source.Task; }, _ => Task.CompletedTask,
            _ => { disposeStarted.SetResult(); return Task.CompletedTask; }));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync();
        var reading = stream.ReadAsync(new byte[32]).AsTask(); await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = handler.DisposeAsync().AsTask(); await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(close.IsCompleted);
        source.SetResult(JsonSerializer.Serialize(new { done = false, base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("private late bytes")) }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reading);
        await close;
        Assert.True(reading.IsCompleted);
    }

    [Fact]
    public async Task Actual_bytes_are_preserved_and_successful_dispose_is_joined()
    {
        var payload = Encoding.UTF8.GetBytes("{\"response\":\"actual bytes\"}\n"); var reads = 0; var closes = 0;
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => Task.FromResult(JsonSerializer.Serialize(new { done = reads++ != 0, base64 = reads == 1 ? Convert.ToBase64String(payload) : "" })),
            _ => Task.CompletedTask, _ => { closes++; return Task.CompletedTask; }));
        using var client = Client(handler); using var response = await Open(client);
        await using (var stream = await response.Content.ReadAsStreamAsync())
        { using var output = new MemoryStream(); await stream.CopyToAsync(output); Assert.Equal(payload, output.ToArray()); }
        await handler.DisposeAsync(); Assert.Equal(1, closes);
    }
    [Fact]
    public async Task Account_retired_begin_refuses_dispatch_but_original_drain_completes()
    {
        var started = Signal(); var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeStarted = Signal();
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => { started.SetResult(); return source.Task; },
            _ => throw new InvalidOperationException("A retired dispatch must not read"), _ => Task.CompletedTask,
            _ => { disposeStarted.SetResult(); return Task.CompletedTask; }));
        using var client = Client(handler);
        var opening = Open(client); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = handler.DisposeAsync().AsTask(); await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(closing.IsCompleted);
        source.SetResult("{\"retired\":true}");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => opening);
        await closing;
    }

    [Fact]
    public async Task Account_retired_read_refuses_private_bytes_and_does_not_poison_reset()
    {
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => Task.FromResult("{\"retired\":true}"), _ => Task.CompletedTask, _ => Task.CompletedTask));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync(new byte[32]).AsTask());
        await handler.DisposeAsync();
    }

    [Fact]
    public async Task Original_send_cancellation_with_retired_read_stays_cancelled_and_drains()
    {
        var started = Signal(); var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => { started.SetResult(); return source.Task; },
            _ => { source.TrySetResult("{\"retired\":true}"); return Task.CompletedTask; }, _ => Task.CompletedTask));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync(); using var send = new CancellationTokenSource();
        var reading = stream.ReadAsync(new byte[32], send.Token).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); send.Cancel();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.Equal(send.Token, failure.CancellationToken);
        Assert.True(reading.IsCanceled);
        await handler.DisposeAsync();
    }

    [Fact]
    public async Task Foreign_faulted_cancellation_is_retained_as_exact_failed_source()
    {
        using var foreign = new CancellationTokenSource(); foreign.Cancel();
        var original = new OperationCanceledException("foreign source fault", foreign.Token);
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => Task.FromException<string>(original), _ => Task.CompletedTask, _ => Task.CompletedTask));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync();
        var reading = stream.ReadAsync(new byte[32]).AsTask();
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => reading);
        Assert.Contains(Leaves(failure), cause => ReferenceEquals(cause, original));
        Assert.True(reading.IsFaulted);
        var closing = await Assert.ThrowsAnyAsync<Exception>(() => handler.DisposeAsync().AsTask());
        Assert.Contains(Leaves(closing), cause => ReferenceEquals(cause, original));
    }

    [Fact]
    public async Task Retired_read_does_not_hide_original_cleanup_failure()
    {
        var original = new IOException("original cleanup failed");
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => Task.FromResult("{\"retired\":true}"), _ => Task.CompletedTask, _ => Task.FromException(original)));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync(new byte[32]).AsTask());
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => handler.DisposeAsync().AsTask());
        Assert.Contains(Leaves(failure), cause => ReferenceEquals(cause, original));
    }

    [Fact]
    public async Task Exact_dispatch_callback_observes_policy_change_during_original_identity_read()
    {
        var started = Signal(); var release = Signal(); var allowed = true; var dispatched = 0;
        Func<bool>? captured = null;
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations(async (_, _, _, _, original) =>
        {
            captured = original; started.SetResult(); await release.Task;
            if (!original()) return "{\"retired\":true}";
            dispatched++; return Headers;
        }, _ => throw new InvalidOperationException(), _ => Task.CompletedTask, _ => Task.CompletedTask), () => allowed);
        using var client = Client(handler); var opening = Open(client);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(captured!()); allowed = false;
        release.SetResult(); await Assert.ThrowsAsync<ObjectDisposedException>(() => opening);
        Assert.Equal(0, dispatched); await handler.DisposeAsync();
        Assert.False(captured());
    }

    [Fact]
    public async Task Retired_read_waits_for_exact_original_cancellation_before_settling()
    {
        var started = Signal(); var cancelStarted = Signal(); var releaseCancel = Signal();
        var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BrowserDulcheHttpHandler(() => { }, new BrowserDulcheHttpHandler.Operations((_, _, _, _, _) => Task.FromResult(Headers),
            _ => { started.SetResult(); return source.Task; },
            _ => { source.TrySetResult("{\"retired\":true}"); cancelStarted.SetResult(); return releaseCancel.Task; }, _ => Task.CompletedTask));
        using var client = Client(handler); using var response = await Open(client);
        var stream = await response.Content.ReadAsStreamAsync(); using var send = new CancellationTokenSource();
        var reading = stream.ReadAsync(new byte[32], send.Token).AsTask(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        send.Cancel(); await cancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var observation = await Task.WhenAny(reading, Task.Delay(100));
            Assert.NotSame(reading, observation);
        }
        finally { releaseCancel.SetResult(); }
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.Equal(send.Token, canceled.CancellationToken); await handler.DisposeAsync();
    }

}
