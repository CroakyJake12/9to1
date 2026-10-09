using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Device-owned READ source/custody controls. Scripted endpoints here never prove
// publisher enrollment, an installed peer or a positive protected Windows activation.
public sealed class HomePackageOriginalReadActivationTests
{
    [Fact]
    public async Task Missing_readonly_endpoint_settles_unavailable_without_mutation_or_artifact_calls_and_without_a_permanent_healthy_capacity_limit()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var artifacts = new MissingArtifacts(); var endpoint = new MissingMutation();
        var fixture = Directory.CreateTempSubdirectory("home-read-activation-control-").FullName;
        var store = new FileHomeCoreStateStore(Path.Combine(fixture, "home.json")); var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(lifetime.Token) ?? throw new InvalidOperationException("Actual OS Home actor missing.");
        var owner = new HomePackageOriginalDeviceOwner(store, artifacts, endpoint, lifetime.Token);
        var raw = new List<Task>(); var errors = new List<Exception>();
        try
        {
            for (var i = 0; i != 140; i++)
            {
                var original = owner.OpenOriginalReadActivationWithinSourceAsync(actor, "observed-os-selection", "spaces",
                    body => body(), raw.Add, lifetime.Token);
                Assert.Null(await original); Assert.True(original.IsCompletedSuccessfully);
            }
            Assert.Equal(0, endpoint.Calls); Assert.Equal(0, artifacts.Calls);
        }
        catch (Exception cause) { errors.Add(cause); }
        Task? close = null;
        try { close = owner.CloseAndDrainAsync(); await close; Assert.Same(close, owner.CloseAndDrainAsync()); }
        catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
        foreach (var original in raw) try { await original; } catch (Exception cause) { errors.Add(original.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual missing-channel source control failed; fixture retained at " + fixture, errors);
        Directory.Delete(fixture, true); // Exclusive healthy fixture only, after all SAME originals join.
    }

    [Fact]
    public async Task Accepted_raw_open_is_joined_and_its_same_late_channel_closed_once_even_when_the_borrowed_scope_then_faults()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var artifacts = new MissingArtifacts(); var endpoint = new HeldReadEndpoint();
        var fixture = Directory.CreateTempSubdirectory("home-read-activation-control-").FullName;
        var store = new FileHomeCoreStateStore(Path.Combine(fixture, "home.json")); var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(lifetime.Token) ?? throw new InvalidOperationException("Actual OS Home actor missing.");
        var owner = new HomePackageOriginalDeviceOwner(store, artifacts, endpoint, lifetime.Token);
        var borrowedFailure = new IOException("Actual post-open scope failure"); var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new List<Task>(); Task<HomePackageOriginalReadActivation?>? original = null; Task? close = null;
        var expected = new HashSet<Task>(ReferenceEqualityComparer.Instance); var errors = new List<Exception>();
        void Retain(Task actual) { lock (raw) raw.Add(actual); if (ReferenceEquals(actual, endpoint.Open.Task)) acquired.TrySetResult(); }
        void Scope(Action body) { body(); if (acquired.Task.IsCompleted) throw borrowedFailure; }
        try
        {
            original = owner.OpenOriginalReadActivationWithinSourceAsync(actor, "observed-os-selection", "spaces", Scope, Retain, lifetime.Token);
            var first = await Task.WhenAny(acquired.Task, original).WaitAsync(lifetime.Token);
            if (ReferenceEquals(first, original)) await original;
            await acquired.Task.WaitAsync(lifetime.Token); Assert.False(original.IsCompleted);
            close = owner.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
            endpoint.Release();
            var failure = await Assert.ThrowsAsync<AggregateException>(async () => await original);
            Assert.True(ContainsReference(failure, borrowedFailure)); expected.Add(original);
            var closeFailure = await Assert.ThrowsAsync<AggregateException>(async () => await close);
            Assert.True(ContainsReference(closeFailure, borrowedFailure)); expected.Add(close);
            Assert.Same(endpoint.Channel, endpoint.Open.Task.GetAwaiter().GetResult()); Assert.Equal(1, endpoint.Channel!.DisposeCalls);
            Assert.Equal(0, artifacts.Calls); Assert.True(endpoint.Channel.OriginalDispose.Task.IsCompletedSuccessfully);
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            endpoint.Release();
            if (original is not null) await Join(original);
            try { close ??= owner.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (close is not null) await Join(close);
            Task[] actuals; lock (raw) actuals = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var actual in actuals) await Join(actual);
            if (endpoint.Channel is not null) await Join(endpoint.Channel.OriginalDispose.Task);
        }
        if (errors.Count != 0) throw new AggregateException("Actual held channel/body/close control failed; fixture retained at " + fixture, errors);
        // Expected source failure is retained with its actual fixture; no reclamation.
        async Task Join(Task actual)
        {
            try { await actual; }
            catch (Exception cause) { if (!expected.Contains(actual) || !ContainsReference(actual.Exception ?? cause, borrowedFailure)) errors.Add(actual.Exception ?? cause); }
        }
    }

    [Fact]
    public async Task Actual_endpoint_callback_cannot_join_device_owner_when_it_restores_an_earlier_execution_context()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var artifacts = new MissingArtifacts(); var endpoint = new MissingReadEndpoint();
        var fixture = Directory.CreateTempSubdirectory("home-read-activation-control-").FullName;
        var store = new FileHomeCoreStateStore(Path.Combine(fixture, "home.json")); var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(lifetime.Token) ?? throw new InvalidOperationException("Actual OS Home actor missing.");
        var owner = new HomePackageOriginalDeviceOwner(store, artifacts, endpoint, lifetime.Token);
        var clean = ExecutionContext.Capture()!; var called = 0;
        endpoint.Callback = () => ExecutionContext.Run(clean.CreateCopy(), _ =>
        { Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); }); called++; }, null);
        Task<HomePackageOriginalReadActivation?>? original = null; Task? close = null; var errors = new List<Exception>(); var raw = new List<Task>();
        try
        {
            original = owner.OpenOriginalReadActivationWithinSourceAsync(actor, "observed-os-selection", "spaces", body => body(), raw.Add, lifetime.Token);
            Assert.Null(await original); Assert.Equal(1, called); Assert.Equal(0, artifacts.Calls);
        }
        catch (Exception cause) { errors.Add(cause); }
        if (original is not null) try { await original; } catch (Exception cause) { errors.Add(original.Exception ?? cause); }
        try { close = owner.CloseAndDrainAsync(); await close; } catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
        foreach (var actual in raw) try { await actual; } catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual restored-context endpoint control failed; fixture retained at " + fixture, errors);
        Directory.Delete(fixture, true); // Exclusive healthy fixture only, after all SAME originals join.
    }

    private static bool ContainsReference(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(value => ContainsReference(value, expected)); // Assertion only; no source authority waiver.
    private class MissingMutation : IHomePackageOriginalRootMutationPort
    {
        internal int Calls;
        public Task<IHomePackageOriginalRootMutation> OpenOriginalAsync(HomePackageOriginalRootRequest request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No mutation is allowed by these READ controls."); }
    }
    private sealed class MissingReadEndpoint : MissingMutation, IHomePackageOriginalRootActivationPort
    {
        internal Action? Callback;
        public Task<IHomePackageOriginalRootActivation?> OpenOriginalActivationAsync(HomePackageOriginalReadActivationRequest request, CancellationToken token)
        { Callback?.Invoke(); return Task.FromResult<IHomePackageOriginalRootActivation?>(null); }
    }
    private sealed class HeldReadEndpoint : MissingMutation, IHomePackageOriginalRootActivationPort
    {
        internal readonly TaskCompletionSource<IHomePackageOriginalRootActivation?> Open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal PartialChannel? Channel;
        public Task<IHomePackageOriginalRootActivation?> OpenOriginalActivationAsync(HomePackageOriginalReadActivationRequest request, CancellationToken token)
        { Channel = new(request); return Open.Task; }
        internal void Release() { if (Channel is not null) Open.TrySetResult(Channel); }
    }
    private sealed class PartialChannel(HomePackageOriginalReadActivationRequest request) : IHomePackageOriginalRootActivation
    {
        public HomePackageOriginalReadActivationRequest OriginalRequest => request;
        public HomePackageOriginalArtifactObservation OriginalArtifact => throw new InvalidOperationException("Postscope failure must prevent artifact admission.");
        public HomeNativeInstalledPeer OriginalInstalledHomeCaller => throw new InvalidOperationException("No installed peer supplied.");
        public string OriginalHomeSessionId => throw new InvalidOperationException("No Home session supplied.");
        public string OriginalActivationOperationId => throw new InvalidOperationException("No activation supplied.");
        public string OriginalPackageId => throw new InvalidOperationException("No package supplied.");
        public Task DemandOriginalChannelCurrentAsync(CancellationToken token) => throw new InvalidOperationException("No accepted channel admission.");
        internal int DisposeCalls; internal readonly TaskCompletionSource OriginalDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask DisposeAsync() { DisposeCalls++; OriginalDispose.TrySetResult(); return new(OriginalDispose.Task); }
    }
    private sealed class MissingArtifacts : IHomePackageOriginalArtifactProvider
    {
        internal int Calls;
        public ValueTask<HomePackageOriginalArtifactObservation?> ResolveAsync(HomePackageActionRequest request, HomePackageDatabaseSnapshot registry, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No artifact admission expected."); }
        public HomePackageOriginalActionMap? ResolveAction(HomePackageAction action, HomePackageArtifactDescriptor descriptor)
        { Calls++; throw new InvalidOperationException("No action admission expected."); }
        public Task DemandOriginalCurrentAsync(HomePackageOriginalArtifactObservation artifact, AuthenticatedResourceActor actor,
            HomeNativeInstalledPeer caller, string session, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No enrolled publisher supplied."); }
    }
}
