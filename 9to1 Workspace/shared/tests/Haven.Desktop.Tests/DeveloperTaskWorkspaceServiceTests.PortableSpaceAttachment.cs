using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Uses the complete maintained Dev fixture linked by the original Desktop.Tests project.
// Repositories/principal are controlled sources; no installed Home, OS permission or
// accepted model/effect/cold-continuation authority is certified by these controls.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [Fact]
    public async Task Portable_saved_project_reopen_preserves_actual_Task_Run_project_and_borrowed_domain()
    {
        if (!OperatingSystem.IsLinux()) return;
        Fixture? f = null; PortableControl? c = null; Exception? primary = null;
        try
        {
            f = await Fixture.CreateAsync(); c = new(f); await c.BuildAsync(TestContext.Current.CancellationToken);
            var token = TestContext.Current.CancellationToken;
            await c.StartAndBindSavedOwnerAsync(token);
            var original = f.Current;
            var attached = await c.First!.AttachOriginalAsync(c.Space!.Id, c.Space.Revision,
                f.Conversation.Id, original.TaskId, original.ExecutionId, f.Reference, token);
            var rawDomainStart = c.Domain!.OriginalStartTask;
            await c.First.CloseAndDrainAsync();
            c.Second = SpaceDevTaskAttachmentSession.CreateOriginal(c.Domain, c.Provider!);
            var reopened = await c.Second.OpenOriginalAsync(attached.View.Space.Id, attached.View.ContextReferenceId,
                original.TaskId, original.ExecutionId, token);
            Assert.Equal(attached.View.Link, reopened.View.Link);
            Assert.Equal(f.Reference, reopened.View.Project.Reference);
            Assert.Same(f.Store.Workspace, reopened.View.Project.Workspace);
            Assert.Equal(original.TaskId, reopened.View.Task.Snapshot!.TaskId);
            Assert.Equal(original.ExecutionId, reopened.View.Task.Snapshot.ExecutionId);
            Assert.Same(original, f.Repository.Current);
            Assert.Equal("historical-observation-only", reopened.View.Task.Snapshot.OwnerBinding!.AuthenticationRevision);
            Assert.Equal(0, f.Store.CreateCalls);
            Assert.Same(rawDomainStart, c.Domain.OriginalStartTask);
            Assert.Null(c.Domain.OriginalCloseTask);
            c.Domain.DemandOriginalStarted();
            Assert.True(c.Second.HasOriginalComposition(c.Domain, c.Spaces!, f.ConversationSource, f.Tasks, f.Dev));
        }
        catch (Exception cause) { primary = cause; }
        finally { await ClosePortableControlAsync(f, c, primary); }
    }

    [Fact]
    public async Task Portable_view_close_waits_held_actual_Home_start_without_requesting_borrowed_Home_retirement()
    {
        if (!OperatingSystem.IsLinux()) return;
        Fixture? f = null; PortableControl? c = null; Exception? primary = null;
        Task? startup = null, close = null;
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        try
        {
            f = await Fixture.CreateAsync(); c = new(f);
            c.Principals.First = release.Task; c.Principals.Entered = entered;
            await c.BuildAsync(TestContext.Current.CancellationToken); startup = c.First!.StartOriginalAsync(TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var raw = c.Domain!.OriginalStartTask;
            Assert.NotNull(raw); Assert.False(raw!.IsCompleted);
            c.First.RequestOriginalObservationRetirement(); close = c.First.CloseAndDrainAsync();
            Assert.False(close.IsCompleted); Assert.False(startup.IsCompleted);
            Assert.Null(c.Domain.OriginalCloseTask);
            release.TrySetResult(PortablePrincipals.Value);
            var startupFailure = await ObservePortableFailureAsync(startup, expected, retainActualInitialOutcome: true);
            var closeFailure = await ObservePortableFailureAsync(close, expected);
            Assert.NotNull(startupFailure); Assert.NotNull(closeFailure);
            Assert.Contains(PortableCauseGraph(startupFailure!), cause => cause is OperationCanceledException);
            Assert.True(raw.IsCompletedSuccessfully);
            Assert.Same(raw, c.Domain.OriginalStartTask);
            Assert.Null(c.Domain.OriginalCloseTask);
            c.Domain.DemandOriginalStarted();
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            release.TrySetResult(PortablePrincipals.Value);
            await ClosePortableControlAsync(f, c, primary, expected, startup, close);
        }
    }

    [Fact]
    public async Task Portable_nested_repository_factory_after_await_refuses_restored_context_self_join()
    {
        if (!OperatingSystem.IsLinux()) return;
        Fixture? f = null; PortableControl? c = null; Exception? primary = null; Task? opened = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeSession = ExecutionContext.Capture()!;
        var refusals = 0;
        try
        {
            f = await Fixture.CreateAsync(); c = new(f); await c.BuildAsync(TestContext.Current.CancellationToken);
            var token = TestContext.Current.CancellationToken;
            await c.StartAndBindSavedOwnerAsync(token);
            var attached = await c.First!.AttachOriginalAsync(c.Space!.Id, c.Space.Revision,
                f.Conversation.Id, f.Current.TaskId, f.Current.ExecutionId, f.Reference, token);
            var reads = 0;
            c.Settings.BeforeRead = () =>
            {
                if (Interlocked.Increment(ref reads) == 1) { entered.TrySetResult(); return release.Task; }
                return Task.CompletedTask;
            };
            f.ConversationSource.BeforeRead = () =>
            {
                ExecutionContext.Run(beforeSession.CreateCopy(), _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => { _ = c.First.CloseAndDrainAsync(); });
                    Interlocked.Increment(ref refusals);
                }, null);
                return Task.CompletedTask;
            };
            var original = f.Current;
            var actual = c.First.OpenOriginalAsync(attached.View.Space.Id, attached.View.ContextReferenceId,
                original.TaskId, original.ExecutionId, token); opened = actual;
            await entered.Task.WaitAsync(token);
            Assert.False(actual.IsCompleted);
            release.TrySetResult();
            var result = await actual.WaitAsync(token);
            Assert.True(refusals > 0);
            Assert.Equal(original.TaskId, result.View.Link.TaskId);
            Assert.Equal(original.ExecutionId, result.View.Link.ExecutionId);
            Assert.Same(original, f.Repository.Current);
            Assert.Null(c.Domain!.OriginalCloseTask);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            release.TrySetResult();
            if (f is not null) f.ConversationSource.BeforeRead = null;
            if (c is not null) c.Settings.BeforeRead = null;
            await ClosePortableControlAsync(f, c, primary, operations: [opened]);
        }
    }

    [Fact]
    public async Task Portable_nested_original_faulted_OCE_and_sibling_remain_full_faults_and_unhealthy_close()
    {
        if (!OperatingSystem.IsLinux()) return;
        Fixture? f = null; PortableControl? c = null; Exception? primary = null;
        Task? opened = null, close = null;
        var oce = new OperationCanceledException("controlled original fault, not a canceled task");
        var io = new IOException("controlled original sibling");
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { oce, io };
        var raw = new TaskCompletionSource<DeveloperOperationResult<DeveloperWorkspace>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            f = await Fixture.CreateAsync(); c = new(f); await c.BuildAsync(TestContext.Current.CancellationToken);
            var token = TestContext.Current.CancellationToken;
            await c.StartAndBindSavedOwnerAsync(token);
            var attached = await c.First!.AttachOriginalAsync(c.Space!.Id, c.Space.Revision,
                f.Conversation.Id, f.Current.TaskId, f.Current.ExecutionId, f.Reference, token);
            f.Store.ActualRead = raw.Task; raw.SetException([oce, io]);
            opened = c.First.OpenOriginalAsync(attached.View.Space.Id, attached.View.ContextReferenceId,
                f.Current.TaskId, f.Current.ExecutionId, token);
            var originalFailure = await ObservePortableFailureAsync(opened, expected);
            Assert.NotNull(originalFailure); Assert.True(opened.IsFaulted); Assert.False(opened.IsCanceled);
            Assert.Contains(PortableCauseGraph(opened.Exception!), cause => ReferenceEquals(cause, oce));
            Assert.Contains(PortableCauseGraph(opened.Exception!), cause => ReferenceEquals(cause, io));
            close = c.First.CloseAndDrainAsync();
            var closeFailure = await ObservePortableFailureAsync(close, expected);
            Assert.NotNull(closeFailure); Assert.True(close.IsFaulted);
            Assert.Contains(PortableCauseGraph(close.Exception!), cause => ReferenceEquals(cause, oce));
            Assert.Contains(PortableCauseGraph(close.Exception!), cause => ReferenceEquals(cause, io));
            Assert.True(raw.Task.IsFaulted); Assert.Null(c.Domain!.OriginalCloseTask);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            raw.TrySetException([oce, io]);
            await ClosePortableControlAsync(f, c, primary, expected, opened, close, raw.Task);
        }
    }

    [Fact]
    public async Task Portable_saved_Task_owner_cannot_be_replaced_by_the_distinct_Home_resource_profile()
    {
        if (!OperatingSystem.IsLinux()) return;
        Fixture? f = null; PortableControl? c = null; Exception? primary = null; Task? opened = null;
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        try
        {
            f = await Fixture.CreateAsync(); c = new(f); await c.BuildAsync(TestContext.Current.CancellationToken);
            var token = TestContext.Current.CancellationToken;
            await c.StartAndBindSavedOwnerAsync(token);
            var original = f.Current;
            var actualActor = await c.TaskActors!.GetCurrentAsync(token);
            var homeActor = c.Domain!.GetOriginalStartedActor();
            Assert.NotNull(actualActor);
            Assert.NotEqual(homeActor.ActorId, actualActor!.ActorId);
            Assert.NotEqual(homeActor.ProfileId, actualActor.ProfileId);
            var attached = await c.First!.AttachOriginalAsync(c.Space!.Id, c.Space.Revision,
                f.Conversation.Id, original.TaskId, original.ExecutionId, f.Reference, token);
            f.Repository.Current = original with { OwnerBinding = original.OwnerBinding! with
                { ActorId = homeActor.ActorId, ProfileId = homeActor.ProfileId,
                  AccountId = homeActor.AccountId, OrganisationId = homeActor.OrganisationId } };
            opened = c.First.OpenOriginalAsync(attached.View.Space.Id, attached.View.ContextReferenceId,
                original.TaskId, original.ExecutionId, token);
            var refusal = await ObservePortableFailureAsync(opened, expected, retainActualInitialOutcome: true);
            Assert.NotNull(refusal);
            Assert.Contains(PortableCauseGraph(refusal!), cause => cause is UnauthorizedAccessException);
            Assert.Equal(original.TaskId, f.Current.TaskId);
            Assert.Equal(original.ExecutionId, f.Current.ExecutionId);
            Assert.Equal(0, f.Store.CreateCalls);
            Assert.Null(c.Domain.OriginalCloseTask);
        }
        catch (Exception cause) { primary = cause; }
        finally { await ClosePortableControlAsync(f, c, primary, expected, opened); }
    }

    [Fact]
    public async Task Portable_configured_Task_actor_source_requires_the_same_authority_reference()
    {
        if (!OperatingSystem.IsLinux()) return;
        Fixture? f = null; PortableControl? c = null; Exception? primary = null;
        try
        {
            f = await Fixture.CreateAsync(); c = new(f); await c.BuildAsync(TestContext.Current.CancellationToken);
            var original = f.Current;
            var foreignSource = new HostLocalTaskActorSource();
            Assert.True(c.Authority!.HasOriginalTaskActorSource(c.TaskActors!));
            Assert.False(c.Authority.HasOriginalTaskActorSource(foreignSource));
            Assert.Throws<InvalidOperationException>(() => SpaceDevTaskAttachmentSession.CreateOriginal(
                c.Domain!, c.Provider!.WithTaskSource(foreignSource)));
            Assert.Same(original, f.Repository.Current);
            Assert.Null(c.Domain!.OriginalStartTask);
            Assert.Null(c.Domain.OriginalCloseTask);
            Assert.Equal(0, f.Store.CreateCalls);
        }
        catch (Exception cause) { primary = cause; }
        finally { await ClosePortableControlAsync(f, c, primary); }
    }

    private sealed class PortableControl(Fixture f)
    {
        internal readonly PortableSettings Settings = new();
        internal readonly PortablePrincipals Principals = new();
        internal string? Root;
        internal HomeLocalDomainComposition? Domain;
        internal SpaceRegistry? Spaces;
        internal SpaceDefinition? Space;
        internal PortableProvider? Provider;
        internal TaskRunPermissionAuthority? Authority;
        internal ProviderConfigurationStore? Providers;
        internal HostLocalTaskActorSource? TaskActors;
        internal readonly List<Task> SetupOriginals = [];
        internal SpaceDevTaskAttachmentSession? First, Second;
        internal async Task BuildAsync(CancellationToken token)
        {
            Root = Directory.CreateTempSubdirectory("haven-portable-domain-control-").FullName;
            Domain = new(new FileHomeCoreStateStore(Path.Combine(Root, "home.json")), Principals);
            // Retire the original synthetic fixture owners before initial test composition.
            // The tested attach/reopen subsequently keeps this genuinely admitted Task/Run.
            var oldCauses = new List<Exception>();
            try { f.Dev.RequestRetirement(); } catch (Exception cause) { oldCauses.Add(cause); }
            try { f.Tasks.RequestOriginalProcessRetirement(); } catch (Exception cause) { oldCauses.Add(cause); }
            var oldCloses = new List<Task>();
            try { oldCloses.Add(f.Dev.CloseAndDrainAsync()); } catch (Exception cause) { oldCauses.Add(cause); }
            try { oldCloses.Add(f.Tasks.CloseAndSuspendOriginalProducersAsync()); } catch (Exception cause) { oldCauses.Add(cause); }
            SetupOriginals.AddRange(oldCloses);
            foreach (var actual in oldCloses)
                try { await actual.ConfigureAwait(false); }
                catch (Exception cause) { oldCauses.Add(actual.Exception ?? cause); }
            if (oldCauses.Count != 0) throw new AggregateException("Original fixture owner retirement failed.", oldCauses);
            TaskActors = new();
            var paths = new PortablePaths(Path.Combine(Root, "task-state"));
            Providers = new ProviderConfigurationStore(paths);
            Authority = new(TaskActors, new ModelProviderRegistry(Array.Empty<IModelProvider>()),
                Providers, new PrivacyPreferenceStore(paths),
                new ModelPermissionEvaluator(new VersionedModelPermissionStore(Settings)));
            f.Repository = new();
            f.Tasks = new(f.Repository, new Events(), admissionAuthority: Authority, toolActionOwner: f.Owner);
            Task<TaskExecutionSnapshot> mint;
            try
            {
                mint = f.Tasks.BeginAuthorizedAsync(f.Conversation.Id, Guid.NewGuid(), "controlled initial Task setup",
                    TaskExecutionDurability.PersistedPlan, [], token);
                SetupOriginals.Add(mint);
            }
            catch (OperationCanceledException cause) { throw new AggregateException("Initial setup source acquisition failed.", cause); }
            try { await mint.ConfigureAwait(false); }
            catch (Exception) when (mint.IsFaulted) { throw mint.Exception!; }
            f.Dev = new(f.Store, new(f.ConversationSource, f.Containers), f.Tasks, f.Owner, new(f.Tools), new Trust(true));
            Spaces = new(Settings);
            var entries = new Dictionary<Type, object>
            {
                [typeof(HomeLocalDomainComposition)] = Domain,
                [typeof(FileHomeCoreStateStore)] = Domain.StateStore,
                [typeof(IHomeCoreStateStore)] = Domain.StateStore,
                [typeof(HomeLocalProfileIdentity)] = Domain.Profiles,
                [typeof(IAuthenticatedResourceActorSource)] = Domain.Profiles,
                [typeof(HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService)] = Domain.Permissions,
                [typeof(ResourceAuthorizationService)] = Domain.Resources,
                [typeof(HomeLocalStoreOwnership)] = Domain.LocalStoreOwnership,
                [typeof(HomeResourceStoreOwnershipAuthority)] = Domain.Ownership,
                [typeof(IResourceStoreOwnershipAuthority)] = Domain.Ownership,
                [typeof(IResourceStoreOwnershipReceiptAuthority)] = Domain.Ownership,
                [typeof(HomeResourceOperationBroker)] = Domain.Broker,
                [typeof(SpaceRegistry)] = Spaces,
                [typeof(IConversationRepository)] = f.ConversationSource,
                [typeof(TaskExecutionCoordinator)] = f.Tasks,
                [typeof(TaskRunPermissionAuthority)] = Authority,
                [typeof(ITaskRunAdmissionAuthority)] = Authority,
                [typeof(HostLocalTaskActorSource)] = TaskActors,
                [typeof(DeveloperTaskWorkspaceService)] = f.Dev,
            };
            Provider = new(entries);
            First = SpaceDevTaskAttachmentSession.CreateOriginal(Domain, Provider);
        }
        internal async Task StartAndBindSavedOwnerAsync(CancellationToken token)
        {
            await First!.StartOriginalAsync(token);
            Domain!.DemandOriginalStarted();
            Space = await Spaces!.CreateAsync("controlled saved Task project", cancellationToken: token);
            f.Conversation = f.Conversation with { SpaceId = Space.Id };
            f.ConversationSource.Current = f.Conversation;
            var saved = f.Current;
            // Preserve the real Task actor/Profile tuple. Only historical authentication
            // metadata is controlled here; saved observation never renews command authority.
            f.Repository.Current = saved with { OwnerBinding = saved.OwnerBinding! with
                { AuthenticationRevision = "historical-observation-only" } };
        }
    }
    private sealed class PortableProvider(IReadOnlyDictionary<Type, object> entries) : IServiceProvider
    {
        public object? GetService(Type serviceType) => entries.GetValueOrDefault(serviceType);
        internal PortableProvider WithTaskSource(HostLocalTaskActorSource foreignSource) =>
            new(entries.ToDictionary(pair => pair.Key, pair => pair.Key == typeof(HostLocalTaskActorSource) ? (object)foreignSource : pair.Value));
    }
    private sealed class PortablePaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    private sealed class PortablePrincipals : ITrustedHostPrincipalSource
    {
        internal const string Value = "controlled-local-principal-not-installed-authority";
        internal Task<string?>? First;
        internal TaskCompletionSource? Entered;
        private int _reads;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _reads) == 1 && First is { } actual)
            { Entered?.TrySetResult(); return new(actual); }
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(Value);
        }
    }
    private sealed class PortableSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _saved = [];
        internal Func<Task>? BeforeRead;
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { if (BeforeRead is { } callback) await callback(); token.ThrowIfCancellationRequested(); return _saved.GetValueOrDefault(key) as T; }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { token.ThrowIfCancellationRequested(); _saved[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token)
        { token.ThrowIfCancellationRequested(); _saved.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
    private static IEnumerable<Exception> PortableCauseGraph(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException group)
            foreach (var child in group.InnerExceptions)
                foreach (var original in PortableCauseGraph(child)) yield return original;
    }
    private static async Task<Exception?> ObservePortableFailureAsync(Task actual, HashSet<Exception> expected,
        bool retainActualInitialOutcome = false)
    {
        try { await actual.WaitAsync(TestContext.Current.CancellationToken); return null; }
        catch (Exception caught)
        {
            if (!actual.IsCompleted) throw;
            var failure = actual.Exception ?? caught;
            bool Expected(Exception cause) => expected.Contains(cause) ||
                cause is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(Expected);
            if (retainActualInitialOutcome)
                foreach (var cause in PortableCauseGraph(failure)) expected.Add(cause);
            else if (!Expected(failure))
                throw new AggregateException("Unexpected original or cleanup cause remains retained.", failure);
            return failure;
        }
    }
    private static async Task ClosePortableControlAsync(Fixture? f, PortableControl? c, Exception? primary,
        HashSet<Exception>? expected = null, params Task?[] operations)
    {
        var failures = new List<Exception>(); if (primary is not null) failures.Add(primary);
        bool Expected(Exception cause) => expected?.Contains(cause) == true ||
            cause is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(Expected);
        var joins = new List<Task>();
        void Acquire(Func<Task> factory)
        { try { joins.Add(factory()); } catch (Exception cause) { if (!Expected(cause)) failures.Add(cause); } }
        // Acquire every owned observer close before awaiting admitted operations.
        if (c?.First is { } first) Acquire(first.CloseAndDrainAsync);
        if (c?.Second is { } second) Acquire(second.CloseAndDrainAsync);
        foreach (var actual in operations.Where(task => task is not null).Cast<Task>().Concat(c?.SetupOriginals ?? []).Concat(joins).Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await actual.ConfigureAwait(false); }
            catch (Exception caught) { var cause = actual.Exception ?? caught; if (!Expected(cause)) failures.Add(cause); }
        joins.Clear();
        // The test is the global process owner. Production session never requests these.
        if (f is not null)
        {
            try { c?.Authority?.RequestOriginalAdmissionSeal(); } catch (Exception cause) { failures.Add(cause); }
            try { f.Dev.RequestRetirement(); } catch (Exception cause) { failures.Add(cause); }
            try { f.Tasks.RequestOriginalProcessRetirement(); } catch (Exception cause) { failures.Add(cause); }
            Acquire(f.Dev.CloseAndDrainAsync); Acquire(f.Tasks.CloseAndSuspendOriginalProducersAsync);
        }
        foreach (var actual in joins)
            try { await actual.ConfigureAwait(false); }
            catch (Exception caught) { var cause = actual.Exception ?? caught; if (!Expected(cause)) failures.Add(cause); }
        if (c?.Authority is { } authority)
        {
            try { await authority.CloseAndDrainOwnerReauthenticationAsync().ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(cause); }
        }
        if (c?.Domain is { } domain)
        {
            try { domain.RequestOriginalProcessRetirement(); }
            catch (Exception cause) { failures.Add(cause); }
            try { await domain.CloseAndDrainAsync().ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(cause); }
        }
        if (c?.Providers is { } providers)
            try { providers.Dispose(); }
            catch (Exception cause) { failures.Add(cause); }
        if (c?.Root is { } root)
            try { Directory.Delete(root, true); }
            catch (Exception cause) { failures.Add(cause); }
        if (failures.Count != 0) throw new AggregateException("Portable original control and independent cleanup failed.", failures);
    }
}
