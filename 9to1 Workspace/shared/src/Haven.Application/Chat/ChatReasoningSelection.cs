namespace Haven.Application;

/// <summary>
/// Dependency-neutral typed read owner for the selected reasoning request. Implemented by a
/// Home-scoped adapter that validates the full current actor/authentication revision, actual
/// provider/model/artifact identity, route/revision and genuine scope against real sources before
/// returning a <see cref="ChatReasoningBinding"/> carrying the exact advertised reasoning ID and the
/// genuine bound target identity (no provider/model prefix guessing, never a fabricated percentage or
/// a guessed enum).
///
/// Contract:
/// - A missing or unverified canonical selection returns <c>null</c> (truthful ordinary generation,
///   never an invented percentage, never a guessed enum).
/// - A selection that cannot be faithfully applied to the requested dispatch target (a stale/mismatched
///   model/provider, an incoherent surface scope, or a non-advertised ID) throws typed
///   <see cref="UnsupportedCapabilityException"/> or returns null BEFORE any provider invocation.
/// - The binding returned carries the genuine provider/model identity and the selected level so the
///   dispatch layer can refuse to forward it to a different provider/model.
/// </summary>
public interface IChatReasoningSelectionReader
{
    /// <summary>Resolves the exact selected reasoning binding for a real dispatch target, or returns
    /// null when no explicit verified selection exists (ordinary generation).</summary>
    Task<ChatReasoningBinding?> ResolveAsync(ChatReasoningSelectionQuery query, CancellationToken token);
}

/// <summary>
/// Describes the requested dispatch target and surface for one chat request. Values are derived from
/// the real resolved turn context, never fabricated. The implementing Home adapter treats its own real
/// projection as authoritative for actor/route/scope validation; the query is only the requested binding
/// it must match. A wrong provider/model or an incoherent scope omits/refuses rather than dispatching a
/// stale selection.
/// </summary>
public sealed record ChatReasoningSelectionQuery(
    string ConversationId,
    string TargetModelName,
    string? RequestedProviderId,
    bool IsLocal,
    string SurfaceScope);

/// <summary>
/// The resolved, verified selection binding. It couples the exact advertised reasoning ID to the
/// genuine bound target identity (provider/model, plus route/scope and authentication revision when
/// available) so dispatch can enforce that the selection is never silently forwarded to a different
/// provider/model. All values derive from the genuine Home projection.
/// </summary>
public sealed record ChatReasoningBinding(
    string ProviderId,
    string ModelName,
    string? ModelId,
    string? ArtifactIdentity,
    string? AuthenticationRevision,
    string ScopeName,
    string? ScopeId,
    string RouteId,
    long RouteRevision,
    string SelectedReasoningId);
