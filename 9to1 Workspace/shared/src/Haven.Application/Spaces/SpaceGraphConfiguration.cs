using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Application;

// Persisted values are append-only identities, not layout indices.
public enum SpaceGraphPage { Harness = 0, Model = 1, UserMessage = 2 }

/// <summary>A reference into Home's canonical graph repository. A newer graph in
/// Home cannot change this Space's effective active revision until association
/// is durably acknowledged. This metadata grants no execution authority.</summary>
public sealed record SpaceGraphReference(Guid GraphId, string CapabilityProfileId,
    long DraftRevision, long? ActiveRevision, GraphPublicationReceipt LastAssociatedPublication);

/// <summary>Durable evidence of one exact pending publication. It is not a
/// restored selection, request claim, ownership receipt or authority token.</summary>
public sealed record SpaceGraphPendingPublication(Guid OperationId, SpaceGraphPage Page,
    GraphPublicationKind Kind, GraphOwnerDescriptor Owner, AuthenticatedResourceActor OriginalActor,
    long OriginalSpaceRevision, long PendingSpaceRevision, long ExpectedGraphRevision,
    Guid GraphId, string CapabilityProfileId, string ArgumentsDigest, JsonElement PublicationArguments,
    SpaceGraphReference? PriorReference, DateTimeOffset RequestedAt);

/// <summary>The three Expert pages refer to canonical Home graphs within the
/// same Space record. Basic/Advanced mutations must preserve these references.
/// A pending operation fences changes to that Space until observed reconciliation.</summary>
public sealed record SpaceGraphConfiguration(SpaceGraphReference? Harness = null,
    SpaceGraphReference? Model = null, SpaceGraphReference? UserMessage = null,
    SpaceGraphPendingPublication? Pending = null)
{
    public SpaceGraphReference? GetReference(SpaceGraphPage page) => page switch
    {
        SpaceGraphPage.Harness => Harness,
        SpaceGraphPage.Model => Model,
        SpaceGraphPage.UserMessage => UserMessage,
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    internal SpaceGraphConfiguration WithReference(SpaceGraphPage page, SpaceGraphReference reference) => page switch
    {
        SpaceGraphPage.Harness => this with { Harness = reference },
        SpaceGraphPage.Model => this with { Model = reference },
        SpaceGraphPage.UserMessage => this with { UserMessage = reference },
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };
}
