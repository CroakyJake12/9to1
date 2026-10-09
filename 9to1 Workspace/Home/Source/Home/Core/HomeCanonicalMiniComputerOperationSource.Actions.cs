using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalMiniComputerOperationSource
{
    public const string StartAction = "mini-computer.start";
    public const string PauseAction = "mini-computer.pause";
    public const string ResumeAction = "mini-computer.resume";
    public const string SaveStateAction = "mini-computer.save-state";
    public const string ShutdownAction = "mini-computer.shutdown";
    internal static bool SupportsOriginalAction(string action) => action is InspectAction or StartAction or PauseAction or ResumeAction or SaveStateAction or ShutdownAction;
    private sealed record OriginalMutation(CanonicalMiniComputerAction Kind, string Action,
        CanonicalMiniComputerTarget Target, string IntentDigest)
    {
        internal string ReviewMessage => Kind == CanonicalMiniComputerAction.Inspect
            ? "Inspect the current state of this exact registered Mini Computer. No lifecycle transition is authorized."
            : $"{Kind} this exact registered Mini Computer through its original provider. Other VM, host and device actions are excluded.";
        internal string AppliedAuditCode => "HOME_MINI_COMPUTER_" + Kind.ToString().ToUpperInvariant();
    }
    private static OriginalMutation CaptureOriginalMutation(ICanonicalMiniComputerOperationSource source,
        ICanonicalMiniComputerOperationIntent intent)
    {
        if (!source.IsIssuedOriginalOperationIntent(intent)) throw new UnauthorizedAccessException("The actual Mini Computer producer did not issue this intent.");
        var action = intent.Action switch
        {
            CanonicalMiniComputerAction.Inspect => InspectAction,
            CanonicalMiniComputerAction.Start => StartAction,
            CanonicalMiniComputerAction.Pause => PauseAction,
            CanonicalMiniComputerAction.Resume => ResumeAction,
            CanonicalMiniComputerAction.SaveState => SaveStateAction,
            CanonicalMiniComputerAction.Shutdown => ShutdownAction,
            _ => throw new UnauthorizedAccessException("The source-issued Mini Computer action is unsupported.")
        };
        var target = intent.Target; var digest = source.GetOriginalOperationIntentDigest(intent);
        if (target.VirtualMachineId == Guid.Empty || target.ProviderId == Guid.Empty || string.IsNullOrWhiteSpace(target.ProviderMachineId) ||
            target.Revision < 1 || target.ConfigurationVersion < 1 || !IsDigest(target.OriginalCatalogSha256) || !IsDigest(digest))
            throw new UnauthorizedAccessException("The exact current VM/provider/configuration observation is unavailable.");
        return new(intent.Action, action, target, digest);
    }
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private sealed partial class Claim
    {
        private OriginalMutation _originalMutation = null!;
        internal string? OriginalAction => _originalMutation?.Action;
        private void DemandSameOriginalMutation()
        {
            var current = CaptureOriginalMutation(owner._creator, intent);
            if (_originalMutation is null || current.Kind != _originalMutation.Kind || current.Action != _originalMutation.Action ||
                !ReferenceEquals(current.Target, _originalMutation.Target) || current.IntentDigest != _originalMutation.IntentDigest)
                throw new UnauthorizedAccessException("The original VM/provider/action/digest changed after review.");
        }
        private (string Code, bool Applied) CaptureAcknowledgedDecisionAudit(ICanonicalMiniComputerOperationAcknowledgment acknowledgment)
        {
            // SAME source and raw provider task/ACK were checked before this observation.
            return (acknowledgment.IsPending ? "HOME_MINI_COMPUTER_PENDING" : _originalMutation.AppliedAuditCode,
                !acknowledgment.IsPending);
        }
    }
}
