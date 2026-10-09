using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Migration;

public sealed partial class LegacyAgentMigrationController : ILegacyAgentMigrationImportController
{
    private readonly HomeOriginalLocalStoreImportSession? _imports;
    private LegacyAgentImportPreview? _currentImportPreview;
    private sealed record OriginalImportPreview(HomePersonalDenSession Home,
        HomeOriginalLocalStoreImportSession.Snapshot Snapshot);
    private enum ImportAction { Request, Refresh, Complete, RetryAudit }

    public Task<LegacyAgentImportPreview> InspectImportAsync(CancellationToken token = default) => _originals.Admit(async () =>
    {
        if (_imports is null)
            return _currentImportPreview = new(_issuer, new object(), null, LegacyAgentImportState.Unavailable,
                "The configured Home import session is unavailable. Reopen Assistants after Home storage setup is ready.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        var observed = await _originals.Source(() => _imports.InspectWithinOriginalSourceAsync(
            _originals.Run, RetainOriginalImportSource, token)).ConfigureAwait(false);
        return await PublishImportAsync(home, observed, token).ConfigureAwait(false);
    });

    public Task<LegacyAgentImportPreview> RequestImportAsync(LegacyAgentImportPreview samePreview,
        CancellationToken token = default) => ActOnImportAsync(samePreview, ImportAction.Request, token);
    public Task<LegacyAgentImportPreview> RefreshImportAsync(LegacyAgentImportPreview samePreview,
        CancellationToken token = default) => ActOnImportAsync(samePreview, ImportAction.Refresh, token);
    public Task<LegacyAgentImportPreview> CompleteImportAsync(LegacyAgentImportPreview samePreview,
        CancellationToken token = default) => ActOnImportAsync(samePreview, ImportAction.Complete, token);
    public Task<LegacyAgentImportPreview> RetryImportAuditAsync(LegacyAgentImportPreview samePreview,
        CancellationToken token = default) => ActOnImportAsync(samePreview, ImportAction.RetryAudit, token);

    private Task<LegacyAgentImportPreview> ActOnImportAsync(LegacyAgentImportPreview samePreview,
        ImportAction action, CancellationToken token) => _originals.Admit(async () =>
    {
        var original = DemandImportPreview(samePreview);
        var home = await OpenHomeAsync(token).ConfigureAwait(false); DemandSameHome(original.Home, home);
        // Another presentation can finish this SAME app-owned request while this view is open.
        // Re-read the actual session; an outdated review returns its fresh observation without
        // silently starting or completing a different request.
        var current = await _originals.Source(() => _imports!.RefreshWithinOriginalSourceAsync(
            _originals.Run, RetainOriginalImportSource, token)).ConfigureAwait(false);
        DemandIssuedImport(current);
        if (action == ImportAction.Refresh || !SameImportReview(original.Snapshot, current))
            return await PublishImportAsync(home, current, token).ConfigureAwait(false);
        var admitted = action switch
        {
            ImportAction.Request => current.CanRequest,
            ImportAction.Complete => current.CanComplete,
            ImportAction.RetryAudit => current.CanRetryAudit,
            _ => false
        };
        if (!admitted) return await PublishImportAsync(home, current, token).ConfigureAwait(false);
        await RecheckHomeAsync(home, token).ConfigureAwait(false);
        _originals.EffectStarting();
        var settled = await _originals.Source(() => action switch
        {
            ImportAction.Request => _imports!.RequestImportWithinOriginalSourceAsync(_originals.Run, RetainOriginalImportSource, token),
            ImportAction.Complete => _imports!.CompleteImportWithinOriginalSourceAsync(_originals.Run, RetainOriginalImportSource, token),
            ImportAction.RetryAudit => _imports!.RetryAuditWithinOriginalSourceAsync(_originals.Run, RetainOriginalImportSource, token),
            _ => throw new InvalidOperationException("Unknown original import operation.")
        }).ConfigureAwait(false);
        return await PublishImportAsync(home, settled, token).ConfigureAwait(false);
    });

    private OriginalImportPreview DemandImportPreview(LegacyAgentImportPreview actual)
    {
        if (_imports is null || actual is null || !ReferenceEquals(actual.Issuer, _issuer) ||
            !ReferenceEquals(actual, _currentImportPreview) || actual.Original is not OriginalImportPreview original ||
            !_imports.IsIssuedOriginalSnapshot(original.Snapshot))
            throw _originals.Refuse("ForeignImportPreview", "Review the current configured store in this migration presentation first.");
        return original;
    }
    private void RetainOriginalImportSource(Task actual) => _originals.RetainImportSource(actual,
        _imports ?? throw new InvalidOperationException("The original Home import session is unavailable."));
    private void DemandIssuedImport(HomeOriginalLocalStoreImportSession.Snapshot actual)
    {
        if (_imports?.IsIssuedOriginalSnapshot(actual) != true)
            throw new InvalidOperationException("Home returned an import observation from another source.");
    }
    private static bool SameImportReview(HomeOriginalLocalStoreImportSession.Snapshot first,
        HomeOriginalLocalStoreImportSession.Snapshot second) => first.StoreId == second.StoreId &&
        first.State == second.State && first.RequestId == second.RequestId;

    private async Task<LegacyAgentImportPreview> PublishImportAsync(HomePersonalDenSession home,
        HomeOriginalLocalStoreImportSession.Snapshot actual, CancellationToken token)
    {
        DemandIssuedImport(actual);
        await RecheckHomeAsync(home, token).ConfigureAwait(false);
        if (actual.CanBrowse)
        {
            // The Home observation is not a content grant. Independently revalidate the
            // actual source and per-kind receipt without reading any legacy content.
            var storeId = await _originals.Source(() => _source.ReadOwnedAsync(home.Actor, _ownership, false,
                source => Task.FromResult(source.Identity.StoreId), _originals.Run, _originals.Retain, token)).ConfigureAwait(false);
            if (actual.StoreId != storeId)
                throw new InvalidOperationException("The imported store is not this actual legacy source.");
            await RecheckHomeAsync(home, token).ConfigureAwait(false);
        }
        var state = actual.State switch
        {
            HomeOriginalLocalStoreImportState.Unavailable => LegacyAgentImportState.Unavailable,
            HomeOriginalLocalStoreImportState.RequiresReview => LegacyAgentImportState.RequiresReview,
            HomeOriginalLocalStoreImportState.PendingApproval => LegacyAgentImportState.PendingApproval,
            HomeOriginalLocalStoreImportState.Declined => LegacyAgentImportState.Declined,
            HomeOriginalLocalStoreImportState.Approved => LegacyAgentImportState.Approved,
            HomeOriginalLocalStoreImportState.Imported => LegacyAgentImportState.Imported,
            HomeOriginalLocalStoreImportState.AuditPending => LegacyAgentImportState.AuditPending,
            HomeOriginalLocalStoreImportState.OutcomeUnconfirmed => LegacyAgentImportState.OutcomeUnconfirmed,
            _ => throw new InvalidOperationException("The actual Home import state is unsupported.")
        };
        return _currentImportPreview = new(_issuer, new OriginalImportPreview(home, actual), actual.StoreId,
            state, actual.Reason, actual.RequestId);
    }
}
