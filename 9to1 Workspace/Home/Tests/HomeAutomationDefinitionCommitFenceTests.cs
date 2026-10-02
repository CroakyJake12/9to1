using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual FileHome/profile/individual approval/ownership protocol; controlled principal and
/// empty-library evidence. Does not simulate settings CAS, Maps/Shelf mutation or native acceptance.</summary>
public sealed class HomeAutomationDefinitionCommitFenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-automation-claim-fence-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly Evidence _evidence = new();
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private HomeResourceOperationBroker? _broker;
    private readonly Guid _definitionId = Guid.NewGuid();
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { operation = "original-library-protocol" });
    public HomeAutomationDefinitionCommitFenceTests()
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
        var action = "automations.update";
        ResourceScope[] scopes = [new("automation.reusable-task", _evidence.StoreID + "/" + _definitionId.ToString("D"), "1", ResourceAccess.Write)];
        _broker = new(new ResourceAuthorizationService(_profiles, [new Owner(_evidence.StoreID + "/" + _definitionId.ToString("D"))]), _permissions);
        var pending = await _broker.AuthorizeForActorAsync(actor, kind, action, scopes, Arguments,
            "Original library approval protocol only", null, "settings-fence-test");
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments));
        Assert.Equal(actor, await _broker.ClaimExecutionAsync(capability, kind, action, scopes, Arguments));
        return (actor, capability);
    }
    private ValueTask<HomeClaimedResourceCommitFence?> Capture(
        (AuthenticatedResourceActor Actor, HomeResourceExecutionCapability Capability) original, Func<bool> lifetime,
        Guid? definition = null, long revision = 1) =>
        HomeClaimedResourceCommitFence.CaptureAutomationDefinitionAsync(_broker!, _store, _profiles, _ownership,
            _evidence.StoreID, definition ?? _definitionId, revision, original.Capability, original.Actor, lifetime);

    [Fact]
    public async Task Original_SQL_definition_claim_holds_actual_Home_block_until_owner_releases()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var ct = timeout.Token;
        var original = await Claim("automations");
        await using var fence = Assert.IsType<HomeClaimedResourceCommitFence>(await Capture(original, () => true));
        Assert.True(await fence.ValidateAsync(ct));
        var blocked = _permissions.BlockCallerAsync(original.Actor.ActorId, ct);
        try { Assert.False(blocked.IsCompleted); Assert.True(await fence.ValidateAsync(ct)); }
        finally { await fence.DisposeAsync(); }
        Assert.True((await blocked.WaitAsync(ct)).Succeeded);
        Assert.False(await fence.ValidateAsync(ct));
    }

    [Fact]
    public async Task Another_definition_or_revision_cannot_use_original_claim_and_Files_config_stays_required()
    {
        var original = await Claim("automations");
        Assert.Null(await Capture(original, () => true, Guid.NewGuid()));
        Assert.Null(await Capture(original, () => true, revision: 2));
        Assert.Null(await Capture(original, () => true, revision: 0));
        Assert.Null(await HomeClaimedResourceCommitFence.CaptureSettingsAsync(_broker!, _store, _profiles, _ownership,
            "automations", _evidence.StoreID, original.Capability, original.Actor, () => true));
        Assert.Null(await HomeClaimedResourceCommitFence.CaptureAsync(_broker!, _store, _profiles, _ownership,
            "files", _evidence.StoreID, original.Capability, original.Actor, [], () => true));
        var foreign = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, []), _permissions);
        Assert.Null(await HomeClaimedResourceCommitFence.CaptureAutomationDefinitionAsync(foreign, _store, _profiles,
            _ownership, _evidence.StoreID, _definitionId, 1, original.Capability, original.Actor, () => true));
    }

    [Fact]
    public async Task Original_receipt_substitution_and_retired_SQL_lifetime_deny_final_acquisition()
    {
        var original = await Claim("automations");
        await using var fence = Assert.IsType<HomeClaimedResourceCommitFence>(await Capture(original, () => true));
        var read = await _store.ReadAsync();
        var record = Assert.Single(read.State!.Records, item => item.RecordType == "home.local-store-ownership");
        var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
        Assert.True((await _store.WriteAsync(record with
            { Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "substituted" }) }, record.Revision)).IsSuccess);
        Assert.False(await fence.ValidateAsync());
        Assert.Null(await Capture(original, () => throw new InvalidOperationException("retired SQL owner")));
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
            ValueTask.FromResult<HomeLocalStoreEvidence?>(id == StoreID && kind == "automations"
                ? new(kind, id, "controlled-original-empty-library", true, true, true) : null);
    }
    private sealed class Owner(string scopeID) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "automation.reusable-task";
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
            ResourceScope scope, CancellationToken ct) => ValueTask.FromResult(new ResourceAccessDecision(
                scope.Kind == ResourceKind && scope.Id == scopeID && scope.Revision == "1" && scope.Access == ResourceAccess.Write,
                "controlled-original-library-protocol", actor.ActorId, scope.Revision, actor.OrganisationId));
    }
}
