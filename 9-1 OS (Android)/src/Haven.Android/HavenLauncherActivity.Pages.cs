using Android.App;
using Android.Widget;
using Haven.Desktop;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private LauncherStoredLayout? _layout;
    private readonly SemaphoreSlim _layoutEdits = new(1, 1);
    private Guid? _movingPlacementId;
    private Action? _refreshDrawer;
    private HomeLauncherLayoutStore LayoutStore => (App.Services ?? throw new InvalidOperationException("Home is unavailable.")).GetRequiredService<HomeLauncherLayoutStore>();

    private async Task EditLayoutAsync(Func<LauncherLayout, LauncherLayout> edit, LauncherStoredLayout? expected = null)
    {
        // Capture what the user actually saw before waiting behind another edit.
        expected ??= _layout;
        try
        {
            await _layoutEdits.WaitAsync(_launcherLifetime.Token);
            try
            {
                if (expected is null) throw new InvalidOperationException("Load the current Home launcher layout first.");
                _layout = await LayoutStore.EditAsync(expected, edit, _launcherLifetime.Token);
                _page = _layout.Current.Pages.ToList().FindIndex(p => p.Id == _layout.Current.ActivePageId);
                _movingPlacementId = null;
                RunOnUiThread(() => { RenderPage(); _refreshDrawer?.Invoke(); });
            }
            finally { _layoutEdits.Release(); }
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            RunOnUiThread(() => { if (_launcherStatus is not null) _launcherStatus.Text = ex.Message; });
            LoadAppsAsync();
        }
    }
    private void ShowPagesMenu()
    {
        if (_layout is null) return;
        var page = _layout.Current.ActivePage;
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(page.Name);
        dialog.SetItems(new[] { "Add page", "Rename page", "Move page earlier", "Move page later", "Remove empty page", "Hidden apps", "Restore previous layout", "Export layout", "Import layout", "Dock settings", "Add folder" }, (_, e) =>
        {
            switch (e.Which)
            {
                case 0: AskPageName("Add page", "", name => EditLayoutAsync(layout => LauncherLayoutEdits.AddPage(layout, name))); break;
                case 1: AskPageName("Rename page", page.Name, name => EditLayoutAsync(layout => LauncherLayoutEdits.RenamePage(layout, page.Id, name))); break;
                case 2: _ = EditLayoutAsync(layout => LauncherLayoutEdits.ReorderPage(layout, page.Id, -1)); break;
                case 3: _ = EditLayoutAsync(layout => LauncherLayoutEdits.ReorderPage(layout, page.Id, 1)); break;
                case 4: _ = EditLayoutAsync(layout => LauncherLayoutEdits.RemovePage(layout, page.Id)); break;
                case 5: ShowHiddenApplications(); break;
                case 10: AskPageName("Add folder", "", name => EditLayoutAsync(layout => LauncherLayoutEdits.CreateFolder(layout, layout.ActivePageId, name))); break;
                case 9: ShowDockSettings(); break;
                case 7: PickLayoutDocument(true); break;
                case 8: PickLayoutDocument(false); break;
                case 6:
                    if (_layout?.Previous is { } previous) _ = EditLayoutAsync(_ => LauncherLayoutEdits.Clone(previous));
                    else Toast.MakeText(this, "No previous launcher layout is available.", ToastLength.Short)?.Show();
                    break;
            }
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }
    private void AskPageName(string title, string value, Func<string, Task> apply)
    {
        var input = new EditText(this) { Text = value, ContentDescription = "Launcher page name" }; input.SetSingleLine(true);
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(title); dialog.SetView(input);
        dialog.SetPositiveButton("Save", (_, _) => _ = apply(input.Text?.Trim() ?? ""));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }
    private void ShowPlacementMenu(LauncherApp app, LauncherPlacement? placement)
    {
        var inOtherContainer = placement is not null && _layout is not null && LauncherLayoutEdits.ContainerForPlacement(_layout.Current, placement.Id) != _layout.Current.ActivePageId;
        var options = placement is null ? new[] { "Add to current page", "Hide from app drawer", "Add to dock" }
            : new[] { "Move shortcut", "Remove shortcut", "Hide from app drawer", inOtherContainer ? "Move to current page" : "Move to dock" };
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(app.Label); dialog.SetItems(options, (_, e) =>
        {
            if (placement is null && e.Which == 2) _ = AddOrMoveToDockAsync(app.ApplicationId, null);
            else if (placement is not null && e.Which == 3)
            {
                if (inOtherContainer) _ = EditLayoutAsync(layout => LauncherLayoutEdits.MoveToContainer(layout, placement.Id, layout.ActivePageId));
                else _ = AddOrMoveToDockAsync(app.ApplicationId, placement);
            }
            else if (placement is null && e.Which == 0)
                _ = EditLayoutAsync(layout => LauncherLayoutEdits.AddApplication(layout, layout.ActivePageId, app.ApplicationId));
            else if (placement is not null && e.Which == 0)
            {
                _movingPlacementId = placement.Id; RenderPage();
                Toast.MakeText(this, "Choose a slot or another shortcut. Swipe to move to a different page.", ToastLength.Long)?.Show();
            }
            else if (placement is not null && e.Which == 1)
                _ = EditLayoutAsync(layout => LauncherLayoutEdits.RemovePlacement(layout, placement.Id));
            else _ = EditLayoutAsync(layout => LauncherLayoutEdits.SetHidden(layout, app.ApplicationId, true));
        }); dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }
    private void ShowHiddenApplications()
    {
        var hidden = _layout?.Current.HiddenApplications.ToArray() ?? [];
        if (hidden.Length == 0) { Toast.MakeText(this, "No apps are hidden from this launcher drawer.", ToastLength.Short)?.Show(); return; }
        var labels = hidden.Select(id => _apps.SingleOrDefault(a => a.ApplicationId == id)?.Label ?? "Unavailable application").ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Show app in drawer"); dialog.SetItems(labels,
            (_, e) => _ = EditLayoutAsync(layout => LauncherLayoutEdits.SetHidden(layout, hidden[e.Which], false)));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }
}
