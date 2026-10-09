namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private async Task RefreshOriginalConversationModelsAsync(CancellationToken token)
    {
        // Publish the actual accepted conversation before querying its configured
        // model owner. The catalogue itself is an observation, never a send grant.
        if (IsRetiring) return;
        PublishSynchronous(() =>
        {
            var current = _controller.Snapshot;
            if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
        });
        var binding = _binding;
        var generation = PresentationGeneration;
        if (binding is null || !IsPresentationCurrent(binding, generation)) return;
        await SourceAsync(() => _controller.ListAvailableModelsAsync(token));
        if (!IsPresentationCurrent(binding, generation)) return;
        PublishSynchronous(() =>
        {
            if (!IsPresentationCurrent(binding, generation)) return;
            var current = _controller.Snapshot;
            if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
        });
    }
}
