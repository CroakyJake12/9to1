using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual bounded metadata reader and observation-custody helper, using controlled
/// bytes/Tasks/scopes. These tests issue no loaded model, peer, permission or capability proof.</summary>
public sealed class LlamaCppRuntimeObservationTests
{
    [Theory]
    [InlineData("512", "256", "1024", "100", 256)]
    [InlineData("max", "99", "100", "150", 0)]
    [InlineData("100", "25", "max", "99", 75)]
    public void ActualHierarchyCalculationIncludesStricterLeafAndEveryParent(
        string leafMaximum, string leafCurrent, string parentMaximum, string parentCurrent, long expected)
    {
        IReadOnlyList<(string Maximum, string Current)> actual = [(leafMaximum, leafCurrent), (parentMaximum, parentCurrent)];
        var method = typeof(LlamaCppModelProvider).GetMethod("OriginalMemoryHeadroom", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, Invoke<long>(method, null, [2048L, actual]));
        var membership = typeof(LlamaCppModelProvider).GetMethod("OriginalCgroupContainsProcess", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(Invoke<bool>(membership, null, ["12\n123\n", 123]));
        Assert.False(Invoke<bool>(membership, null, ["12\n1234\n", 123]));
    }

    [Fact]
    public async Task DisabledScopedCatalogueUsesActualOwnedWholeAndDoesNotReadOrMintModels()
    {
        var configurations = new DisabledConfigurations(); var caller = new Caller { Substitute = true };
        var provider = new LlamaCppModelProvider(new(), configurations);
        Task<IReadOnlyList<ProviderModelDescriptor>>? actual = null; Task? close = null; Exception? primary = null;
        var failures = new List<Exception>();
        try
        {
            actual = provider.GetModelsWithinOriginalSourceAsync(caller, CancellationToken.None);
            Assert.Empty(await actual);
            Assert.True(actual.IsCompletedSuccessfully);
            Assert.Equal(0, configurations.Reads);
            Assert.Contains(caller.Originals, task => ReferenceEquals(task, actual));
            close = provider.CloseAndDrainAsync(); await close;
            Assert.True(close.IsCompletedSuccessfully);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // All genuine owned originals settle before the provider is released. Unexpected
            // close failure remains observable rather than being discarded as teardown noise.
            if (actual is not null) Keep(await Capture(actual));
            try { close ??= provider.CloseAndDrainAsync(); } catch (Exception error) { Keep(error); }
            if (close is not null) Keep(await Capture(close));
        }
        Keep(primary);
        if (failures.Count != 0) throw new AggregateException("The actual scoped catalogue control and independent cleanup failed.", failures);
        void Keep(Exception? error)
        {
            if (error is not null && !failures.Any(known => ReferenceEquals(known, error))) failures.Add(error);
        }
    }

    [Fact]
    public void ActualMetadataReaderReturnsExactArchitectureAndNumericFileType()
    {
        using var actual = Gguf("qwen2", 15);
        var read = ReadMetadata(actual);
        Assert.Equal("qwen2", Property<string>(read, "Architecture"));
        Assert.Equal(15u, Property<uint>(read, "FileType"));
        Assert.True(actual.Position > 24);
        Assert.True(actual.Position <= actual.Length);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    public void ActualMetadataReaderRefusesUnknownDuplicateOrTruncatedFacts(string kind)
    {
        using var actual = Gguf("qwen2", 15, kind);
        Assert.Throws<InvalidDataException>(() => ReadMetadata(actual));
    }

    [Fact]
    public void ActualScopeDiscardsSubstitutedReturnAndRefusesDeferredOrRepeatedFactories()
    {
        var caller = new Caller { Substitute = true };
        var actual = Scope(caller);
        Assert.Equal("original", actual.InvokeOriginalFactory(() => "original"));
        var repeated = Scope(new Caller { RepeatAndSwallow = true }); var factories = 0;
        Assert.Throws<AggregateException>(() => repeated.InvokeOriginalFactory(() => { factories++; return "actual"; }));
        Assert.Equal(1, factories);
        var deferredCaller = new Caller { Deferred = true }; var deferred = Scope(deferredCaller);
        Assert.Throws<AggregateException>(() => deferred.InvokeOriginalFactory(() => { factories++; return "late"; }));
        Assert.NotNull(deferredCaller.Saved);
        Assert.Throws<InvalidOperationException>(() => deferredCaller.Saved!());
        Assert.Equal(1, factories);
    }

    [Fact]
    public async Task ActualRawChildIsJoinedAfterScopeFailureWithEveryExactCause()
    {
        var primary = new IOException("scope exit original");
        var oce = new OperationCanceledException("faulted original child"); var sibling = new IOException("raw sibling");
        var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = new Caller { After = primary }; var scope = Scope(caller);
        var actual = Read(scope, () => raw.Task);
        try
        {
            Assert.False(actual.IsCompleted);
            Assert.Contains(caller.Originals, item => ReferenceEquals(item, raw.Task));
            raw.SetException([oce, sibling]);
            var observed = await Capture(actual);
            Assert.True(actual.IsFaulted); Assert.True(raw.Task.IsFaulted);
            Assert.True(Contains(observed, oce)); Assert.True(Contains(observed, sibling));
            await Call<Task>(scope, "JoinOriginals");
            var full = Assert.Throws<AggregateException>(() => Call<object?>(scope, "ThrowFailures"));
            Assert.True(Contains(full, primary)); Assert.True(Contains(full, oce)); Assert.True(Contains(full, sibling));
            Assert.Same(oce, raw.Task.Exception!.InnerExceptions[0]);
            Assert.Same(sibling, raw.Task.Exception.InnerExceptions[1]);
        }
        finally
        {
            raw.TrySetException([oce, sibling]);
            _ = await Capture(actual);
            await Call<Task>(scope, "JoinOriginals");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualCanceledSourceDiffersFromFaultedOceAndRetainsSameRawTask(bool canceled)
    {
        using var token = new CancellationTokenSource(); token.Cancel();
        var cause = new OperationCanceledException("faulted original", token.Token);
        var raw = canceled ? Task.FromCanceled<int>(token.Token) : Task.FromException<int>(cause);
        var caller = new Caller(); var scope = Scope(caller); var actual = Read(scope, () => raw);
        var observed = await Capture(actual);
        Assert.Equal(canceled, actual.IsCanceled); Assert.Equal(!canceled, actual.IsFaulted);
        Assert.Equal(canceled, raw.IsCanceled); Assert.Equal(!canceled, raw.IsFaulted);
        Assert.Contains(caller.Originals, item => ReferenceEquals(item, raw));
        await Call<Task>(scope, "JoinOriginals");
        if (canceled) Assert.Throws<OperationCanceledException>(() => Call<object?>(scope, "ThrowFailures"));
        else
        {
            Assert.True(Contains(observed, cause));
            Assert.True(Contains(Assert.Throws<AggregateException>(() => Call<object?>(scope, "ThrowFailures")), cause));
            Assert.Same(cause, raw.Exception!.InnerExceptions[0]);
        }
    }

    private static MemoryStream Gguf(string architecture, uint fileType, string? broken = null)
    {
        var stream = new MemoryStream();
        using (var write = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
        {
            write.Write(0x46554747u); write.Write(3u); write.Write(1ul);
            write.Write(broken == "missing" ? 1ul : broken == "duplicate" ? 3ul : 2ul);
            Text("general.architecture"); write.Write(8u);
            if (broken == "oversized") write.Write(257ul); else Text(architecture);
            if (broken != "missing") { Text("general.file_type"); write.Write(4u); write.Write(fileType); }
            if (broken == "duplicate") { Text("general.architecture"); write.Write(8u); Text(architecture); }
            void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); write.Write((ulong)bytes.Length); write.Write(bytes); }
        }
        if (broken == "truncated") stream.SetLength(stream.Length - 1);
        stream.Position = 0; return stream;
    }
    private static object ReadMetadata(Stream stream) => Invoke<object>(typeof(LlamaCppModelProvider)
        .GetNestedType("GgufRuntimeMetadata", BindingFlags.NonPublic)!.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!,
        null, [stream, CancellationToken.None]);
    private static T Property<T>(object owner, string name) => (T)owner.GetType().GetProperty(name)!.GetValue(owner)!;
    private static IInferenceEngineOriginalSourceScope Scope(Caller caller) => (IInferenceEngineOriginalSourceScope)Activator.CreateInstance(
        typeof(LlamaCppRuntimeObservationSource).GetNestedType("ObservationScope", BindingFlags.NonPublic)!,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [caller, (Action)(() => { })], null)!;
    private static Task<int> Read(IInferenceEngineOriginalSourceScope scope, Func<Task<int>> factory) => Invoke<Task<int>>(
        scope.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(method => method.Name == "Read" && method.IsGenericMethod)
            .MakeGenericMethod(typeof(int)), scope, [factory]);
    private static T Call<T>(object scope, string method) => Invoke<T>(scope.GetType().GetMethod(method,
        BindingFlags.Public | BindingFlags.Instance)!, scope, []);
    private static T Invoke<T>(MethodInfo method, object? owner, object?[] arguments)
    {
        try { return (T)method.Invoke(owner, arguments)!; }
        catch (TargetInvocationException error) when (error.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
    private static async Task<Exception?> Capture(Task task)
    { try { await task; return null; } catch (Exception error) { return task.Exception ?? error; } }
    private static bool Contains(Exception? observed, Exception actual) => ReferenceEquals(observed, actual)
        || observed is AggregateException group && group.InnerExceptions.Any(item => Contains(item, actual))
        || observed?.InnerException is { } inner && Contains(inner, actual);
    private sealed class Caller : IInferenceEngineOriginalSourceScope
    {
        public bool Substitute, Deferred, RepeatAndSwallow; public Exception? After; public Action? Saved;
        public readonly List<Task> Originals = [];
        public T InvokeOriginalFactory<T>(Func<T> factory)
        {
            if (Deferred) { Saved = () => _ = factory(); return default!; }
            var original = factory();
            if (RepeatAndSwallow) { try { _ = factory(); } catch (InvalidOperationException) { } }
            if (After is not null) ExceptionDispatchInfo.Capture(After).Throw();
            return Substitute ? default! : original;
        }
        public T InvokeOriginalCleanup<T>(Func<T> factory) => factory();
        public void RetainOriginalTask(Task sameActualTask) { if (!Originals.Contains(sameActualTask)) Originals.Add(sameActualTask); }
    }
    private sealed class DisabledConfigurations : IProviderConfigurationStore
    {
        public int Reads;
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken token)
        { Reads++; throw new InvalidOperationException("A disabled catalogue must not acquire configuration."); }
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken token) => throw new NotSupportedException();
    }
}
