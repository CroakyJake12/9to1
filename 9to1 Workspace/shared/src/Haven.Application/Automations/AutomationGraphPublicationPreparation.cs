using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;
using Haven.Application.NodeGraph;

namespace Haven.Application.Automations;

/// <summary>Detached publication proposal, not graph authority or permission to run.
/// Actual guarded graph publication and SQL linked-row commit remain separate owner operations.</summary>
public sealed class AutomationGraphPublicationPreparation
{
    private readonly byte[] _snapshot;
    private AutomationGraphPublicationPreparation(byte[] snapshot)
    {
        _snapshot = snapshot;
        Digest = Convert.ToHexString(SHA256.HashData(_snapshot));
    }
    /// <summary>Bounded detached inspection metadata only; not an issuer selection or publication grant.</summary>
    public static AutomationGraphPublicationPreparation FromBoundedSnapshot(ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.Length == 0 || snapshot.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("The publication proposal exceeds its bound.");
        _ = JsonSerializer.Deserialize<GraphPublicationSnapshot>(snapshot)
            ?? throw new InvalidDataException("The publication proposal is unavailable.");
        return new(snapshot.ToArray());
    }
    public string Digest { get; }
    public GraphPublicationSnapshot Read() => JsonSerializer.Deserialize<GraphPublicationSnapshot>(_snapshot)
        ?? throw new InvalidDataException("The captured graph publication proposal is unavailable.");

    /// <summary>Captures one schema-validated next Draft revision from the exact saved Draft.
    /// This is preparation only: it does not save, activate, grant execution, or mutate either owner.</summary>
    public static AutomationGraphPublicationPreparation PrepareNextDraft(
        GraphPublicationSnapshot current,
        GraphDocument editedDraft,
        NodeGraphSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(editedDraft);
        ArgumentNullException.ThrowIfNull(schemas);

        var reusable = current.Reusable ?? throw new ArgumentException("The canonical Automation identity is required.", nameof(current));
        var schedule = current.LinkedSchedule ?? throw new ArgumentException("The linked canonical Automation is required.", nameof(current));
        var saved = current.Draft ?? throw new ArgumentException("The saved Draft is required.", nameof(current));
        var reusableBinding = reusable.GraphBinding;
        var scheduleBinding = schedule.GraphBinding;
        if (saved.SchemaVersion != GraphDocument.CurrentSchemaVersion || saved.State != GraphRevisionState.Draft ||
            reusable.Id == Guid.Empty || schedule.Id != reusable.Id || reusableBinding is null || scheduleBinding is null ||
            reusableBinding.GraphId == Guid.Empty || reusableBinding.GraphId != scheduleBinding.GraphId ||
            saved.GraphId != reusableBinding.GraphId || saved.GraphId != scheduleBinding.GraphId ||
            reusableBinding.DraftRevision != saved.Revision || scheduleBinding.DraftRevision != saved.Revision ||
            reusableBinding.ActiveRevision != current.OriginalActiveRevision ||
            scheduleBinding.ActiveRevision != current.OriginalActiveRevision ||
            current.ExpectedGraphRevision != saved.Revision || saved.Revision < 1 || saved.Revision == long.MaxValue)
            throw new InvalidOperationException("The preparation no longer matches the exact saved Automation Draft and Active revisions.");

        if (editedDraft.SchemaVersion != GraphDocument.CurrentSchemaVersion || editedDraft.GraphId != saved.GraphId ||
            editedDraft.Revision != saved.Revision + 1 || editedDraft.State != GraphRevisionState.Draft ||
            !string.Equals(editedDraft.CapabilityProfileId, saved.CapabilityProfileId, StringComparison.Ordinal))
            throw new InvalidOperationException("The edited graph must be the next Draft revision of the same Automation graph and capability profile.");
        if (schemas.Validate(editedDraft).Count != 0)
            throw new InvalidOperationException("The edited Draft failed registered Node Graph schema validation.");

        var preparedReusable = reusable with
        {
            GraphBinding = reusableBinding with { DraftRevision = editedDraft.Revision }
        };
        var preparedSchedule = schedule with
        {
            GraphBinding = scheduleBinding with { DraftRevision = editedDraft.Revision }
        };
        var prepared = new GraphPublicationSnapshot(Guid.NewGuid(), preparedReusable, preparedSchedule,
            editedDraft, saved.Revision, current.OriginalActiveRevision);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(prepared);
        if (bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("The prepared Draft exceeds its bounded publication payload.");
        return new AutomationGraphPublicationPreparation(bytes);
    }
}

public sealed record GraphPublicationSnapshot(Guid OperationId, ReusableTaskDefinition Reusable,
    AutomationDefinition? LinkedSchedule, GraphDocument Draft, long ExpectedGraphRevision,
    long? OriginalActiveRevision);
