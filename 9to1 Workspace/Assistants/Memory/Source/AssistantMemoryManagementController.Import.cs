using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantMemoryManagementController : IAssistantMemoryImportManagementController
{
    private readonly object _importViewGate = new();
    private long _importRequestVersion;
    private AssistantMemoryImportPreview? _currentImport;
    private sealed record OriginalMemoryImportReview(AssistantConversationBinding Binding,
        HomeOriginalLocalStoreImportSession Session, HomeOriginalLocalStoreImportSession.Snapshot Snapshot);
    private enum ImportAction { Request, Refresh, Complete, RetryAudit }
    public bool HasOriginalImportSession => _source.OriginalMemoryImportSession is not null;

    public Task<AssistantMemoryImportPreview> InspectImportAsync(AssistantConversationBinding binding,
        CancellationToken token = default) => RunImport(async (scope, session, version) =>
    {
        if (session is null) return PublishUnavailableImport(version, "The configured Home memory import owner is unavailable.");
        var reason = await scope.Read(() => _source.ValidateOriginalMemoryImportBindingAsync(
            binding, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        if (reason is not null) return PublishUnavailableImport(version, reason);
        var observed = await scope.Read(() => session.InspectWithinOriginalSourceAsync(scope.Run,
            actual => scope.RetainOriginalHomeImportSource(actual, session), token)).ConfigureAwait(false);
        return await PublishOriginalImportAsync(binding, session, observed, version, scope, token).ConfigureAwait(false);
    });
    public Task<AssistantMemoryImportPreview> RequestImportAsync(AssistantMemoryImportPreview preview,
        CancellationToken token = default) => ActOnImportAsync(preview, ImportAction.Request, token);
    public Task<AssistantMemoryImportPreview> RefreshImportAsync(AssistantMemoryImportPreview preview,
        CancellationToken token = default) => ActOnImportAsync(preview, ImportAction.Refresh, token);
    public Task<AssistantMemoryImportPreview> CompleteImportAsync(AssistantMemoryImportPreview preview,
        CancellationToken token = default) => ActOnImportAsync(preview, ImportAction.Complete, token);
    public Task<AssistantMemoryImportPreview> RetryImportAuditAsync(AssistantMemoryImportPreview preview,
        CancellationToken token = default) => ActOnImportAsync(preview, ImportAction.RetryAudit, token);

    private Task<AssistantMemoryImportPreview> ActOnImportAsync(AssistantMemoryImportPreview preview,
        ImportAction action, CancellationToken token) => RunImport(async (scope, session, version) =>
    {
        OriginalMemoryImportReview? original;
        lock (_importViewGate)
        {
            original = preview is not null && ReferenceEquals(preview.Issuer, _issuer) &&
                ReferenceEquals(preview, _currentImport) ? preview.Original as OriginalMemoryImportReview : null;
        }
        if (session is null || original is null || !ReferenceEquals(original.Session, session) ||
            !session.IsIssuedOriginalSnapshot(original.Snapshot))
            return PublishUnavailableImport(version, "Review this presentation's current configured memory store before requesting import.");
        var reason = await scope.Read(() => _source.ValidateOriginalMemoryImportBindingAsync(
            original.Binding, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        if (reason is not null) return PublishUnavailableImport(version, reason);
        var current = await scope.Read(() => session.RefreshWithinOriginalSourceAsync(scope.Run,
            actual => scope.RetainOriginalHomeImportSource(actual, session), token)).ConfigureAwait(false);
        DemandOriginalSnapshot(session, current);
        var sameReview = current.StoreId == original.Snapshot.StoreId && current.State == original.Snapshot.State &&
            current.RequestId == original.Snapshot.RequestId;
        var admitted = sameReview && (action switch
        {
            ImportAction.Request => current.CanRequest,
            ImportAction.Complete => current.CanComplete,
            ImportAction.RetryAudit => current.CanRetryAudit,
            _ => false
        });
        if (!admitted) return await PublishOriginalImportAsync(original.Binding, session, current, version, scope, token).ConfigureAwait(false);
        // A fresh observation of a changed review is returned above; never silently
        // request, approve or complete another operation from a stale view.
        var settled = await scope.Read(() => action switch
        {
            ImportAction.Request => session.RequestImportWithinOriginalSourceAsync(scope.Run,
                actual => scope.RetainOriginalHomeImportSource(actual, session), token),
            ImportAction.Complete => session.CompleteImportWithinOriginalSourceAsync(scope.Run,
                actual => scope.RetainOriginalHomeImportSource(actual, session), token),
            ImportAction.RetryAudit => session.RetryAuditWithinOriginalSourceAsync(scope.Run,
                actual => scope.RetainOriginalHomeImportSource(actual, session), token),
            _ => throw new InvalidOperationException("Unsupported original memory import action.")
        }).ConfigureAwait(false);
        return await PublishOriginalImportAsync(original.Binding, session, settled, version, scope, token).ConfigureAwait(false);
    });

    private async Task<AssistantMemoryImportPreview> PublishOriginalImportAsync(AssistantConversationBinding binding,
        HomeOriginalLocalStoreImportSession session, HomeOriginalLocalStoreImportSession.Snapshot observed,
        long version, AssistantMemoryOriginals.Scope scope, CancellationToken token)
    {
        DemandOriginalSnapshot(session, observed);
        var reason = await scope.Read(() => _source.ValidateOriginalMemoryImportBindingAsync(
            binding, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        if (reason is not null && observed.State is not (HomeOriginalLocalStoreImportState.AuditPending or HomeOriginalLocalStoreImportState.OutcomeUnconfirmed))
            return PublishUnavailableImport(version, reason);
        var state = observed.State switch
        {
            HomeOriginalLocalStoreImportState.Unavailable => AssistantMemoryImportState.Unavailable,
            HomeOriginalLocalStoreImportState.RequiresReview => AssistantMemoryImportState.RequiresReview,
            HomeOriginalLocalStoreImportState.PendingApproval => AssistantMemoryImportState.PendingApproval,
            HomeOriginalLocalStoreImportState.Declined => AssistantMemoryImportState.Declined,
            HomeOriginalLocalStoreImportState.Approved => AssistantMemoryImportState.Approved,
            HomeOriginalLocalStoreImportState.Imported => AssistantMemoryImportState.Imported,
            HomeOriginalLocalStoreImportState.AuditPending => AssistantMemoryImportState.AuditPending,
            HomeOriginalLocalStoreImportState.OutcomeUnconfirmed => AssistantMemoryImportState.OutcomeUnconfirmed,
            _ => throw new InvalidOperationException("The actual Home memory import state is unsupported.")
        };
        var result = new AssistantMemoryImportPreview(_issuer, new OriginalMemoryImportReview(binding, session, observed),
            observed.StoreId, state, observed.Reason, observed.RequestId);
        lock (_importViewGate) if (version == _importRequestVersion) _currentImport = result;
        return result;
    }
    private AssistantMemoryImportPreview PublishUnavailableImport(long version, string reason)
    {
        var result = new AssistantMemoryImportPreview(_issuer, new object(), null, AssistantMemoryImportState.Unavailable, reason);
        lock (_importViewGate) if (version == _importRequestVersion) _currentImport = result;
        return result;
    }
    private static void DemandOriginalSnapshot(HomeOriginalLocalStoreImportSession session, HomeOriginalLocalStoreImportSession.Snapshot observed)
    {
        if (!session.IsIssuedOriginalSnapshot(observed))
            throw new InvalidOperationException("The SAME original Home memory import session must issue this observation.");
    }
    private Task<AssistantMemoryImportPreview> RunImport(Func<AssistantMemoryOriginals.Scope,
        HomeOriginalLocalStoreImportSession?, long, Task<AssistantMemoryImportPreview>> body) => _originals.Admit(async () =>
    {
        long version; lock (_importViewGate) version = ++_importRequestVersion;
        var scope = _originals.CreateScope(action => action(), _ => { });
        var session = _source.OriginalMemoryImportSession;
        AssistantMemoryImportPreview? result = null; var errors = new List<Exception>();
        try { result = await body(scope, session, version).ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
        var pending = result?.Original is OriginalMemoryImportReview review &&
            review.Snapshot.State == HomeOriginalLocalStoreImportState.AuditPending ? review.Snapshot : null;
        try { await scope.JoinAsync(session, pending).ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
        AssistantMemoryOriginals.Throw(errors);
        return result ?? throw new InvalidOperationException("No actual memory import observation was returned.");
    });
}
