using Haven.Application;
using HavenOS.Home.Core;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed class OriginalTaskActorObservationTests
{
    [Fact] public Task Actual_OS_Task_actor_stays_distinct_from_Home_profile_and_uses_exact_configured_source() => Run(async rig =>
    {
        var source = new HostLocalTaskActorSource(); var authority = rig.Authority(source);
        Assert.True(authority.HasOriginalTaskActorSource(source));
        Assert.False(authority.HasOriginalTaskActorSource(new HostLocalTaskActorSource()));
        var home = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(rig.Root!, "home.json")), new OperatingSystemPrincipalSource());
        var homeActor = await home.GetCurrentAsync(rig.Token); Assert.NotNull(homeActor);
        var actual = rig.Own(authority.ObserveOriginalTaskActorAsync(body => body(), rig.OwnRaw, rig.Token));
        var actor = await actual; Assert.NotNull(actor);
        Assert.NotEqual(homeActor, actor); Assert.StartsWith("host-local-task:", actor!.ActorId);
        Assert.StartsWith("host-local-os-profile:", actor.ProfileId);
        Assert.Equal(await source.GetCurrentAsync(rig.Token), actor); Assert.NotEmpty(rig.Originals);
    });

    [Fact] public Task Actual_parent_scope_refuses_authority_self_join_and_preserves_synchronous_OCE_as_fault() => Run(async rig =>
    {
        var source = new HostLocalTaskActorSource(); var authority = rig.Authority(source);
        var prior = ExecutionContext.Capture(); Assert.NotNull(prior);
        void Scope(Action body)
        {
            ExecutionContext.Run(prior!, state => Assert.Throws<InvalidOperationException>(() => { _ = authority.CloseAndDrainOwnerReauthenticationAsync(); }), null);
            body();
        }
        var healthy = rig.Own(authority.ObserveOriginalTaskActorAsync(Scope, rig.OwnRaw, rig.Token)); Assert.NotNull(await healthy);
        var cause = new OperationCanceledException("synthetic finite parent scope fault after actual OS observation"); rig.Expected.Add(cause);
        bool thrown = false;
        void FaultScope(Action body) { body(); if (!thrown && rig.Originals.Count != 0) { thrown = true; throw cause; } }
        var actual = rig.Own(authority.ObserveOriginalTaskActorAsync(FaultScope, rig.OwnRaw, rig.Token));
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(cause, Leaves(error)); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        Assert.True(thrown); Assert.All(rig.Originals, raw => Assert.True(raw.IsCompleted));
    });

    [Fact] public Task Unsupported_actor_refuses_before_any_unscoped_factory() => Run(rig =>
    {
        var source = new LegacyActor(); var authority = rig.Authority(source);
        Assert.True(authority.HasOriginalTaskActorSource(source));
        Assert.Throws<InvalidOperationException>(() => { _ = authority.ObserveOriginalTaskActorAsync(body => body(), rig.OwnRaw, rig.Token); });
        Assert.Equal(0, source.Reads); return Task.CompletedTask;
    });

    [Fact] public Task More_than_128_successful_actual_OS_observations_do_not_prune_an_original_fault() => Run(async rig =>
    {
        var source = new HostLocalTaskActorSource(); var authority = rig.Authority(source);
        var cause = new IOException("original finite identity observation fault retained across healthy reads"); rig.Expected.Add(cause);
        bool threw = false;
        void Scope(Action body) { body(); if (!threw) { threw = true; throw cause; } }
        var failed = rig.Own(authority.ObserveOriginalTaskActorAsync(Scope, rig.OwnRaw, rig.Token));
        var originalError = await Assert.ThrowsAsync<AggregateException>(() => failed);
        Assert.Contains(cause, Leaves(originalError)); Assert.True(failed.IsFaulted);
        for (var index = 0; index < 160; index++)
        {
            var actual = rig.Own(authority.ObserveOriginalTaskActorAsync(body => body(), rig.OwnRaw, rig.Token));
            Assert.NotNull(await actual); Assert.True(actual.IsCompletedSuccessfully);
        }
        var close = rig.Own(authority.CloseAndDrainOwnerReauthenticationAsync());
        var error = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(cause, Leaves(error)); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
    });

    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Run(Func<Rig, Task> body)
    {
        using var observationLifetime = new CancellationTokenSource();
        var rig = new Rig(observationLifetime.Token); Exception? primary = null; var failures = new List<Exception>();
        try { rig.Root = Directory.CreateTempSubdirectory("actual-task-actor-").FullName; await body(rig); }
        catch (Exception cause) { primary = cause; }
        // Acquire all actual owner closes independently before joining any observation.
        var closes = new List<Task>();
        foreach (var authority in rig.Authorities) try { closes.Add(authority.CloseAndDrainOwnerReauthenticationAsync()); } catch (Exception cause) { Add(cause); }
        foreach (var actual in rig.Originals.Distinct<Task>(ReferenceEqualityComparer.Instance).Concat(closes))
            try { await actual; } catch (Exception cause) { Add((Exception?)actual.Exception ?? cause); }
        try { rig.Configurations?.Dispose(); } catch (Exception cause) { Add(cause); }
        try { if (rig.Root is not null) Directory.Delete(rig.Root, true); } catch (Exception cause) { Add(cause); }
        if (primary is not null) failures.Insert(0, primary);
        if (failures.Count != 0) throw new AggregateException("Actual Task actor observation and independent cleanup failed.", failures);
        void Add(Exception cause) { foreach (var leaf in Leaves(cause)) if (!rig.Expected.Contains(leaf) && !failures.Any(value => ReferenceEquals(value, leaf))) failures.Add(leaf); }
    }
    private sealed class Rig(CancellationToken testToken)
    {
        internal string? Root; internal ProviderConfigurationStore? Configurations;
        internal CancellationToken Token => testToken;
        internal readonly List<TaskRunPermissionAuthority> Authorities = []; internal readonly List<Task> Originals = [];
        internal readonly HashSet<Exception> Expected = new(ReferenceEqualityComparer.Instance);
        internal T Own<T>(T raw) where T : Task { OwnRaw(raw); return raw; }
        internal void OwnRaw(Task raw) { if (!Originals.Any(value => ReferenceEquals(value, raw))) Originals.Add(raw); }
        internal TaskRunPermissionAuthority Authority(IAuthenticatedResourceActorSource actor)
        {
            var paths = new Paths(Root!); Configurations ??= new(paths);
            var authority = new TaskRunPermissionAuthority(actor, new ModelProviderRegistry([]), Configurations,
                new PrivacyPreferenceStore(paths), new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths))));
            Authorities.Add(authority); return authority;
        }
    }
    private sealed class LegacyActor : IAuthenticatedResourceActorSource
    { internal int Reads; public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { Reads++; return ValueTask.FromResult<AuthenticatedResourceActor?>(null); } }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "test.db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
