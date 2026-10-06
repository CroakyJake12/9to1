using System.Reflection;
using Dulche.Runtime;

namespace Haven.Infrastructure.Tests;

public sealed partial class LlamaCppOriginalProtocolCustodyTests
{
    [Fact]
    public async Task ScopedPostAcquisitionFaultJoinsTheSameHeldRawTaskBeforeWholeFailure()
    {
        var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var injected = new IOException("actual scoped post-acquisition failure");
        var calls = 0; var raised = 0;
        var scope = new RuntimeScope
        {
            After = value =>
            {
                if (ReferenceEquals(value, raw.Task) && Interlocked.CompareExchange(ref raised, 1, 0) == 0)
                { entered.TrySetResult(); throw injected; }
            }
        };
        var provider = Provider(new ConfigurationSource());
        Task<bool>? whole = null; Task? close = null; Exception? primary = null;
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { injected };
        var errors = new List<Exception>();
        try
        {
            whole = StartScoped(provider, async _ =>
            {
                await Task.Yield();
                return await Observe(provider, () => { calls++; return raw.Task; });
            }, scope);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(whole.IsCompleted);
            Assert.Contains(scope.Originals, task => ReferenceEquals(task, raw.Task));
            Assert.Contains(OriginalSources(provider), task => ReferenceEquals(task, raw.Task));
            raw.TrySetResult(true);
            Assert.True(Contains(await Capture(whole), injected));
            Assert.True(whole.IsFaulted);
            Assert.False(whole.IsCanceled);
            Assert.Equal(1, calls);
            close = provider.CloseAndDrainAsync();
            Assert.True(Contains(await Capture(close), injected));
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            raw.TrySetResult(true);
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            await JoinRuntimeControl(raw.Task, errors);
            if (whole is not null) await JoinRuntimeControl(whole, errors);
            if (close is not null) await JoinRuntimeControl(close, errors);
        }
        DemandRuntimeControlOutcome(primary, errors, expected);
    }

    [Fact]
    public async Task ScopedCallerCannotSubstituteTheActualProviderTask()
    {
        var actualRaw = Task.FromResult(true);
        var substituted = Task.FromResult(false);
        var scope = new RuntimeScope { Substitute = substituted };
        var provider = Provider(new ConfigurationSource());
        Task<bool>? whole = null; Task? close = null; Exception? primary = null;
        var errors = new List<Exception>();
        try
        {
            whole = StartScoped(provider, _ => actualRaw, scope);
            Assert.True(await whole.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains(scope.Originals, task => ReferenceEquals(task, actualRaw));
            Assert.DoesNotContain(scope.Originals, task => ReferenceEquals(task, substituted));
            close = provider.CloseAndDrainAsync();
            await close.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (whole is not null) await JoinRuntimeControl(whole, errors);
            if (close is not null) await JoinRuntimeControl(close, errors);
        }
        DemandRuntimeControlOutcome(primary, errors, new HashSet<Exception>(ReferenceEqualityComparer.Instance));
    }

    [Fact]
    public async Task ScopedSwallowedRepeatCannotInvokeAnotherProviderFactory()
    {
        var actualRaw = Task.FromResult(true);
        var scope = new RuntimeScope { RepeatActual = actualRaw };
        var provider = Provider(new ConfigurationSource());
        var calls = 0; Task<bool>? whole = null; Task? close = null; Exception? primary = null;
        var errors = new List<Exception>();
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        try
        {
            whole = StartScoped(provider, _ => { calls++; return actualRaw; }, scope);
            var observed = await Capture(whole);
            Assert.NotNull(scope.RepeatRefusal);
            Assert.IsType<InvalidOperationException>(scope.RepeatRefusal);
            expected.Add(scope.RepeatRefusal!);
            Assert.True(Contains(observed, scope.RepeatRefusal!));
            Assert.True(whole.IsFaulted);
            Assert.Equal(1, calls);
            close = provider.CloseAndDrainAsync();
            Assert.True(Contains(await Capture(close), scope.RepeatRefusal!));
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            if (scope.RepeatRefusal is not null) expected.Add(scope.RepeatRefusal);
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (whole is not null) await JoinRuntimeControl(whole, errors);
            if (close is not null) await JoinRuntimeControl(close, errors);
        }
        DemandRuntimeControlOutcome(primary, errors, expected);
    }

    [Fact]
    public async Task ScopedPostAcquisitionFaultRetiresTheSameConcreteResourceOnce()
    {
        var resource = new OriginalDisposable();
        var injected = new IOException("actual concrete resource post-acquisition failure");
        var scope = new RuntimeScope { After = value => { if (ReferenceEquals(value, resource)) throw injected; } };
        var provider = Provider(new ConfigurationSource());
        Task<bool>? whole = null; Task? close = null; Exception? primary = null;
        var errors = new List<Exception>();
        try
        {
            whole = StartScoped(provider, unusedToken =>
            {
                var method = typeof(LlamaCppModelProvider).GetMethod("AcquireOriginalDisposable", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(typeof(OriginalDisposable));
                _ = Invoke<OriginalDisposable>(method, provider, [new Func<OriginalDisposable>(() => resource)]);
                return Task.FromResult(true);
            }, scope);
            Assert.True(Contains(await Capture(whole), injected));
            Assert.True(whole.IsFaulted);
            Assert.Equal(1, resource.Disposals);
            close = provider.CloseAndDrainAsync();
            Assert.True(Contains(await Capture(close), injected));
            Assert.Equal(1, resource.Disposals);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (whole is not null) await JoinRuntimeControl(whole, errors);
            if (close is not null) await JoinRuntimeControl(close, errors);
            if (resource.Disposals == 0) resource.Dispose();
        }
        DemandRuntimeControlOutcome(primary, errors, new HashSet<Exception>(ReferenceEqualityComparer.Instance) { injected });
    }

    [Fact]
    public void ReportedHeldDescriptorMapsOnlyToTheSamePeerNamespace()
    {
        Assert.Equal("/proc/123/fd/3", LlamaCppModelProvider.OriginalModelReadPath(123, "/actual/model.gguf", "/proc/self/fd/3"));
        Assert.Equal("/proc/123/root/actual/model.gguf", LlamaCppModelProvider.OriginalModelReadPath(123, "/actual/model.gguf", "/actual/model.gguf"));
        foreach (var reported in new string?[] { null, "/elsewhere/model.gguf", "/proc/124/fd/3", "/proc/self/fd/03",
            "/proc/self/fd/2", "/proc/self/fd/-3", "/proc/self/fd/3/../4", "/proc/self/fd/2147483648" })
            Assert.Throws<UnauthorizedAccessException>(() => LlamaCppModelProvider.OriginalModelReadPath(123, "/actual/model.gguf", reported));
        Assert.Throws<UnauthorizedAccessException>(() => LlamaCppModelProvider.OriginalModelReadPath(0, "/actual/model.gguf", "/proc/self/fd/3"));
        Assert.Throws<UnauthorizedAccessException>(() => LlamaCppModelProvider.OriginalModelReadPath(123, "/proc/self/fd/3", "/proc/self/fd/3"));
    }

    [Fact]
    public async Task WholePublicationFaultRetainsTheSameWholeAndStartsNoProductiveBody()
    {
        var injected = new IOException("actual whole enrollment refused after acquisition");
        var scope = new RuntimeScope { OnRetain = actual => throw injected };
        var provider = Provider(new ConfigurationSource());
        var calls = 0; Task<bool>? whole = null; Task? close = null; Exception? primary = null;
        var errors = new List<Exception>();
        try
        {
            Assert.Same(injected, Assert.Throws<IOException>((Action)(() => { _ = StartScoped(provider,
                unusedToken => { calls++; return Task.FromResult(true); }, scope); })));
            whole = Assert.Single(scope.Originals.OfType<Task<bool>>());
            Assert.True(Contains(await Capture(whole), injected));
            Assert.True(whole.IsFaulted); Assert.False(whole.IsCanceled);
            Assert.Equal(0, calls);
            close = provider.CloseAndDrainAsync();
            Assert.True(Contains(await Capture(close), injected));
            Assert.Equal(0, calls);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            whole ??= scope.Originals.OfType<Task<bool>>().SingleOrDefault();
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (whole is not null) await JoinRuntimeControl(whole, errors);
            if (close is not null) await JoinRuntimeControl(close, errors);
        }
        DemandRuntimeControlOutcome(primary, errors, new HashSet<Exception>(ReferenceEqualityComparer.Instance) { injected });
    }

    private static Task<bool> StartScoped(LlamaCppModelProvider owner, Func<CancellationToken, Task<bool>> body, RuntimeScope scope) =>
        Invoke<Task<bool>>(typeof(LlamaCppModelProvider).GetMethod("StartOriginalCore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(bool)), owner, [body, CancellationToken.None, false, null, scope, false]);
    private sealed class RuntimeScope : IInferenceEngineOriginalSourceScope
    {
        internal readonly List<Task> Originals = [];
        internal Action<object?>? After;
        internal Action<Task>? OnRetain;
        internal object? Substitute;
        internal object? RepeatActual;
        internal Exception? RepeatRefusal;
        public T InvokeOriginalFactory<T>(Func<T> factory)
        {
            var actual = factory();
            After?.Invoke(actual);
            if (ReferenceEquals(actual, RepeatActual))
                try { _ = factory(); } catch (Exception cause) { RepeatRefusal = cause; }
            return actual is Task<bool> && Substitute is Task<bool> replacement ? (T)(object)replacement : actual;
        }
        public T InvokeOriginalCleanup<T>(Func<T> cleanup) => cleanup();
        public void RetainOriginalTask(Task actual) { lock (Originals) Originals.Add(actual); OnRetain?.Invoke(actual); }
    }
    private sealed class OriginalDisposable : IDisposable
    { internal int Disposals; public void Dispose() => Disposals++; }
    private static async Task JoinRuntimeControl(Task actual, List<Exception> causes)
    {
        try { await actual.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception cause)
        {
            foreach (var leaf in RuntimeLeaves(actual.IsFaulted ? actual.Exception! : cause))
                if (!causes.Any(prior => ReferenceEquals(prior, leaf))) causes.Add(leaf);
        }
    }
    private static IEnumerable<Exception> RuntimeLeaves(Exception cause) => cause is AggregateException group
        ? group.InnerExceptions.SelectMany(RuntimeLeaves) : [cause];
    private static void DemandRuntimeControlOutcome(Exception? primary, IEnumerable<Exception> causes, IReadOnlySet<Exception> expected)
    {
        var unexpected = causes.Where(cause => !expected.Contains(cause)).ToList();
        if (primary is not null) unexpected.Insert(0, primary);
        if (unexpected.Count != 0) throw new AggregateException("Every actual runtime-source control and independent cleanup cause is retained.", unexpected);
    }
}
