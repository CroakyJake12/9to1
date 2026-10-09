using System.Collections.Frozen;
using Haven.Application;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootClient : IHomeNativeControlledLaunchAuthority
{
    private async Task<(HomeNativeInstalledPeer Peer, NativeWindowsHomeRootWire.SelectedWitness Witness)?> VerifyOriginalSelectedPeerOwned(
        CloudflareOriginalTaskLedger source, HomeNativeObservedPeer observed, AuthenticatedResourceActor actor, CancellationToken token)
    {
        if (observed.ProcessId <= 0 || observed.ProcessId == Environment.ProcessId || _homeProfiles is null || _registry is null ||
            _publishedListening is null || _connect?.IsCompletedSuccessfully != true) return null;
        await DemandOriginalListeningCurrent(source, actor, token).ConfigureAwait(false);
        var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "observe-selected-application") { ProcessId = observed.ProcessId }).ConfigureAwait(false);
        if (!response.Accepted || response.SelectedWitness is not { } witness || witness.ProcessId != observed.ProcessId ||
            witness.OperatingSystemPrincipalId != observed.OperatingSystemPrincipalId || witness.OriginalChoice.OriginalListening != _publishedListening ||
            witness.OriginalChoice.OriginalListening.Actor != actor) return null;
        var choice = witness.OriginalChoice;
        var package = await Take(source, () => ObserveOriginalPackageWithinSourceAsync(actor, choice.AppId,
            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
        if (package is null || !IsIssuedOriginalPackageObservation(package) || package.PackageId != choice.PackageId || package.Fingerprint != choice.ActivationSha256 ||
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package.Artifact.SignedDescriptorBytes.Span)) != choice.DescriptorSha256) return null;
        var row = await Take(source, () => _registry.ResolveLaunchForActorWithinOriginalSourceAsync(choice.InstalledApplicationId,
            choice.InstalledApplicationRevision, actor, body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token).AsTask()).ConfigureAwait(false);
        if (row is null) return null;
        source.Invoke(() => { DemandOriginalLaunchRow(row, package, actor); return true; });
        var executable = "sha256:" + package.Activation.ActivatedFiles.Single(file => file.RelativePath == package.Activation.EntrypointRelativePath).Sha256;
        if (witness.ExecutableIdentity != executable || !witness.ProcessStartIdentity.StartsWith("windows-filetime:", StringComparison.Ordinal)) return null;
        // Observe actual child/current activation again after all finite local reads.
        // Source-issued Root custody and kernel observed PID/SID, not a requester's
        // package string or a signed dependency list, establish installed admission.
        var latest = await ExchangeOwned(source, new(1, Guid.NewGuid(), "observe-selected-application") { ProcessId = observed.ProcessId }).ConfigureAwait(false);
        if (!latest.Accepted || latest.SelectedWitness != witness) return null;
        await DemandOriginalListeningCurrent(source, actor, token).ConfigureAwait(false);
        var descriptor = source.Invoke(() => ParseOriginalSelectedDescriptor(package));
        return (new(choice.AppId, row.ApplicationId, package.Fingerprint, executable,
            descriptor.RequiredServiceIds.ToFrozenSet(StringComparer.Ordinal))
            { Roles = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal) }, witness);
    }
    public ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation, CancellationToken token) =>
        new(Admit(body => body(), _ => { }, async source =>
        {
            if (_homeProfiles is null || _publishedListening is null || observation.RequiredRole != string.Empty ||
                observation.ProfileId != _publishedListening.ProfileId || observation.SessionLeaseIdentity != _publishedListening.LeaseIdentity.ToString("D")) return false;
            var actor = await Take(source, () => _homeProfiles.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, token)).ConfigureAwait(false);
            if (actor is null) return false;
            var actual = await VerifyOriginalSelectedPeerOwned(source, new(observation.ProcessId, observation.OperatingSystemPrincipalId), actor, token).ConfigureAwait(false);
            return actual is { } same && same.Peer.AppId == observation.AppId &&
                same.Witness.ProcessStartIdentity == observation.ProcessStartIdentity && same.Witness.ExecutableIdentity == observation.ExecutableIdentity;
        }));
}
