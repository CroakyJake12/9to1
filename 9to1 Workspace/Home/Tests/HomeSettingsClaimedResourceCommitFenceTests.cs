using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual FileHome/profile/individual approval/ownership protocol; controlled principal and
/// empty-library evidence. Does not simulate settings CAS, Maps/Shelf mutation or native acceptance.</summary>
public sealed class HomeSettingsClaimedResourceCommitFenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-settings-claim-fence-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly Evidence _evidence = new();
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private HomeResourceOperationBroker? _broker;
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { operation = "original-library-protocol" });
    public HomeSettingsClaimedResourceCommitFenceTests()
    {
        _store = new(Path.Combine(_root, "home.json"));
        _profiles = new(_store, new Principal());
        _permissions = new(_store, (_, _) => new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true));
        _ownership = new(new HomeLocalStoreOwnership(_store, _profiles, _evidence, _permissions), _profiles);
    }
    private async Task<(AuthenticatedResourceActor Actor, HomeResourceExecutionCapability Capability)> Claim(string kind)
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        var localOwnership = new HomeLocalStoreOwnership(_store, _profiles, _evidence, _permissions);
        await localOwnership.BindNewEmptyAsync(actor, kind, _evidence.StoreID);
        var action = kind == "maps" ? "maps.journey.save" : "shelf.item.add";
        ResourceScope[] scopes = [new(kind + ".library", _evidence.StoreID, "1", ResourceAccess.Write)];
        _broker = new(new ResourceAuthorizationService(_profiles, [new Owner(kind, _evidence.StoreID)]), _permissions);
        var pending = await _broker.AuthorizeForActorAsync(actor, kind, action, scopes, Arguments,
            "Original library approval protocol only", null, "settings-fence-test");
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments));
        Assert.Equal(actor, await _broker.ClaimExecutionAsync(capability, kind, action, scopes, Arguments));
        return (actor, capability);
    }
    private ValueTask<HomeClaimedResourceCommitFence?> Capture(string kind,
        (AuthenticatedResourceActor Actor, HomeResourceExecutionCapability Capability) original, Func<bool> lifetime) =>
        HomeClaimedResourceCommitFence.CaptureSettingsAsync(_broker!, _store, _profiles, _ownership, kind,
            _evidence.StoreID, original.Capability, original.Actor, lifetime);

    [Theory]
    [InlineData("maps")]
    [InlineData("shelf")]
    public async Task Actual_settings_claim_without_Home_provider_configuration_holds_raw_revocation_until_owner_release(string kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var ct = timeout.Token;
        var original = await Claim(kind);
        var records = (await _store.ReadAsync(ct)).State!.Records;
        Assert.DoesNotContain(records, item => item.RecordType == "files.native-workspace");
        await using var fence = Assert.IsType<HomeClaimedResourceCommitFence>(await Capture(kind, original, () => true));
        Assert.True(await fence.ValidateAsync(ct));
        var blocked = _permissions.BlockCallerAsync(original.Actor.ActorId, ct);
        try
        {
            Assert.False(blocked.IsCompleted);
            Assert.True(await fence.ValidateAsync(ct)); // Block owns permission semaphore; no Home callback can reenter.
        }
        finally { await fence.DisposeAsync(); }
        Assert.True((await blocked.WaitAsync(ct)).Succeeded);
        Assert.False(await fence.ValidateAsync(ct));
    }

    [Fact]
    public async Task Settings_variant_cannot_bypass_mandatory_Files_config_or_match_foreign_UUID_or_issuer()
    {
        var original = await Claim("maps");
        Assert.Null(await Capture("files", original, () => true));
        Assert.Null(await HomeClaimedResourceCommitFence.CaptureAsync(_broker!, _store, _profiles, _ownership,
            "files", _evidence.StoreID, original.Capability, original.Actor, [], () => true));
        Assert.Null(await HomeClaimedResourceCommitFence.CaptureSettingsAsync(_broker!, _store, _profiles, _ownership,
            "maps", Guid.NewGuid().ToString("D"), original.Capability, original.Actor, () => true));
        var foreign = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, []), _permissions);
        Assert.Null(await HomeClaimedResourceCommitFence.CaptureSettingsAsync(foreign, _store, _profiles, _ownership,
            "maps", _evidence.StoreID, original.Capability, original.Actor, () => true));
    }

    [Fact]
    public async Task Actual_owned_settings_receipt_substitution_before_final_acquisition_denies()
    {
        var original = await Claim("maps");
        await using var fence = Assert.IsType<HomeClaimedResourceCommitFence>(await Capture("maps", original, () => true));
        var read = await _store.ReadAsync();
        var record = Assert.Single(read.State!.Records, item => item.RecordType == "home.local-store-ownership");
        var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
        Assert.True((await _store.WriteAsync(record with
            { Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "substituted" }) }, record.Revision)).IsSuccess);
        Assert.False(await fence.ValidateAsync());
    }

    [Fact]
    public async Task Settings_original_lifetime_failure_only_denies_and_disposal_never_reacquires()
    {
        var original = await Claim("shelf");
        Assert.Null(await Capture("shelf", original, () => throw new InvalidOperationException("Controlled lifetime refusal")));
        var throws = false;
        var fence = Assert.IsType<HomeClaimedResourceCommitFence>(await Capture("shelf", original,
            () => throws ? throw new InvalidOperationException("Controlled retired original lifetime") : true));
        throws = true;
        Assert.False(await fence.ValidateAsync());
        await fence.DisposeAsync(); throws = false;
        Assert.False(await fence.ValidateAsync());
    }

    [Fact]
    public async Task Completion_cannot_reserve_local_terminal_outcome_while_actual_owner_fence_is_held()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var ct = timeout.Token;
        var original = await Claim("maps");
        await using var fence = Assert.IsType<HomeClaimedResourceCommitFence>(await Capture("maps", original, () => true));
        Assert.True(await fence.ValidateAsync(ct));
        var completion = _broker!.CompleteExecutionAsync(original.Capability,
            new(HomePermissionRequestState.Cancelled, "ExternalTerminal", "Actual competing completion", []), ct);
        try
        {
            Assert.False(completion.IsCompleted);
            Assert.NotNull(_broker.CaptureClaimedAttestation(original.Capability));
            Assert.True(await fence.ValidateAsync(ct));
        }
        finally { await fence.DisposeAsync(); }
        Assert.True((await completion.WaitAsync(ct)).Succeeded);
        Assert.Null(_broker.CaptureClaimedAttestation(original.Capability));
        Assert.False(await fence.ValidateAsync(ct));
    }

    [Fact]
    public async Task Completion_between_capture_and_actual_owner_entry_denies_without_reacquiring()
    {
        var original = await Claim("shelf");
        await using var fence = Assert.IsType<HomeClaimedResourceCommitFence>(await Capture("shelf", original, () => true));
        Assert.True((await _broker!.CompleteExecutionAsync(original.Capability,
            new(HomePermissionRequestState.Cancelled, "ExternalTerminal", "Actual completion before owner entry", []))).Succeeded);
        Assert.False(await fence.ValidateAsync());
        Assert.False(await fence.ValidateAsync());
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>("controlled-settings-fence-principal");
    }
    private sealed class Evidence : IHomeLocalStoreEvidenceSource
    {
        public string StoreID { get; } = Guid.NewGuid().ToString("D");
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string kind, string id, CancellationToken ct) =>
            ValueTask.FromResult<HomeLocalStoreEvidence?>(id == StoreID && kind is "maps" or "shelf"
                ? new(kind, id, "controlled-original-empty-library", true, true, true) : null);
    }
    private sealed class Owner(string kind, string storeID) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => kind + ".library";
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
            ResourceScope scope, CancellationToken ct) => ValueTask.FromResult(new ResourceAccessDecision(
                scope.Kind == ResourceKind && scope.Id == storeID && scope.Revision == "1" && scope.Access == ResourceAccess.Write,
                "controlled-original-library-protocol", actor.ActorId, scope.Revision, actor.OrganisationId));
    }
}
