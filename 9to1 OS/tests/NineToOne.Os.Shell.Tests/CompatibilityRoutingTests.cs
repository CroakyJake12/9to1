using Haven.Application;
using Haven.Application.Compatibility;
using Haven.Core;

namespace NineToOne.Os.Shell.Tests;

public sealed class CompatibilityRoutingTests
{
    [Fact]
    public async Task IneligiblePreferenceDoesNotSilentlySwitchAndPolicyTrustFailClosed()
    {
        var fixture = new Fixture();
        fixture.Observation = fixture.Observation with { PreferredBackendId = "winboat" };
        var unavailable = await fixture.Service.PreviewAsync(fixture.App.ApplicationId, 7);
        Assert.Equal(CompatibilityRoutingStatus.PreferredBackendUnavailable, unavailable.Status);
        Assert.Null(unavailable.ProposedBackend);
        fixture.Observation = fixture.Observation with { PreferredBackendId = null, PackageTrustVerified = false };
        Assert.Equal(CompatibilityRoutingStatus.Denied, (await fixture.Service.PreviewAsync(fixture.App.ApplicationId, 7)).Status);
        fixture.Observation = fixture.Observation with { PackageTrustVerified = true, PolicyAllowed = false, PolicyReason = "Managed by school" };
        var denied = await fixture.Service.PreviewAsync(fixture.App.ApplicationId, 7);
        Assert.Equal("Managed by school", denied.Reason); Assert.Null(denied.ProposedBackend);
    }

    [Fact]
    public async Task RetainedProposalCopiesBackendEvidenceAndStillRequiresHumanReview()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.PreviewAsync(fixture.App.ApplicationId, 7);
        Assert.Equal(CompatibilityRoutingStatus.ReviewRequired, result.Status);
        Assert.Equal("wine", result.ProposedBackend!.BackendId);
        Assert.Equal(fixture.App.ApplicationId, result.ApplicationId);
        fixture.Backends.Clear();
        Assert.Equal(2, result.Backends.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<CompatibilityBackendObservation>)result.Backends).Clear());
    }

    [Fact]
    public async Task SessionChangedDuringOwnerObservationRejectsProposal()
    {
        var fixture = new Fixture();
        fixture.OnObserve = () => fixture.Actor = fixture.Actor with { AuthenticationRevision = "session2" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Service.PreviewAsync(fixture.App.ApplicationId, 7));
    }

    [Fact]
    public async Task OwnerCannotSubstituteAppAndStaleCanonicalRevisionNeverReachesOwner()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Service.PreviewAsync(fixture.App.ApplicationId, 6));
        Assert.Equal(0, fixture.Observations);
        fixture.Observation = fixture.Observation with { ApplicationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<IOException>(async () => await fixture.Service.PreviewAsync(fixture.App.ApplicationId, 7));
    }

    private sealed class Fixture : IAuthenticatedResourceActorSource, IInstalledApplicationRegistry, ICompatibilityApplicationOwner
    {
        public AuthenticatedResourceActor Actor = new("actor", "profile", null, null, "session1");
        public InstalledApplicationReference App = new(Guid.NewGuid(), "profile", "compatibility", "platform", "package", "entry", "Label", "1", true, true, false, 7, default!);
        public List<CompatibilityBackendObservation> Backends = [new("wine", CompatibilityBackendKind.Wine, "prefix1", "1", 1, true, null),
            new("winboat", CompatibilityBackendKind.WindowsEnvironment, "guest1", "1", 2, false, "No current guest runtime")];
        public CompatibilityApplicationObservation Observation;
        public Action? OnObserve;
        public int Observations;
        public CompatibilityRoutingService Service { get; }
        public Fixture()
        {
            Observation = new(App.ApplicationId, 7, "owner1", true, null, true, null, Backends);
            Service = new(this, this, this);
        }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor);
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<InstalledApplicationReference>>([App]);
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) =>
            ValueTask.FromResult<InstalledApplicationReference?>(id == App.ApplicationId && revision == App.Revision ? App : null);
        public ValueTask<CompatibilityApplicationObservation?> ObserveAsync(AuthenticatedResourceActor actor, InstalledApplicationReference app, CancellationToken ct)
        { Observations++; OnObserve?.Invoke(); return ValueTask.FromResult<CompatibilityApplicationObservation?>(Observation); }
    }
}
