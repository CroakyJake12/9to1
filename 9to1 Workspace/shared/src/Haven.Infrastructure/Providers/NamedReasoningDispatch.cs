using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Smallest dependency-neutral dispatch seam for the shipping request bridge. It forwards a
/// transport request that carries an exact advertised named-reasoning id to a provider that
/// implements <see cref="INamedReasoningProvider"/>, passing the id verbatim on the wire. A backend
/// without that faithful port refuses with typed <see cref="UnsupportedCapabilityException"/> BEFORE
/// any provider invocation — never a guessed enum, an ignored request field, or a silent substitute.
/// Requests without an explicit reasoning selection (the ordinary path) never require the port.</summary>
internal static class NamedReasoningDispatch
{
    private static bool TryReasoningId(OllamaChatRequest request, out string id)
    {
        id = request.ReasoningId ?? string.Empty;
        return !string.IsNullOrWhiteSpace(id);
    }

    private static bool TryReasoningId(OllamaToolRequest request, out string id)
    {
        id = request.ReasoningId ?? string.Empty;
        return !string.IsNullOrWhiteSpace(id);
    }

    /// <summary>Streams a chat frame, routing an explicit reasoning id through the named-reasoning port.</summary>
    public static IAsyncEnumerable<string> Stream(IModelProvider provider, OllamaChatRequest request, CancellationToken token)
    {
        if (!TryReasoningId(request, out var id)) return provider.StreamChatAsync(request, token);
        return provider is INamedReasoningProvider capable
            ? capable.StreamChatWithReasoningAsync(request, id, token)
            : throw new UnsupportedCapabilityException(
                "This provider cannot carry a named reasoning level faithfully.", id);
    }

    /// <summary>Completes one finite frame. A non-streaming completion has no faithful named-reasoning
    /// port, so an explicit reasoning id is refused BEFORE invocation rather than silently downgraded.</summary>
    public static Task<string> Complete(IModelProvider provider, OllamaChatRequest request, CancellationToken token)
    {
        if (!TryReasoningId(request, out var id)) return provider.CompleteAsync(request, token);
        throw new UnsupportedCapabilityException(
            "This provider cannot carry a named reasoning level faithfully on a non-streaming completion.", id);
    }

    /// <summary>Executes a typed tool turn, routing an explicit reasoning id through the named-reasoning port.</summary>
    public static Task<OllamaToolResponse> Tools(IModelProvider provider, OllamaToolRequest request, CancellationToken token)
    {
        if (!TryReasoningId(request, out var id)) return provider.ChatWithToolsAsync(request, token);
        return provider is INamedReasoningProvider capable
            ? capable.ChatWithToolsWithReasoningAsync(request, id, token)
            : throw new UnsupportedCapabilityException(
                "This provider cannot carry a named reasoning level faithfully.", id);
    }
}
