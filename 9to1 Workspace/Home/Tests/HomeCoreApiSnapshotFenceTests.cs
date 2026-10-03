using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Controlled authorization/actor awaits exercise the real Home runtime and API; no platform grant is proved.</summary>
public sealed class HomeCoreApiSnapshotFenceTests
{
    private static readonly HomeCallerIdentity Caller = new("controlled-app", "controlled-origin", "controlled-test-input");

    [Theory]
    [InlineData("state")]
    [InlineData("services")]
    [InlineData("service")]
    public async Task Original_snapshot_change_during_authorization_returns_conflict_without_data(string shape)
    {
        await using var runtime = new HomeCoreRuntime();
        var authorization = new PendingAuthorization();
        var api = new HomeCoreApi(runtime, authorization);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var original = runtime.Current;
        var pending = Read(api, shape, deadline.Token);
        try
        {
            await authorization.Entered.Task.WaitAsync(deadline.Token);
            runtime.ReportServiceState("home.core", HomeServiceLifecycleState.Ready, true, "changed while pending");
            Assert.True(runtime.Current.Revision > original.Revision);
        }
        finally { authorization.Decision.TrySetResult(true); }
        var result = await pending.WaitAsync(deadline.Token);
        Assert.False(result.Succeeded); Assert.Equal("HomeStateConflict", result.Code);
        Assert.True(result.Recoverable); Assert.False(result.HasValue);
        Assert.Equal(runtime.Current.Revision, result.Revision);
        Assert.Equal("9to1.Home." + (shape == "state" ? "GetState" : shape == "services" ? "GetServices" : "GetService"), authorization.Target);
        Assert.Equal("home.services.read", Assert.Single(authorization.Scopes!));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("services")]
    [InlineData("service")]
    public async Task Unchanged_authorized_snapshot_returns_one_revision_and_original_service_value(string shape)
    {
        await using var runtime = new HomeCoreRuntime();
        var authorization = new PendingAuthorization();
        var api = new HomeCoreApi(runtime, authorization);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var original = runtime.Current;
        var expected = Assert.Single(original.Services, service => service.ServiceId == "home.core");
        var pending = Read(api, shape, deadline.Token);
        await authorization.Entered.Task.WaitAsync(deadline.Token);
        authorization.Decision.SetResult(true);
        var result = await pending.WaitAsync(deadline.Token);
        Assert.True(result.Succeeded); Assert.True(result.HasValue);
        Assert.Equal(original.Revision, result.Revision);
        Assert.Equal(expected, result.CoreService);
        if (shape == "state") Assert.Equal(result.Revision, result.SnapshotRevision);
        runtime.ReportServiceState("home.core", HomeServiceLifecycleState.Ready, true);
        Assert.Equal(expected, result.CoreService); // Already returned data is an immutable original snapshot.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_original_profile_change_or_loss_denies_after_authorization(bool lost)
    {
        await using var runtime = new HomeCoreRuntime();
        var actors = new Actors();
        var authorization = new PendingAuthorization();
        var api = new HomeCoreApi(runtime, authorization, actors);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = api.GetStateAsync(Caller, deadline.Token);
        await authorization.Entered.Task.WaitAsync(deadline.Token);
        actors.Current = lost ? null : actors.Current! with { AuthenticationRevision = "changed" };
        authorization.Decision.SetResult(true);
        var result = await pending.WaitAsync(deadline.Token);
        Assert.False(result.Succeeded); Assert.Equal("PermissionDenied", result.Code); Assert.Null(result.Value);
        Assert.Equal(2, actors.Reads);
    }

    [Fact]
    public async Task Missing_current_profile_never_calls_authorization_or_returns_state()
    {
        await using var runtime = new HomeCoreRuntime();
        var actors = new Actors { Current = null };
        var authorization = new PendingAuthorization();
        var result = await new HomeCoreApi(runtime, authorization, actors).GetStateAsync(Caller);
        Assert.False(result.Succeeded); Assert.Equal("CallerIdentityUnverified", result.Code); Assert.Null(result.Value);
        Assert.False(authorization.Entered.Task.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authority_denial_or_failure_returns_no_data_even_when_runtime_changes(bool fault)
    {
        await using var runtime = new HomeCoreRuntime();
        var authorization = new PendingAuthorization();
        var pending = new HomeCoreApi(runtime, authorization).GetStateAsync(Caller);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await authorization.Entered.Task.WaitAsync(deadline.Token);
        runtime.ReportServiceState("home.core", HomeServiceLifecycleState.Ready, true);
        if (fault) authorization.Decision.SetException(new IOException("controlled authority unavailable"));
        else authorization.Decision.SetResult(false);
        var result = await pending.WaitAsync(deadline.Token);
        Assert.False(result.Succeeded); Assert.Equal("PermissionDenied", result.Code); Assert.Null(result.Value);
    }

    [Fact]
    public async Task Cancellation_while_authorization_waits_is_preserved_without_success()
    {
        await using var runtime = new HomeCoreRuntime();
        var authorization = new PendingAuthorization();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = new HomeCoreApi(runtime, authorization).GetStateAsync(Caller, lifetime.Token);
        await authorization.Entered.Task.WaitAsync(lifetime.Token);
        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        authorization.Decision.TrySetResult(false);
    }

    [Fact]
    public async Task Default_runtime_authority_and_unverified_metadata_never_grant_service_data()
    {
        await using var runtime = new HomeCoreRuntime();
        var authorization = new DeniedAuthorization();
        var api = new HomeCoreApi(runtime, authorization);
        var denied = await api.GetStateAsync(Caller);
        Assert.False(denied.Succeeded); Assert.Equal("PermissionDenied", denied.Code); Assert.Null(denied.Value);
        var unverified = await api.GetStateAsync(Caller with { VerificationMethod = "" });
        Assert.False(unverified.Succeeded); Assert.Equal("CallerIdentityUnverified", unverified.Code); Assert.Null(unverified.Value);
        var compatibility = await api.GetCompatibilityAsync(Caller, new("controlled-app", "1", [new("home.core", 1)]));
        Assert.Equal(HomeCompatibilityState.PermissionDenied, compatibility.State);
        Assert.Empty(compatibility.AcceptedServices);
    }

    private sealed record Observation(bool Succeeded, string Code, bool Recoverable, long? Revision,
        bool HasValue, long? SnapshotRevision, HomeServiceDescriptor? CoreService);

    private static async Task<Observation> Read(HomeCoreApi api, string shape, CancellationToken ct)
    {
        if (shape == "state")
        {
            var result = await api.GetStateAsync(Caller, ct);
            return new(result.Succeeded, result.Code, result.Recoverable, result.Revision, result.Value is not null,
                result.Value?.Revision, result.Value?.Services.Single(service => service.ServiceId == "home.core"));
        }
        if (shape == "services")
        {
            var result = await api.GetServicesAsync(Caller, ct);
            return new(result.Succeeded, result.Code, result.Recoverable, result.Revision, result.Value is not null,
                null, result.Value?.Single(service => service.ServiceId == "home.core"));
        }
        var single = await api.GetServiceAsync(Caller, "home.core", ct);
        return new(single.Succeeded, single.Code, single.Recoverable, single.Revision, single.Value is not null,
            null, single.Value);
    }

    private sealed class PendingAuthorization : IHomeCoreAuthorization
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Target { get; private set; }
        public IReadOnlySet<string>? Scopes { get; private set; }
        public async ValueTask<bool> IsAllowedAsync(HomeCallerIdentity caller, string target,
            IReadOnlySet<string> scopes, CancellationToken ct)
        {
            Target = target; Scopes = scopes;
            Entered.TrySetResult();
            return await Decision.Task.WaitAsync(ct);
        }
    }

    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current = new("controlled-actor", "controlled-profile", null, null, "original");
        public int Reads;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Reads++; return ValueTask.FromResult(Current); }
    }

    private sealed class DeniedAuthorization : IHomeCoreAuthorization
    {
        public ValueTask<bool> IsAllowedAsync(HomeCallerIdentity caller, string target,
            IReadOnlySet<string> scopes, CancellationToken ct) => ValueTask.FromResult(false);
    }
}
