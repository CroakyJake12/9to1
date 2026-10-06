using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

// Uses actual authority/model evaluator and maintained synthetic provider implementations.
// No genuine context/CAS producer is substituted by a ready Boolean; actual rotation remains
// unavailable here. The identity-source14 and signed-browser12 controls are separate proofs.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Missing_context_and_custody_do_not_seal_an_existing_legitimate_local_attempt()
    {
        var rig = new Setup(); var proposed = Proposed();
        var task = proposed with { OwnerBinding = await rig.Authority.AuthorizeStartAsync(proposed, default) };
        var candidate = await rig.Authority.CaptureSelectedRouteAsync(task, rig.Provider.Model, [ToolCapability.Text], []);
        var lease = await rig.Authority.AuthorizeAttemptAsync(task, Guid.NewGuid(), candidate, null, default);
        var scope = new UnissuedQuiescence();
        try
        {
            Assert.False(rig.Authority.HasOwnerReauthenticationSources);
            Assert.Throws<InvalidOperationException>(() => { _ = rig.Authority.PrepareOriginalAsync(
                task with { State = TaskExecutionLifecycle.Suspended, PersistenceRevision = 1 }, scope, default); });
            Assert.Equal(0, scope.Closes); Assert.Equal(0, scope.Pins);
            await lease.RevalidateAsync(default); Assert.Equal(task.OwnerBinding, lease.Owner);
            var pin = await ((ITaskRunAdmissionCommitLease)lease).AcquireOriginalCommitPinAsync(default);
            Assert.NotNull(pin); await pin!.DisposeAsync();
        }
        finally { await lease.DisposeAsync(); await rig.Authority.CloseAndDrainOwnerReauthenticationAsync(); }
    }

    [Fact]
    public async Task Foreign_actor_reader_cannot_become_the_configured_renewal_identity_source()
    {
        var rig = new Setup(); var other = new Actors();
        var identity = new TaskRunVerifiedActorReauthenticationSource(other);
        try
        {
            Assert.Throws<ArgumentException>(() => rig.Create(identity));
            Assert.Equal(0, other.Reads); Assert.Equal(0, rig.Actors.Reads);
        }
        finally { await identity.DisposeAsync(); }
    }

    [Fact]
    public async Task Verified_identity_source_alone_does_not_enable_Task_binding_rotation()
    {
        var rig = new Setup(); var identity = new TaskRunVerifiedActorReauthenticationSource(rig.Actors);
        var authority = rig.Create(identity); var proposed = Proposed();
        var current = proposed with { OwnerBinding = await authority.AuthorizeStartAsync(proposed, default) };
        var before = rig.Actors.Reads; var unissued = new UnissuedQuiescence();
        try
        {
            Assert.False(authority.HasOwnerReauthenticationSources);
            Assert.Throws<InvalidOperationException>(() => { _ = authority.PrepareOriginalAsync(
                current with { State = TaskExecutionLifecycle.Suspended, PersistenceRevision = 1 }, unissued, default); });
            Assert.Equal(before, rig.Actors.Reads); Assert.Equal(0, unissued.Closes);
            Assert.Equal("revision-one", current.OwnerBinding!.AuthenticationRevision);
        }
        finally { await authority.CloseAndDrainOwnerReauthenticationAsync(); await identity.DisposeAsync(); }
    }

    private static TaskExecutionSnapshot Proposed() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Synthetic existing local task", TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan,
        1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private sealed class Setup
    {
        public readonly Actors Actors = new(); public readonly Provider Provider = new("ollama", true);
        public readonly Configurations Configurations = new(); public readonly Privacy Privacy = new();
        public readonly Permissions Permissions = new(); public readonly TaskRunPermissionAuthority Authority;
        public Setup()
        {
            Configurations.Rows[Provider.Id] = new(Provider.Id, Provider.Kind, Provider.DisplayName,
                "http://127.0.0.1:11434", true, true, false, new Dictionary<string,string>(), DateTimeOffset.UnixEpoch);
            Authority = new(Actors, new Registry(Provider), Configurations, Privacy, new(Permissions));
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
