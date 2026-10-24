using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private readonly IProviderSecretStore? _manualRecoveryKeys;
    private readonly IAppPaths? _manualRecoveryPaths;
    private DeveloperDraftRecoveryScope? _manualRecoveryScope;
    private long _manualRecoveryGeneration, _manualRecoverySerial, _manualRecoveryDriverId;
    private bool _manualRecoveryReady, _manualRecoveryBlocked;
    private DeveloperDraftRecoveryPayload? _manualRecoveryRequested, _manualRecoveryAcknowledged;
    private Task? _manualRecoveryDriver;
    private static readonly TimeSpan ManualRecoveryQuietPeriod = TimeSpan.FromMilliseconds(600);

    private DeveloperDraftRecoveryPayload CaptureManualRecoveryPayload() => new(
        Array.AsReadOnly(_sourceTabs.Select(tab =>
        {
            var state = tab.Manual ?? throw new InvalidOperationException("A manual recovery tab must belong to the manual source owner.");
            return new DeveloperDraftRecoveryTab(tab.Path, state.BeforeSha256, state.Draft, tab.IsPinned);
        }).ToArray()), CurrentSourceTab()?.Path);

    internal static bool SameManualRecoveryPayload(DeveloperDraftRecoveryPayload? left, DeveloperDraftRecoveryPayload? right) =>
        left is not null && right is not null && StringComparer.Ordinal.Equals(left.SelectedRelativePath, right.SelectedRelativePath) &&
        left.Tabs.SequenceEqual(right.Tabs);

    private bool HasExactProtectedManualRecovery() => _manual && _manualRecoveryReady && !_manualRecoveryBlocked &&
        _manualRecoveryCandidates.Count == 0 && SameManualRecoveryPayload(_manualRecoveryAcknowledged, CaptureManualRecoveryPayload());

    private async Task RestoreManualRecoveryAsync(DesktopOriginalWorkLifetime.Original original,
        DeveloperResolvedProject project, CancellationToken token)
    {
        lock (_gate)
        {
            if (!_manual || _manualRecoveryReady || _manualRecoveryBlocked || _manualReader is null ||
                _editorSessionStore is null || _editorSessionExecution is null || _manualRecoveryKeys is null || _manualRecoveryPaths is null) return;
        }
        var observed = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.RestoreManualDraftRecoveryAsync(
            _editorSessionStore!, _manualRecoveryKeys!, _manualRecoveryPaths!, project, token))).ConfigureAwait(false);
        await PublishAsync(original, () =>
        {
            if (!observed.Succeeded || observed.Value is not { } restored)
            {
                _manualRecoveryBlocked = true;
                _status = observed.Error?.Message ?? "Protected recovery is unavailable; live drafts still veto close.";
                return;
            }
            StoreSelectedTab();
            if (_sourceTabs.Any(IsTabDirty))
            {
                _manualRecoveryBlocked = true;
                _status = "Live authored drafts appeared during recovery and were preserved. No saved draft was substituted.";
                return;
            }
            _manualRecoveryScope = restored.Scope; _manualRecoveryGeneration = restored.Generation;
            _manualRecoveryReady = true;
            if (!restored.HasSavedSnapshot) return;
            if (restored.Tabs.Any(value => !value.BaselineMatches))
            {
                StageManualRecoveryConflicts(restored);
                _status = "Saved source changed while drafts were closed. Your protected drafts are preserved; inspect and explicitly reconcile them against current source.";
                return;
            }
            // The original source owner already verified every baseline under the SAME actual
            // Home/Files/kernel read. These are unreviewed drafts, with no approval/change set.
            var prepared = restored.Tabs.Select(value => new SourceTab(Guid.NewGuid(), value.Draft.RelativePath, null,
                new(value.Draft.DraftText, value.FreshBaselineText, value.FreshBaselineText, "",
                    value.Draft.OriginalSha256, "", 1, -1, false)) { IsPinned = value.Draft.IsPinned }).ToArray();
            _sourceTabs.Clear(); _sourceTabs.AddRange(prepared);
            _selectedSourceTab = null; _editor = null; _pass = null; _sourcePath = "";
            _manualDraftText = ""; _manualDraftBaseline = ""; _manualBeforeText = ""; _manualAfterText = "";
            _manualBeforeSha256 = ""; _manualAfterSha256 = ""; _manualReviewed = false; _manualReviewRevision = -1;
            if (prepared.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.Path, restored.SelectedRelativePath)) is { } selected)
                SelectTab(selected);
            _manualDraftRevision++;
            _manualRecoveryRequested = CaptureManualRecoveryPayload();
            _manualRecoveryAcknowledged = _manualRecoveryRequested;
            _status = $"Recovered {prepared.Count(IsTabDirty)} unsaved drafts from protected storage. Review them before applying source changes.";
        }).ConfigureAwait(false);
    }

    /// <summary>One page-owned coalesced driver, never one unobserved task/timer per keypress.
    /// Later typing replaces the requested immutable payload; an older physical save only
    /// acknowledges its own captured payload. Business work retains its original token.</summary>
    private void ScheduleManualRecoverySave(bool forceCompleteSnapshot = false)
    {
        long generation;
        lock (_gate)
        {
            if (!_manual || !_manualRecoveryReady || _manualRecoveryBlocked || _manualRecoveryCandidates.Count != 0 || _manualRecoveryScope is null ||
                _editorSessionStore is null || _editorSessionExecution is null || _manualReader is null ||
                _manualRecoveryKeys is null || _manualRecoveryPaths is null || _work.IsRetiring) return;
            StoreSelectedTab();
            // Do not generate a protected key/copy clean source merely because a project was
            // opened. Once recovery exists, later clean/empty snapshots replace obsolete drafts.
            if (_manualRecoveryAcknowledged is null && !_sourceTabs.Any(IsTabDirty) && !forceCompleteSnapshot) return;
            var payload = CaptureManualRecoveryPayload();
            if (SameManualRecoveryPayload(payload, _manualRecoveryRequested)) return;
            _manualRecoveryRequested = payload; _manualRecoverySerial++;
            if (_manualRecoveryDriver is not null) return;
            generation = _generation;
            var driverId = ++_manualRecoveryDriverId;
            _work.RunAsync(original => DriveManualRecoveryAsync(original, generation, driverId),
                actual => _manualRecoveryDriver = actual); // Actual Task retained before its driver opens.
        }
    }

    private async Task DriveManualRecoveryAsync(DesktopOriginalWorkLifetime.Original original, long generation, long driverId)
    {
        original.BindPublicationGuard(() => IsCurrent(generation));
        try
        {
            while (true)
            {
                long serial;
                lock (_gate) serial = _manualRecoverySerial;
                // This finite presentation delay uses no business cancellation token. Retirement
                // joins it and the already-admitted final snapshot instead of discarding drafts.
                await original.AwaitAsync(Task.Delay(ManualRecoveryQuietPeriod)).ConfigureAwait(false);
                DeveloperDraftRecoveryPayload? payload = null;
                DeveloperResolvedProject? project = null;
                DeveloperDraftRecoveryScope? scope = null;
                long expected = 0;
                while (true)
                {
                    bool busy;
                    lock (_gate)
                    {
                        if (_manualRecoveryBlocked || !_manualRecoveryReady || _manualRecoveryCandidates.Count != 0) return;
                        if (serial != _manualRecoverySerial) break; // Restart quiet period around the newer payload.
                        busy = _pending;
                        if (!busy)
                        {
                            _pending = true; // Same page command fence; typing remains available.
                            payload = _manualRecoveryRequested; project = _project;
                            scope = _manualRecoveryScope; expected = _manualRecoveryGeneration;
                        }
                    }
                    if (!busy) break;
                    await original.AwaitAsync(Task.Delay(TimeSpan.FromMilliseconds(50))).ConfigureAwait(false);
                }
                if (payload is null || project is null || scope is null) continue;
                try
                {
                    var saved = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.SaveManualDraftRecoveryAsync(
                        _editorSessionStore!, _editorSessionExecution!, _manualRecoveryKeys!, _manualRecoveryPaths!,
                        project, scope, expected, payload, _businessToken))).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (saved.Succeeded)
                        {
                            _manualRecoveryGeneration = saved.Value!.Generation;
                            _manualRecoveryAcknowledged = payload; // Never overwrite newer typed tab text.
                        }
                        else _manualRecoveryBlocked = true;
                    }
                    await PublishAsync(original, () =>
                    {
                        _status = saved.Succeeded
                            ? "Protected recovery saved. Only the exact acknowledged drafts can close safely; source changes still require review."
                            : saved.Error!.Message;
                    }).ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate) _pending = false;
                    await PublishAsync(original, () => { }).ConfigureAwait(false);
                }
                lock (_gate)
                {
                    if (_manualRecoveryBlocked || serial == _manualRecoverySerial)
                    {
                        // Release this exact idle slot under the SAME request gate. A keystroke
                        // arriving after this decision starts its own retained driver; the older
                        // finally cannot erase that successor's custody.
                        if (_manualRecoveryDriverId == driverId) _manualRecoveryDriver = null;
                        return;
                    }
                }
            }
        }
        catch
        {
            lock (_gate) _manualRecoveryBlocked = true; // Never automatically replay an unknown source/key outcome.
            throw;
        }
        finally
        {
            lock (_gate) if (_manualRecoveryDriverId == driverId) _manualRecoveryDriver = null;
        }
    }
}
