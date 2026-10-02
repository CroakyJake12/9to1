#if ASTRA_ANDROID_CONTEXT_PROBE
using System.Security.Cryptography;
using System.Text.Json;
using Android.Views;
using Android.Widget;
using HavenOS.Home.Core;
using NineToOne.Launcher;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Android;

// Conditional real production-native callback fixture only. No synthetic peer, actor, registry entry or profile.
// Programmatic native callbacks do not prove physical Menu/DPAD/touch, default HOME role or native launch transport.
public sealed partial class HavenLauncherActivity
{
    private bool _actualDrawerNameArgumentProbeStarted;
    private void TryRunActualDrawerNameArgumentProbe()
    {
        if (_actualDrawerNameArgumentProbeStarted || Intent?.GetBooleanExtra("astra_drawer_name_probe", false) != true ||
            !_homeReady || !_activityStarted || _root?.IsAttachedToWindow != true || _layout is null) return;
        _actualDrawerNameArgumentProbeStarted = true;
        _ = RunActualDrawerNameArgumentProbeAsync();
    }
    private async Task RunActualDrawerNameArgumentProbeAsync()
    {
        Guid? addedCategoryId = null; var saved = false; var cleaned = false;
        string? originalName = null, mutatedName = null; long? beforeRevision = null, savedRevision = null;
        try
        {
            var expected = _layout ?? throw new InvalidOperationException("Actual Home layout unavailable.");
            var root = _root ?? throw new InvalidOperationException("Actual native root unavailable.");
            var oldCategories = JsonSerializer.Serialize(expected.Current.Drawer?.Categories.ToArray() ?? []);
            await WidgetSessions.RequireOriginalActorAsync(DisplayedLayouts.Require(expected), _launcherLifetime.Token);
            void RequireOriginalRender()
            {
                if (!_activityStarted || _launcherLifetime.IsCancellationRequested || !ReferenceEquals(_layout, expected) ||
                    !ReferenceEquals(_root, root) || !root.IsAttachedToWindow)
                    throw new UnauthorizedAccessException("Actual original name render retired.");
            }
            var home = (App.Services ?? throw new InvalidOperationException("Actual Home graph unavailable.")).GetRequiredService<IHomeCoreStateStore>();
            var before = await home.ReadAsync(_launcherLifetime.Token);
            if (!before.IsSuccess) throw new IOException("Actual Home read failed.");
            var beforeRecord = before.State!.Records.Single(r => r.RecordId == expected.AuthorityId);
            if (beforeRecord.Revision != expected.Revision) throw new InvalidOperationException("Actual original Home revision changed.");
            beforeRevision = beforeRecord.Revision; RequireOriginalRender(); CloseOriginalDrawerDialogs(); ShowDrawerOrganization();
            var organization = _originalDrawerDialogs.SingleOrDefault() ?? throw new InvalidOperationException("Actual organization dialog unavailable.");
            var list = organization.ListView ?? throw new InvalidOperationException("Actual organization list unavailable.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (list.GetChildAt(1) is null)
            {
                RequireOriginalRender();
                if (!organization.IsShowing || DateTime.UtcNow >= deadline) throw new TimeoutException("Actual Add category row unavailable.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            if (list.FirstVisiblePosition != 0) throw new InvalidOperationException("Actual Add category row is not in its original native position.");
            var sequence = _actualDrawerChoiceSequence;
            if (!list.PerformItemClick(list.GetChildAt(1)!, 1, list.GetItemIdAtPosition(1)) || _actualDrawerChoiceSequence != sequence + 1)
                throw new InvalidOperationException("Actual Add category native callback did not execute exactly once.");
            await _actualDrawerChoiceCompletion.WaitAsync(TimeSpan.FromSeconds(5)); RequireOriginalRender();
            var naming = _originalDrawerDialogs.SingleOrDefault() ?? throw new InvalidOperationException("Actual original name dialog unavailable.");
            var input = _actualDrawerNameInput ?? throw new InvalidOperationException("Actual native name input unavailable.");
            originalName = "Astra original native name " + Guid.NewGuid().ToString("N");
            input.Text = originalName;
            var save = naming.GetButton((int)global::Android.Content.DialogButtonType.Positive) ?? throw new InvalidOperationException("Actual native Save button unavailable.");
            sequence = _actualDrawerChoiceSequence;
            if (!save.PerformClick() || _actualDrawerChoiceSequence != sequence + 1) throw new InvalidOperationException("Actual native name Save callback did not execute exactly once.");
            mutatedName = input.Text;
            if (mutatedName == originalName) throw new InvalidOperationException("Actual native input mutation did not occur after the original text snapshot.");
            await _actualDrawerChoiceCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            var current = _layout ?? throw new InvalidOperationException("Actual saved Home layout unavailable.");
            var beforeIds = expected.Current.Drawer?.Categories.Select(category => category.Id).ToHashSet() ?? [];
            var candidates = current.Current.Drawer?.Categories.Where(category => !beforeIds.Contains(category.Id) &&
                (category.Name == originalName || category.Name == mutatedName)).ToArray() ?? [];
            if (candidates.Length != 1) throw new InvalidOperationException("No unique actual newly-added diagnostic category can be identified safely.");
            var added = candidates[0]; addedCategoryId = added.Id;
            if (added.Name != originalName) throw new InvalidOperationException("Actual production Home saved the mutated native name instead of its accepted original command.");
            if (current.Current.Drawer!.Categories.Any(category => category.Name == mutatedName)) throw new InvalidOperationException("Mutable native name replaced the accepted original command.");
            var savedActor = await WidgetSessions.RequireOriginalActorAsync(DisplayedLayouts.Require(current), _launcherLifetime.Token);
            var persisted = await LayoutStore.ReadExistingForActorAsync(savedActor, _launcherLifetime.Token)
                ?? throw new InvalidOperationException("Actual saved canonical launcher record unavailable.");
            if (persisted.AuthorityId != current.AuthorityId || persisted.Revision != current.Revision ||
                persisted.Current.Drawer?.Categories.SingleOrDefault(category => category.Id == added.Id)?.Name != originalName)
                throw new InvalidOperationException("Actual persisted canonical category did not retain the original native command name.");
            var readSaved = await home.ReadAsync(_launcherLifetime.Token);
            if (!readSaved.IsSuccess) throw new IOException("Actual saved Home read failed.");
            var savedRecord = readSaved.State!.Records.Single(r => r.RecordId == current.AuthorityId);
            if (savedRecord.Revision != current.Revision || savedRecord.Revision <= beforeRecord.Revision) throw new InvalidOperationException("Actual production Home revision did not advance coherently.");
            savedRevision = savedRecord.Revision; saved = true;
            await EditLayoutAsync(layout => LauncherLayoutEdits.RemoveDrawerCategory(layout, added.Id), current);
            var restored = _layout ?? throw new InvalidOperationException("Actual restored Home unavailable.");
            var restoredActor = await WidgetSessions.RequireOriginalActorAsync(DisplayedLayouts.Require(restored), _launcherLifetime.Token);
            var persistedRestored = await LayoutStore.ReadExistingForActorAsync(restoredActor, _launcherLifetime.Token)
                ?? throw new InvalidOperationException("Actual cleaned canonical launcher record unavailable.");
            if (persistedRestored.AuthorityId != restored.AuthorityId || persistedRestored.Revision != restored.Revision ||
                oldCategories != JsonSerializer.Serialize(persistedRestored.Current.Drawer?.Categories.ToArray() ?? [])) throw new InvalidOperationException("Actual native diagnostic category cleanup did not preserve previous categories.");
            cleaned = true; addedCategoryId = null;
            global::Android.Util.Log.Info("AstraDrawerNameProbe", JsonSerializer.Serialize(new
            { code = "ActualOriginalNativeNameSavedDespiteMutableInput", success = true, saved, cleaned, originalName, mutatedName, beforeRevision, savedRevision,
                processId = global::Android.OS.Process.MyPid(), nativeUid = global::Android.OS.Process.MyUid(), physicalInput = false, defaultHomeRole = false }));
        }
        catch (Exception ex)
        { global::Android.Util.Log.Error("AstraDrawerNameProbe", JsonSerializer.Serialize(new
            { code = "ActualNativeNameArgumentProbeFailed", success = false, saved, cleaned, originalName, mutatedName, beforeRevision, savedRevision, error = ex.Message })); }
        finally
        {
            CloseOriginalDrawerDialogs();
            if (addedCategoryId is { } id && _layout is { } current && _activityStarted && !_launcherLifetime.IsCancellationRequested)
            {
                try { await EditLayoutAsync(layout => LauncherLayoutEdits.RemoveDrawerCategory(layout, id), current); }
                catch (Exception ex) { global::Android.Util.Log.Error("AstraDrawerNameProbe", "ActualDiagnosticCategoryCleanupFailed: " + ex.Message); }
            }
        }
    }
    private bool _actualCancelledPlacementProbeStarted;
    private void TryRunActualCancelledPlacementProbe()
    {
        if (_actualCancelledPlacementProbeStarted || Intent?.GetBooleanExtra("astra_cancelled_placement_probe", false) != true ||
            !_homeReady || !_activityStarted || _root?.IsAttachedToWindow != true || _layout is null) return;
        _actualCancelledPlacementProbeStarted = true; _ = RunActualCancelledPlacementProbeAsync();
    }
    private async Task RunActualCancelledPlacementProbeAsync()
    {
        try
        {
            var expected = _layout ?? throw new InvalidOperationException("Actual Home layout unavailable.");
            var root = _root ?? throw new InvalidOperationException("Actual native root unavailable.");
            var grid = _grid ?? throw new InvalidOperationException("Actual native page grid unavailable.");
            var epoch = _widgetRenderEpoch; var original = DisplayedLayouts.Require(expected);
            var actor = await WidgetSessions.RequireOriginalActorAsync(original, _launcherLifetime.Token);
            var before = await LayoutStore.ReadExistingForActorAsync(actor, _launcherLifetime.Token)
                ?? throw new IOException("Actual persisted Home record unavailable.");
            void RequireRender()
            {
                if (!_activityStarted || !_homeReady || _launcherLifetime.IsCancellationRequested || root.IsAttachedToWindow != true ||
                    !ReferenceEquals(root, _root) || !ReferenceEquals(grid, _grid) || !ReferenceEquals(expected, _layout) || epoch != _widgetRenderEpoch)
                    throw new UnauthorizedAccessException("Actual original placement render retired.");
            }
            RequireRender(); CloseOriginalDrawerDialogs();
            var tile = Enumerable.Range(0, grid.ChildCount).Select(i => grid.GetChildAt(i)).OfType<LinearLayout>()
                .FirstOrDefault(view => view.Focusable && view.Clickable && view.IsAttachedToWindow)
                ?? throw new InvalidOperationException("No actual rendered app tile exists; no synthetic app is supplied.");
            var moving = _movingPlacementId;
            if (!tile.PerformLongClick()) throw new InvalidOperationException("Actual native tile long-click not accepted.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (_placementMenuDialog?.IsShowing != true)
            {
                RequireRender(); if (DateTime.UtcNow >= deadline) throw new TimeoutException("Actual native placement menu unavailable.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            var dialog = _placementMenuDialog!; var list = dialog.ListView ?? throw new InvalidOperationException("Actual native placement ListView unavailable.");
            while (list.GetChildAt(0) is null)
            {
                RequireRender(); if (!dialog.IsShowing || DateTime.UtcNow >= deadline) throw new TimeoutException("Actual native placement row unavailable.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            RequireRender(); var row = list.GetChildAt(0)!; var position = list.FirstVisiblePosition; var nativeId = list.GetItemIdAtPosition(position);
            var sequence = _actualPlacementChoiceSequence; var admitted = _actualDrawerAdmittedActionSequence;
            // Raw actual Dismiss deliberately leaves root/layout/epoch/original tile live. Calling ClosePlacementMenu
            // would also advance generation and could conceal absence of authentic canceled-dialog provenance.
            dialog.Dismiss(); RequireRender();
            if (dialog.IsShowing || _originalDrawerDialogs.Contains(dialog) || !tile.IsAttachedToWindow)
                throw new InvalidOperationException("Actual dismissed menu/original still-live tile prerequisite failed.");
            if (!list.PerformItemClick(row, position, nativeId) || _actualPlacementChoiceSequence != sequence + 1)
                throw new InvalidOperationException("Actual retained canceled placement callback not executed exactly once.");
            await _actualPlacementChoiceCompletion.WaitAsync(TimeSpan.FromSeconds(5)); RequireRender();
            if (_actualDrawerAdmittedActionSequence != admitted || _movingPlacementId != moving || _originalDrawerDialogs.Count != 0)
                throw new InvalidOperationException("Actual canceled placement callback admitted an action or changed native move state.");
            var after = await LayoutStore.ReadExistingForActorAsync(actor, _launcherLifetime.Token)
                ?? throw new IOException("Actual persisted Home unavailable after native callback.");
            RequireRender(); var beforeBytes = JsonSerializer.SerializeToUtf8Bytes(before); var afterBytes = JsonSerializer.SerializeToUtf8Bytes(after);
            if (!beforeBytes.SequenceEqual(afterBytes)) throw new IOException("Actual canceled placement callback changed durable Home.");
            global::Android.Util.Log.Info("AstraCancelledPlacementProbe", JsonSerializer.Serialize(new
            { success = true, code = "ActualCanceledPlacementCallbackDeniedOnLiveOriginalTile", actualNativeCallback = true, admittedActions = 0,
                beforeSha = Convert.ToHexString(SHA256.HashData(beforeBytes)), afterSha = Convert.ToHexString(SHA256.HashData(afterBytes)),
                originalAuthority = before.AuthorityId, originalRevision = before.Revision,
                processId = global::Android.OS.Process.MyPid(), nativeUid = global::Android.OS.Process.MyUid(), physicalInput = false }));
        }
        catch (Exception error) { global::Android.Util.Log.Error("AstraCancelledPlacementProbe", JsonSerializer.Serialize(new
            { success = false, code = "ActualCanceledPlacementCallbackDeniedOnLiveOriginalTile", reason = error.Message, physicalInput = false })); }
        finally { ClosePlacementMenu(); CloseOriginalDrawerDialogs(); }
    }
    private bool _actualPagesChoiceProbeStarted;
    private void TryRunActualPagesChoiceProbe()
    {
        if (_actualPagesChoiceProbeStarted || Intent?.GetBooleanExtra("astra_pages_choice_probe", false) != true ||
            !_homeReady || !_activityStarted || _root?.IsAttachedToWindow != true || _layout is null) return;
        _actualPagesChoiceProbeStarted = true; _ = RunActualPagesChoiceProbeAsync();
    }
    private async Task RunActualPagesChoiceProbeAsync()
    {
        try
        {
            var expected = _layout ?? throw new InvalidOperationException("Actual Home page layout unavailable.");
            var originalRoot = _root ?? throw new InvalidOperationException("Actual Home root unavailable.");
            var epoch = _widgetRenderEpoch; var original = DisplayedLayouts.Require(expected);
            var actor = await WidgetSessions.RequireOriginalActorAsync(original, _launcherLifetime.Token);
            var before = await LayoutStore.ReadExistingForActorAsync(actor, _launcherLifetime.Token)
                ?? throw new IOException("Actual persisted Home page record unavailable.");
            void RequireRender()
            {
                if (!_activityStarted || _launcherLifetime.IsCancellationRequested || originalRoot.IsAttachedToWindow != true ||
                    !ReferenceEquals(originalRoot, _root) || !ReferenceEquals(expected, _layout) || epoch != _widgetRenderEpoch)
                    throw new UnauthorizedAccessException("Actual original page-menu render retired.");
            }
            RequireRender(); CloseOriginalDrawerDialogs(); ShowPagesMenu();
            var dialog = _originalDrawerDialogs.SingleOrDefault() ?? throw new InvalidOperationException("Actual tracked native page menu unavailable.");
            var list = dialog.ListView ?? throw new InvalidOperationException("Actual page native ListView unavailable.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (list.GetChildAt(0) is null)
            {
                RequireRender(); if (!dialog.IsShowing || DateTime.UtcNow >= deadline) throw new TimeoutException("Actual page native row unavailable.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            RequireRender(); var row = list.GetChildAt(0)!; var position = list.FirstVisiblePosition;
            var nativeId = list.GetItemIdAtPosition(position);
            var sequence = _actualDrawerChoiceSequence; var admitted = _actualDrawerAdmittedActionSequence;
            dialog.Dismiss(); RequireRender();
            if (dialog.IsShowing || _originalDrawerDialogs.Contains(dialog)) throw new InvalidOperationException("Actual native page menu did not retire on dismissal.");
            if (!list.PerformItemClick(row, position, nativeId) || _actualDrawerChoiceSequence != sequence + 1)
                throw new InvalidOperationException("Actual retained native page callback did not execute exactly once.");
            await _actualDrawerChoiceCompletion.WaitAsync(TimeSpan.FromSeconds(5)); RequireRender();
            if (_actualDrawerAdmittedActionSequence != admitted || _originalDrawerDialogs.Count != 0)
                throw new InvalidOperationException("Dismissed native page callback admitted an action or created a child name dialog.");
            var after = await LayoutStore.ReadExistingForActorAsync(actor, _launcherLifetime.Token)
                ?? throw new IOException("Actual persisted page record unavailable after callback.");
            RequireRender();
            var beforeBytes = JsonSerializer.SerializeToUtf8Bytes(before); var afterBytes = JsonSerializer.SerializeToUtf8Bytes(after);
            if (!beforeBytes.SequenceEqual(afterBytes)) throw new IOException("Dismissed native page callback changed actual durable Home.");
            global::Android.Util.Log.Info("AstraPagesChoiceProbe", JsonSerializer.Serialize(new
            { success = true, code = "ActualDismissedNativePagesChoiceDenied", actualNativeCallback = true, admittedActions = 0,
                beforeSha = Convert.ToHexString(SHA256.HashData(beforeBytes)), afterSha = Convert.ToHexString(SHA256.HashData(afterBytes)),
                originalAuthority = before.AuthorityId, originalRevision = before.Revision,
                processId = global::Android.OS.Process.MyPid(), nativeUid = global::Android.OS.Process.MyUid(), physicalInput = false }));
        }
        catch (Exception error) { global::Android.Util.Log.Error("AstraPagesChoiceProbe", JsonSerializer.Serialize(new
            { success = false, code = "ActualDismissedNativePagesChoiceDenied", reason = error.Message, physicalInput = false })); }
        finally { CloseOriginalDrawerDialogs(); }
    }
    private bool _actualDrawerChoiceProbeStarted;
    private void TryRunActualDrawerChoiceProbe()
    {
        if (_actualDrawerChoiceProbeStarted || Intent?.GetBooleanExtra("astra_drawer_choice_probe", false) != true ||
            !_homeReady || !_activityStarted || _root?.IsAttachedToWindow != true || _layout is null) return;
        _actualDrawerChoiceProbeStarted = true;
        _ = RunActualDrawerChoiceProbeAsync();
    }
    private async Task RunActualDrawerChoiceProbeAsync()
    {
        View? originalRoot = null; View? retirementSurface = null;
        string? beforeSha = null, afterSha = null; var dispatched = false; var choiceCompleted = false;
        try
        {
            var expected = _layout ?? throw new InvalidOperationException("Actual original layout is unavailable.");
            originalRoot = _root ?? throw new InvalidOperationException("Actual original native root is unavailable.");
            var original = DisplayedLayouts.Require(expected);
            await WidgetSessions.RequireOriginalActorAsync(original, _launcherLifetime.Token);
            var home = (App.Services ?? throw new InvalidOperationException("Actual Home graph is unavailable.")).GetRequiredService<IHomeCoreStateStore>();
            async Task<byte[]> ActualRecordAsync()
            {
                var read = await home.ReadAsync(_launcherLifetime.Token);
                if (!read.IsSuccess) throw new IOException("Actual Home read failed.");
                var record = read.State!.Records.Single(r => r.RecordId == expected.AuthorityId);
                if (record.Revision != expected.Revision) throw new InvalidOperationException("Actual original layout revision changed.");
                return JsonSerializer.SerializeToUtf8Bytes(record);
            }
            void RequireOriginalRender()
            {
                if (!_activityStarted || _launcherLifetime.IsCancellationRequested || !ReferenceEquals(_layout, expected) ||
                    !ReferenceEquals(_root, originalRoot) || originalRoot.IsAttachedToWindow != true)
                    throw new UnauthorizedAccessException("Actual original drawer native render retired before callback.");
            }
            var before = await ActualRecordAsync(); beforeSha = Convert.ToHexString(SHA256.HashData(before));
            RequireOriginalRender(); CloseOriginalDrawerDialogs(); ShowDrawerOrganization();
            var actualDialog = _originalDrawerDialogs.SingleOrDefault() ?? throw new InvalidOperationException("Actual drawer organization dialog unavailable.");
            var list = actualDialog.ListView ?? throw new InvalidOperationException("Actual drawer native list unavailable.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (list.GetChildAt(0) is null)
            {
                RequireOriginalRender();
                if (!actualDialog.IsShowing || DateTime.UtcNow >= deadline) throw new TimeoutException("Actual drawer native row unavailable.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            RequireOriginalRender();
            if (list.FirstVisiblePosition != 0) throw new InvalidOperationException("Actual original first drawer row is not visible.");
            var row = list.GetChildAt(0) ?? throw new InvalidOperationException("Actual native drawer row retired.");
            var category = _drawerCategoryId; var sequence = _actualDrawerChoiceSequence; var admitted = _actualDrawerAdmittedActionSequence;
            // Detach the actual production render, retaining the real already-issued native dialog callback.
            retirementSurface = new FrameLayout(this); SetContentView(retirementSurface);
            if (originalRoot.IsAttachedToWindow) throw new InvalidOperationException("Actual original render did not detach.");
            dispatched = list.PerformItemClick(row, 0, list.GetItemIdAtPosition(0));
            if (!dispatched || _actualDrawerChoiceSequence != sequence + 1) throw new InvalidOperationException("Actual drawer native callback did not execute exactly once.");
            await _actualDrawerChoiceCompletion.WaitAsync(TimeSpan.FromSeconds(5)); choiceCompleted = true;
            var after = await ActualRecordAsync(); afterSha = Convert.ToHexString(SHA256.HashData(after));
            if (!before.AsSpan().SequenceEqual(after) || category != _drawerCategoryId || admitted != _actualDrawerAdmittedActionSequence)
                throw new InvalidOperationException("Retired actual drawer render admitted an action or changed Home/category state.");
            // A cancelled original dialog must not grant an unaccepted retained callback on the SAME live render.
            SetContentView(originalRoot); retirementSurface = null;
            RequireOriginalRender(); CloseOriginalDrawerDialogs(); ShowDrawerOrganization();
            var cancelled = _originalDrawerDialogs.SingleOrDefault() ?? throw new InvalidOperationException("Actual cancellable drawer dialog unavailable.");
            var cancelledList = cancelled.ListView ?? throw new InvalidOperationException("Actual cancellable drawer list unavailable.");
            deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (cancelledList.GetChildAt(0) is null)
            {
                RequireOriginalRender();
                if (!cancelled.IsShowing || DateTime.UtcNow >= deadline) throw new TimeoutException("Actual cancellable drawer row unavailable.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            if (cancelledList.FirstVisiblePosition != 0) throw new InvalidOperationException("Actual cancelled first row is not visible.");
            var cancelledRow = cancelledList.GetChildAt(0) ?? throw new InvalidOperationException("Actual cancelled drawer row unavailable.");
            var cancelledSequence = _actualDrawerChoiceSequence; var cancelledAdmitted = _actualDrawerAdmittedActionSequence;
            cancelled.Dismiss();
            if (cancelled.IsShowing || _originalDrawerDialogs.Contains(cancelled)) throw new InvalidOperationException("Actual native dialog did not retire.");
            var cancelledDispatched = cancelledList.PerformItemClick(cancelledRow, 0, cancelledList.GetItemIdAtPosition(0));
            if (!cancelledDispatched || _actualDrawerChoiceSequence != cancelledSequence + 1)
                throw new InvalidOperationException("Actual retained cancelled native callback did not execute exactly once.");
            await _actualDrawerChoiceCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            RequireOriginalRender();
            var cancelledAfter = await ActualRecordAsync();
            if (!before.AsSpan().SequenceEqual(cancelledAfter) || category != _drawerCategoryId || cancelledAdmitted != _actualDrawerAdmittedActionSequence)
                throw new InvalidOperationException("Cancelled original native dialog admitted an unaccepted action on its still-live render.");
            global::Android.Util.Log.Info("AstraDrawerChoiceProbe", JsonSerializer.Serialize(new
            { code = "ActualRetiredNativeDrawerCallbackDenied", success = true, dispatched, choiceCompleted, cancelledDispatched, cancelledChoiceCompleted = true, beforeSha, afterSha,
                processId = global::Android.OS.Process.MyPid(), nativeUid = global::Android.OS.Process.MyUid(), physicalInput = false, defaultHomeRole = false }));
        }
        catch (Exception ex)
        { global::Android.Util.Log.Error("AstraDrawerChoiceProbe", JsonSerializer.Serialize(new
            { code = "ActualNativeDrawerProbeFailed", success = false, dispatched, choiceCompleted, beforeSha, afterSha, error = ex.Message })); }
        finally
        {
            CloseOriginalDrawerDialogs();
            if (originalRoot is not null && retirementSurface is not null && ReferenceEquals(_root, originalRoot) &&
                _activityStarted && !_launcherLifetime.IsCancellationRequested && !IsFinishing && !IsDestroyed)
                SetContentView(originalRoot);
        }
    }
    private bool _actualPlacementContextProbeStarted;
    private void TryRunActualPlacementContextProbe()
    {
        if (_actualPlacementContextProbeStarted || Intent?.GetBooleanExtra("astra_context_probe", false) != true ||
            !_homeReady || !_activityStarted || _root?.IsAttachedToWindow != true || _grid is null || _layout is null) return;
        _actualPlacementContextProbeStarted = true;
        _ = RunActualPlacementContextProbeAsync();
    }
    private async Task RunActualPlacementContextProbeAsync()
    {
        string? beforeSha = null, afterSha = null; var dispatched = false; var choiceCompleted = false;
        try
        {
            if (!_homeReady || !_activityStarted || _root?.IsAttachedToWindow != true || _grid is null || _layout is null)
                throw new InvalidOperationException("The real production Home launcher is not ready.");
            var expected = _layout ?? throw new InvalidOperationException("The actual original layout is unavailable.");
            var actualGrid = _grid ?? throw new InvalidOperationException("The actual original grid is unavailable.");
            var actualRoot = _root ?? throw new InvalidOperationException("The actual original root is unavailable.");
            var original = DisplayedLayouts.Require(expected);
            void RequireOriginalRender()
            {
                if (!_activityStarted || _launcherLifetime.IsCancellationRequested || !ReferenceEquals(_layout, expected) ||
                    !ReferenceEquals(_grid, actualGrid) || !ReferenceEquals(_root, actualRoot) || actualRoot.IsAttachedToWindow != true)
                    throw new UnauthorizedAccessException("The actual original native render retired during the fixture.");
            }
            await WidgetSessions.RequireOriginalActorAsync(original, _launcherLifetime.Token);
            var home = (App.Services ?? throw new InvalidOperationException("The real Home graph is unavailable."))
                .GetRequiredService<IHomeCoreStateStore>();
            async Task<byte[]> ActualRecordAsync()
            {
                var read = await home.ReadAsync(_launcherLifetime.Token);
                if (!read.IsSuccess) throw new IOException("The actual Home store is unavailable.");
                var record = read.State!.Records.Single(r => r.RecordId == expected.AuthorityId);
                if (record.Revision != expected.Revision) throw new InvalidOperationException("The original persisted layout changed.");
                return JsonSerializer.SerializeToUtf8Bytes(record);
            }
            var before = await ActualRecordAsync(); beforeSha = Convert.ToHexString(SHA256.HashData(before));
            var moving = _movingPlacementId;
            // Actual page-native app tiles are focusable clickable LinearLayouts; no reconstructed canonical app is used.
            RequireOriginalRender();
            var tiles = Enumerable.Range(0, actualGrid.ChildCount).Select(i => actualGrid.GetChildAt(i))
                .OfType<LinearLayout>().Where(v => v.Focusable && v.Clickable && v.IsAttachedToWindow).ToArray();
            var tile = tiles.FirstOrDefault() ?? throw new InvalidOperationException("No real rendered app tile exists for the fixture.");
            if (!tile.PerformLongClick()) throw new InvalidOperationException("The actual native long-click callback was not accepted.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (_placementMenuDialog?.IsShowing != true)
            {
                RequireOriginalRender();
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("The actual original placement menu did not appear.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            var actualDialog = _placementMenuDialog!;
            var list = actualDialog.ListView ?? throw new InvalidOperationException("The actual native choice list is unavailable.");
            if (list.FirstVisiblePosition != 0) throw new InvalidOperationException("The original native first choice is not visible.");
            while (list.GetChildAt(0) is null)
            {
                RequireOriginalRender();
                if (!actualDialog.IsShowing || DateTime.UtcNow >= deadline) throw new TimeoutException("The actual original native choice row did not render.");
                await Task.Delay(10, _launcherLifetime.Token);
            }
            RequireOriginalRender();
            var choice = list.GetChildAt(0) ?? throw new InvalidOperationException("The actual original native choice row retired.");
            var oldSequence = _actualPlacementChoiceSequence;
            // Retire the actual native view. The retained real menu callback must not mutate the original Home or move state.
            actualGrid.RemoveView(tile);
            if (tile.IsAttachedToWindow) throw new InvalidOperationException("The original native tile did not detach.");
            dispatched = list.PerformItemClick(choice, 0, list.GetItemIdAtPosition(0));
            if (!dispatched || _actualPlacementChoiceSequence != oldSequence + 1)
                throw new InvalidOperationException("The actual retained native item callback did not execute exactly once.");
            await _actualPlacementChoiceCompletion.WaitAsync(TimeSpan.FromSeconds(5)); choiceCompleted = true;
            var after = await ActualRecordAsync(); afterSha = Convert.ToHexString(SHA256.HashData(after));
            if (!before.AsSpan().SequenceEqual(after) || _movingPlacementId != moving)
                throw new InvalidOperationException("A retired original native tile changed Home layout or move state.");
            global::Android.Util.Log.Info("AstraContextProbe", JsonSerializer.Serialize(new
            { code = "ActualRetiredNativeContextCallbackDenied", success = true, dispatched, choiceCompleted, beforeSha, afterSha,
                processId = global::Android.OS.Process.MyPid(), nativeUid = global::Android.OS.Process.MyUid(), originalLayoutRevision = expected.Revision,
                physicalInput = false, defaultHomeRole = false, installedProcessAuthority = false }));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("AstraContextProbe", JsonSerializer.Serialize(new
            { code = "ActualNativeContextProbeFailed", success = false, dispatched, choiceCompleted, beforeSha, afterSha,
                errorType = ex.GetType().Name, error = ex.Message, physicalInput = false, defaultHomeRole = false, installedProcessAuthority = false }));
        }
        finally
        {
            ClosePlacementMenu();
            if (_activityStarted && !_launcherLifetime.IsCancellationRequested && _layout is not null) RenderPage();
        }
    }
}
#endif
