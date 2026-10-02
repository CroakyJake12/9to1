using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real FileHome/profile/model-route-profile resolver and individual permission protocol.
/// The trusted principal is controlled. No SQL mutation, graph effect or native dispatch is simulated as success.</summary>
public sealed class HomeClaimedResourceAttestationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-claimed-attestation-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _store;
    private readonly Principal _principal = new();
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly ResourceAuthorizationService _resources;
    private readonly HomeResourceOperationBroker _broker;
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { actualProtocolOnly = true });
    public HomeClaimedResourceAttestationTests()
    {
        Directory.CreateDirectory(_root);
        _store = new(Path.Combine(_root, "home.json"));
        _profiles = new(_store, _principal);
        _permissions = new(_store, new HomeModelRouteActionPolicies().TryGet);
        _resources = new(_profiles, [new HomeModelRouteProfileOwner(_profiles)]);
        _broker = new(_resources, _permissions);
    }
    private async Task<(AuthenticatedResourceActor Actor, HomeResourceExecutionCapability Capability, HomeClaimedResourceAttestation Attestation)> Claim()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        ResourceScope[] scopes = [new("home.profile-model-routes", actor.ProfileId, actor.AuthenticationRevision, ResourceAccess.Write)];
        var pending = await _broker.AuthorizeForActorAsync(actor, HomeModelPickerFeatureProvider.AppId,
            "models.routes.update", scopes, Arguments, "Actual permission protocol; no model edit", null, "original-session");
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments));
        Assert.Equal(actor, await _broker.ClaimExecutionAsync(capability, HomeModelPickerFeatureProvider.AppId,
            "models.routes.update", scopes, Arguments));
        return (actor, capability, Assert.IsType<HomeClaimedResourceAttestation>(_broker.CaptureClaimedAttestation(capability)));
    }
    [Fact]
    public async Task Actual_claimed_individual_tuple_lease_holds_revocation_and_checks_without_recursive_permission_reads()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var token = timeout.Token;
        var original = await Claim();
        var lease = Assert.IsAssignableFrom<IHomeLocalOperationLease>(await _broker.AcquireClaimedResourceLeaseAsync(_store, _profiles, [original.Attestation], token));
        var blocked = _permissions.BlockCallerAsync(original.Actor.ActorId, token);
        try
        {
            Assert.False(blocked.IsCompleted);
            Assert.True(await lease.IsCurrentAsync(token)); // Blocked operation owns permission semaphore: no recursive read allowed.
        }
        finally { await lease.DisposeAsync(); }
        Assert.True((await blocked.WaitAsync(token)).Succeeded);
        Assert.Null(await _broker.AcquireClaimedResourceLeaseAsync(_store, _profiles, [original.Attestation], token));
    }
    [Fact]
    public async Task Foreign_issuer_and_changed_actual_principal_cannot_borrow_original_attestation()
    {
        var original = await Claim();
        var foreign = new HomeResourceOperationBroker(_resources, _permissions);
        Assert.Null(foreign.CaptureClaimedAttestation(original.Capability));
        Assert.Null(await foreign.AcquireClaimedResourceLeaseAsync(_store, _profiles, [original.Attestation]));
        _principal.Current = "controlled-different-kernel-principal";
        Assert.Null(await _broker.AcquireClaimedResourceLeaseAsync(_store, _profiles, [original.Attestation]));
    }
    [Theory]
    [InlineData("Revision")]
    [InlineData("Access")]
    [InlineData("SessionId")]
    public async Task Actual_persisted_full_original_tuple_substitution_denies_without_regrant(string field)
    {
        var original = await Claim();
        var read = await _store.ReadAsync();
        var record = Assert.Single(read.State!.Records, item => item.RecordId == "home.permissions-trust");
        var payload = JsonNode.Parse(record.Payload.GetRawText())!.AsObject();
        var request = Assert.Single(payload["Requests"]!.AsArray(), item => item!["RequestId"]!.GetValue<string>() == original.Capability.RequestId)!;
        if (field == "SessionId") request[field] = "substituted-session";
        else
        {
            var scope = request["Impact"]!["ResourceBinding"]!["Scopes"]!.AsArray()[0]!;
            scope[field] = field == "Revision" ? JsonValue.Create("substituted-resource-revision") : JsonValue.Create((int)ResourceAccess.Read);
        }
        Assert.True((await _store.WriteAsync(record with { Payload = JsonSerializer.SerializeToElement(payload) }, record.Revision)).IsSuccess);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        Assert.Null(await _broker.AcquireClaimedResourceLeaseAsync(_store, _profiles, [original.Attestation]));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    [Fact]
    public async Task Actual_recorded_owner_outcome_retires_attestation_without_claim_or_effect_replay()
    {
        var original = await Claim();
        Assert.True((await _broker.CompleteExecutionAsync(original.Capability,
            new(HomePermissionRequestState.Cancelled, "ProtocolNoEffect", "No owner mutation entered; actual cancelled protocol outcome.", []))).Succeeded);
        Assert.Null(_broker.CaptureClaimedAttestation(original.Capability));
        Assert.Null(await _broker.AcquireClaimedResourceLeaseAsync(_store, _profiles, [original.Attestation]));
    }
    public void Dispose() { Directory.Delete(_root, true); }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Current = "controlled-original-kernel-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>(Current); }
    }
}
