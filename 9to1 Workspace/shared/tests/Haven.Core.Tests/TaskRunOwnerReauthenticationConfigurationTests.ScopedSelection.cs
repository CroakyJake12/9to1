using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

// Actual authority private selection/admission; controlled sources issue no native/cold receipt.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Scoped_local_selection_uses_current_catalogue_and_can_feed_normal_attempt_admission()
    {
        var originals = new List<Task>(); var provider = new SelectionProvider(); var actors = new SelectionActors();
        var authority = SelectionAuthority(actors, provider, new SelectionConfigurations(provider));
        ITaskRunAdmissionLease? lease = null;
        await RunSelectionControl(authority, () => lease, originals, [], async () =>
        {
            var proposed = Proposed(); var mint = authority.AuthorizeStartAsync(proposed, default); originals.Add(mint);
            var current = proposed with { OwnerBinding = await mint };
            TaskRunRouteCandidate? candidate = null;
            for (var index = 0; index != 160; index++)
            {
                var actual = authority.CaptureSelectedRouteWithinOriginalSourceAsync(current, provider.Model,
                    [ToolCapability.Text], [], callback => callback(), originals.Add, default); originals.Add(actual);
                candidate = await actual;
            }
            Assert.NotNull(candidate); Assert.False(candidate!.UsesCloud); Assert.Equal(provider.Id, candidate.ProviderId);
            var admission = authority.AuthorizeAttemptAsync(current, Guid.NewGuid(), candidate, null, default);
            originals.Add(admission); lease = await admission; Assert.Same(candidate, lease.Candidate);
            Assert.True(actors.ScopedReads >= 320); Assert.True(provider.Reads >= 160);
        });
    }
    [Fact]
    public async Task Scoped_route_held_configuration_protects_restored_context_provider_getter_from_owner_join()
    {
        var originals = new List<Task>(); var provider = new SelectionProvider(); var actors = new SelectionActors();
        var configuration = new SelectionConfigurations(provider); var authority = SelectionAuthority(actors, provider, configuration);
        var held = new TaskCompletionSource<ProviderConfiguration?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var context = ExecutionContext.Capture()!;
        var guarded = 0; Task<TaskRunRouteCandidate>? actual = null;
        await RunSelectionControl(authority, () => null, originals, [], async () =>
        {
            try
            {
                var proposed = Proposed(); var mint = authority.AuthorizeStartAsync(proposed, default); originals.Add(mint);
                var current = proposed with { OwnerBinding = await mint }; configuration.Read = () => held.Task;
                provider.Metadata = () => ExecutionContext.Run(context, _ =>
                {
                    Assert.Throws<InvalidOperationException>((Action)(() => { _ = authority.CloseAndDrainOwnerReauthenticationAsync(); }));
                    guarded++;
                }, null);
                actual = authority.CaptureSelectedRouteWithinOriginalSourceAsync(current, provider.Model, [ToolCapability.Text], [],
                    callback => callback(), raw => { originals.Add(raw); if (ReferenceEquals(raw, held.Task)) enrolled.TrySetResult(); }, default);
                originals.Add(actual);
                using var observationCancellation = new CancellationTokenSource();
                await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), observationCancellation.Token);
                Assert.Contains(originals, raw => ReferenceEquals(raw, held.Task)); Assert.False(actual.IsCompleted);
                held.TrySetResult(configuration.Configuration); await actual;
                Assert.True(guarded > 0); Assert.Equal(1, provider.Reads);
            }
            finally { provider.Metadata = null; held.TrySetResult(configuration.Configuration); }
        });
    }
    [Fact]
    public async Task Scoped_route_retains_actual_faulted_catalogue_OCE_and_direct_sibling()
    {
        var originals = new List<Task>(); var provider = new SelectionProvider(); var authority = SelectionAuthority(new SelectionActors(), provider, new SelectionConfigurations(provider));
        var cancellation = new OperationCanceledException("original catalogue fault"); var sibling = new IOException("original catalogue sibling");
        var raw = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([cancellation, sibling]);
        await RunSelectionControl(authority, () => null, originals, [cancellation, sibling], async () =>
        {
            var proposed = Proposed(); var mint = authority.AuthorizeStartAsync(proposed, default); originals.Add(mint);
            var current = proposed with { OwnerBinding = await mint }; provider.Read = () => raw.Task;
            var actual = authority.CaptureSelectedRouteWithinOriginalSourceAsync(current, provider.Model, [ToolCapability.Text], [], callback => callback(), originals.Add, default);
            originals.Add(actual); var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.True(raw.Task.IsFaulted);
            Assert.Contains(originals, value => ReferenceEquals(value, raw.Task));
            Assert.Contains(SelectionLeaves(error), value => ReferenceEquals(value, cancellation));
            Assert.Contains(SelectionLeaves(error), value => ReferenceEquals(value, sibling));
        });
    }
    [Fact]
    public async Task Scoped_route_swallowed_repeated_callback_refuses_and_joins_already_acquired_raw_actor()
    {
        var originals = new List<Task>(); var provider = new SelectionProvider(); var actors = new SelectionActors();
        var authority = SelectionAuthority(actors, provider, new SelectionConfigurations(provider)); var expected = new List<Exception>();
        await RunSelectionControl(authority, () => null, originals, expected, async () =>
        {
            var proposed = Proposed(); var mint = authority.AuthorizeStartAsync(proposed, default); originals.Add(mint);
            var current = proposed with { OwnerBinding = await mint }; var calls = 0;
            var actual = authority.CaptureSelectedRouteWithinOriginalSourceAsync(current, provider.Model, [ToolCapability.Text], [], callback =>
            {
                callback(); calls++; try { callback(); } catch (InvalidOperationException refusal) { expected.Add(refusal); }
            }, originals.Add, default); originals.Add(actual);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Equal(1, calls); Assert.Single(expected); Assert.Equal(1, actors.ScopedReads); Assert.Equal(0, provider.Reads);
            Assert.Contains(originals, raw => ReferenceEquals(raw, actors.LastScopedTask));
            Assert.Contains(SelectionLeaves(error), value => ReferenceEquals(value, expected[0]));
        });
    }
    [Fact]
    public async Task Scoped_route_propagates_parent_to_actual_catalogue_factory_after_held_child_await()
    {
        var originals = new List<Task>(); var inner = new SelectionProvider(); var actors = new SelectionActors();
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = ExecutionContext.Capture()!; TaskRunPermissionAuthority? authority = null; var refused = 0;
        var deep = new DeepSelectionProvider(inner, held.Task, () => ExecutionContext.Run(context, _ =>
        {
            Assert.Throws<InvalidOperationException>((Action)(() => { _ = authority!.CloseAndDrainOwnerReauthenticationAsync(); })); refused++;
        }, null));
        authority = new(actors, new Registry(deep), new SelectionConfigurations(inner), new Privacy(), new(new Permissions()));
        await RunSelectionControl(authority, () => null, originals, [], async () =>
        {
            try
            {
                var proposed = Proposed(); var mint = authority.AuthorizeStartAsync(proposed, default); originals.Add(mint);
                var current = proposed with { OwnerBinding = await mint };
                var actual = authority.CaptureSelectedRouteWithinOriginalSourceAsync(current, inner.Model, [ToolCapability.Text], [],
                    callback => callback(), raw => { originals.Add(raw); if (ReferenceEquals(raw, held.Task)) enrolled.TrySetResult(); }, default); originals.Add(actual);
                using var observationCancellation = new CancellationTokenSource();
                await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), observationCancellation.Token);
                Assert.False(actual.IsCompleted); Assert.Equal(0, refused);
                held.TrySetResult(true); var candidate = await actual;
                Assert.Equal(1, refused); Assert.Equal(inner.Model.Name, candidate.ModelId);
                Assert.Contains(originals, raw => ReferenceEquals(raw, held.Task)); Assert.Equal(1, inner.Reads);
            }
            finally { held.TrySetResult(true); }
        });
    }
    private sealed class DeepSelectionProvider(SelectionProvider inner, Task<bool> held, Action laterFactory)
        : IModelProvider, ITaskRunOriginalProviderCatalogueSource
    {
        public string Id => inner.Id; public string DisplayName => inner.DisplayName; public bool IsLocal => inner.IsLocal;
        public ModelProviderKind Kind => inner.Kind; public bool CanManageModels => inner.CanManageModels;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => inner.GetModelsAsync(token);
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalTaskSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var first = OriginalFixtureFactory(() => held, scope, retain); await first.ConfigureAwait(false);
            var second = OriginalFixtureFactory(() => { laterFactory(); return inner.GetModelsAsync(token); }, scope, retain);
            return await second.ConfigureAwait(false);
        }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => inner.CheckHealthAsync(token);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => inner.StreamChatAsync(request, token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => inner.CompleteAsync(request, token);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => inner.ChatWithToolsAsync(request, token);
    }
    private static TaskRunPermissionAuthority SelectionAuthority(SelectionActors actors, SelectionProvider provider, SelectionConfigurations configurations) =>
        new(actors, new Registry(provider), configurations, new Privacy(), new(new Permissions()));
    private static async Task RunSelectionControl(TaskRunPermissionAuthority authority, Func<ITaskRunAdmissionLease?> lease,
        List<Task> originals, IReadOnlyCollection<Exception> expected, Func<Task> body)
    {
        Exception? primary = null; var failures = new List<Exception>(); Task? leaseClose = null, ownerClose = null;
        try { await body(); } catch (Exception error) { primary = error; }
        try { if (lease() is { } actual) { leaseClose = actual.DisposeAsync().AsTask(); originals.Add(leaseClose); } } catch (Exception error) { failures.Add(error); }
        try { ownerClose = authority.CloseAndDrainOwnerReauthenticationAsync(); originals.Add(ownerClose); } catch (Exception error) { failures.Add(error); }
        foreach (var raw in originals.Distinct().ToArray())
            try { await raw; } catch (Exception error) { failures.Add(raw.Exception ?? error); }
        var unknown = failures.SelectMany(SelectionLeaves).Distinct<Exception>(ReferenceEqualityComparer.Instance)
            .Where(error => !expected.Any(value => ReferenceEquals(value, error))).ToArray();
        if (unknown.Length != 0) throw new AggregateException("Actual scoped selection cleanup failed.", primary is null ? unknown : new[] { primary }.Concat(unknown));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private static IEnumerable<Exception> SelectionLeaves(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(SelectionLeaves) : [error];
    private sealed class SelectionActors : ITaskRunOriginalTaskActorObservationSource
    {
        private readonly Actors _actual = new(); public int ScopedReads; public Task? LastScopedTask;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => _actual.GetCurrentAsync(token);
        public Task<AuthenticatedResourceActor?> GetOriginalCurrentWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<AuthenticatedResourceActor?>? raw = null; scope(() => { ScopedReads++; raw = _actual.GetCurrentAsync(token).AsTask(); LastScopedTask = raw; retain(raw); });
            return raw ?? throw new InvalidOperationException("Actual controlled actor callback was not invoked.");
        }
    }
    private sealed class SelectionConfigurations(SelectionProvider provider) : IProviderConfigurationStore
    {
        internal readonly ProviderConfiguration Configuration = new("ollama", ModelProviderKind.Ollama, "controlled", "http://127.0.0.1:11434", true, true, false, new Dictionary<string,string>(), DateTimeOffset.UnixEpoch);
        internal Func<Task<ProviderConfiguration?>>? Read;
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) { token.ThrowIfCancellationRequested(); return Read?.Invoke() ?? Task.FromResult<ProviderConfiguration?>(id == provider.Id ? Configuration : null); }
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Configuration]);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class SelectionProvider : IModelProvider, ITaskRunOriginalProviderCatalogueSource
    {
        internal Action? Metadata; internal int Reads; internal Func<Task<IReadOnlyList<ProviderModelDescriptor>>>? Read;
        internal readonly ProviderModelDescriptor Model = new("ollama", true, new ModelDescriptor("controlled-local", 123, "controlled", "7B", "Q8", new HashSet<ToolCapability>{ToolCapability.Text}, DateTimeOffset.UnixEpoch));
        public string Id => "ollama"; public string DisplayName => "controlled"; public bool IsLocal { get { Metadata?.Invoke(); return true; } }
        public ModelProviderKind Kind => ModelProviderKind.Ollama; public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) { Reads++; return Read?.Invoke() ?? Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalTaskSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            OriginalFixtureFactory(() => GetModelsAsync(token), scope, retain);
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
