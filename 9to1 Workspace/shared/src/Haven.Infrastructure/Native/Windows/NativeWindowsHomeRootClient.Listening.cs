using System.Collections.Frozen;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootClient : IHomeNativeInstalledPeerOriginalActorVerifier, IHomeNativeSessionHostVerifier
{
    private HomeNativeWindowsComposition? _actualListeningHome;
    private HomeNativeWindowsComposition.OriginalRootListeningObservation? _actualListening;
    private InstalledApplicationReference? _actualHomeIndex;
    private NativeWindowsHomeRootWire.Listening? _publishedListening;
    /// <summary>Only the actual signed, controlled Home process publishes its SAME
    /// independently successful host/listener and maintained registry-issued ID. This
    /// does not create a session lease, application GUID, role from metadata, permission
    /// or action consent. The Root kernel boundary independently matches the child.</summary>
    public Task PublishOriginalHomeListeningWithinSourceAsync(HomeNativeWindowsComposition sameHome,
        HomeInstalledApplicationRegistry sameRegistry, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Admit(scope, retain, async source =>
        {
            if (!ReferenceEquals(sameHome.StateStore, _homeStore) || !ReferenceEquals(sameHome.Profiles, _homeProfiles) ||
                !ReferenceEquals(sameRegistry, _registry) || !sameRegistry.HasOriginalComposition(_homeStore!, _homeProfiles!, [this]))
                throw new UnauthorizedAccessException("Use the SAME actual Home/index/actor/platform inventory tuple.");
            var listening = await Take(source, () => sameHome.ObserveOriginalRootListeningWithinSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false);
            if (!sameHome.IsIssuedOriginalRootListeningObservation(listening))
                throw new UnauthorizedAccessException("The actual Home did not issue this successful held listening observation.");
            var actor = listening.Actor;
            var home = await Take(source, () => ObserveOriginalPackageWithinSourceAsync(actor, "home",
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual full enrolled Home activation is unavailable.");
            if (!IsIssuedOriginalPackageObservation(home)) throw new UnauthorizedAccessException("The actual Root package witness was not issued.");
            var rows = await Take(source, () => sameRegistry.RefreshForActorWithinOriginalSourceAsync(actor,
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token).AsTask()).ConfigureAwait(false);
            var row = rows.SingleOrDefault(value => value.HomeProfileId == actor.ProfileId && value.ProviderId == ProviderId &&
                value.PlatformProfileId == home.OsPrincipal && value.OsApplicationId == "9to1.package:" + home.PackageId &&
                value.Entrypoint == home.Entrypoint && value.Version == home.Version && value.StableLaunchIdentity == "home" &&
                value.Enabled && value.ProfileAccessible)
                ?? throw new UnauthorizedAccessException("The SAME maintained actual Home registry did not issue its exact installed Home identity.");
            if (row.ApplicationId == Guid.Empty || row.Revision < 1)
                throw new UnauthorizedAccessException("The actual Home registry ID/revision is unavailable.");
            // Session correlation comes from Home's actual held native lease, never
            // an arbitrary requested ID or a fake installed-application GUID.
            var payload = new NativeWindowsHomeRootWire.Listening(actor, listening.ActualLease.ProfileId,
                listening.ActualLease.LeaseIdentity, listening.ActualLease.PipeName, row.ApplicationId,
                home.Fingerprint, listening.ActualLease.LeaseIdentity.ToString("D"));
            var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "publish-listening") { Listening = payload }).ConfigureAwait(false);
            if (!response.Accepted || response.Listening != payload) throw new UnauthorizedAccessException("The actual Root did not acknowledge this original controlled Home listener.");
            source.Invoke(() =>
            {
                if (!sameHome.IsCurrentOriginalRootListeningObservation(listening))
                    throw new UnauthorizedAccessException("The actual Home listening owner retired before publication.");
                _actualListeningHome = sameHome; _actualListening = listening; _actualHomeIndex = row; _publishedListening = payload;
                return true;
            });
            return true;
        });
    public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observedPeer, CancellationToken token)
        => new(Admit(body => body(), _ => { }, async source =>
        {
            if (_homeProfiles is null || _connect?.IsCompletedSuccessfully != true) return null;
            var actor = await Take(source, () => _homeProfiles!.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
            return actor is null ? null : await VerifyControlledHomeOwned(source, observedPeer, actor, token).ConfigureAwait(false);
        }));
    public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observedPeer,
        AuthenticatedResourceActor expectedActor, CancellationToken token)
        => new(Admit(body => body(), _ => { }, source => VerifyControlledHomeOwned(source, observedPeer, expectedActor, token)));
    public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observedPeer,
        HomeNativeSessionHostRequirement actualRequirement, CancellationToken token)
        => new(Admit(body => body(), _ => { }, async source =>
        {
            if (_homeProfiles is null || _connect?.IsCompletedSuccessfully != true) return null;
            var actor = await Take(source, () => _homeProfiles!.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
            if (actor is null || actualRequirement.AppId != "home" || _actualHomeIndex is null ||
                actualRequirement.OperatingSystemApplicationId != _actualHomeIndex.OsApplicationId) return null;
            return await VerifyControlledHomeOwned(source, observedPeer, actor, token).ConfigureAwait(false);
        }));
    private async Task<HomeNativeInstalledPeer?> VerifyControlledHomeOwned(CloudflareOriginalTaskLedger source,
        HomeNativeObservedPeer observed, AuthenticatedResourceActor actor, CancellationToken token)
    {
        // Selected callers require the actual independent Root-controlled child
        // source. Structural installed declaration remains separate from all consent.
        if (observed.ProcessId != Environment.ProcessId)
        {
            var actual = await VerifyOriginalSelectedPeerOwned(source, observed, actor, token).ConfigureAwait(false);
            return actual?.Peer;
        }
        var profiles = _homeProfiles;
        if (profiles is null || observed.ProcessId != Environment.ProcessId || _actualListeningHome is null || _actualListening is null ||
            _publishedListening is null || _actualHomeIndex is null || _connect?.IsCompletedSuccessfully != true ||
            !_actualListeningHome.IsCurrentOriginalRootListeningObservation(_actualListening)) return null;
        if (actor != await Take(source, () => profiles.GetCurrentWithinOriginalSourceAsync(
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false)) return null;
        var package = await Take(source, () => ObserveOriginalPackageWithinSourceAsync(actor, "home",
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
        if (package is null || !IsIssuedOriginalPackageObservation(package) || observed.OperatingSystemPrincipalId != package.OsPrincipal ||
            package.Fingerprint != _publishedListening.InstallationRevision || _actualHomeIndex.HomeProfileId != actor.ProfileId) return null;
        var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "observe-listening")).ConfigureAwait(false);
        if (!response.Accepted || response.Listening != _publishedListening || response.Listening.Actor != actor) return null;
        var current = await Take(source, () => _registry!.ResolveLaunchForActorWithinOriginalSourceAsync(
            _actualHomeIndex.ApplicationId, _actualHomeIndex.Revision, actor, body => source.Invoke(() => { body(); return true; }),
            raw => { _ = source.Track(raw); }, token).AsTask()).ConfigureAwait(false);
        if (current is null || current.ProviderId != ProviderId || current.StableLaunchIdentity != "home" || current.Entrypoint != package.Entrypoint) return null;
        var listening = await Take(source, () => _actualListeningHome.ObserveOriginalRootListeningWithinSourceAsync(
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
        if (!_actualListeningHome.IsIssuedOriginalRootListeningObservation(listening) || listening.Actor != actor ||
            listening.ActualLease != _actualListening.ActualLease) return null;
        if (actor != await Take(source, () => profiles.GetCurrentWithinOriginalSourceAsync(
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false)) return null;
        return new("home", current.ApplicationId, package.Fingerprint, "sha256:" +
            package.Activation.ActivatedFiles.Single(file => file.RelativePath == package.Activation.EntrypointRelativePath).Sha256,
            source.Invoke(() =>
            {
                // The SAME enrolled signed descriptor is structural installed-service
                // admission. Every service READ/WRITE still requires its actual Home
                // policy, authenticated session/currentness and separate consent.
                if (!IsIssuedOriginalPackageObservation(package))
                    throw new UnauthorizedAccessException("The source-issued current Home package is required for its service declaration.");
                var descriptor = HomePackageOriginalArtifactDescriptorParser.Parse(
                    package.Artifact.SignedDescriptorBytes, package.Artifact.DescriptorPayloadBytes,
                    package.Artifact.CatalogueRevision, new HomePackageActionRequest(package.PackageId,
                        HomePackageAction.Install, "root.listener.declaration", package.Version,
                        ExpectedRevision: package.Activation.CatalogueRevision)).Descriptor;
                if (descriptor.AppId != "home" || descriptor.AppId != package.Activation.AppId ||
                    descriptor.Version != package.Activation.Version || descriptor.Platform != package.Activation.Platform ||
                    descriptor.Abi != package.Activation.Abi)
                    throw new UnauthorizedAccessException("The authenticated Home service declaration differs from its protected activation.");
                return descriptor.RequiredServiceIds.ToFrozenSet(StringComparer.Ordinal);
            }))
        { Roles = new[] { HomeNativeSessionHostRequirement.RequiredRole }.ToFrozenSet(StringComparer.Ordinal) };
    }
}
