using Haven.Application.NodeGraph;
using Haven.Core.Forms;

namespace Haven.Application;

/// <summary>Pins shared graph revisions through the normal authorized Forms draft transaction.
/// This reference service does not execute graph nodes or grant access to their consequential actions.</summary>
public sealed class FormGraphReferenceService(FormPublicationService publications,
    IVersionedNodeGraphRepository graphs, NodeGraphSchemaRegistry schemas, TimeProvider? clock = null)
{
    public const string StateProfile = "forms.state";
    public const string LogicProfile = "forms.logic";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<FormPublicationResult> PinDraftAsync(Guid formID, long publicationRevision,
        CancellationToken token = default)
    {
        var loaded = await publications.ReadAsync(formID, token).ConfigureAwait(false);
        if (!loaded.Success) return loaded;
        if (loaded.Publication!.Revision != publicationRevision) return new(false, "RevisionConflict", null);
        var project = FormAuthoringService.Decode(loaded.Publication.Draft);
        var state = project.ModeDefinition.StateGraphID is { } stateID
            ? await ResolveAsync(formID, stateID, null, StateProfile, project.ModeDefinition.StartNodeID, token).ConfigureAwait(false) : null;
        var logic = project.LogicGraphID is { } logicID
            ? await ResolveAsync(formID, logicID, null, LogicProfile, null, token).ConfigureAwait(false) : null;
        var updated = project with
        {
            ModeDefinition = project.ModeDefinition with { StateGraphRevision = state?.Revision },
            LogicGraphRevision = logic?.Revision, Revision = checked(project.Revision + 1), ModifiedAt = _clock.GetUtcNow()
        };
        return await publications.SaveDraftAsync(formID, publicationRevision, FormProjectEditor.Project(updated), token).ConfigureAwait(false);
    }

    /// <summary>Validates the exact immutable publication dependency after the caller has authorized
    /// the FormVersion/session. Repository ownership is checked here; raw graph IDs confer no authority.</summary>
    public async Task ValidatePinnedAsync(FormProject project, CancellationToken token = default)
    {
        var captured = FormProjectCodec.Capture(project);
        if (captured.ModeDefinition.StateGraphID is { } stateID)
            _ = await ResolveAsync(captured.FormID, stateID, captured.ModeDefinition.StateGraphRevision
                ?? throw new InvalidDataException("State graph revision is not pinned."), StateProfile,
                captured.ModeDefinition.StartNodeID, token).ConfigureAwait(false);
        if (captured.LogicGraphID is { } logicID)
            _ = await ResolveAsync(captured.FormID, logicID, captured.LogicGraphRevision
                ?? throw new InvalidDataException("Logic graph revision is not pinned."), LogicProfile, null, token).ConfigureAwait(false);
    }

    private async Task<GraphDocument> ResolveAsync(Guid formID, Guid graphID, long? revision,
        string profile, Guid? startID, CancellationToken token)
    {
        var owned = await graphs.GetAsync(graphID, token).ConfigureAwait(false);
        if (owned is null || owned.OwnerAppId != "forms" || owned.OwnerEntityId != formID.ToString("D"))
            throw new InvalidDataException("The referenced graph does not belong to this form.");
        var graph = revision is { } pinned
            ? owned.ActivatedRevisions.SingleOrDefault(candidate => candidate.Revision == pinned)
            : owned.Active;
        if (graph is null || graph.GraphId != graphID || graph.State != GraphRevisionState.Active
            || graph.CapabilityProfileId != profile || schemas.Validate(graph).Count != 0
            || startID is { } start && !graph.Nodes.Any(node => node.NodeId == start))
            throw new InvalidDataException("The referenced activated Forms graph revision is unavailable or invalid.");
        return graph;
    }
}
