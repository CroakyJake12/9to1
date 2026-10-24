using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private readonly DeveloperTaskDraftRecoveryOwner? _taskRecoveryOwner;
    private DeveloperDraftRecoveryScope? _taskRecoveryScope;
    private DeveloperDraftRecoveryPayload? _taskRecoveryRequested, _taskRecoveryAcknowledged;
    private long _taskRecoveryGeneration, _taskRecoverySerial, _taskRecoveryDriverId;
    private bool _taskRecoveryReady, _taskRecoveryBlocked, _taskRecoveryHasSavedSnapshot;
    private Task? _taskRecoveryDriver;

    private DeveloperDraftRecoveryPayload CaptureTaskRecoveryPayload() => new(
        Array.AsReadOnly(_sourceTabs.Select(tab =>
        {
            var editor = tab.Editor ?? throw new InvalidOperationException("The actual Task source editor is missing.");
            return new DeveloperDraftRecoveryTab(tab.Path, editor.OriginalSha256, editor.DraftText, tab.IsPinned);
        }).ToArray()), CurrentSourceTab()?.Path);
    private bool HasExactProtectedTaskRecovery() => !_manual && _taskRecoveryReady && !_taskRecoveryBlocked &&
        _taskRecoveryOwner is not null && SameManualRecoveryPayload(_taskRecoveryAcknowledged, CaptureTaskRecoveryPayload());
    private Action<Action> TaskRecoveryScopeCallback => body => InvokeOriginalSource(() => { body(); return true; });

    private Task<T> RunTaskSourceAsync<T>(DesktopOriginalWorkLifetime.Original original,
        DeveloperResolvedProject project, DeveloperCanonicalActionContext exact, Func<Task<T>> factory, CancellationToken token)
    {
        // Existing non-native compositions retain their original owner; no replacement grant
        // is created. In the genuine native host the page-owned READ frame is mandatory.
        var actual = InvokeOriginalSource(() => _taskRecoveryOwner is null ? factory() :
            _taskRecoveryOwner.RunOriginalSourceAsync(project, exact, factory, TaskRecoveryScopeCallback, token));
        return original.AwaitAsync(actual);
    }

    private async Task RestoreTaskRecoveryAsync(DesktopOriginalWorkLifetime.Original original,
        DeveloperResolvedProject project, CancellationToken token)
    {
        lock (_gate)
            if (_manual || _taskRecoveryReady || _taskRecoveryBlocked || _taskRecoveryOwner is null) return;
        try
        {
            var restored = await original.AwaitAsync(InvokeOriginalSource(() => _taskRecoveryOwner.RestoreAsync(
                project, TaskRecoveryScopeCallback, token))).ConfigureAwait(false);
            await PublishAsync(original, () =>
            {
                StoreSelectedTab();
                if (_sourceTabs.Any(IsTabDirty))
                    throw new InvalidOperationException("Live authored drafts appeared during task recovery; they were preserved.");
                _taskRecoveryHasSavedSnapshot = restored.HasSavedSnapshot;
            }).ConfigureAwait(false);
            if (restored.Tabs.Any(value => !value.BaselineMatches))
            {
                await PublishAsync(original, () =>
                {
                    _taskRecoveryBlocked = true;
                    _status = "Saved source changed while this task was closed. Protected task drafts were preserved; no saved text was applied. Open current source and reconcile explicitly.";
                }).ConfigureAwait(false);
                return;
            }
            var prepared = new List<(DeveloperPreparedSourceOpen Source, DeveloperDraftRecoveryTab Draft)>();
            try
            {
                foreach (var saved in restored.Tabs)
                {
                    var context = await CaptureContextAsync(original, token).ConfigureAwait(false);
                    var document = DemandValue(await original.AwaitAsync(InvokeOriginalSource(() =>
                        _resolveDocument(project, saved.Draft.RelativePath, token))).ConfigureAwait(false));
                    var opened = DemandValue(await RunTaskSourceAsync(original, project, context,
                        () => _review!.PrepareOpenAsync(project.Reference, context, document, _businessToken), _businessToken).ConfigureAwait(false));
                    prepared.Add((opened, saved.Draft));
                    if (!StringComparer.OrdinalIgnoreCase.Equals(opened.Snapshot.OriginalSha256, saved.Draft.OriginalSha256))
                        throw new InvalidOperationException("Source changed during the genuine task recovery READ. Protected drafts remain unchanged.");
                }
                await PublishAsync(original, () =>
                {
                    StoreSelectedTab();
                    if (_sourceTabs.Count != 0)
                        throw new InvalidOperationException("Existing task source tabs were preserved; saved recovery was not substituted.");
                    // Each prepared source is genuinely read and still clean. Enrol all clean
                    // owners before publishing any recovered text; no approval/pass is restored.
                    var editors = prepared.Select(item => DemandValue(InvokeOriginalSource(() =>
                        _review!.AcceptPreparedOpen(item.Source, null)))).ToArray();
                    for (var i = 0; i < editors.Length; i++)
                    {
                        var editor = InvokeOriginalSource(() => _review!.UpdateDraft(editors[i].EditorId,
                            editors[i].DraftRevision, prepared[i].Draft.DraftText));
                        _sourceTabs.Add(new(editor.EditorId, editor.Document.RelativePath, editor, null)
                            { IsPinned = prepared[i].Draft.IsPinned });
                    }
                    if (_sourceTabs.SingleOrDefault(tab => StringComparer.Ordinal.Equals(tab.Path, restored.SelectedRelativePath)) is { } selected)
                        SelectTab(selected);
                    _taskRecoveryScope = restored.Scope; _taskRecoveryGeneration = restored.Generation;
                    _taskRecoveryReady = true;
                    if (restored.HasSavedSnapshot)
                    {
                        _taskRecoveryAcknowledged = CaptureTaskRecoveryPayload();
                        _taskRecoveryRequested = _taskRecoveryAcknowledged;
                        _status = $"Recovered {_sourceTabs.Count(IsTabDirty)} protected task drafts. Review before applying source changes.";
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                foreach (var item in prepared)
                    InvokeOriginalSource(() => { _review!.AbandonPreparedOpen(item.Source); return true; });
            }
        }
        catch (Exception cause)
        {
            lock (_gate) _taskRecoveryBlocked = true;
            await PublishAsync(original, () => _status = "Task draft recovery could not finish. Live drafts and existing protected recovery were preserved; keep unsaved work open. " + cause.Message).ConfigureAwait(false);
            throw;
        }
    }

    private void ScheduleTaskRecoverySave()
    {
        long generation;
        lock (_gate)
        {
            if (_manual || !_taskRecoveryReady || _taskRecoveryBlocked || _taskRecoveryOwner is null ||
                _taskRecoveryScope is null || _work.IsRetiring) return;
            StoreSelectedTab();
            if (_taskRecoveryAcknowledged is null && !_sourceTabs.Any(IsTabDirty)) return;
            var payload = CaptureTaskRecoveryPayload();
            if (SameManualRecoveryPayload(payload, _taskRecoveryRequested)) return;
            _taskRecoveryRequested = payload; _taskRecoverySerial++;
            if (_taskRecoveryDriver is not null) return;
            generation = _generation; var id = ++_taskRecoveryDriverId;
            _work.RunAsync(original => DriveTaskRecoveryAsync(original, generation, id),
                actual => _taskRecoveryDriver = actual);
        }
    }
    private async Task DriveTaskRecoveryAsync(DesktopOriginalWorkLifetime.Original original, long generation, long id)
    {
        original.BindPublicationGuard(() => IsCurrent(generation));
        try
        {
            while (true)
            {
                long serial; lock (_gate) serial = _taskRecoverySerial;
                await original.AwaitAsync(Task.Delay(ManualRecoveryQuietPeriod)).ConfigureAwait(false);
                DeveloperDraftRecoveryPayload? payload = null; DeveloperResolvedProject? project = null;
                DeveloperDraftRecoveryScope? partition = null; long expected = 0;
                while (true)
                {
                    bool busy;
                    lock (_gate)
                    {
                        if (_taskRecoveryBlocked || !_taskRecoveryReady) return;
                        if (serial != _taskRecoverySerial) break;
                        busy = _pending;
                        if (!busy)
                        {
                            _pending = true; payload = _taskRecoveryRequested; project = _project;
                            partition = _taskRecoveryScope; expected = _taskRecoveryGeneration;
                        }
                    }
                    if (!busy) break;
                    await original.AwaitAsync(Task.Delay(TimeSpan.FromMilliseconds(50))).ConfigureAwait(false);
                }
                if (payload is null || project is null || partition is null) continue;
                try
                {
                    var saved = await original.AwaitAsync(InvokeOriginalSource(() => _taskRecoveryOwner!.SaveAsync(
                        project, partition, expected, payload, TaskRecoveryScopeCallback, _businessToken))).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _taskRecoveryGeneration = saved.Generation; _taskRecoveryAcknowledged = payload;
                        _taskRecoveryHasSavedSnapshot = true;
                    }
                    await PublishAsync(original, () => _status = "Protected task recovery saved. Newer typing stays unsaved until its exact snapshot is acknowledged; source edits still require review.").ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate) _pending = false;
                    if (IsCurrent(generation)) await PublishAsync(original, () => { }).ConfigureAwait(false);
                }
                lock (_gate)
                {
                    if (_taskRecoveryBlocked || serial == _taskRecoverySerial)
                    { if (_taskRecoveryDriverId == id) _taskRecoveryDriver = null; return; }
                }
            }
        }
        catch
        {
            lock (_gate) _taskRecoveryBlocked = true;
            throw; // No replay of unknown key/write/native/capsule outcomes.
        }
        finally { lock (_gate) if (_taskRecoveryDriverId == id) _taskRecoveryDriver = null; }
    }
}
