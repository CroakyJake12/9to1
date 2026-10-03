using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    /// <summary>Pure exact original tuple comparison on this issuer's still-uncompleted actual
    /// claimed handle. This is neither claim, current authorization nor owner write permission.
    /// The owner must reconstruct arguments from its detached typed proposal, keep original scope
    /// revisions and enforce canonical owner/private lifetime checks plus the final raw Home fence.</summary>
    public bool MatchesClaimedOriginalArguments(HomeResourceExecutionCapability originalCapability,
        string targetAppId, string actionId, IReadOnlyList<ResourceScope> originalScopes, JsonElement arguments)
    {
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalScopes);
        var attestation = CaptureClaimedAttestation(originalCapability);
        if (attestation is null || attestation.Submission.Scope.TargetAppId != targetAppId ||
            attestation.Submission.Scope.ActionName != actionId || arguments.ValueKind == JsonValueKind.Undefined ||
            attestation.Submission.Impact.ArgumentsDigest != Digest(arguments) ||
            attestation.Submission.Impact.ResourceBinding is not { SchemaVersion: 1 } binding) return false;
        var scopes = new List<ResourceScope>();
        foreach (var scope in originalScopes)
        {
            if (scopes.Count == 1000 || scope is null) return false;
            scopes.Add(scope);
        }
        return binding.OriginalActor == attestation.Actor && binding.Scopes.SequenceEqual(scopes);
    }
}
