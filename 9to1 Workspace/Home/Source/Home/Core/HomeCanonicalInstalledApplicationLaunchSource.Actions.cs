using Haven.Application;
namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalInstalledApplicationLaunchSource
{
    internal static bool SupportsOriginalAction(string action) => action == WriteAction;
    private sealed record OriginalMutation(Guid OperationId, AuthenticatedResourceActor Actor,
        string AppId, string PackageId, Guid InstalledApplicationId, long InstalledApplicationRevision,
        string ActivationSha256, string DescriptorSha256, string OriginalHomeLeaseIdentity, string IntentDigest)
    {
        internal string Action => WriteAction;
        internal string ReviewMessage => "Open this exact currently installed application under its original user. This launch does not approve its data access, provider/network operations or other service actions.";
    }
    private static OriginalMutation CaptureOriginalMutation(ICanonicalInstalledApplicationLaunchProducer source,
        ICanonicalInstalledApplicationLaunchIntent intent)
    {
        if (!source.IsIssuedOriginalLaunchIntent(intent))
            throw new UnauthorizedAccessException("The actual installed application producer did not issue this launch choice.");
        var observed = new OriginalMutation(intent.OperationId, intent.Actor, intent.AppId, intent.PackageId,
            intent.InstalledApplicationId, intent.InstalledApplicationRevision, intent.ActivationSha256,
            intent.DescriptorSha256, intent.OriginalHomeLeaseIdentity, source.GetOriginalLaunchIntentDigest(intent));
        if (observed.OperationId == Guid.Empty || observed.Actor is null || observed.InstalledApplicationId == Guid.Empty ||
            observed.InstalledApplicationRevision < 1 || !BoundedId(observed.AppId) || !BoundedId(observed.PackageId) ||
            !Guid.TryParseExact(observed.OriginalHomeLeaseIdentity, "D", out var lease) || lease == Guid.Empty ||
            !IsDigest(observed.ActivationSha256) || !IsDigest(observed.DescriptorSha256) || !IsDigest(observed.IntentDigest))
            throw new UnauthorizedAccessException("The actual installed application/index/activation/listener choice is unavailable.");
        return observed;
    }
    private static bool BoundedId(string? value) => value is { Length: > 0 and <= 256 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private sealed partial class Claim
    {
        private OriginalMutation _originalMutation = null!;
        internal string? OriginalAction => _originalMutation?.Action;
        private void DemandSameOriginalMutation()
        {
            var current = CaptureOriginalMutation(owner._creator, intent);
            if (_originalMutation is null || current != _originalMutation || Actor != current.Actor)
                throw new UnauthorizedAccessException("The original installed choice/actor/index/activation/listener changed after review.");
        }
        private (string Code, bool Applied) CaptureAcknowledgedDecisionAudit(ICanonicalInstalledApplicationLaunchAcknowledgment acknowledgment)
        {
            if (!ReferenceEquals(acknowledgment.OriginalIntent, intent))
                throw new UnauthorizedAccessException("Another native launch acknowledgment cannot settle this action.");
            var applied = acknowledgment.Applied;
            if (applied && (acknowledgment.ProcessId < 1 || !acknowledgment.ProcessStartIdentity.StartsWith("windows-filetime:", StringComparison.Ordinal) ||
                !acknowledgment.ExecutableIdentity.StartsWith("sha256:", StringComparison.Ordinal) || !IsDigest(acknowledgment.ExecutableIdentity[7..])))
                throw new InvalidDataException("The actual controlled launch has no observed original PID/start/executable.");
            return (applied ? "HOME_INSTALLED_APPLICATION_LAUNCHED" : "HOME_INSTALLED_APPLICATION_NOT_LAUNCHED", applied);
        }
    }
}
