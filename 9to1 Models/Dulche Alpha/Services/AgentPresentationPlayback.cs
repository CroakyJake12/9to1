namespace NineToOne.Dulche.Den;

/// <summary>One caller-owned presentation context, never an Agent executor. The observation source
/// supplies actual activity; synthetic preview stays in the existing authoring API. This controller
/// reads current Den configuration/assets and changes only its transient display state.</summary>
public sealed class AgentPresentationPlayback
{
    private readonly AgentPresentationService _canonical;
    private readonly IAgentPresentationObservationSource _observations;
    private readonly AgentPresentationContext _context;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _definitionRevision = -1;
    private string? _stateId;
    private AgentPresentationObservation? _last;
    // Availability and display resets cannot erase this fixed context's replay fence.
    private AgentPresentationObservation? _highWater;

    public AgentPresentationPlayback(AgentPresentationService canonical,
        IAgentPresentationObservationSource observations, AgentPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.NamespaceId) || string.IsNullOrWhiteSpace(context.AgentId) ||
            string.IsNullOrWhiteSpace(context.SourceId) || context.SourceId.Length > 256)
            throw new DenException(DenErrorCode.InvalidRecord, "An actual Agent activity context is required.");
        _canonical = canonical; _observations = observations; _context = context;
    }

    public async Task<AgentPresentationFrame> ReadAsync(bool reducedMotion,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var agent = await _canonical.GetAsync(_context.NamespaceId, _context.AgentId, cancellationToken).ConfigureAwait(false);
            var definition = AgentAvatarPresentation.Snapshot(agent.Presentation ??
                throw new DenException(DenErrorCode.NotFound, "Agent presentation is not configured."));
            var observation = await _observations.GetCurrentAsync(_context, cancellationToken).ConfigureAwait(false);
            if (observation is not null && (observation.Context != _context || observation.Sequence < 1))
                throw new DenException(DenErrorCode.InvalidRecord, "The observed activity belongs to another context.");
            if (observation is not null && _highWater is not null &&
                (observation.Sequence < _highWater.Sequence || (observation.Sequence == _highWater.Sequence && observation != _highWater)))
                throw new DenException(DenErrorCode.Conflict, "The observed activity is stale or inconsistent.");

            var changedDefinition = agent.Revision != _definitionRevision;
            var state = changedDefinition || observation is null ? definition.InitialStateId : _stateId;
            var description = observation is null ? (EventId: (string?)null, ReadableActivity: "Activity unavailable") :
                AgentPresentationEvents.Describe(observation.Kind);
            var eventId = changedDefinition || observation != _last ? description.EventId : null;
            // Resolve display state separately so reduced motion or a missing animated asset does
            // not forget the current state. This pure value does not prove asset or activity access.
            var resolved = AgentAvatarPresentation.Present(agent with { Presentation = definition }, state,
                eventId, description.ReadableActivity, reducedMotion: false);
            var frame = await _canonical.PresentCurrentAsync(_context.NamespaceId, _context.AgentId,
                agent.Revision, resolved.StateId, null, description.ReadableActivity, reducedMotion,
                cancellationToken).ConfigureAwait(false);
            // Recheck the owning Run/Conversation permission and exact observation after all
            // asynchronous asset checks. No caller-supplied observation is a read capability.
            var currentObservation = await _observations.GetCurrentAsync(_context, cancellationToken).ConfigureAwait(false);
            if (currentObservation != observation)
                throw new DenException(DenErrorCode.Conflict, "The activity changed during presentation.");
            var currentAgent = await _canonical.GetAsync(_context.NamespaceId, _context.AgentId, cancellationToken).ConfigureAwait(false);
            if (currentAgent.Revision != agent.Revision)
                throw new DenException(DenErrorCode.Conflict, "The Agent changed during presentation.");
            cancellationToken.ThrowIfCancellationRequested();
            _definitionRevision = agent.Revision; _stateId = resolved.StateId; _last = observation;
            if (observation is not null) _highWater = observation;
            return frame;
        }
        catch (DenException error) when (error.Code is DenErrorCode.Forbidden or DenErrorCode.NotFound)
        {
            _definitionRevision = -1; _stateId = null; _last = null;
            throw;
        }
        finally { _gate.Release(); }
    }
}
