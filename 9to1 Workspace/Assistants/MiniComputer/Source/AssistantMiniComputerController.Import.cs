using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.MiniComputer;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed partial class AssistantMiniComputerController : IAssistantMiniComputerImportController
{
    private AssistantMiniComputerImportPreview? _currentImport;
    private long _importGeneration;
    public Task<AssistantMiniComputerImportPreview> InspectImportAsync(AssistantConversationBinding binding,
        CancellationToken token = default) => ImportCoreAsync(binding, null, null, token);
    public Task<AssistantMiniComputerImportPreview> ImportAsync(AssistantMiniComputerImportPreview samePreview,
        AssistantMiniComputerImportAction action, CancellationToken token = default) =>
        ImportCoreAsync(samePreview.Binding, samePreview, action, token);
    private Task<AssistantMiniComputerImportPreview> ImportCoreAsync(AssistantConversationBinding binding,
        AssistantMiniComputerImportPreview? prior, AssistantMiniComputerImportAction? action, CancellationToken token) =>
        _originals.Run(body => body(), _ => { }, async scope =>
        {
            var generation = Interlocked.Increment(ref _importGeneration);
            var session = _source.OriginalImportSession;
            if (!ReferenceEquals(_workspace.Snapshot.ConversationBinding, binding) || session is null)
                return Unavailable(binding, "Open the actual current Assistant conversation and configured Home import source first.");
            if (action is not null && (prior is null || !ReferenceEquals(prior, _currentImport) ||
                !ReferenceEquals(prior.Issuer, _issuer) || prior.Original is not HomeOriginalLocalStoreImportSession.Snapshot issued ||
                !session.IsIssuedOriginalSnapshot(issued) || !Allowed(prior, action.Value)))
                return Unavailable(binding, "Review this presentation's SAME current catalogue import before taking that action.");
            var refusal = await scope.Read(() => _source.ValidateOriginalImportBindingAsync(binding,
                scope.Run, scope.Retain, token)).ConfigureAwait(false);
            if (refusal is not null) return Publish(Unavailable(binding, refusal));
            var raw = new List<Task>();
            void Retain(Task actual)
            {
                // Capture globally before caller publication. A pending audit may be
                // displayed while its actual failed source still bars owner retirement.
                _originals.RetainOriginalHomeImportSource(actual, session); raw.Add(actual); scope.Retain(actual);
            }
            var snapshot = await scope.Read(() => action switch
            {
                null => session.InspectWithinOriginalSourceAsync(scope.Run, Retain, token),
                AssistantMiniComputerImportAction.Request => session.RequestImportWithinOriginalSourceAsync(scope.Run, Retain, token),
                AssistantMiniComputerImportAction.Refresh => session.RefreshWithinOriginalSourceAsync(scope.Run, Retain, token),
                AssistantMiniComputerImportAction.Complete => session.CompleteImportWithinOriginalSourceAsync(scope.Run, Retain, token),
                AssistantMiniComputerImportAction.RetryAudit => session.RetryAuditWithinOriginalSourceAsync(scope.Run, Retain, token),
                _ => throw new InvalidOperationException("No original import operation was selected.")
            }).ConfigureAwait(false);
            foreach (var actual in raw) scope.AcknowledgeOriginalRefusal(actual,
                task => session.IsAcknowledgedOriginalSource(task) || session.IsOwnedOriginalPendingSource(task, snapshot));
            refusal = await scope.Read(() => _source.ValidateOriginalImportBindingAsync(binding,
                scope.Run, scope.Retain, token)).ConfigureAwait(false);
            if (refusal is not null) return Publish(Unavailable(binding, refusal));
            return Publish(new(_issuer, binding, snapshot, snapshot.State.ToString(), snapshot.Reason, snapshot.RequestId,
                snapshot.CanRequest, snapshot.CanComplete, snapshot.CanRetryAudit, snapshot.CanBrowse));
            AssistantMiniComputerImportPreview Publish(AssistantMiniComputerImportPreview value)
            {
                if (generation == Volatile.Read(ref _importGeneration) && ReferenceEquals(_workspace.Snapshot.ConversationBinding, binding))
                    _currentImport = value;
                return value;
            }
        });
    private AssistantMiniComputerImportPreview Unavailable(AssistantConversationBinding binding, string reason) =>
        new(_issuer, binding, null, "Unavailable", reason, null, false, false, false, false);
    private static bool Allowed(AssistantMiniComputerImportPreview prior, AssistantMiniComputerImportAction action) => action switch
    {
        AssistantMiniComputerImportAction.Request => prior.CanRequest,
        AssistantMiniComputerImportAction.Complete => prior.CanComplete,
        AssistantMiniComputerImportAction.RetryAudit => prior.CanRetryAudit,
        AssistantMiniComputerImportAction.Refresh => true,
        _ => false
    };
}
