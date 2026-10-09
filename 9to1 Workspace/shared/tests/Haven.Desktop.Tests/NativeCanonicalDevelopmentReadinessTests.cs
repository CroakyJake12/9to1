using System.Reflection;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// SAME maintained actual Dev/coordinator fixture and actual Task/Dev hosts. Private
// active-state enrollment isolates identity/currentness only. No Window.Show, real
// startup, actor, authority, frame or installed native readiness is supplied.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [AvaloniaFact]
    public async Task Exact_acquired_Dev_child_follows_Dev_host_after_origin_hide_and_missing_startup_stays_unavailable()
    {
        var f = await Fixture.CreateAsync();
        using var windowLifetime = new CancellationTokenSource();
        var settings = new NoDevelopmentReadinessSettings();
        var actors = new NoDevelopmentReadinessActors();
        SpaceTaskWidgetPage? origin = null;
        DeveloperProjectWorkbenchPage? dev = null;
        ICuiSceneReadiness? acquired = null;
        NativeCanonicalTaskSceneReadiness? issuer = null;
        Task? devClose = null, originClose = null, childClose = null;
        Exception? primary = null;
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference, TestContext.Current.CancellationToken)).Value!;
            var source = new SpaceTaskWorkspaceService(new SpaceRegistry(settings), f.ConversationSource, f.Tasks);
            origin = new(source, f.Tasks, Guid.NewGuid(), f.Conversation.Id, new NoPresentationReadiness(),
                expectedOriginalTaskId: f.Current.TaskId, expectedOriginalExecutionId: f.Current.ExecutionId);
            issuer = await origin.AcquireOriginalNativeReadinessAsync(page =>
                NativeCanonicalTaskSceneReadiness.BindOriginal(page, null, actors, CancellationToken.None, windowLifetime.Token));
            DevelopmentTaskField("_active").SetValue(origin, true);
            var originalOriginGeneration = origin.OriginalReadinessGeneration;
            var expected = f.Current;
            dev = CreateReadinessChildPage(f, resolved, page => dev = page, page =>
            {
                Assert.Same(dev, page); // Actual partial captured before child acquisition/publication.
                acquired = issuer.BindOriginalDevelopment(page);
                return acquired;
            });
            Assert.NotNull(acquired);
            Assert.Equal((expected.TaskId, expected.ExecutionId, expected.ContextId), dev.OriginalReadinessContext);
            Assert.NotSame(origin.OriginalHost, dev.OriginalHost);
            Field("_active").SetValue(dev, true); // Component currentness enrollment, not native presentation.
            var originalDevGeneration = dev.OriginalReadinessGeneration;
            Assert.True(dev.IsOriginalReadinessPresentationCurrent(dev.OriginalHost, originalDevGeneration));
            origin.Deactivate();
            Assert.False(origin.IsOriginalReadinessPresentationCurrent(origin.OriginalHost, originalOriginGeneration));
            Assert.True(dev.IsOriginalReadinessPresentationCurrent(dev.OriginalHost, originalDevGeneration));
            var actualCheck = acquired!.CheckAsync(TestContext.Current.CancellationToken).AsTask(); // Convert SAME ValueTask once.
            var observation = await actualCheck.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(actualCheck.IsCompletedSuccessfully);
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, observation.State);
            Assert.Equal("HomeStartupAttachmentUnavailable", observation.Code);
            Assert.Equal(0, actors.Calls);
            Assert.Equal(0, settings.Calls);
            Assert.Same(expected, f.Current);
            var participant = Assert.IsAssignableFrom<IDesktopOriginalRetirementParticipant>(acquired);
            Assert.IsAssignableFrom<IDesktopOriginalRetirementJoinGuard>(acquired).DemandExternalOriginalRetirementJoin();
            participant.RequestRetirement(); childClose = participant.CloseAndDrainAsync();
            await childClose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Same(childClose, participant.CloseAndDrainAsync());
            Assert.True(childClose.IsCompletedSuccessfully);
            Assert.Throws<ObjectDisposedException>(() => { _ = acquired.CheckAsync(TestContext.Current.CancellationToken); });
            dev.Deactivate();
            Assert.False(dev.IsOriginalReadinessPresentationCurrent(dev.OriginalHost, originalDevGeneration));
            Assert.Same(expected, f.Current);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            var errors = new List<Exception>();
            if (acquired is IDesktopOriginalRetirementParticipant child)
                try { child.RequestRetirement(); childClose ??= child.CloseAndDrainAsync(); await childClose; }
                catch (Exception error) { errors.Add(childClose?.IsFaulted == true ? childClose.Exception! : error); }
            if (dev is not null)
                try { dev.RequestRetirement(); devClose ??= dev.CloseAndDrainAsync(); await devClose; }
                catch (Exception error) { errors.Add(devClose?.IsFaulted == true ? devClose.Exception! : error); }
            if (origin is not null)
                try { origin.RequestRetirement(); originClose ??= origin.CloseAndDrainAsync(); await originClose; }
                catch (Exception error) { errors.Add(originClose?.IsFaulted == true ? originClose.Exception! : error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) { if (primary is not null) errors.Insert(0, primary); throw new AggregateException("Independent actual Dev child/page/origin/global fixture drains failed.", errors); }
        }
    }

    [AvaloniaFact]
    public async Task Different_actual_Dev_Task_Run_context_and_retired_origin_refuse_before_any_borrowed_source_check()
    {
        var f = await Fixture.CreateAsync();
        using var windowLifetime = new CancellationTokenSource();
        var settings = new NoDevelopmentReadinessSettings();
        var actors = new NoDevelopmentReadinessActors();
        SpaceTaskWidgetPage? origin = null;
        DeveloperProjectWorkbenchPage? dev = null;
        Task? devClose = null, originClose = null;
        Exception? primary = null;
        try
        {
            var expected = f.Current;
            var resolved = (await f.Dev.ResolveAsync(f.Reference, TestContext.Current.CancellationToken)).Value!;
            var source = new SpaceTaskWorkspaceService(new SpaceRegistry(settings), f.ConversationSource, f.Tasks);
            origin = new(source, f.Tasks, Guid.NewGuid(), f.Conversation.Id, new NoPresentationReadiness(),
                expectedOriginalTaskId: expected.TaskId, expectedOriginalExecutionId: expected.ExecutionId);
            var issuer = await origin.AcquireOriginalNativeReadinessAsync(page =>
                NativeCanonicalTaskSceneReadiness.BindOriginal(page, null, actors, CancellationToken.None, windowLifetime.Token));
            DevelopmentTaskField("_active").SetValue(origin, true);
            dev = new(f.Dev, f.Tasks, resolved, Guid.NewGuid(), expected.ExecutionId, expected.ContextId,
                new NoPresentationReadiness(), (_, _, _) => throw new InvalidOperationException("No file selection requested."), () => { });
            Assert.NotEqual(expected.TaskId, dev.OriginalReadinessContext.TaskId);
            var mismatch = Assert.Throws<InvalidOperationException>(() => issuer.BindOriginalDevelopment(dev));
            Assert.Contains("different original Task/Run/context", mismatch.Message);
            Assert.Equal(0, actors.Calls); Assert.Equal(0, settings.Calls);
            origin.Deactivate();
            var withdrawn = Assert.Throws<InvalidOperationException>(() => issuer.BindOriginalDevelopment(dev));
            Assert.Contains("originating Task page", withdrawn.Message);
            Assert.Equal(0, actors.Calls); Assert.Equal(0, settings.Calls);
            Assert.Same(expected, f.Current);
            Assert.NotEqual(expected.TaskId, dev.OriginalReadinessContext.TaskId);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            var errors = new List<Exception>();
            if (dev is not null)
                try { dev.RequestRetirement(); devClose ??= dev.CloseAndDrainAsync(); await devClose; }
                catch (Exception error) { errors.Add(devClose?.IsFaulted == true ? devClose.Exception! : error); }
            if (origin is not null)
                try { origin.RequestRetirement(); originClose ??= origin.CloseAndDrainAsync(); await originClose; }
                catch (Exception error) { errors.Add(originClose?.IsFaulted == true ? originClose.Exception! : error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) { if (primary is not null) errors.Insert(0, primary); throw new AggregateException("Independent actual mismatch-page/origin/global fixture drains failed.", errors); }
        }
    }

    [AvaloniaTheory]
    [InlineData("historical")]
    [InlineData("fresh-authentication-race")]
    [InlineData("actor")]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("organisation")]
    public async Task Dev_readonly_child_preserves_historical_authentication_but_refuses_current_race_or_other_owner(string control)
    {
        var f = await Fixture.CreateAsync();
        using var windowLifetime = new CancellationTokenSource();
        using var connectionLifetime = new CancellationTokenSource();
        var settings = new DevelopmentSavedOwnerSettings();
        var spaces = new SpaceRegistry(settings);
        var space = await spaces.CreateAsync("Controlled existing Dev readiness Space", cancellationToken: TestContext.Current.CancellationToken);
        f.ConversationSource.Current = f.ConversationSource.Current with { SpaceId = space.Id };
        var expected = f.Current;
        var owner = expected.OwnerBinding!;
        var actor = new AuthenticatedResourceActor(owner.ActorId, owner.ProfileId, owner.AccountId,
            owner.OrganisationId, "controlled-current-process-authentication");
        actor = control switch
        {
            "actor" => actor with { ActorId = "different controlled actor" },
            "profile" => actor with { ProfileId = "different controlled profile" },
            "account" => actor with { AccountId = Guid.NewGuid() },
            "organisation" => actor with { OrganisationId = Guid.NewGuid() },
            _ => actor
        };
        var actors = new DevelopmentSavedOwnerActors(actor);
        var home = new DevelopmentSavedOwnerStartup();
        if (control != "fresh-authentication-race") home.Release();
        SpaceTaskWidgetPage? origin = null;
        DeveloperProjectWorkbenchPage? dev = null;
        ICuiSceneReadiness? child = null;
        Task<CuiSceneAvailability>? check = null;
        Exception? primary = null;
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference, TestContext.Current.CancellationToken)).Value!;
            var source = new SpaceTaskWorkspaceService(spaces, f.ConversationSource, f.Tasks);
            origin = new(source, f.Tasks, space.Id, expected.ContextId, new NoPresentationReadiness(),
                expectedOriginalTaskId: expected.TaskId, expectedOriginalExecutionId: expected.ExecutionId);
            var issuer = await origin.AcquireOriginalNativeReadinessAsync(page => NativeCanonicalTaskSceneReadiness.BindOriginal(
                page, home, actors, connectionLifetime.Token, windowLifetime.Token));
            DevelopmentTaskField("_active").SetValue(origin, true); // Component enrollment, no native Ready/frame claim.
            dev = CreateReadinessChildPage(f, resolved, page => dev = page, page => child = issuer.BindOriginalDevelopment(page));
            Field("_active").SetValue(dev, true);
            check = child!.CheckAsync(TestContext.Current.CancellationToken).AsTask(); // SAME ValueTask consumed once.
            if (control == "fresh-authentication-race")
            {
                await home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.False(check.IsCompleted);
                actors.Current = actors.Current with { AuthenticationRevision = "controlled-changed-current-authentication" };
                home.Release();
            }
            if (control == "historical")
            {
                var actual = await check.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.True(check.IsCompletedSuccessfully);
                Assert.Equal(CuiSceneAvailabilityState.Unavailable, actual.State);
                Assert.Equal("CONTROLLED_DEV_HOME_UNAVAILABLE", actual.Code);
                Assert.True(actors.Calls >= 3); Assert.Equal(1, home.Calls);
            }
            else
            {
                await Assert.ThrowsAnyAsync<Exception>(() => check.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.True(check.IsFaulted);
                Assert.True(ContainsType<UnauthorizedAccessException>(check.Exception!));
                Assert.Equal(control == "fresh-authentication-race" ? 1 : 0, home.Calls);
            }
            Assert.Same(expected, f.Current); Assert.Same(owner, f.Current.OwnerBinding);
            Assert.Equal("revision", f.Current.OwnerBinding!.AuthenticationRevision);
            Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, home.CloseCalls);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            home.Release();
            var errors = new List<Exception>();
            if (check is not null)
                try { await check; } catch (Exception error) { if (control == "historical" || primary is not null) errors.Add(check.Exception ?? error); }
            if (child is IDesktopOriginalRetirementParticipant participant)
            {
                participant.RequestRetirement();
                var actual = participant.CloseAndDrainAsync();
                try { await actual; }
                catch (Exception error)
                { if (check?.IsFaulted != true || control == "historical") errors.Add(actual.Exception ?? error); else Assert.True(ContainsType<UnauthorizedAccessException>(actual.Exception ?? error)); }
            }
            if (dev is not null)
                try { dev.RequestRetirement(); await dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (origin is not null)
                try { origin.RequestRetirement(); await origin.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) { if (primary is not null) errors.Insert(0, primary); throw new AggregateException("Actual Dev child and borrowed fixture joins failed.", errors); }
        }
    }

    private sealed class DevelopmentSavedOwnerActors(AuthenticatedResourceActor original) : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = original;
        internal int Calls;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class DevelopmentSavedOwnerStartup : IHomeNativeStartupSession
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HomeNativeStartupObservation> _actual = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls, CloseCalls;
        public Task<HomeNativeStartupObservation> CheckAsync(CancellationToken token)
        { Calls++; Entered.TrySetResult(); return _actual.Task; }
        internal void Release() => _actual.TrySetResult(new(HomeNativeStartupState.Unready,
            "CONTROLLED_DEV_HOME_UNAVAILABLE", "Controlled unavailable Home; no installed service or Ready/frame witness."));
        public Task CloseAndDrainAsync() { CloseCalls++; return Task.CompletedTask; }
    }
    private sealed class DevelopmentSavedOwnerSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { token.ThrowIfCancellationRequested(); return Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : null); }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { token.ThrowIfCancellationRequested(); _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token)
        { token.ThrowIfCancellationRequested(); _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }

    private static FieldInfo DevelopmentTaskField(string name) =>
        typeof(SpaceTaskWidgetPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private sealed class NoDevelopmentReadinessActors : IAuthenticatedResourceActorSource
    {
        internal int Calls;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { Calls++; throw new InvalidOperationException("A missing original startup must not probe the Task actor."); }
    }
    private sealed class NoDevelopmentReadinessSettings : IVersionedSettingsStore
    {
        internal int Calls;
        private InvalidOperationException Refuse() { Calls++; return new("No native context read is admitted in this missing-startup component."); }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => throw Refuse();
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => throw Refuse();
        public Task RemoveAsync(string key, CancellationToken token) => throw Refuse();
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw Refuse();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw Refuse();
    }
}
