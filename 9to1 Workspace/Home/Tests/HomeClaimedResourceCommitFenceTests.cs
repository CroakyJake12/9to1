using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real FileHome/profile/model-route-profile resolver and individual permission protocol.
/// The trusted principal is controlled. No SQL mutation, graph effect or native dispatch is simulated as success.</summary>
public sealed class HomeClaimedResourceCommitFenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-claimed-attestation-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _store;
    private readonly Principal _principal = new();
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly ResourceAuthorizationService _resources;
    private readonly HomeResourceOperationBroker _broker;
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { actualProtocolOnly = true });
    public HomeClaimedResourceCommitFenceTests()
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
    private async Task<HomeClaimedResourceCommitFence> CaptureFence(Func<bool> available)
    {
        var original = await Claim();
        var evidence = new Evidence();
        var localOwnership = new HomeLocalStoreOwnership(_store, _profiles, evidence, _permissions);
        await localOwnership.BindNewEmptyAsync(original.Actor, "files", evidence.StoreID);
        var configuration = new HomeCoreStateRecord("test.original-configuration", "test.original-configuration", 1,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(new { provider = "original" }));
        Assert.True((await _store.WriteAsync(configuration, 0)).IsSuccess);
        return Assert.IsType<HomeClaimedResourceCommitFence>(await HomeClaimedResourceCommitFence.CaptureAsync(_broker,
            _store, _profiles, new HomeResourceStoreOwnershipAuthority(localOwnership, _profiles), "files", evidence.StoreID,
            original.Capability, original.Actor, [configuration], available));
    }

    [Fact]
    public async Task Final_claimed_fence_holds_actual_Home_revocation_until_explicit_owner_release()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var ct = timeout.Token;
        await using var fence = await CaptureFence(() => true);
        var actor = (await _profiles.GetCurrentAsync(ct))!;
        Assert.True(await fence.ValidateAsync(ct));
        var blocked = _permissions.BlockCallerAsync(actor.ActorId, ct);
        try
        {
            Assert.False(blocked.IsCompleted);
            Assert.True(await fence.ValidateAsync(ct));
        }
        finally { await fence.DisposeAsync(); }
        Assert.True((await blocked.WaitAsync(ct)).Succeeded);
        Assert.False(await fence.ValidateAsync(ct));
    }

    [Fact]
    public async Task Original_configuration_swap_between_capture_and_final_acquisition_denies()
    {
        await using var fence = await CaptureFence(() => true);
        var read = await _store.ReadAsync();
        var original = Assert.Single(read.State!.Records, item => item.RecordId == "test.original-configuration");
        Assert.True((await _store.WriteAsync(original with { Payload = JsonSerializer.SerializeToElement(new { provider = "foreign" }) }, original.Revision)).IsSuccess);
        Assert.False(await fence.ValidateAsync());
    }

    [Fact]
    public async Task Retired_original_lifetime_or_disposed_fence_never_reacquires_authority()
    {
        var live = true;
        var fence = await CaptureFence(() => live);
        Assert.True(await fence.ValidateAsync());
        live = false;
        Assert.False(await fence.ValidateAsync());
        await fence.DisposeAsync();
        await fence.DisposeAsync();
        live = true;
        Assert.False(await fence.ValidateAsync());
    }

    [Fact]
    public async Task Actual_original_ownership_receipt_substitution_before_final_acquisition_denies()
    {
        await using var fence = await CaptureFence(() => true);
        var read = await _store.ReadAsync();
        var record = Assert.Single(read.State!.Records, item => item.RecordType == "home.local-store-ownership");
        var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
        Assert.True((await _store.WriteAsync(record with
        { Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "substituted-revision" }) }, record.Revision)).IsSuccess);
        Assert.False(await fence.ValidateAsync());
    }

    [Fact]
    public async Task Actual_caller_revocation_before_final_acquisition_denies_without_reclaim()
    {
        await using var fence = await CaptureFence(() => true);
        var actor = (await _profiles.GetCurrentAsync(default))!;
        Assert.True((await _permissions.BlockCallerAsync(actor.ActorId)).Succeeded);
        Assert.False(await fence.ValidateAsync());
        Assert.False(await fence.ValidateAsync());
    }

    private sealed class Evidence : IHomeLocalStoreEvidenceSource
    {
        public string StoreID { get; } = Guid.NewGuid().ToString("D");
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string kind, string storeID, CancellationToken ct) =>
            ValueTask.FromResult<HomeLocalStoreEvidence?>(kind == "files" && storeID == StoreID
                ? new("files", StoreID, "actual-helper-test-empty-store", true, true, true) : null);
    }
    public void Dispose() { Directory.Delete(_root, true); }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Current = "controlled-original-kernel-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>(Current); }
    }
}
