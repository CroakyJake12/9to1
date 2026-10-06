using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Core.Tests;

// These controls exercise the actual private cold policy reader and actual authority physical
// guard, with source-owned raw tasks. They issue no cold claim, context, activation or grant.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Cold_second_provider_callback_after_held_first_refuses_restored_context_authority_join()
    {
        var held = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new PolicyProvider("first", () => { entered.TrySetResult(); return held.Task; });
        var secondCalls = 0; var refused = false;
        var originalContext = ExecutionContext.Capture()!;
        TaskRunPermissionAuthority? authority = null;
        var second = new PolicyProvider("second", () =>
        {
            secondCalls++;
            ExecutionContext.Run(originalContext, state =>
            {
                Assert.Throws<InvalidOperationException>((Action)(() =>
                { _ = authority!.CloseAndDrainOwnerReauthenticationAsync(); }));
                refused = true;
            }, null);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]);
        });
        authority = PolicyAuthority(new PolicyStore(), first, second);
        var sources = PolicySources(authority);
        var read = ReadActualColdPolicy(authority, sources, PolicyInput(first.Model));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(read.IsCompleted); Assert.Equal(0, secondCalls);
            held.TrySetResult([first.Model]); await read.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(refused); Assert.Equal(1, secondCalls);
            Assert.Contains(sources.OriginalTasks, task => ReferenceEquals(task, held.Task));
            Assert.Empty(sources.OriginalErrors);
        }
        finally
        {
            held.TrySetResult([first.Model]);
            try { await read; } finally { await sources.ObserveAllOriginalTasksAsync(); await authority.CloseAndDrainOwnerReauthenticationAsync(); }
        }
    }

    [Fact]
    public async Task Cold_catalogue_faulted_OCE_and_sibling_are_retained_even_with_a_healthy_selected_model()
    {
        var selected = new PolicyProvider("selected", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        selected.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([selected.Model]);
        var cancellation = new OperationCanceledException("actual provider fault");
        var sibling = new IOException("actual provider sibling");
        var original = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        original.SetException([cancellation, sibling]);
        var failed = new PolicyProvider("failed", () => original.Task);
        var authority = PolicyAuthority(new PolicyStore(), selected, failed); var sources = PolicySources(authority);
        try
        {
            var read = ReadActualColdPolicy(authority, sources, PolicyInput(selected.Model));
            var failure = await Assert.ThrowsAsync<AggregateException>(() => read);
            Assert.True(original.Task.IsFaulted); Assert.False(original.Task.IsCanceled);
            Assert.Same(cancellation, original.Task.Exception!.InnerExceptions[0]);
            Assert.Same(sibling, original.Task.Exception.InnerExceptions[1]);
            Assert.Contains(sources.OriginalTasks, task => ReferenceEquals(task, original.Task));
            Assert.Contains(sources.OriginalErrors, cause => ReferenceEquals(cause, cancellation));
            Assert.Contains(sources.OriginalErrors, cause => ReferenceEquals(cause, sibling));
            Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, sibling));
        }
        finally { await sources.ObserveAllOriginalTasksAsync(); await authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Cold_restricted_policy_retains_the_actual_faulted_policy_Task_and_all_direct_siblings()
    {
        var cancellation = new OperationCanceledException("actual policy fault"); var sibling = new IOException("actual policy sibling");
        var original = new TaskCompletionSource<ModelPermissionPolicy>(TaskCreationOptions.RunContinuationsAsynchronously);
        original.SetException([cancellation, sibling]);
        var store = new PolicyStore { Read = () => original.Task };
        var provider = new PolicyProvider("selected", () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]));
        provider.Model = provider.Model with { Model = provider.Model.Model with { Capabilities = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Browser } } };
        provider.Read = () => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([provider.Model]);
        var authority = PolicyAuthority(store, provider); var sources = PolicySources(authority);
        try
        {
            var read = ReadActualColdPolicy(authority, sources, PolicyInput(provider.Model) with { ExplicitCapabilities = [ToolCapability.Browser] });
            var failure = await Assert.ThrowsAsync<AggregateException>(() => read);
            Assert.Equal(1, store.Reads); Assert.True(original.Task.IsFaulted); Assert.False(read.IsCanceled);
            Assert.Contains(sources.OriginalTasks, task => ReferenceEquals(task, original.Task));
            Assert.Contains(sources.OriginalErrors, cause => ReferenceEquals(cause, cancellation));
            Assert.Contains(sources.OriginalErrors, cause => ReferenceEquals(cause, sibling));
            Assert.Same(cancellation, failure.InnerExceptions[0]); Assert.Same(sibling, failure.InnerExceptions[1]);
        }
        finally { await sources.ObserveAllOriginalTasksAsync(); await authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }
    private static TaskRunPermissionAuthority PolicyAuthority(PolicyStore store, params PolicyProvider[] providers)
    {
        var configurations = new Configurations();
        foreach (var provider in providers) configurations.Rows[provider.Id] = new(provider.Id, provider.Kind, provider.DisplayName,
            "http://127.0.0.1:11434", true, true, false, new Dictionary<string,string>(), DateTimeOffset.UnixEpoch);
        return new(new Actors(), new Registry(providers), configurations, new Privacy(), new(store));
    }
    private static CloudflareOriginalTaskLedger PolicySources(TaskRunPermissionAuthority authority)
    {
        var method = typeof(TaskRunPermissionAuthority).GetMethod("InvokeRenewalPhysical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var invoke = method.MakeGenericMethod(typeof(bool)).CreateDelegate<Func<Func<bool>, bool>>(authority);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(authority);
        sources.BindOriginalCallerCallback(callback => { _ = invoke(() => { callback(); return true; }); });
        return sources;
    }
    private static Task ReadActualColdPolicy(TaskRunPermissionAuthority authority, CloudflareOriginalTaskLedger sources, TaskRunColdChatInput input) =>
        typeof(TaskRunPermissionAuthority).GetMethod("ReadColdPolicyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<CloudflareOriginalTaskLedger, TaskRunColdChatInput, CancellationToken, Task>>(authority)(sources, input, default);
    private static TaskRunColdChatInput PolicyInput(ProviderModelDescriptor model) => new(null!, "source control", model.Model,
        EffortLevel.Low, [], "", "", DuoMode.Solo, null, null, null, null, null, null, null,
        PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask, [ToolCapability.Text], null, null);
    private sealed class PolicyStore : IModelPermissionStore
    {
        internal int Reads; internal Func<Task<ModelPermissionPolicy>> Read = () => Task.FromResult(ModelPermissionPolicy.Empty);
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) { Reads++; return Read(); }
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed partial class PolicyProvider(string id, Func<Task<IReadOnlyList<ProviderModelDescriptor>>> read) : IModelProvider
    {
        internal Func<Task<IReadOnlyList<ProviderModelDescriptor>>> Read = read;
        internal ProviderModelDescriptor Model = new(id, true, new ModelDescriptor("synthetic-model", 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));
        public string Id => id; public string DisplayName => id; public bool IsLocal => true;
        public ModelProviderKind Kind => ModelProviderKind.Ollama; public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => Read();
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
