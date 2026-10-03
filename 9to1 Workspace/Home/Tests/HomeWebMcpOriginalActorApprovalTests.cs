using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Home.Tests;

/// <summary>Actual FileHome graph and unavailable genuine Browser-owner negatives; no native/external dispatch claim.</summary>
public sealed class HomeWebMcpOriginalActorApprovalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-webmcp-home-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _store;
    private readonly Principal _principal = new();
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomeWebMcpOriginalActorApproval _owner;
    public HomeWebMcpOriginalActorApprovalTests()
    {
        Directory.CreateDirectory(_root); _store = new(Path.Combine(_root, "home.json"));
        _profiles = new(_store, _principal);
        _permissions = new(_store, (_, _) => null); // Missing genuine action catalogue, never a fabricated policy/grant.
        _broker = new(new ResourceAuthorizationService(_profiles, []), _permissions); // Missing actual Browser resolver denies.
        _owner = new(_store, _profiles, _broker, _permissions);
    }
    private static WebMcpInvocationRequest Request() => new("https://example.invalid/", "controlled-document",
        "controlled-browser", "controlled-capability", true, "controlled-tool",
        JsonSerializer.SerializeToElement(new { type = "object" }), JsonSerializer.SerializeToElement(new { }));
    private async Task<IWebMcpOriginalActorReview> Prepare()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        return _owner.PrepareReview(actor, Request(), [new("webmcp.document", "controlled-document", "controlled-document", ResourceAccess.Write)]);
    }
    [Fact]
    public async Task Preparation_retains_exact_private_request_before_any_Home_submission_and_no_owner_is_inferred()
    {
        var review = await Prepare(); var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        Assert.False(string.IsNullOrWhiteSpace(review.RequestId));
        Assert.Null(await _permissions.ReadRequestObservationAsync(review.RequestId));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.Equal(WebMcpPreparedReviewState.Rejected, (await review.SubmitPreparedAsync(default)).State);
        Assert.Null(await _permissions.ReadRequestObservationAsync(review.RequestId));
        Assert.Equal(WebMcpDispatchBeginState.Rejected, (await review.BeginDispatchAsync(Request(), default)).State);
        Assert.False((await review.FinishAdmissionAuditAsync(default)).OutcomeKnown);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    [Fact]
    public async Task Different_same_path_store_or_permission_producer_cannot_be_substituted_for_actual_graph()
    {
        await Prepare(); var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var other = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"));
        Assert.Throws<InvalidOperationException>(() => new HomeWebMcpOriginalActorApproval(other, _profiles, _broker, _permissions));
        var otherPermissions = new HomePermissionTrustService(other, (_, _) => null);
        Assert.Throws<InvalidOperationException>(() => new HomeWebMcpOriginalActorApproval(_store, _profiles, _broker, otherPermissions));
        var sameStoreOtherPermissions = new HomePermissionTrustService(_store, (_, _) => null);
        Assert.Throws<InvalidOperationException>(() => new HomeWebMcpOriginalActorApproval(_store, _profiles, _broker, sameStoreOtherPermissions));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    [Fact]
    public async Task Changed_actual_OS_principal_after_preparation_cannot_create_pending_review_or_dispatch()
    {
        var review = await Prepare(); var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        _principal.Value = "different-controlled-OS-principal";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await review.SubmitPreparedAsync(default));
        Assert.Null(await _permissions.ReadRequestObservationAsync(review.RequestId));
        Assert.Equal(WebMcpDispatchBeginState.OutcomeUnconfirmed, (await review.BeginDispatchAsync(Request(), default)).State);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Value = "controlled-webmcp-home-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>(Value); }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
