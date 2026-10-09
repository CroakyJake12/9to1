#if !ANDROID
using System.Reflection;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

// These controlled provider Tasks witness original metadata custody only. No
// provider result, Home authority, installed app or Den store is fabricated.
public sealed class OriginalAppModelCatalogueLifetimeTests
{
    private static readonly List<object[]> FailedOwners = [];

    [Fact]
    public async Task Actual_singleton_factory_and_failed_startup_join_wait_for_the_same_original_catalogue()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var held = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ModelProviderRegistry([new Provider(held.Task)]);
        var app = new App();
        var services = Configure(app, registry);
        Task<IReadOnlyList<ProviderModelDescriptor>>? original = null; Task? close = null, parentClose = null;
        var rawSources = new List<Task>();
        try
        {
            // Real Microsoft DI calls this factory with its canonical root
            // ServiceProviderEngineScope, not the outer ServiceProvider.
            Assert.Same(registry, services.GetRequiredService<RetainedRegistry>().Actual);
            Assert.Same(registry, Field<ModelProviderRegistry>(app, "_actualModelCatalogueRegistry"));
            Assert.Null(Field<object>(app, "_actualAssistantPersonalDen"));
            original = registry.GetModelsWithinOriginalSourceAsync(new(), body => body(), raw =>
            { rawSources.Add(raw); Assert.Same(held.Task, raw); captured.TrySetResult(); }, timeout.Token);
            await AwaitGate(captured.Task, original, timeout.Token);
            parentClose = Call<Task>(app, "JoinOriginalAppProducersAsync");
            close = await AwaitPublishedCatalogueClose(app, parentClose, timeout.Token);
            Assert.Same(close, Call<Task>(app, "JoinOriginalUntransferredAssistantPersonalDenAsync"));
            Assert.False(close.IsCompleted);
            Assert.Null(Field<Task>(app, "_actualModelCatalogueDenClose"));
            Assert.Same(held.Task, Assert.Single(rawSources));
            held.SetResult([]);
            Assert.Empty(await original.WaitAsync(timeout.Token));
            await close.WaitAsync(timeout.Token);
            Assert.Same(registry.OriginalCataloguesClose, Field<Task>(app, "_actualModelCatalogueClose"));
            Assert.NotNull(Field<Task>(app, "_actualModelCatalogueDenClose"));
            Assert.Null(Field<object>(app, "_actualAssistantPersonalDen"));
        }
        finally
        {
            held.TrySetResult([]);
            await FinishHealthy(app, services, original, close, parentClose);
        }
    }

    [Fact]
    public async Task Faulted_original_OCE_and_IO_keep_the_same_cached_close_and_Den_dependency_unentered()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var oce = new OperationCanceledException("actual faulted metadata occurrence");
        var io = new IOException("independent actual metadata occurrence");
        var held = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ModelProviderRegistry([new Provider(held.Task)]);
        var app = new App(); var services = Configure(app, registry);
        Assert.Same(registry, services.GetRequiredService<RetainedRegistry>().Actual);
        Task<IReadOnlyList<ProviderModelDescriptor>>? original = null; Task? close = null, parentClose = null;
        var actualSources = new List<Task>();
        try
        {
            original = registry.GetModelsWithinOriginalSourceAsync(new(), body => body(), raw =>
            { actualSources.Add(raw); captured.TrySetResult(); }, timeout.Token);
            await AwaitGate(captured.Task, original, timeout.Token);
            parentClose = Call<Task>(app, "JoinOriginalAppProducersAsync");
            close = await AwaitPublishedCatalogueClose(app, parentClose, timeout.Token);
            Assert.False(close.IsCompleted);
            held.SetException([oce, io]);
            AssertKnown(await Record.ExceptionAsync(() => original.WaitAsync(timeout.Token)), oce, io);
            Assert.True(held.Task.IsFaulted); Assert.True(original.IsFaulted);
            AssertKnown(await Record.ExceptionAsync(() => close.WaitAsync(timeout.Token)), oce, io);
            Assert.True(close.IsFaulted);
            Assert.Same(close, Call<Task>(app, "JoinOriginalUntransferredAssistantPersonalDenAsync"));
            Assert.Same(held.Task, Assert.Single(actualSources));
            var rawClose = Assert.IsAssignableFrom<Task>(Field<Task>(app, "_actualModelCatalogueClose"));
            Assert.Same(registry.OriginalCataloguesClose, rawClose);
            AssertKnown(rawClose.Exception, oce, io);
            Assert.Null(Field<Task>(app, "_actualModelCatalogueDenClose"));
            Assert.Null(Field<object>(app, "_actualAssistantPersonalDen"));
            Assert.NotNull(Field<object>(app, "_actualModelCatalogueCloseSources"));
        }
        finally
        {
            // Root all owners/raws before inspecting any terminal failure. A new
            // unknown sibling may fail an assertion; its custody must still survive.
            lock (FailedOwners) FailedOwners.Add([app, services, registry, held.Task, original!, close!, parentClose!, actualSources]);
            held.TrySetException([oce, io]);
            if (original is not null) AssertKnown(await Record.ExceptionAsync(() => original), oce, io);
            if (close is not null) AssertKnown(await Record.ExceptionAsync(() => close), oce, io);
            if (parentClose is not null) AssertKnown(await Record.ExceptionAsync(() => parentClose), oce, io);
            // This control never disposes borrowed services through a failed join.
        }
    }

    [Fact]
    public async Task Actual_retainer_under_restored_context_and_live_logical_owner_cannot_begin_global_join()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var neutral = ExecutionContext.Capture()!;
        var held = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ModelProviderRegistry([new Provider(held.Task)]);
        var app = new App(); var services = Configure(app, registry);
        Assert.Same(registry, services.GetRequiredService<RetainedRegistry>().Actual);
        Task<IReadOnlyList<ProviderModelDescriptor>>? original = null; Task? close = null, parentClose = null; InvalidOperationException? physical = null;
        try
        {
            using (CloudflareOriginalExecutionGuard.EnterOriginal(registry))
                Assert.Throws<InvalidOperationException>(() => { _ = Call<Task>(app, "JoinOriginalUntransferredAssistantPersonalDenAsync"); });
            Assert.Null(registry.OriginalCataloguesClose);
            Assert.Null(Field<Task>(app, "_actualModelCatalogueAndDenClose"));
            original = registry.GetModelsWithinOriginalSourceAsync(new(), body => body(), raw =>
            {
                Assert.Same(held.Task, raw);
                ExecutionContext.Run(neutral, _ => physical = Assert.Throws<InvalidOperationException>(
                    () => { _ = Call<Task>(app, "JoinOriginalUntransferredAssistantPersonalDenAsync"); }), null);
                Assert.Null(registry.OriginalCataloguesClose);
                Assert.Null(Field<Task>(app, "_actualModelCatalogueAndDenClose"));
                captured.TrySetResult();
            }, timeout.Token);
            await AwaitGate(captured.Task, original, timeout.Token);
            Assert.NotNull(physical);
            parentClose = Call<Task>(app, "JoinOriginalAppProducersAsync");
            close = await AwaitPublishedCatalogueClose(app, parentClose, timeout.Token);
            Assert.False(close.IsCompleted);
            held.SetResult([]); Assert.Empty(await original.WaitAsync(timeout.Token));
            await close.WaitAsync(timeout.Token);
            Assert.Same(close, Call<Task>(app, "JoinOriginalUntransferredAssistantPersonalDenAsync"));
        }
        finally { held.TrySetResult([]); await FinishHealthy(app, services, original, close, parentClose); }
    }

    private static ServiceProvider Configure(App app, ModelProviderRegistry actual)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IModelProviderRegistry>(actual);
        collection.AddSingleton(provider => new RetainedRegistry(Call<ModelProviderRegistry>(app,
            "RetainOriginalModelCatalogueRegistry", provider)));
        var configured = collection.BuildServiceProvider();
        typeof(App).GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, configured);
        return configured;
    }
    private sealed record RetainedRegistry(ModelProviderRegistry Actual);
    private static T? Field<T>(App app, string name) where T : class =>
        (T?)typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app);
    private static T Call<T>(App app, string name, params object[] arguments)
    {
        try { return (T)typeof(App).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, arguments)!; }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
    private static async Task AwaitGate(Task gate, Task actual, CancellationToken token)
    {
        var observed = await Task.WhenAny(gate, actual).WaitAsync(token);
        if (ReferenceEquals(observed, actual)) await actual;
        Assert.Same(gate, observed); await gate;
    }
    private static async Task<Task> AwaitPublishedCatalogueClose(App app, Task sameParentClose, CancellationToken token)
    {
        while (Field<Task>(app, "_actualModelCatalogueAndDenClose") is not { } actual)
        {
            token.ThrowIfCancellationRequested();
            var observed = await Task.WhenAny(sameParentClose, Task.Delay(10, token)).WaitAsync(token);
            if (ReferenceEquals(observed, sameParentClose))
            {
                await sameParentClose; // Expose an early real shutdown failure, never disguise it as publication.
                throw new InvalidOperationException("The actual App close completed without publishing its catalogue phase.");
            }
        }
        return Field<Task>(app, "_actualModelCatalogueAndDenClose")!;
    }
    private static async Task FinishHealthy(App app, ServiceProvider services, Task? original, Task? close, Task? parentClose)
    {
        var failures = new List<Exception>();
        foreach (var raw in new[] { original, close, parentClose }.OfType<Task>())
            try { await raw; } catch (Exception caught) { failures.Add(raw.Exception ?? caught); }
        Task? appClose = null;
        try { appClose = Call<Task>(app, "JoinOriginalAppProducersAsync"); }
        catch (Exception caught) { failures.Add(caught); }
        if (appClose is not null)
            try { await appClose; } catch (Exception caught) { failures.Add(appClose.Exception ?? caught); }
        if (failures.Count != 0)
        {
            lock (FailedOwners) FailedOwners.Add([app, services, original!, close!, parentClose!, appClose!, failures]);
            throw new AggregateException("Actual App model catalogue cleanup remains retained.", failures);
        }
        await services.DisposeAsync();
    }
    private static void AssertKnown(Exception? actual, params Exception[] known)
    {
        Assert.NotNull(actual);
        foreach (var cause in known) Assert.True(Contains(actual, cause));
        DemandKnown(actual, known);
    }
    private static bool Contains(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(child => Contains(child, expected));
    private static void DemandKnown(Exception actual, Exception[] known)
    {
        if (known.Any(cause => ReferenceEquals(actual, cause))) return;
        if (actual is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var child in group.InnerExceptions) DemandKnown(child, known); return; }
        Assert.Fail("An independently unknown actual cleanup cause remains: " + actual.GetType().FullName);
    }
    private sealed class Provider(Task<IReadOnlyList<ProviderModelDescriptor>> actual) : IModelProvider
    {
        public string Id => "custody-fixture"; public string DisplayName => Id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true; public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => actual;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
#endif
