#if !ANDROID
using Haven.Application;
using HavenOS.Apps.Assistants.Canonical;

namespace Haven.Desktop.Services;

internal sealed partial class OriginalAssistantGeneratedUiHost
{
    private OriginalAssistantGeneratedUiOriginOwner? _interactionOrigins;
    internal bool HasOriginalOriginComposition(DenAssistantCanonicalBridge sameBridge, GenUiInstanceStore sameInstances) =>
        ReferenceEquals(_controller.OriginalCanonicalBridge, sameBridge) && ReferenceEquals(_instances, sameInstances);
    internal void BindOriginalInteractionOrigins(OriginalAssistantGeneratedUiOriginOwner actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        _work.RunSynchronous(original =>
        {
            if (_interactionOrigins is not null || _mounts.Count != 0 ||
                _controller.OriginalCanonicalBridge is not DenAssistantCanonicalBridge bridge)
                throw new InvalidOperationException("Bind the SAME configured process origin before this host publishes any generated controls.");
            actual.BindOriginalPublisher(this, bridge);
            _interactionOrigins = actual;
        });
    }
}
#endif
