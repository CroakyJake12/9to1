using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalMiniComputerCatalogIdentitySource
{
    internal static bool SupportsOriginalAction(string action) => action == WriteAction;
    private sealed record OriginalMutation(Guid OperationId, AuthenticatedResourceActor Actor,
        string CatalogueName, string OriginalCatalogSha256, string OriginalFileEvidenceSha256,
        long OriginalByteLength, int OriginalSchemaVersion, string IntentDigest)
    {
        internal string Action => WriteAction;
        internal string ReviewMessage => "Add a durable identity to this exact existing protected Mini Computer catalogue. Preserve all existing catalogue fields and VM records. No VM/provider operation, catalogue creation or import approval is authorized.";
    }
    private static OriginalMutation CaptureOriginalMutation(ICanonicalMiniComputerCatalogIdentitySource source,
        ICanonicalMiniComputerCatalogIdentityIntent intent)
    {
        // Issuer proof precedes observation of this interface's metadata.
        if (!source.IsIssuedOriginalIdentityIntent(intent))
            throw new UnauthorizedAccessException("The actual protected catalogue producer did not issue this intent.");
        var observed = new OriginalMutation(intent.OperationId, intent.Actor, intent.CatalogueName,
            intent.OriginalCatalogSha256, intent.OriginalFileEvidenceSha256, intent.OriginalByteLength,
            intent.OriginalSchemaVersion, source.GetOriginalIdentityIntentDigest(intent));
        if (observed.OperationId == Guid.Empty || observed.Actor is null ||
            string.IsNullOrWhiteSpace(observed.CatalogueName) || observed.CatalogueName.Length > 4096 ||
            observed.OriginalByteLength is < 1 or > 16 * 1024 * 1024 || observed.OriginalSchemaVersion != 1 ||
            !IsDigest(observed.OriginalCatalogSha256) || !IsDigest(observed.OriginalFileEvidenceSha256) || !IsDigest(observed.IntentDigest))
            throw new UnauthorizedAccessException("The actual catalogue byte/native identity/schema observation is unavailable.");
        return observed;
    }
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private sealed partial class Claim
    {
        private OriginalMutation _originalMutation = null!;
        internal string? OriginalAction => _originalMutation?.Action;
        private void DemandSameOriginalMutation()
        {
            var current = CaptureOriginalMutation(owner._creator, intent);
            if (_originalMutation is null || current != _originalMutation || Actor != current.Actor)
                throw new UnauthorizedAccessException("The original catalogue native identity/bytes/actor/operation/digest changed after review.");
        }
        private (string Code, bool Applied) CaptureAcknowledgedDecisionAudit(ICanonicalMiniComputerCatalogIdentityAcknowledgment acknowledgment)
        {
            // SAME creator/raw atomic Task/ACK provenance was independently checked first.
            if (!ReferenceEquals(acknowledgment.OriginalIntent, intent))
                throw new UnauthorizedAccessException("Another catalogue identity acknowledgment cannot settle this WRITE.");
            var applied = acknowledgment.Applied;
            if (applied && (acknowledgment.CreatedIdentity is not { SchemaVersion: 1 } identity ||
                identity.StoreId == Guid.Empty || !IsDigest(acknowledgment.PublishedCatalogSha256)))
                throw new InvalidDataException("The actual applied identity decision has no valid published identity observation.");
            return (applied ? "HOME_MINI_COMPUTER_IDENTITY_INITIALIZED" : "HOME_MINI_COMPUTER_IDENTITY_NOT_APPLIED", applied);
        }
    }
}
