using Android.App;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private Guid? _drawerCategoryId;

    private void ShowDrawerOrganization()
    {
        var expected = _layout; if (expected is null) return;
        var drawer = expected.Current.Drawer ?? LauncherDrawer.Empty;
        var categories = drawer.Categories.ToArray();
        var labels = new[] { "Show all apps", "Add category", "Sort apps" }
            .Concat(categories.Select(c => c.Name)).ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Drawer categories");
        dialog.SetItems(labels, (_, e) =>
        {
            if (e.Which == 0) { _drawerCategoryId = null; _refreshDrawer?.Invoke(); }
            else if (e.Which == 1) AskPageName("Add category", "", name => EditLayoutAsync(layout => LauncherLayoutEdits.AddDrawerCategory(layout, name), expected));
            else if (e.Which == 2) ShowDrawerSorting(expected);
            else ShowDrawerCategoryMenu(categories[e.Which - 3], expected);
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }

    private void ShowDrawerSorting(LauncherStoredLayout expected)
    {
        var options = new[] { "Name A–Z", "Name Z–A", "Category order" };
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Sort drawer apps");
        dialog.SetItems(options, (_, e) => _ = EditLayoutAsync(layout => LauncherLayoutEdits.SetDrawerSort(layout, (LauncherDrawerSort)e.Which), expected));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }

    private void ShowDrawerCategoryMenu(LauncherDrawerCategory category, LauncherStoredLayout expected)
    {
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(category.Name);
        dialog.SetItems(new[] { "Show category", "Rename", "Edit app order", "Remove category" }, (_, e) =>
        {
            if (e.Which == 0) { _drawerCategoryId = category.Id; _refreshDrawer?.Invoke(); }
            else if (e.Which == 1) AskPageName("Rename category", category.Name,
                name => EditLayoutAsync(layout => LauncherLayoutEdits.RenameDrawerCategory(layout, category.Id, name), expected));
            else if (e.Which == 2) ShowDrawerCategoryOrder(category, expected);
            else
            {
                var confirm = new AlertDialog.Builder(this); confirm.SetTitle("Remove category?");
                confirm.SetMessage("Applications and home shortcuts will stay in place.");
                confirm.SetPositiveButton("Remove", (_, _) => _ = EditLayoutAsync(layout => LauncherLayoutEdits.RemoveDrawerCategory(layout, category.Id), expected));
                confirm.SetNegativeButton("Cancel", (_, _) => { }); confirm.Show();
            }
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }

    private void ShowDrawerCategoryOrder(LauncherDrawerCategory category, LauncherStoredLayout expected)
    {
        if (category.Applications.Count == 0)
        { Toast.MakeText(this, "Long-press an app and choose Categories to add it.", ToastLength.Long)?.Show(); return; }
        var ids = category.Applications.ToArray();
        var labels = ids.Select(id => _apps.SingleOrDefault(a => a.ApplicationId == id)?.Label ?? "Unavailable application").ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Choose app to reorder");
        dialog.SetItems(labels, (_, e) =>
        {
            var id = ids[e.Which]; var actions = new AlertDialog.Builder(this); actions.SetTitle(labels[e.Which]);
            actions.SetItems(new[] { "Move earlier", "Move later", "Remove from category" }, (_, action) =>
            {
                if (action.Which == 2) _ = EditLayoutAsync(layout => LauncherLayoutEdits.SetDrawerCategoryMembership(layout, category.Id, id, false), expected);
                else _ = EditLayoutAsync(layout => LauncherLayoutEdits.ReorderDrawerApplication(layout, category.Id, id, action.Which == 0 ? -1 : 1), expected);
            });
            actions.SetNegativeButton("Cancel", (_, _) => { }); actions.Show();
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }

    private void ShowAppCategories(LauncherApp app)
    {
        var expected = _layout; if (expected is null) return;
        var categories = expected.Current.Drawer?.Categories.ToArray() ?? [];
        if (categories.Length == 0) { ShowDrawerOrganization(); return; }
        var included = categories.Select(c => c.Applications.Contains(app.ApplicationId)).ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(app.Label + " · " + app.ProfileLabel + " categories");
        dialog.SetMultiChoiceItems(categories.Select(c => c.Name).ToArray(), included, (_, e) => included[e.Which] = e.IsChecked);
        dialog.SetPositiveButton("Save", (_, _) => _ = EditLayoutAsync(layout =>
        {
            for (var index = 0; index < categories.Length; index++)
                layout = LauncherLayoutEdits.SetDrawerCategoryMembership(layout, categories[index].Id, app.ApplicationId, included[index]);
            return layout;
        }, expected));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }
}
