using System.Runtime.CompilerServices;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed partial class AssistantMiniComputerController : IAssistantMiniComputerIdentitySetupController
{
    private AssistantMiniComputerIdentitySetupPreview? _currentIdentitySetup;
    private long _identitySetupGeneration;
    private readonly object _identitySetupGate = new();
    private readonly ConditionalWeakTable<AssistantMiniComputerIdentitySetupPreview, Task<AssistantMiniComputerIdentitySetupResult>> _identitySetupExecutions = new();
    public Task<AssistantMiniComputerIdentitySetupPreview> PrepareIdentitySetupAsync(AssistantConversationBinding binding,
        Guid operationId, CancellationToken token = default) => _originals.Run(body => body(), _ => { }, async source =>
    {
        var generation = Interlocked.Increment(ref _identitySetupGeneration);
        if (!ReferenceEquals(_workspace.Snapshot.ConversationBinding, binding))
            return new AssistantMiniComputerIdentitySetupPreview(_issuer, binding, null, "Open the current Assistant conversation before reviewing catalogue setup.");
        var prepared = await source.Read(() => _source.PrepareIdentitySetupWithinSourceAsync(binding, operationId,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var value = new AssistantMiniComputerIdentitySetupPreview(_issuer, binding, prepared.Intent, prepared.Reason);
        if (generation == Volatile.Read(ref _identitySetupGeneration) && ReferenceEquals(_workspace.Snapshot.ConversationBinding, binding))
            lock (_identitySetupGate) _currentIdentitySetup = value;
        return value;
    });
    public Task<AssistantMiniComputerIdentitySetupResult> ExecuteIdentitySetupAsync(AssistantMiniComputerIdentitySetupPreview preview,
        CancellationToken token = default)
    {
        lock (_identitySetupGate)
        {
            if (_identitySetupExecutions.TryGetValue(preview, out var prior)) return prior;
            var actual = _originals.Run(body => body(), _ => { }, async source =>
            {
                if (!ReferenceEquals(preview, _currentIdentitySetup) || !ReferenceEquals(preview.Issuer, _issuer) ||
                    preview.Intent is null || !ReferenceEquals(_workspace.Snapshot.ConversationBinding, preview.Binding))
                    return new AssistantMiniComputerIdentitySetupResult(false, "Review this presentation’s SAME current catalogue setup preview first.");
                return await source.Read(() => _source.ExecuteIdentitySetupWithinSourceAsync(preview.Binding, preview.Intent,
                    source.Run, source.Retain, token)).ConfigureAwait(false);
            });
            _identitySetupExecutions.Add(preview, actual); return actual;
        }
    }
    public AssistantMiniComputerPendingObservation ObserveOriginalIdentitySetup(AssistantMiniComputerIdentitySetupPreview preview) =>
        ReferenceEquals(preview.Issuer, _issuer) && preview.Intent is not null
            ? _source.ObserveIdentitySetup(preview.Intent) : new(null, "Use the original catalogue setup preview.", false);
}
