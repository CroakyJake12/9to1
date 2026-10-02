using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual FileHome/profile/model-route-profile owner/permission protocol. No model edit, native JS or external effect is claimed.</summary>
public sealed class HomePreparedResourceReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-prepared-home-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _physical;
    private readonly FaultStore _fault;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly ResourceAuthorizationService _resources;
    private readonly HomeResourceOperationBroker _broker;
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { intent = "controlled prepared protocol only" });
    public HomePreparedResourceReviewTests()
    {
        Directory.CreateDirectory(_root);
        _physical = new(Path.Combine(_root, "home.json"));
        _fault = new(_physical);
        _profiles = new(_physical, new Principal());
        _permissions = new(_fault, new HomeModelRouteActionPolicies().TryGet);
        _resources = new(_profiles, [new HomeModelRouteProfileOwner(_profiles)]);
        _broker = new(_resources, _permissions);
    }
    private async Task<(AuthenticatedResourceActor Actor, ResourceScope Scope)> Origin()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        return (actor, new("home.profile-model-routes", actor.ProfileId, actor.AuthenticationRevision, ResourceAccess.Write));
    }
    private HomeResourcePreparedReview Prepare(AuthenticatedResourceActor actor, IReadOnlyList<ResourceScope> scopes) =>
        _broker.PrepareReviewForActor(actor, HomeModelPickerFeatureProvider.AppId, "models.routes.update", scopes,
            Arguments, "Controlled prepared protocol: no actual model route edit", null, "original-prepared-session");
    [Fact]
    public async Task Preparation_and_unattempted_observation_have_no_durable_side_effect()
    {
        var origin = await Origin(); var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var prepared = Prepare(origin.Actor, [origin.Scope]);
        Assert.Equal(HomePreparedReviewState.NotAttempted, (await _broker.ObservePreparedReviewAsync(prepared)).State);
        Assert.Null(await _permissions.ReadRequestObservationAsync(prepared.RequestId));
        Assert.Equal(0, _fault.PermissionWrites);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.True(await _broker.RetirePreparedReviewAsync(prepared));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _broker.AuthorizePreparedReviewAsync(prepared));
    }
    [Fact]
    public async Task Lost_actual_pending_publication_return_recovers_full_original_intent_without_second_authorization_or_Begin()
    {
        var origin = await Origin(); var prepared = Prepare(origin.Actor, [origin.Scope]);
        _fault.LoseNextPermissionWrite = true;
        await Assert.ThrowsAsync<IOException>(() => _broker.AuthorizePreparedReviewAsync(prepared));
        Assert.Equal(1, _fault.PermissionWrites);
        var persisted = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var recovered = await _broker.ObservePreparedReviewAsync(prepared);
        Assert.Equal(HomePreparedReviewState.RequestObserved, recovered.State); Assert.True(recovered.BindingRecovered);
        Assert.Equal(prepared.RequestId, recovered.Request!.RequestId);
        Assert.Equal(origin.Actor, recovered.Request.Impact.ResourceBinding!.OriginalActor);
        Assert.Equal(origin.Scope, Assert.Single(recovered.Request.Impact.ResourceBinding.Scopes));
        Assert.Equal(HomePermissionRequestState.PendingApproval, recovered.Request.State);
        Assert.Equal(persisted, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.Equal(HomePreparedReviewState.RequestObserved, (await _broker.AuthorizePreparedReviewAsync(prepared)).State);
        Assert.Equal(1, _fault.PermissionWrites);
        Assert.False(await _broker.RetirePreparedReviewAsync(prepared));
        Assert.True((await _permissions.DecideAsync(prepared.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await _broker.BeginExecutionCapabilityAsync(prepared.RequestId, Arguments));
        Assert.Equal(HomePermissionRequestState.Executing, (await _broker.ObservePreparedReviewAsync(prepared)).Request!.State);
        Assert.Null(await _broker.BeginExecutionCapabilityAsync(prepared.RequestId, Arguments));
        Assert.NotNull(await _broker.ClaimExecutionAsync(capability, HomeModelPickerFeatureProvider.AppId, "models.routes.update", [origin.Scope], Arguments));
        Assert.True((await _broker.CompleteExecutionAsync(capability, new(HomePermissionRequestState.Cancelled,
            "CONTROLLED_PROTOCOL_CANCELLED", "No actual model route or external tool was executed.", []))).Succeeded);
        Assert.True(await _broker.RetirePreparedReviewAsync(prepared));
    }
    [Theory]
    [InlineData("Revision")]
    [InlineData("Access")]
    [InlineData("AuthenticationRevision")]
    public async Task Actual_durable_full_tuple_tamper_cannot_recover_a_binding(string field)
    {
        var origin = await Origin(); var prepared = Prepare(origin.Actor, [origin.Scope]);
        _fault.LoseNextPermissionWrite = true;
        await Assert.ThrowsAsync<IOException>(() => _broker.AuthorizePreparedReviewAsync(prepared));
        var state = (await _physical.ReadAsync()).State!;
        var record = Assert.Single(state.Records, item => item.RecordId == "home.permissions-trust");
        var node = JsonNode.Parse(record.Payload.GetRawText())!;
        var binding = node["Requests"]!.AsArray()[0]!["Impact"]!["ResourceBinding"]!;
        if (field == "AuthenticationRevision") binding["OriginalActor"]![field] = "different-original-session";
        else binding["Scopes"]!.AsArray()[0]![field] = field == "Revision"
            ? JsonValue.Create("different-scope-revision") : JsonValue.Create((int)ResourceAccess.Read);
        Assert.True((await _physical.WriteAsync(record with { Payload = JsonSerializer.SerializeToElement(node) }, record.Revision)).IsSuccess);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var observed = await _broker.ObservePreparedReviewAsync(prepared);
        Assert.Equal(HomePreparedReviewState.OutcomeUnconfirmed, observed.State); Assert.False(observed.BindingRecovered);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.True((await _permissions.DecideAsync(prepared.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var writes = _fault.PermissionWrites;
        Assert.Null(await _broker.BeginExecutionCapabilityAsync(prepared.RequestId, Arguments));
        Assert.Equal(writes, _fault.PermissionWrites);
        Assert.False(await _broker.RetirePreparedReviewAsync(prepared)); // Mismatched actual intent remains unconfirmed, never guessed terminal.
    }
    [Fact]
    public async Task Foreign_actual_broker_cannot_submit_or_observe_the_original_opaque_prepared_handle()
    {
        var origin = await Origin(); var prepared = Prepare(origin.Actor, [origin.Scope]);
        var foreign = new HomeResourceOperationBroker(_resources, _permissions);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.AuthorizePreparedReviewAsync(prepared));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.ObservePreparedReviewAsync(prepared));
        Assert.Equal(0, _fault.PermissionWrites); Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    [Fact]
    public async Task Original_scope_array_is_detached_and_readonly_observation_cannot_mutate_durable_metadata()
    {
        var origin = await Origin(); ResourceScope[] scopes = [origin.Scope];
        var prepared = Prepare(origin.Actor, scopes); scopes[0] = origin.Scope with { Revision = "caller-changed-after-preparation" };
        var observed = await _broker.AuthorizePreparedReviewAsync(prepared);
        Assert.Equal(origin.Scope, Assert.Single(observed.Request!.Impact.ResourceBinding!.Scopes));
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var copy = (await _permissions.ReadRequestObservationAsync(prepared.RequestId))!;
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceScope>)copy.Impact.ResourceBinding!.Scopes)[0] = scopes[0]);
        Assert.Equal(origin.Scope, Assert.Single((await _permissions.ReadRequestObservationAsync(prepared.RequestId))!.Impact.ResourceBinding!.Scopes));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.True((await _permissions.DecideAsync(prepared.RequestId, HomeApprovalChoice.Decline)).Succeeded);
        Assert.True(await _broker.RetirePreparedReviewAsync(prepared));
        Assert.Null(await _broker.BeginExecutionCapabilityAsync(prepared.RequestId, Arguments));
    }
    [Fact]
    public async Task Cancelled_first_attempt_remains_observation_only_and_never_creates_another_request()
    {
        var origin = await Origin(); var prepared = Prepare(origin.Actor, [origin.Scope]);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _broker.AuthorizePreparedReviewAsync(prepared, cancelled.Token));
        Assert.Equal(HomePreparedReviewState.OutcomeUnconfirmed, (await _broker.AuthorizePreparedReviewAsync(prepared)).State);
        Assert.Null(await _permissions.ReadRequestObservationAsync(prepared.RequestId));
        Assert.False(await _broker.RetirePreparedReviewAsync(prepared));
        Assert.Equal(0, _fault.PermissionWrites); Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    [Fact]
    public async Task Actual_scope_enumeration_stops_at_existing_resource_ceiling_before_any_Home_authorization()
    {
        var origin = await Origin(); var scopes = new LyingScopes(origin.Scope);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        Assert.Throws<ArgumentException>(() => Prepare(origin.Actor, scopes));
        Assert.Equal(1001, scopes.Enumerated); Assert.Equal(0, _fault.PermissionWrites);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    private sealed class LyingScopes(ResourceScope scope) : IReadOnlyList<ResourceScope>
    {
        public int Count => 1; public int Enumerated;
        public ResourceScope this[int index] => throw new InvalidOperationException("Enumeration must be bounded directly.");
        public IEnumerator<ResourceScope> GetEnumerator() { for (var index = 0; index < 1_000_000; index++) { Enumerated++; yield return scope; } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("controlled-actual-home-prepared-principal"); }
    private sealed class FaultStore(FileHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool LoseNextPermissionWrite; public int PermissionWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
            => inner.WriteGuardedAsync(record, expected, actor, guard, ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            var result = await inner.WriteAsync(record, expected, ct);
            if (record.RecordType == "home.permissions-trust")
            {
                PermissionWrites++;
                if (LoseNextPermissionWrite) { LoseNextPermissionWrite = false; Assert.True(result.IsSuccess); throw new IOException("Lost return AFTER actual Home pending publication."); }
            }
            return result;
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
