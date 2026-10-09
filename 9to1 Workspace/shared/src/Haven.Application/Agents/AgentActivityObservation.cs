using Haven.Core;

namespace Haven.Application;

/// <summary>Serializable observations for display/reproduction. Schema stamps do not establish authenticity or grant authority.</summary>
public sealed record AgentActivityObservation(int SchemaVersion, string Producer, Guid AgentRunId,
    bool ObservationComplete, bool HasDeferredInvocations, IReadOnlyList<ToolActivity> Activities,
    IReadOnlyList<ToolInvocationEvidence> Invocations)
{
    public int CanonicalBindingVersion { get; init; }
    public AgentRunCanonicalBinding? CanonicalTask { get; init; }

    public const int CurrentSchemaVersion = 1;
    public const string OwningProducer = "haven.chat-dispatch.agent";
    public const string ToolIdentityNamespace = "haven.registered-local-tool";

    internal static AgentActivityObservation Capture(Guid runId, IReadOnlyList<ToolActivity> activities, bool streamCompleted)
    {
        var detached = activities.Select(item => item with
        {
            InvocationEvidence = item.InvocationEvidence is { } entries ? Array.AsReadOnly(entries.ToArray()) : null
        }).ToArray();
        var invocations = detached.SelectMany(item => item.InvocationEvidence ?? []).ToArray();
        var deferred = detached.Any(item => item.HasDeferredInvocations);
        var seen = new HashSet<Guid>();
        var complete = streamCompleted && !deferred && runId != Guid.Empty && detached.Length <= 1024 && invocations.Length <= 2048 &&
            detached.All(item => item.InvocationEvidence is not null);
        foreach (var entry in invocations)
        {
            var runtimeKnown = entry.RuntimeKey is "Workspace" or "Computer" or "Browser" or "Automation" or "Mcp" or "Calendar" or "Plugin";
            var valid = entry.InvocationId != Guid.Empty && !string.IsNullOrWhiteSpace(entry.ToolName) && entry.ToolName.Length <= 512 &&
                entry.EndedAt >= entry.StartedAt && (entry.RetryOfInvocationId is null || seen.Contains(entry.RetryOfInvocationId.Value)) &&
                (entry.Status switch
                {
                    ToolInvocationObservationStatus.DeniedBeforeDispatch => entry.RuntimeKey is null && entry.ReportedResultSucceeded is null,
                    ToolInvocationObservationStatus.UnavailableBeforeDispatch => (entry.RuntimeKey is null || runtimeKnown) && entry.ReportedResultSucceeded is null,
                    ToolInvocationObservationStatus.RuntimeReturned => runtimeKnown && entry.ReportedResultSucceeded is not null,
                    _ => false
                });
            if (!seen.Add(entry.InvocationId) || !valid) complete = false;
        }
        return new(CurrentSchemaVersion, OwningProducer, runId, complete, deferred,
            Array.AsReadOnly(detached), Array.AsReadOnly(invocations));
    }
}

/// <summary>Default-denying observation source. Hosts authorize access to the owning run before exposing this port.
/// The observation grants no tool/action permission and does not imply underlying owner mutation.</summary>
public interface IRecordedAgentInvocationSource
{
    Task<RecordedAgentInvocationEvidence?> GetRecordedInvocationEvidenceAsync(Guid runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RecordedAgentInvocationEvidence?>(null);
}

/// <summary>Host-lifetime evidence issued from an actual completed producer and freshly matched canonical row.
/// No public constructor or deserialization path recreates a producer receipt from user-supplied JSON.</summary>
public sealed class RecordedAgentInvocationEvidence
{
    internal RecordedAgentInvocationEvidence(Guid runId, Guid agentId, AgentActivityObservation observation)
    { AgentRunId = runId; AgentId = agentId; Invocations = observation.Invocations; }
    public Guid AgentRunId { get; }
    public Guid AgentId { get; }
    public string ToolIdentityNamespace => AgentActivityObservation.ToolIdentityNamespace;
    public IReadOnlyList<ToolInvocationEvidence> Invocations { get; }
}
