using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual maintained protocol/owner stages with controlled streams and Tasks.
/// These controls issue no local peer, model, capability, permission or live inference proof.</summary>
public sealed class LlamaCppOriginalProtocolCustodyTests
{
    [Fact]
    public async Task TruncatedSseRetainsShownDeltaAndFaultsActualProducerBeforeClose()
    {
        var provider = Provider(new ConfigurationSource());
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"accepted\"}}]}\n\n"));
        using var reader = new StreamReader(bytes, leaveOpen: true);
        var bounded = BoundedReader(provider, reader);
        var output = Channel.CreateBounded<string>(8);
        var actual = Start(provider, async token =>
        {
            await Observe(provider, () => Call<Task>(provider, "ReadOriginalSseAsync", bounded, output.Writer, token));
            return true;
        });
        try
        {
            var cause = await Capture(actual);
            Assert.IsType<InvalidDataException>(cause);
            Assert.True(actual.IsFaulted);
            Assert.True(output.Reader.TryRead(out var shown));
            Assert.Equal("accepted", shown);
            Assert.False(output.Reader.TryRead(out _));
            Assert.Contains(OriginalWholes(provider), whole => ReferenceEquals(whole, actual));
            var close = provider.CloseAndDrainAsync();
            Assert.True(Contains(await Capture(close), cause!));
            Assert.Same(close, provider.CloseAndDrainAsync());
        }
        finally { _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    [Fact]
    public async Task ActualDoneTerminalCompletesSameProducerAndRetiresSuccessfully()
    {
        var provider = Provider(new ConfigurationSource());
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"accepted\"}}]}\ndata: [DONE]\n"));
        using var reader = new StreamReader(bytes, leaveOpen: true);
        var bounded = BoundedReader(provider, reader);
        var output = Channel.CreateBounded<string>(8);
        var actual = Start(provider, async token =>
        {
            await Observe(provider, () => Call<Task>(provider, "ReadOriginalSseAsync", bounded, output.Writer, token));
            return true;
        });
        try
        {
            Assert.True(await actual.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(actual.IsCompletedSuccessfully);
            Assert.True(output.Reader.TryRead(out var shown));
            Assert.Equal("accepted", shown);
            await provider.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    [Fact]
    public async Task FaultedConfigurationOceKeepsRawStatusAndExactCauseAtWholeAndClose()
    {
        var originalCause = new OperationCanceledException("faulted configuration original");
        var raw = new TaskCompletionSource<ProviderConfiguration?>(); raw.SetException(originalCause);
        var provider = Provider(new ConfigurationSource { Read = _ => raw.Task });
        var actual = provider.ObserveOriginalEndpointAsync(CancellationToken.None);
        try
        {
            var observed = await Capture(actual);
            Assert.True(actual.IsFaulted);
            Assert.True(raw.Task.IsFaulted);
            Assert.True(Contains(observed, originalCause));
            Assert.Contains(OriginalSources(provider), source => ReferenceEquals(source, raw.Task));
            Assert.True(Contains(await Capture(provider.CloseAndDrainAsync()), originalCause));
        }
        finally { _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    [Fact]
    public async Task SynchronousConfigurationOceWithCanceledOwnerNeverBecomesCanceledTask()
    {
        using var caller = new CancellationTokenSource();
        var originalCause = new OperationCanceledException("synchronous callback fault", caller.Token);
        var provider = Provider(new ConfigurationSource { Read = _ => { caller.Cancel(); throw originalCause; } });
        var actual = provider.ObserveOriginalEndpointAsync(caller.Token);
        try
        {
            Assert.True(Contains(await Capture(actual), originalCause));
            Assert.True(caller.IsCancellationRequested);
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            Assert.True(Contains(await Capture(provider.CloseAndDrainAsync()), originalCause));
        }
        finally { _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    [Fact]
    public async Task ReturnedCanceledConfigurationTaskRemainsSeparateFromFaultedOce()
    {
        using var caller = new CancellationTokenSource(); Task<ProviderConfiguration?>? raw = null;
        var provider = Provider(new ConfigurationSource { Read = _ => { caller.Cancel(); return raw = Task.FromCanceled<ProviderConfiguration?>(caller.Token); } });
        var actual = provider.ObserveOriginalEndpointAsync(caller.Token);
        try
        {
            var actualCause = Assert.IsAssignableFrom<OperationCanceledException>(await Capture(actual));
            Assert.NotNull(raw);
            Assert.True(raw!.IsCanceled);
            Assert.True(actual.IsCanceled);
            Assert.Contains(OriginalSources(provider), source => ReferenceEquals(source, raw));
            Assert.True(Contains(await Capture(provider.CloseAndDrainAsync()), actualCause));
        }
        finally { _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    [Fact]
    public async Task ConfigurationFactoryAfterAwaitRejectsCloseEvenWithRestoredForeignContext()
    {
        var foreign = ExecutionContext.Capture()!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<ProviderConfiguration?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0; Exception? refusal = null;
        var provider = Provider(new ConfigurationSource());
        var source = new ConfigurationSource { Read = unusedToken =>
        {
            calls++;
            ExecutionContext.Run(foreign, unused =>
            {
                try { _ = provider.CloseAndDrainAsync(); }
                catch (Exception cause) { refusal = cause; }
            }, null);
            return raw.Task;
        } };
        var actual = Start(provider, async token =>
        {
            entered.SetResult(); await release.Task.ConfigureAwait(false);
            var originalSource = Observe(provider, () => source.GetAsync("llama-cpp", token));
            acquired.SetResult();
            var observed = await originalSource;
            return observed is null;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); release.SetResult();
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.False(actual.IsCompleted);
            Assert.Contains(OriginalSources(provider), task => ReferenceEquals(task, raw.Task));
            raw.SetResult(null);
            Assert.True(await actual.WaitAsync(TimeSpan.FromSeconds(5)));
            await provider.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, calls);
        }
        finally { release.TrySetResult(); raw.TrySetResult(null); _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    [Fact]
    public async Task HeldRealDisposeAndFaultedCleanupOceDenyCancellationUntilWholeCleanupJoins()
    {
        using var caller = new CancellationTokenSource();
        var originalCleanup = new OperationCanceledException("faulted original stream disposal");
        var stream = new HeldStream();
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(stream) };
        var provider = Provider(new ConfigurationSource());
        var actual = Start(provider, async token =>
        {
            _ = await Observe(provider, () => Call<Task<JsonElement>>(provider, "ReadBoundedJsonAsync", response, token));
            return true;
        }, caller.Token);
        try
        {
            await stream.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel(); stream.ReadOriginal.SetCanceled(caller.Token);
            await stream.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(actual.IsCompleted);
            Assert.True(stream.ReadOriginal.Task.IsCanceled);
            await WaitRetained(provider, stream.ReadOriginal.Task);
            await WaitRetained(provider, stream.CloseOriginal.Task);
            Assert.Contains(OriginalSources(provider), task => ReferenceEquals(task, stream.ReadOriginal.Task));
            Assert.Contains(OriginalSources(provider), task => ReferenceEquals(task, stream.CloseOriginal.Task));
            stream.CloseOriginal.SetException(originalCleanup);
            Assert.True(Contains(await Capture(actual), originalCleanup));
            Assert.True(actual.IsFaulted);
            Assert.True(stream.CloseOriginal.Task.IsFaulted);
            Assert.True(Contains(await Capture(provider.CloseAndDrainAsync()), originalCleanup));
            Assert.Equal(1, stream.AsyncCloseCalls);
        }
        finally
        {
            stream.ReadOriginal.TrySetCanceled(caller.Token); stream.CloseOriginal.TrySetException(originalCleanup);
            _ = await Capture(provider.CloseAndDrainAsync());
        }
    }

    [Fact]
    public async Task FaultedRawStreamReadOceWithCanceledCallerCannotBeNormalizedByTextReader()
    {
        using var caller = new CancellationTokenSource();
        using var stream = new HeldStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        var provider = Provider(new ConfigurationSource());
        var bounded = BoundedReader(provider, reader); var output = Channel.CreateBounded<string>(8);
        var originalCause = new OperationCanceledException("actual faulted raw stream read", caller.Token);
        var actual = Start(provider, async token =>
        {
            await Observe(provider, () => Call<Task>(provider, "ReadOriginalSseAsync", bounded, output.Writer, token));
            return true;
        }, caller.Token);
        try
        {
            await stream.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel(); stream.ReadOriginal.SetException(originalCause);
            Assert.True(Contains(await Capture(actual), originalCause));
            Assert.True(actual.IsFaulted);
            Assert.True(stream.ReadOriginal.Task.IsFaulted);
            Assert.Same(originalCause, stream.ReadOriginal.Task.Exception!.InnerExceptions[0]);
            Assert.Contains(OriginalSources(provider), source => ReferenceEquals(source, stream.ReadOriginal.Task));
            Assert.False(output.Reader.TryRead(out _));
            Assert.True(Contains(await Capture(provider.CloseAndDrainAsync()), originalCause));
        }
        finally { stream.ReadOriginal.TrySetException(originalCause); _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    [Fact]
    public async Task DirectConfigurationSiblingsStayExactInRawWholeAndCloseCustody()
    {
        var first = new OperationCanceledException("faulted sibling"); var second = new IOException("second original cause");
        var raw = new TaskCompletionSource<ProviderConfiguration?>(); raw.SetException([first, second]);
        var provider = Provider(new ConfigurationSource { Read = _ => raw.Task });
        var actual = provider.ObserveOriginalEndpointAsync(CancellationToken.None);
        try
        {
            var observed = await Capture(actual);
            Assert.True(actual.IsFaulted);
            Assert.True(Contains(observed, first)); Assert.True(Contains(observed, second));
            Assert.Same(first, raw.Task.Exception!.InnerExceptions[0]);
            Assert.Same(second, raw.Task.Exception.InnerExceptions[1]);
            var close = await Capture(provider.CloseAndDrainAsync());
            Assert.True(Contains(close, first)); Assert.True(Contains(close, second));
        }
        finally { _ = await Capture(provider.CloseAndDrainAsync()); }
    }

    private static LlamaCppModelProvider Provider(ConfigurationSource source) => new(
        new(true, "/tmp/unconnected-original-protocol-test.sock", new string('0', 64),
            "/tmp/unloaded-original-protocol-test.gguf", new string('0', 64), "unloaded-original-protocol-test"), source);
    private static object BoundedReader(LlamaCppModelProvider owner, StreamReader reader) => Activator.CreateInstance(
        typeof(LlamaCppModelProvider).GetNestedType("BoundedLineReader", BindingFlags.NonPublic)!,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [owner, reader], null)!;
    private static Task<bool> Start(LlamaCppModelProvider owner, Func<CancellationToken, Task<bool>> body, CancellationToken token = default) =>
        Invoke<Task<bool>>(typeof(LlamaCppModelProvider).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "StartOriginal").MakeGenericMethod(typeof(bool)), owner, [body, token]);
    private static Task<T> Observe<T>(LlamaCppModelProvider owner, Func<Task<T>> factory) =>
        Invoke<Task<T>>(typeof(LlamaCppModelProvider).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ObserveOriginalAsync" && method.IsGenericMethod).MakeGenericMethod(typeof(T)), owner, [factory, false]);
    private static Task Observe(LlamaCppModelProvider owner, Func<Task> factory) =>
        Invoke<Task>(typeof(LlamaCppModelProvider).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ObserveOriginalAsync" && !method.IsGenericMethod), owner, [factory, false]);
    private static T Call<T>(object owner, string name, params object?[] args) =>
        Invoke<T>(owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!, owner, args);
    private static T Invoke<T>(MethodInfo method, object owner, object?[] args)
    {
        try { return (T)method.Invoke(owner, args)!; }
        catch (TargetInvocationException cause) when (cause.InnerException is { } original)
        { ExceptionDispatchInfo.Capture(original).Throw(); throw; }
    }
    private static IEnumerable<Task> OriginalWholes(LlamaCppModelProvider owner) => Works(owner).Select(work =>
        (Task)work.GetType().GetField("Whole", BindingFlags.Instance | BindingFlags.Public)!.GetValue(work)!);
    private static IEnumerable<Task> OriginalSources(LlamaCppModelProvider owner)
    {
        var gate = typeof(LlamaCppModelProvider).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        lock (gate) return Works(owner).SelectMany(work =>
            ((IEnumerable)work.GetType().GetField("Sources", BindingFlags.Instance | BindingFlags.Public)!.GetValue(work)!).Cast<Task>()).ToArray();
    }
    private static async Task WaitRetained(LlamaCppModelProvider owner, Task original)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!OriginalSources(owner).Any(actual => ReferenceEquals(actual, original)))
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
                throw new TimeoutException("The actual source was not enrolled.");
            await Task.Yield();
        }
    }
    private static IEnumerable<object> Works(LlamaCppModelProvider owner)
    {
        var gate = typeof(LlamaCppModelProvider).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        lock (gate) return ((IEnumerable)typeof(LlamaCppModelProvider).GetField("_work", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!).Cast<object>().ToArray();
    }
    private static async Task<Exception?> Capture(Task actual)
    { try { await actual.WaitAsync(TimeSpan.FromSeconds(5)); return null; } catch (Exception cause) { return cause; } }
    private static bool Contains(Exception? observed, Exception original) => ReferenceEquals(observed, original)
        || observed is AggregateException group && group.InnerExceptions.Any(cause => Contains(cause, original));
    private sealed class ConfigurationSource : IProviderConfigurationStore
    {
        public Func<CancellationToken, Task<ProviderConfiguration?>> Read { get; init; } = _ => throw new NotSupportedException();
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Read(token);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class HeldStream : Stream
    {
        public TaskCompletionSource<int> ReadOriginal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseOriginal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AsyncCloseCalls { get; private set; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { ReadEntered.TrySetResult(); return new(ReadOriginal.Task); }
        public override ValueTask DisposeAsync()
        { AsyncCloseCalls++; CloseEntered.TrySetResult(); return new(CloseOriginal.Task); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
