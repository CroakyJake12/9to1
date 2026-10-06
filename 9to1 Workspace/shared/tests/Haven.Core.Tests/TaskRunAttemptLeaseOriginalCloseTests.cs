using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

// Synthetic provider/actor admission only. These controls exercise the real authority Lease
// and actual commit-pin/cloud-close Tasks, not genuine cloud/account/context authority.
public sealed class TaskRunAttemptLeaseOriginalCloseTests
{
    [Fact]
    public async Task Same_actual_close_waits_held_pin_then_actual_cloud_close_original()
    {
        var rig = new Setup(); var entered = NewSignal(); var held = NewSignal();
        rig.Cloud.Lease.Close = () => { entered.TrySetResult(); return new(held.Task); };
        var lease = await Issue(rig); var pin = await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default);
        Assert.NotNull(pin); Task? close = null;
        try
        {
            close = lease.DisposeAsync().AsTask(); Assert.Same(close, lease.DisposeAsync().AsTask());
            Assert.False(close.IsCompleted); Assert.Equal(0, rig.Cloud.Lease.Closes);
            await pin!.DisposeAsync(); pin = null;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(close.IsCompleted); Assert.False(held.Task.IsCompleted);
            Assert.Null(await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default));
            held.TrySetResult(); await close; Assert.Equal(1, rig.Cloud.Lease.Closes); Assert.True(close.IsCompletedSuccessfully);
        }
        finally
        {
            held.TrySetResult(); if (pin is not null) await pin.DisposeAsync(); close ??= lease.DisposeAsync().AsTask();
            await close; await rig.Authority.CloseAndDrainOwnerReauthenticationAsync();
        }
    }

    [Fact]
    public async Task Faulted_actual_cloud_close_OCE_and_sibling_remain_faulted_and_retained()
    {
        var rig = new Setup(); var raw = NewSignal();
        var first = new OperationCanceledException("synthetic faulted close OCE"); var sibling = new IOException("synthetic close sibling");
        raw.TrySetException([first, sibling]); rig.Cloud.Lease.Close = () => new(raw.Task);
        var lease = await Issue(rig); var close = lease.DisposeAsync().AsTask();
        try
        {
            var actual = await Record.ExceptionAsync(() => close); Assert.NotNull(actual);
            Assert.True(close.IsFaulted); Assert.False(close.IsCanceled); Assert.True(raw.Task.IsFaulted);
            Assert.True(Has(actual!, first)); Assert.True(Has(actual!, sibling));
            Assert.Same(close, lease.DisposeAsync().AsTask()); Assert.Equal(1, rig.Cloud.Lease.Closes);
        }
        finally { await Observe(close); await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Synchronous_cloud_close_OCE_keeps_exact_fault_in_actual_driver()
    {
        var rig = new Setup(); var exact = new OperationCanceledException("synthetic synchronous close fault");
        rig.Cloud.Lease.Close = () => throw exact;
        var lease = await Issue(rig); var close = lease.DisposeAsync().AsTask();
        try
        {
            var error = await Record.ExceptionAsync(() => close); Assert.NotNull(error); Assert.True(Has(error!, exact));
            Assert.True(close.IsFaulted); Assert.False(close.IsCanceled); Assert.Same(close, lease.DisposeAsync().AsTask());
        }
        finally { await Observe(close); await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Actual_canceled_cloud_close_does_not_certify_healthy_attempt_retirement()
    {
        var rig = new Setup(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var raw = Task.FromCanceled(canceled.Token); rig.Cloud.Lease.Close = () => new(raw);
        var lease = await Issue(rig); var close = lease.DisposeAsync().AsTask();
        try
        {
            Assert.NotNull(await Record.ExceptionAsync(() => close)); Assert.True(raw.IsCanceled);
            Assert.True(close.IsFaulted); Assert.False(close.IsCompletedSuccessfully); Assert.Same(close, lease.DisposeAsync().AsTask());
        }
        finally { await Observe(close); await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Restored_context_actual_cloud_close_callback_cannot_join_containing_close()
    {
        var rig = new Setup(); var external = ExecutionContext.Capture()!; ITaskRunAdmissionLease? lease = null;
        Exception? denied = null, deniedPin = null;
        rig.Cloud.Lease.Close = () =>
        {
            ExecutionContext.Run(external, state =>
            {
                denied = Record.Exception(() => { _ = lease!.DisposeAsync(); });
                deniedPin = Record.Exception(() => { _ = ((ITaskRunAdmissionCommitLease)lease!).AcquireOriginalCommitPinAsync(default); });
            }, null);
            return ValueTask.CompletedTask;
        };
        lease = await Issue(rig); var close = lease.DisposeAsync().AsTask();
        try
        {
            await close; Assert.IsType<InvalidOperationException>(denied); Assert.True(close.IsCompletedSuccessfully);
            Assert.IsType<InvalidOperationException>(deniedPin);
        }
        finally { await close; await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Retired_captured_close_context_allows_later_same_original_task_retrieval()
    {
        var rig = new Setup(); ExecutionContext? inherited = null;
        rig.Cloud.Lease.Close = () => { inherited = ExecutionContext.Capture(); return ValueTask.CompletedTask; };
        var lease = await Issue(rig); var close = lease.DisposeAsync().AsTask(); Task? same = null;
        try
        {
            await close; Assert.NotNull(inherited);
            ExecutionContext.Run(inherited!, state => { same = lease.DisposeAsync().AsTask(); }, null);
            Assert.Same(close, same); Assert.Equal(1, rig.Cloud.Lease.Closes);
        }
        finally { await close; await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Nested_actual_lease_close_preserves_live_ancestor_through_restored_context()
    {
        var first = new Setup(); var second = new Setup(); var external = ExecutionContext.Capture()!;
        var firstLease = await Issue(first); var secondLease = await Issue(second); Exception? refused = null; Task? secondClose = null;
        first.Cloud.Lease.Close = () =>
        {
            ExecutionContext.Run(external, state => { secondClose = secondLease.DisposeAsync().AsTask(); }, null);
            return new(secondClose!); // First actual original waits for the second actual original.
        };
        second.Cloud.Lease.Close = () =>
        {
            ExecutionContext.Run(external, state => { refused = Record.Exception(() => { _ = firstLease.DisposeAsync(); }); }, null);
            return ValueTask.CompletedTask;
        };
        var firstClose = firstLease.DisposeAsync().AsTask();
        try
        {
            await firstClose.WaitAsync(TimeSpan.FromSeconds(5)); Assert.NotNull(secondClose); await secondClose!;
            Assert.IsType<InvalidOperationException>(refused); Assert.Same(firstClose, firstLease.DisposeAsync().AsTask());
            Assert.Equal(1, first.Cloud.Lease.Closes); Assert.Equal(1, second.Cloud.Lease.Closes);
        }
        finally
        {
            await firstClose; secondClose ??= secondLease.DisposeAsync().AsTask(); await secondClose;
            await first.Authority.CloseAndDrainOwnerReauthenticationAsync(); await second.Authority.CloseAndDrainOwnerReauthenticationAsync();
        }
    }

    private static async Task<ITaskRunAdmissionLease> Issue(Setup rig)
    {
        var proposed = Proposed(); var current = proposed with { OwnerBinding = await rig.Authority.AuthorizeStartAsync(proposed, default) };
        var candidate = await rig.Authority.CaptureSelectedRouteAsync(current, rig.Provider.Model, [ToolCapability.Text], []);
        return await rig.Authority.AuthorizeAttemptAsync(current, Guid.NewGuid(), candidate, null, default);
    }
    private sealed class CloseSource : ITaskRunCloudAdmissionSource
    {
        public readonly CloseLease Lease = new();
        public ValueTask<ITaskRunCloudAdmissionLease?> AcquireOriginalAsync(TaskExecutionOwnerBinding owner,
            ProviderModelDescriptor model, ProviderConfiguration configuration, TaskRunRouteCandidate candidate, CancellationToken token)
            => ValueTask.FromResult<ITaskRunCloudAdmissionLease?>(Lease);
    }
    private sealed class CloseLease : ITaskRunCloudAdmissionLease
    {
        public Func<ValueTask> Close { get; set; } = () => ValueTask.CompletedTask;
        public int Closes;
        public ValueTask RevalidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Closes); return Close(); }
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Observe(Task actual) { try { await actual; } catch { _ = actual.Exception; } }
    private static bool Has(Exception error, Exception exact) => ReferenceEquals(error, exact) ||
        error is AggregateException group && group.InnerExceptions.Any(inner => Has(inner, exact));
    private static TaskExecutionSnapshot Proposed() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Synthetic existing local task", TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan,
        1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private sealed class Setup
    {
        public readonly Actors Actors = new(); public readonly Provider Provider = new("synthetic-remote", false);
        public readonly Configurations Configurations = new(); public readonly Privacy Privacy = new();
        public readonly Permissions Permissions = new(); public readonly CloseSource Cloud = new(); public readonly TaskRunPermissionAuthority Authority;
        public Setup()
        {
            Configurations.Rows[Provider.Id] = new(Provider.Id, Provider.Kind, Provider.DisplayName,
                "https://synthetic.invalid", true, false, false, new Dictionary<string,string>(), DateTimeOffset.UnixEpoch);
            Authority = new(Actors, new Registry(Provider), Configurations, Privacy, new(Permissions), cloud: Cloud);
        }
        public TaskRunPermissionAuthority Create(ITaskRunVerifiedReauthenticationSource identity) => new(
            Actors, new Registry(Provider), Configurations, Privacy, new(Permissions),
            cloud: null, receipts: null, routes: null, originalTasks: null,
            verifiedReauthentication: identity, contextReauthentication: null, reauthenticationCustody: null);
    }
    private sealed class UnissuedQuiescence : ITaskRunReauthenticationQuiescence
    {
        public int Closes, Pins;
        public ValueTask DisposeAsync() { Closes++; return ValueTask.CompletedTask; }
        public ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken token)
        { Pins++; throw new UnauthorizedAccessException("Synthetic unissued scope must never be consumed."); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public int Reads; public AuthenticatedResourceActor? Actor = new("synthetic-product-owner", "synthetic-profile", Guid.Parse("222f7a40-c183-488e-a7ad-5566c82f06a4"), null, "revision-one");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { Reads++; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Actor); }
    }
    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public string Id => id; public string DisplayName => id; public bool IsLocal => local; public bool CanManageModels => false;
        public ModelProviderKind Kind => local ? ModelProviderKind.Ollama : ModelProviderKind.OpenAI;
        public ProviderModelDescriptor Model = new(id, local, new ModelDescriptor("synthetic-model", 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Func<Task>? HeldRead { get; set; }
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { if (HeldRead is { } read) await read(); token.ThrowIfCancellationRequested(); return [Model]; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Registry(params IModelProvider[] providers) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => providers;
        public IModelProvider? Find(string id) => providers.SingleOrDefault(provider => provider.Id == id);
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { var result = new List<ProviderModelDescriptor>(); foreach (var provider in providers) result.AddRange(await provider.GetModelsAsync(token)); return result; }
    }
    private sealed class Configurations : IProviderConfigurationStore
    {
        public readonly Dictionary<string, ProviderConfiguration> Rows = new(StringComparer.Ordinal);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(Rows.Values.ToArray());
        public Task UpsertAsync(ProviderConfiguration config, CancellationToken token) { Rows[config.Id] = config; return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken token) { Rows.Remove(id); return Task.CompletedTask; }
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
    }
    private sealed class Permissions : IModelPermissionStore
    {
        public ModelPermissionPolicy Policy = ModelPermissionPolicy.Empty;
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(Policy);
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) { Policy = value; return Task.CompletedTask; }
    }
}
