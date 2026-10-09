using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

// Controlled provider Tasks exercise custody and maintained catalogue metadata
// rules only; these controls do not qualify a real model/provider execution.
public sealed class ModelProviderRegistryOriginalCatalogueTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Scope(Action body) => body();

    [Fact]
    public async Task Same_registered_policy_identity_locality_and_dedup_rules_preserve_actual_instances()
    {
        var local = new Provider("local", true, Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(
            [Model("local", true, "a"), Model("LOCAL", true, "a"), Model("foreign", true, "wrong")]));
        var remote = new Provider("remote", false, Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(
            [Model("remote", true, "cloud")]));
        var registry = new ModelProviderRegistry([remote, local, local]);
        Assert.Same(local, registry.Providers[0]);
        Assert.Same(remote, registry.Providers[1]);
        Assert.Throws<NotSupportedException>(() => ((IList<IModelProvider>)registry.Providers)[0] = remote);
        var raws = new List<Task>();
        var rows = await registry.GetModelsWithinOriginalSourceAsync(new(AllowRemote: false), Scope, raws.Add, Token);
        Assert.Single(rows);
        Assert.Equal("local:a", rows[0].Key);
        Assert.Equal(1, local.Calls); Assert.Equal(0, remote.Calls);
        Assert.Same(local.Original, Assert.Single(raws));
        rows = await registry.GetModelsWithinOriginalSourceAsync(new(), Scope, raws.Add, Token);
        Assert.False(Assert.Single(rows, row => row.ProviderId == "remote").IsLocal);
        var close = registry.CloseOriginalCataloguesAndDrainAsync();
        await close; Assert.Same(close, registry.OriginalCataloguesClose);
        Assert.Same(close, registry.CloseOriginalCataloguesAndDrainAsync());
        // Sealing the additive cohort leaves the legacy catalogue behavior intact.
        Assert.NotEmpty(await registry.GetModelsAsync(Token));
    }

    [Fact]
    public async Task Held_same_raw_is_joined_after_retainer_and_replacement_scope_faults()
    {
        var held = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var io = new IOException("actual retainer");
        var replacement = new InvalidOperationException("actual replacement scope");
        var opaque = new AggregateException("foreign opaque group", new IOException("foreign leaf"));
        var rawOce = new OperationCanceledException("faulted source occurrence");
        var provider = new Provider("local", true, held.Task); var registry = new ModelProviderRegistry([provider]);
        void Retain(Task raw)
        { Assert.Same(held.Task, raw); acquired.TrySetResult(); throw io; }
        void Replacing(Action body)
        { try { body(); } catch { } if (provider.Calls != 0) throw replacement; }
        var original = registry.GetModelsWithinOriginalSourceAsync(new(), Replacing, Retain, Token);
        try
        {
            Assert.Same(acquired.Task, await Task.WhenAny(acquired.Task, original).WaitAsync(Token));
            Assert.False(original.IsCompleted);
            var close = registry.CloseOriginalCataloguesAndDrainAsync();
            Assert.False(close.IsCompleted);
            held.SetException([opaque, rawOce]);
            var failure = await Record.ExceptionAsync(() => original);
            Assert.True(original.IsFaulted); Assert.NotNull(failure);
            AssertContains(failure, io, replacement, opaque, rawOce);
            AssertOnlyKnown(failure, io, replacement, opaque, rawOce);
            var closeFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeFailure); Assert.True(close.IsFaulted);
            AssertContains(closeFailure, io, replacement, opaque, rawOce);
            AssertOnlyKnown(closeFailure, io, replacement, opaque, rawOce);
            Assert.Same(close, registry.CloseOriginalCataloguesAndDrainAsync());
        }
        finally
        {
            held.TrySetException([opaque, rawOce]);
            var retained = await Record.ExceptionAsync(() => original);
            Assert.NotNull(retained); AssertOnlyKnown(retained, io, replacement, opaque, rawOce);
            var retainedClose = await Record.ExceptionAsync(registry.CloseOriginalCataloguesAndDrainAsync);
            Assert.NotNull(retainedClose); AssertOnlyKnown(retainedClose, io, replacement, opaque, rawOce);
        }
    }

    [Fact]
    public async Task Swallowed_body_and_second_callback_protocol_errors_remain_faulted()
    {
        var sentinel = new IOException("actual synchronous provider factory");
        var provider = new Provider("local", true, Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]))
            { FactoryFailure = sentinel };
        var registry = new ModelProviderRegistry([provider]);
        void Swallowing(Action body) { try { body(); } catch { } }
        var original = registry.GetModelsWithinOriginalSourceAsync(new(), Swallowing, _ => { }, Token);
        var failure = await Record.ExceptionAsync(() => original);
        Assert.NotNull(failure); Assert.True(original.IsFaulted); AssertContains(failure, sentinel);
        var closed = await Record.ExceptionAsync(registry.CloseOriginalCataloguesAndDrainAsync);
        Assert.NotNull(closed); AssertOnlyKnown(closed, sentinel);

        var second = new ModelProviderRegistry([]); var calls = 0;
        void Twice(Action body) { body(); calls++; try { body(); } catch { } }
        original = second.GetModelsWithinOriginalSourceAsync(new(), Twice, _ => { }, Token);
        Assert.NotNull(await Record.ExceptionAsync(() => original));
        Assert.True(original.IsFaulted); Assert.Equal(1, calls);
        Assert.NotNull(await Record.ExceptionAsync(second.CloseOriginalCataloguesAndDrainAsync));
    }

    [Fact]
    public async Task Restored_context_retainer_cannot_join_its_own_cached_global_close()
    {
        var neutral = ExecutionContext.Capture()!;
        var held = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ModelProviderRegistry([new Provider("local", true, held.Task)]);
        InvalidOperationException? refusal = null;
        void Retain(Task raw)
        {
            Assert.Same(held.Task, raw);
            ExecutionContext.Run(neutral, state => refusal = Assert.Throws<InvalidOperationException>(
                () => { _ = registry.CloseOriginalCataloguesAndDrainAsync(); }), null);
            Assert.Null(registry.OriginalCataloguesClose); acquired.SetResult();
        }
        var original = registry.GetModelsWithinOriginalSourceAsync(new(), Scope, Retain, Token);
        try
        {
            Assert.Same(acquired.Task, await Task.WhenAny(acquired.Task, original).WaitAsync(Token));
            var close = registry.CloseOriginalCataloguesAndDrainAsync(); Assert.False(close.IsCompleted);
            held.SetResult([]); await original; await close;
            Assert.NotNull(refusal); Assert.Same(close, registry.CloseOriginalCataloguesAndDrainAsync());
        }
        finally
        { held.TrySetResult([]); await original; await registry.CloseOriginalCataloguesAndDrainAsync(); }
    }

    [Fact]
    public async Task Independently_joined_healthy_catalogues_retire_before_the_unchanged_bound()
    {
        var provider = new Provider("local", true, Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        var registry = new ModelProviderRegistry([provider]);
        for (var index = 0; index < 260; index++)
            Assert.Empty(await registry.GetModelsWithinOriginalSourceAsync(new(), Scope, _ => { }, Token));
        Assert.Equal(260, provider.Calls);
        await registry.CloseOriginalCataloguesAndDrainAsync();
    }

    [Fact]
    public async Task Faulted_OCE_and_independent_scope_IO_preserve_actual_raw_fault_state()
    {
        var oce = new OperationCanceledException("actual faulted provider source");
        var io = new IOException("independent scope after raw publication");
        var raw = Task.FromException<IReadOnlyList<ProviderModelDescriptor>>(oce);
        var provider = new Provider("local", true, raw); var registry = new ModelProviderRegistry([provider]);
        void Postguard(Action body) { body(); if (provider.Calls != 0) throw io; }
        var original = registry.GetModelsWithinOriginalSourceAsync(new(), Postguard, _ => { }, Token);
        var failure = await Record.ExceptionAsync(() => original);
        Assert.True(raw.IsFaulted); Assert.True(original.IsFaulted); Assert.NotNull(failure);
        AssertContains(failure, oce, io); AssertOnlyKnown(failure, oce, io);
        var close = registry.CloseOriginalCataloguesAndDrainAsync();
        var closing = await Record.ExceptionAsync(() => close);
        Assert.NotNull(closing); Assert.True(close.IsFaulted); AssertContains(closing, oce, io); AssertOnlyKnown(closing, oce, io);
    }

    private static void AssertOnlyKnown(Exception actual, params Exception[] expected)
    {
        if (expected.Any(cause => ReferenceEquals(actual, cause))) return;
        if (actual is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var child in group.InnerExceptions) AssertOnlyKnown(child, expected); return; }
        Assert.Fail("An independently unknown model-source or cleanup cause was returned: " + actual.GetType().FullName);
    }
    private static void AssertContains(Exception actual, params Exception[] expected)
    { foreach (var cause in expected) Assert.True(Contains(actual, cause)); }
    private static bool Contains(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(child => Contains(child, expected));
    private static ProviderModelDescriptor Model(string provider, bool local, string name) =>
        new(provider, local, new(name, 0, "fixture", "", "", new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch));
    private sealed class Provider(string id, bool local, Task<IReadOnlyList<ProviderModelDescriptor>> original) : IModelProvider
    {
        public string Id => id; public string DisplayName => id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => local; public bool CanManageModels => false;
        internal Task<IReadOnlyList<ProviderModelDescriptor>> Original => original;
        internal int Calls; internal Exception? FactoryFailure;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { Calls++; if (FactoryFailure is { } cause) throw cause; return original; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
