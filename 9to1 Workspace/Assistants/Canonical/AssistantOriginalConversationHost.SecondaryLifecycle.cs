using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class AssistantOriginalConversationHost
{
    // This application-scoped fence serializes the final canonical ordinary send
    // admission and ordinary secondary archive across Assistant presentations.
    // It is never held while driving the provider or joining work.
    private readonly SemaphoreSlim _secondaryLifecycleFence = new(1, 1);

    internal async Task<T> WithSecondaryLifecycleFenceAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await _secondaryLifecycleFence.WaitAsync(token).ConfigureAwait(false);
        try { return await action().ConfigureAwait(false); }
        finally { _secondaryLifecycleFence.Release(); }
    }

    internal bool IsSuccessfullySettledForSecondaryLifecycle(Guid conversationId)
    {
        lock (_gate)
        {
            if (_retired) throw new ObjectDisposedException(nameof(AssistantOriginalConversationHost));
            return !_active.TryGetValue(conversationId, out var driver) || driver.IsCompletedSuccessfully;
        }
    }
}
