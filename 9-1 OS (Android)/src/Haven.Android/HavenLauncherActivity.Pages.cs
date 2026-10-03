using Android.App;
using Android.Widget;
using Haven.Desktop;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private LauncherStoredLayout? _layout;
    private AndroidLauncherLayoutSessions? _displayedLayouts;
    private AndroidLauncherLayoutSessions DisplayedLayouts => _displayedLayouts ??= new(WidgetSessions);
    private readonly SemaphoreSlim _layoutEdits = new(1, 1);
    private Guid? _movingPlacementId;
    private Action? _refreshDrawer;
    private HomeLauncherLayoutStore LayoutStore => (App.Services ?? throw new InvalidOperationException("Home is unavailable.")).GetRequiredService<HomeLauncherLayoutStore>();

    // Retain the actual acknowledged original result even when its native view retires before adoption.
    private readonly Dictionary<LauncherSessionSnapshot, LauncherStoredLayout> _knownOriginalLayoutCommits = new();
    private void RetainKnownOriginalLayoutCommit(LauncherSessionSnapshot original, LauncherStoredLayout saved)
    {
        _knownOriginalLayoutCommits.Clear();
        _knownOriginalLayoutCommits.Add(original, saved);
    }
    private async Task EditLayoutAsync(Func<LauncherLayout, LauncherLayout> edit, LauncherStoredLayout? expected = null)
    {
        // Capture what the user actually saw before waiting behind another edit.
        expected ??= _layout; var epoch = _widgetRenderEpoch; var ct = _launcherLifetime.Token;
        void RequireOriginalHost()
        {
            ct.ThrowIfCancellationRequested();
            if (!_activityStarted || epoch != _widgetRenderEpoch)
                throw new UnauthorizedAccessException("The original launcher view changed. Reopen it before editing.");
        }
        try
        {
            if (expected is null) throw new InvalidOperationException("Load the current Home launcher layout first.");
            var original = DisplayedLayouts.Require(expected); RequireOriginalHost();
            await _layoutEdits.WaitAsync(ct);
            try
            {
                RequireOriginalHost();
                // Safe to evaluate under Home's writer lease: managed fields only, no native UI/store access.
                bool OriginalHostCurrent() => !ct.IsCancellationRequested && global::System.Threading.Volatile.Read(ref _activityStarted) &&
                    epoch == global::System.Threading.Volatile.Read(ref _widgetRenderEpoch);
                var saved = await WidgetSessions.EditForOriginalHostAsync(original, edit, OriginalHostCurrent, ct);
                RetainKnownOriginalLayoutCommit(original, saved);
                RequireOriginalHost();
                var current = await WidgetSessions.ReadAfterEditAsync(original, saved, ct);
                RequireOriginalHost();
                if (current is null || current.Layout.AuthorityId != saved.AuthorityId || current.Layout.Revision != saved.Revision)
                    throw new InvalidOperationException("Launcher changed after saving. Reload the current layout.");
                _layout = DisplayedLayouts.Bind(current);
                _page = _layout.Current.Pages.ToList().FindIndex(p => p.Id == _layout.Current.ActivePageId);
                _movingPlacementId = null;
                RunOnUiThread(() => { if (_activityStarted && epoch == _widgetRenderEpoch && !ct.IsCancellationRequested) { RenderPage(); _refreshDrawer?.Invoke(); } });
            }
            finally { _layoutEdits.Release(); }
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            RunOnUiThread(() => { if (_activityStarted && epoch == _widgetRenderEpoch && !ct.IsCancellationRequested && _launcherStatus is not null) _launcherStatus.Text = ex.Message; });
            // Do not adopt an ambient replacement session after an original-view refusal.
        }
    }
    private void ShowPagesMenu()
    {
        var expected = _layout; if (expected is null) return;
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var page = expected.Current.ActivePage;
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(page.Name);
        dialog.SetItems(new[] { "Add page", "Rename page", "Move page earlier", "Move page later", "Remove empty page", "Hidden apps", "Restore previous layout", "Export layout", "Import layout", "Dock settings", "Add folder", "Gestures" }, (_, e) =>
        {
            var selected = e.Which; // Immutable original native choice, frozen before the owner await.
            _ = choice.Accept(() =>
            {
            switch (selected)
            {
                case 11: ShowGestureSettings(); break;
                case 0: AskPageName("Add page", "", name => EditLayoutAsync(layout => LauncherLayoutEdits.AddPage(layout, name), expected)); break;
                case 1: AskPageName("Rename page", page.Name, name => EditLayoutAsync(layout => LauncherLayoutEdits.RenamePage(layout, page.Id, name), expected)); break;
                case 2: return EditLayoutAsync(layout => LauncherLayoutEdits.ReorderPage(layout, page.Id, -1), expected);
                case 3: return EditLayoutAsync(layout => LauncherLayoutEdits.ReorderPage(layout, page.Id, 1), expected);
                case 4: return EditLayoutAsync(layout => LauncherLayoutEdits.RemovePage(layout, page.Id), expected);
                case 5: ShowHiddenApplications(expected); break;
                case 10: AskPageName("Add folder", "", name => EditLayoutAsync(layout => LauncherLayoutEdits.CreateFolder(layout, layout.ActivePageId, name), expected)); break;
                case 9: ShowDockSettings(expected); break;
                case 7: PickLayoutDocument(true, expected); break;
                case 8: PickLayoutDocument(false, expected); break;
                case 6:
                    if (expected.Previous is { } previous) return EditLayoutAsync(_ => LauncherLayoutEdits.Clone(previous), expected);
                    else Toast.MakeText(this, "No previous launcher layout is available.", ToastLength.Short)?.Show();
                    break;
            }
            return Task.CompletedTask;
            });
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }
    private void AskPageName(string title, string value, Func<string, Task> apply)
    {
        var expected = _layout; if (expected is null) return;
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var input = new EditText(this) { Text = value, ContentDescription = title }; input.SetSingleLine(true);
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(title); dialog.SetView(input);
        dialog.SetPositiveButton("Save", (_, _) =>
        {
            var originalName = input.Text?.Trim() ?? "";
            _ = choice.Accept(() => apply(originalName));
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }
    private AlertDialog? _placementMenuDialog;
    private long _placementMenuGeneration;
#if ASTRA_ANDROID_CONTEXT_PROBE
    private int _actualPlacementChoiceSequence;
    private Task _actualPlacementChoiceCompletion = Task.CompletedTask;
#endif
    private void ClosePlacementMenu()
    { ++_placementMenuGeneration; _placementMenuDialog?.Dismiss(); _placementMenuDialog = null; }
    private async Task ShowPlacementMenuAsync(LauncherApp app, LauncherPlacement? placement, LauncherStoredLayout? expected, Func<bool> originalViewCurrent)
    {
        if (expected is null || !originalViewCurrent()) return;
        ClosePlacementMenu(); var generation = _placementMenuGeneration; var ct = _launcherLifetime.Token;
        bool Current() => generation == _placementMenuGeneration && originalViewCurrent() && ReferenceEquals(_layout, expected);
        try
        {
            var original = DisplayedLayouts.Require(expected);
            await WidgetSessions.RequireOriginalActorAsync(original, ct);
            if (!Current()) return;
            if (placement is not null)
            {
                var canonicalPlacement = LauncherLayoutEdits.Placements(expected.Current).SingleOrDefault(item => item.Id == placement.Id);
                if (canonicalPlacement is null || canonicalPlacement.FolderId is not null || canonicalPlacement.ApplicationId != app.ApplicationId) return;
                placement = canonicalPlacement;
            }
            var inOtherContainer = placement is not null && LauncherLayoutEdits.ContainerForPlacement(expected.Current, placement.Id) != expected.Current.ActivePageId;
            var options = placement is null ? new[] { "Add to current page", "Hide from app drawer", "Add to dock", "Categories", "App shortcuts" }
                : new[] { "Move shortcut", "Remove shortcut", "Hide from app drawer", inOtherContainer ? "Move to current page" : "Move to dock", "Categories", "App shortcuts" };
            async Task ApplyOriginalChoiceAsync(int choice)
            {
                try
                {
                    if (!Current() || choice < 0 || choice >= options.Length) return;
                    await WidgetSessions.RequireOriginalActorAsync(original, ct);
                    if (!Current()) return;
                    if (choice == options.Length - 1) { await ShowApplicationShortcutsAsync(app, expected); return; }
                    if (choice == options.Length - 2) { ShowAppCategories(app, expected); return; }
                    if (placement is null && choice == 2) await AddOrMoveToDockAsync(app.ApplicationId, null, expected);
                    else if (placement is not null && choice == 3)
                    {
                        if (inOtherContainer) await EditLayoutAsync(layout => LauncherLayoutEdits.MoveToContainer(layout, placement.Id, layout.ActivePageId), expected);
                        else await AddOrMoveToDockAsync(app.ApplicationId, placement, expected);
                    }
                    else if (placement is null && choice == 0)
                        await EditLayoutAsync(layout => LauncherLayoutEdits.AddApplication(layout, layout.ActivePageId, app.ApplicationId), expected);
                    else if (placement is not null && choice == 0)
                    {
                        _movingPlacementId = placement.Id; RenderPage();
                        Toast.MakeText(this, "Choose a slot or another shortcut. Swipe to move to a different page.", ToastLength.Long)?.Show();
                    }
                    else if (placement is not null && choice == 1)
                        await EditLayoutAsync(layout => LauncherLayoutEdits.RemovePlacement(layout, placement.Id), expected);
                    else await EditLayoutAsync(layout => LauncherLayoutEdits.SetHidden(layout, app.ApplicationId, true), expected);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                { if (Current()) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
            }
            var nativeChoice = CaptureOriginalDrawerChoice(expected); if (!nativeChoice.Current()) return;
            var builder = new AlertDialog.Builder(this); builder.SetTitle(app.Label);
            builder.SetItems(options, (_, e) =>
            {
                var selected = e.Which;
                // Reject the specific original tile synchronously BEFORE private dialog admission can await Home.
                // A retained callback on a detached tile must not observe original Home merely because its root survived.
                var actualChoice = Current() ? nativeChoice.Accept(() => ApplyOriginalChoiceAsync(selected)) : Task.CompletedTask;
#if ASTRA_ANDROID_CONTEXT_PROBE
                ++_actualPlacementChoiceSequence; _actualPlacementChoiceCompletion = actualChoice;
#endif
                _ = actualChoice;
            }); builder.SetNegativeButton("Cancel", (_, _) => { });
            var shown = builder.Create() ?? throw new InvalidOperationException("Android could not create the original app context menu.");
            nativeChoice.Attach(shown);
            _originalDrawerDialogs.Add(shown);
            _placementMenuDialog = shown;
            shown.DismissEvent += (_, _) =>
            {
                _originalDrawerDialogs.Remove(shown);
                if (ReferenceEquals(_placementMenuDialog, shown)) _placementMenuDialog = null;
            };
            if (!Current()) { shown.Dismiss(); return; } shown.Show();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { if (Current()) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
    }
    private void ShowHiddenApplications(LauncherStoredLayout expected)
    {
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var hidden = expected.Current.HiddenApplications.ToArray();
        if (hidden.Length == 0) { Toast.MakeText(this, "No apps are hidden from this launcher drawer.", ToastLength.Short)?.Show(); return; }
        var labels = hidden.Select(id => _apps.SingleOrDefault(a => a.ApplicationId == id)?.Label ?? "Unavailable application").ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Show app in drawer"); dialog.SetItems(labels,
            (_, e) =>
            {
                var selected = e.Which;
                if (selected < 0 || selected >= hidden.Length) return;
                var originalApplication = hidden[selected];
                _ = choice.Accept(() => EditLayoutAsync(layout => LauncherLayoutEdits.SetHidden(layout, originalApplication, false), expected));
            });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }
}
