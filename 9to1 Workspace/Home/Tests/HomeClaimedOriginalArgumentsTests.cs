using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real FileHome/profile/model-route-profile resolver and individual permission protocol.
/// The trusted principal is controlled. No SQL mutation, graph effect or native dispatch is simulated as success.</summary>
public sealed class HomeClaimedOriginalArgumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-claimed-attestation-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _store;
    private readonly Principal _principal = new();
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly ResourceAuthorizationService _resources;
    private readonly HomeResourceOperationBroker _broker;
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { actualProtocolOnly = true });
    public HomeClaimedOriginalArgumentsTests()
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
    public async Task Claimed_original_argument_matcher_rejects_substituted_proposal_and_completed_or_foreign_capability()
    {
        var original = await Claim();
        ResourceScope[] scopes = [new("home.profile-model-routes", original.Actor.ProfileId,
            original.Actor.AuthenticationRevision, ResourceAccess.Write)];
        Assert.True(_broker.MatchesClaimedOriginalArguments(original.Capability, HomeModelPickerFeatureProvider.AppId,
            "models.routes.update", scopes, Arguments));
        Assert.False(_broker.MatchesClaimedOriginalArguments(original.Capability, "foreign-app", "models.routes.update", scopes, Arguments));
        Assert.False(_broker.MatchesClaimedOriginalArguments(original.Capability, HomeModelPickerFeatureProvider.AppId, "foreign-action", scopes, Arguments));
        Assert.False(_broker.MatchesClaimedOriginalArguments(original.Capability, HomeModelPickerFeatureProvider.AppId, "models.routes.update", [], Arguments));
        Assert.False(_broker.MatchesClaimedOriginalArguments(original.Capability, HomeModelPickerFeatureProvider.AppId,
            "models.routes.update", scopes, JsonSerializer.SerializeToElement(new { substituted = true })));
        var foreign = new HomeResourceOperationBroker(_resources, _permissions);
        Assert.False(foreign.MatchesClaimedOriginalArguments(original.Capability, HomeModelPickerFeatureProvider.AppId,
            "models.routes.update", scopes, Arguments));
        Assert.True((await _broker.CompleteExecutionAsync(original.Capability,
            new(HomePermissionRequestState.Cancelled, "NoEffect", "Actual helper protocol cancelled", []))).Succeeded);
        Assert.False(_broker.MatchesClaimedOriginalArguments(original.Capability, HomeModelPickerFeatureProvider.AppId,
            "models.routes.update", scopes, Arguments));
    }

    public void Dispose() { Directory.Delete(_root, true); }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Current = "controlled-original-kernel-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>(Current); }
    }
}
