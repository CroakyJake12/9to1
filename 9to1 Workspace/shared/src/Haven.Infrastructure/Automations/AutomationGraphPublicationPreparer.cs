using System.Text.Json;
using Haven.Core;
using Haven.Application.NodeGraph;
using Haven.Application.Automations;

namespace Haven.Infrastructure;

/// <summary>Prepares against actual canonical library and graph reads under the same original caller selection.
/// This class never writes either store, enables a definition, or interprets a journal as an execution grant.</summary>
public sealed class AutomationGraphPublicationPreparer(AutomationDefinitionReviewCaller caller,
    AutomationRepository definitions, WorkspaceStateRepository tasks,
    IVersionedNodeGraphRepository graphs, NodeGraphAutomationAdapter projection, NodeGraphSchemaRegistry schemas)
{
    public async Task<AutomationGraphPublicationPreparation> PrepareAsync(
        IAutomationDefinitionCallerSelection originalSelection, ReusableTaskDefinition reusable,
        AutomationDefinition? linkedSchedule, GraphDocument proposedDraft, long expectedGraphRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalSelection);
        if (!caller.HasRepositoryOrigin(originalSelection, definitions, tasks))
            throw new UnauthorizedAccessException("The graph proposal does not retain the original caller repository graph.");
        ArgumentNullException.ThrowIfNull(reusable);
        ArgumentNullException.ThrowIfNull(proposedDraft);
        if (expectedGraphRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedGraphRevision));
        // Capture before the first asynchronous owner check. Never reread caller-owned JSON/lists afterward.
        var operationID = Guid.NewGuid();
        var intent = GraphPublicationIntent.Capture(new("automations", originalSelection.StoreId,
            "automation.reusable-task", reusable.Id), originalSelection.Actor, operationID,
            GraphPublicationKind.SaveDraft, proposedDraft, expectedGraphRevision,
            [new("automation.reusable-task", originalSelection.StoreId.ToString("D") + "/" + reusable.Id.ToString("D"),
                reusable.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Write)], schemas, new(2 * 1024 * 1024, 32));
        // Registered schema budgets enumerate real nodes/ports/connections before serialization;
        // the output stream stops before retaining an oversized metadata/string payload.
        using var bounded = new BoundedSnapshotStream();
        JsonSerializer.Serialize(bounded, new GraphPublicationSnapshot(operationID, reusable,
            linkedSchedule, intent.Graph, expectedGraphRevision, null));
        var bytes = bounded.ToArray();
        var captured = JsonSerializer.Deserialize<GraphPublicationSnapshot>(bytes)
            ?? throw new InvalidDataException("The publication proposal is unavailable.");
        if (captured.Reusable.Id == Guid.Empty || captured.Reusable.IsEnabled || captured.Reusable.ArchivedAt is not null ||
            captured.LinkedSchedule is { IsEnabled: true } || captured.LinkedSchedule?.ArchivedAt is not null ||
            captured.Draft.State != GraphRevisionState.Draft || expectedGraphRevision == long.MaxValue || captured.Draft.Revision != expectedGraphRevision + 1)
            throw new InvalidDataException("Publication requires disabled, unarchived definitions and the exact next draft.");
        await caller.RequireCurrentAsync(originalSelection, cancellationToken).ConfigureAwait(false);
        var reusableRow = await tasks.GetOwnedTaskAsync(captured.Reusable.Id, cancellationToken).ConfigureAwait(false);
        if (reusableRow is null || reusableRow.RequiresRecovery || reusableRow.Value.Revision != captured.Reusable.Revision ||
            reusableRow.Value.OwnerBinding is not { } actualOwner || reusableRow.Value.ArchivedAt is not null ||
            actualOwner.StoreId != originalSelection.StoreId || actualOwner.ProfileId != originalSelection.Actor.ProfileId ||
            actualOwner.AccountId != originalSelection.Actor.AccountId || actualOwner.OrganisationId != originalSelection.Actor.OrganisationId ||
            captured.Reusable.OwnerBinding != actualOwner)
            throw new UnauthorizedAccessException("The original reusable definition is stale or unavailable.");
        if (captured.LinkedSchedule is { } schedule)
        {
            var row = await definitions.GetOwnedAsync(schedule.Id, cancellationToken).ConfigureAwait(false);
            if (row is null || row.RequiresRecovery || row.Value.Revision != schedule.Revision ||
                row.Value.OwnerBinding != reusableRow.Value.OwnerBinding || schedule.OwnerBinding != row.Value.OwnerBinding || schedule.ContainerId != captured.Reusable.ContainerId)
                throw new UnauthorizedAccessException("The original linked schedule is stale or unavailable.");
        }
        var current = await graphs.GetAsync(captured.Draft.GraphId, cancellationToken).ConfigureAwait(false);
        if (current is null ? expectedGraphRevision != 0 :
            current.OwnerAppId != "automations" || current.OwnerEntityId != "automation.reusable-task/" + originalSelection.StoreId.ToString("D") + "/" + captured.Reusable.Id.ToString("D") ||
            current.Draft.Revision != expectedGraphRevision)
            throw new UnauthorizedAccessException("The original canonical graph owner or revision changed.");
        if (reusableRow.Value.GraphBinding is { } binding &&
            (binding.GraphId != captured.Draft.GraphId || binding.DraftRevision != expectedGraphRevision) ||
            reusableRow.Value.GraphBinding is null && expectedGraphRevision != 0)
            throw new UnauthorizedAccessException("Publication cannot silently substitute the stable graph identity.");
        _ = projection.Compile(captured.Draft); // Sole registered node bindings/schema; model JSON cannot supply a catalogue.
        await caller.RequireCurrentAsync(originalSelection, cancellationToken).ConfigureAwait(false);
        using var finalSnapshot = new BoundedSnapshotStream();
        JsonSerializer.Serialize(finalSnapshot, captured with { OriginalActiveRevision = current?.Active?.Revision });
        return AutomationGraphPublicationPreparation.FromBoundedSnapshot(finalSnapshot.ToArray());
    }
    private sealed class BoundedSnapshotStream : MemoryStream
    {
        private const long Limit = 2 * 1024 * 1024;
        private void Require(int count)
        { if (count < 0 || Position > Limit - count) throw new InvalidDataException("The publication proposal exceeds its bound."); }
        public override void Write(byte[] buffer, int offset, int count)
        { Require(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Require(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value)
        { Require(1); base.WriteByte(value); }
    }

}
