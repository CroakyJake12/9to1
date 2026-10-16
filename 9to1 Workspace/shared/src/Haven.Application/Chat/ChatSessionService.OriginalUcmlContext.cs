using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private async Task<IChatOriginalUcmlContext?> ReadOriginalUcmlContextAsync(GenerationOptions? options,
        Conversation conversation, string prompt, ChatOrdinaryOriginalInvocation? ordinary,
        CancellationToken token)
    {
        // Ordinary Assistant Chat has a live Home-issued binding. Canonical Task cold context
        // capture/replay needs its own original inventory integration and receives no UCML here.
        if (ordinary is null || conversation.IsTemporary || conversation.IsArchived ||
            options?.RequestedContextConstraints?.AllowPersistentMemoryRead != true ||
            options.RequestedRoutingConstraints?.AllowCloud != false ||
            options.OriginalPersistentMemoryInput is not { } input ||
            OriginalPersistentMemorySource is not IChatOriginalUcmlContextSource owner ||
            ollama is not IOriginalPrivateContextModelClient) return null;
        DemandOriginalPersistentMemoryRequest(options, ordinary, null);
        // At most 512 candidate records, including each authenticated decrypt/read and cleanup.
        // Every child still joins independently; this is a finite custody budget, not a waiver.
        var source = new OriginalMemorySourceScope(ordinary, null, maximumChildren: 4096);
        return await source.Read(() => owner.SelectOriginalUcmlContextWithinSourceAsync(input,
            conversation, prompt, source.Run, source.Retain, token)).ConfigureAwait(false);
    }

    private sealed class OriginalUcmlDispatchGuard(IChatOriginalUcmlContext selection,
        ChatOrdinaryOriginalInvocation ordinary) : IOriginalPrivateContextDispatchGuard
    {
        public Task ValidateAsync(bool actualProviderIsLocal, CancellationToken token)
        {
            if (!actualProviderIsLocal) throw new UnauthorizedAccessException("UCML disclosure is restricted to a local model.");
            var source = new OriginalMemorySourceScope(ordinary, null, maximumChildren: 4096);
            return source.Read(() => selection.ValidateOriginalWithinSourceAsync(source.Run, source.Retain, token));
        }
    }
}
