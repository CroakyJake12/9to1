using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>
/// Home-scoped typed read owner for the selected reasoning request. It resolves the genuine Home-owned
/// selection through the SAME <see cref="HomeModelSelectionSource"/> the universal picker/top-bar use
/// (real current actor/authentication revision, route/revision, actual provider/model identity and
/// genuine route-derived scope), validates the resolved projection matches the requested dispatch target,
/// and returns a <see cref="ChatReasoningBinding"/> carrying the exact advertised reasoning ID and the
/// genuine bound provider/model identity — never a guessed enum, a percentage, an arbitrary string match,
/// or a split provider/model name.
///
/// The target model is matched by its EXACT full registered name (a native Ollama "qwen:tag" is
/// compared verbatim, never split into a fake provider/model). A wrong provider/model, an incoherent
/// surface scope, or an absent/unverified selection returns null so ordinary generation proceeds; a
/// selected ID that is not faithfully advertised by the matched model raises typed
/// <see cref="UnsupportedCapabilityException"/> BEFORE any provider invocation.
/// </summary>
public sealed class HomeChatReasoningSelectionReader : IChatReasoningSelectionReader
{
    private readonly HomeModelSelectionSource _source;
    private long _generation;

    public HomeChatReasoningSelectionReader(HomeModelSelectionSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<ChatReasoningBinding?> ResolveAsync(ChatReasoningSelectionQuery query, CancellationToken token)
    {
        // The Home source re-reads the real current actor, authentication revision, route/revision and
        // genuine scope on every resolve (never a startup-cached actor) and rejects stale publication
        // after its asynchronous preview/metadata reads.
        var snapshot = await _source.ResolveAsync(Interlocked.Increment(ref _generation), token).ConfigureAwait(false);
        if (snapshot is null) return null;

        // Exact full-name target match: the requested model must equal the genuine registered model id
        // verbatim (Ordinal). This never splits a native "qwen:tag" into a fake provider/model.
        if (!string.Equals(snapshot.ModelId, query.TargetModelName, StringComparison.Ordinal))
            return null;

        // When the caller genuinely knows the target provider, it must be the resolved one (duplicate
        // model ids across providers must never cross-authorise).
        if (!string.IsNullOrWhiteSpace(query.RequestedProviderId)
            && !string.Equals(snapshot.ProviderId, query.RequestedProviderId, StringComparison.Ordinal))
            return null;

        // A genuine route-derived scope is required; the conversation surface must be coherent with it.
        if (string.IsNullOrWhiteSpace(snapshot.ScopeName) || string.IsNullOrWhiteSpace(query.SurfaceScope))
            return null;
        if (!string.Equals(snapshot.ScopeName, query.SurfaceScope, StringComparison.Ordinal))
            return null;

        // Locality coherence: a caller asserting a local target must not apply a remote/unknown selection.
        if (query.IsLocal && !snapshot.IsLocal)
            return null;

        // No explicit selection -> ordinary generation (truthful absent, never a default).
        var selectedId = snapshot.SelectedReasoningId;
        if (string.IsNullOrWhiteSpace(selectedId)) return null;

        // Exact Ordinal membership against the model's actual advertised levels. A non-advertised or
        // stale selected ID is an unrepresentable selection for this target: typed Unsupported BEFORE
        // invocation, never a Medium substitute.
        var advertised = snapshot.AdvertisedReasoningIds;
        if (advertised.Count == 0 || !advertised.Contains(selectedId, StringComparer.Ordinal))
            throw new UnsupportedCapabilityException(
                $"The selected reasoning level {selectedId} is not advertised by {query.TargetModelName}.", selectedId);

        return new ChatReasoningBinding(
            snapshot.ProviderId!,
            snapshot.ModelId!,
            snapshot.ModelId,
            ArtifactIdentity: null,
            snapshot.AuthenticationRevision,
            snapshot.ScopeName,
            snapshot.ScopeId,
            snapshot.RouteId,
            snapshot.RouteRevision,
            selectedId);
    }
}
