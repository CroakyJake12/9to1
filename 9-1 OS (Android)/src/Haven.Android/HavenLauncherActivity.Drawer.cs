using Android.App;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private Guid? _drawerCategoryId;
#if ASTRA_ANDROID_CONTEXT_PROBE
    private EditText? _actualDrawerNameInput;
    private long _actualDrawerChoiceSequence;
    private long _actualDrawerAdmittedActionSequence;
    private Task _actualDrawerChoiceCompletion = Task.CompletedTask;
#endif
    private readonly HashSet<AlertDialog> _originalDrawerDialogs = [];
    private void CloseOriginalDrawerDialogs()
    {
        foreach (var dialog in _originalDrawerDialogs.ToArray()) dialog.Dismiss();
        _originalDrawerDialogs.Clear();
    }
    private sealed record OriginalDrawerChoice(Func<bool> Current, Func<bool> CanAccept, Func<Func<Task>, Task> Accept, Action<AlertDialog> Attach);
    private OriginalDrawerChoice CaptureOriginalDrawerChoice(LauncherStoredLayout expected)
    {
        var original = DisplayedLayouts.Require(expected);
        var root = _root; var epoch = _widgetRenderEpoch; var ct = _launcherLifetime.Token;
        bool Current() => _activityStarted && _homeReady && !ct.IsCancellationRequested && !IsFinishing && !IsDestroyed &&
            root is not null && root.IsAttachedToWindow && ReferenceEquals(_root, root) &&
            ReferenceEquals(_layout, expected) && _widgetRenderEpoch == epoch;
        AlertDialog? issuedDialog = null; var accepted = false;
        bool CanAccept() => !accepted && issuedDialog is not null && issuedDialog.IsShowing &&
            _originalDrawerDialogs.Contains(issuedDialog) && Current();
        void Attach(AlertDialog dialog)
        {
            if (issuedDialog is not null) throw new InvalidOperationException("The native drawer choice was already issued.");
            issuedDialog = dialog;
        }
        Task Accept(Func<Task> action)
        {
#if ASTRA_ANDROID_CONTEXT_PROBE
            ++_actualDrawerChoiceSequence;
            if (!CanAccept()) return _actualDrawerChoiceCompletion = Task.CompletedTask;
            accepted = true; // Reserve synchronously before Android dismisses the accepted native choice.
            return _actualDrawerChoiceCompletion = AcceptCore(action);
#else
            if (!CanAccept()) return Task.CompletedTask;
            accepted = true;
            return AcceptCore(action);
#endif
        }
        async Task AcceptCore(Func<Task> action)
        {
            try
            {
                if (!Current()) return;
                await WidgetSessions.RequireOriginalActorAsync(original, ct);
                if (!Current()) return;
#if ASTRA_ANDROID_CONTEXT_PROBE
                ++_actualDrawerAdmittedActionSequence;
#endif
                await action();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { if (Current()) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
        }
        return new(Current, CanAccept, Accept, Attach);
    }
    private void ShowOriginalDrawerDialog(AlertDialog.Builder builder, OriginalDrawerChoice choice)
    {
        if (!choice.Current()) return;
        var shown = builder.Create() ?? throw new InvalidOperationException("Android could not create the original drawer dialog.");
        if (!choice.Current()) { shown.Dismiss(); return; }
        choice.Attach(shown);
        _originalDrawerDialogs.Add(shown);
        shown.DismissEvent += (_, _) => _originalDrawerDialogs.Remove(shown);
        shown.Show();
    }

    private void AskOriginalDrawerName(string title, string initial, LauncherStoredLayout expected, Func<string, Task> save)
    {
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var input = new EditText(this); input.Text = initial;
#if ASTRA_ANDROID_CONTEXT_PROBE
        _actualDrawerNameInput = input;
#endif
        var builder = new AlertDialog.Builder(this); builder.SetTitle(title); builder.SetView(input);
        builder.SetPositiveButton("Save", (_, _) =>
        {
            var originalName = input.Text ?? "";
#if ASTRA_ANDROID_CONTEXT_PROBE
            if (Intent?.GetBooleanExtra("astra_drawer_name_probe", false) == true)
                input.Text = originalName + " native mutation after snapshot";
#endif
            _ = choice.Accept(() => save(originalName));
        });
        builder.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(builder, choice);
    }

    private void ShowDrawerOrganization()
    {
        var expected = _layout; if (expected is null) return;
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var drawer = expected.Current.Drawer ?? LauncherDrawer.Empty;
        var categories = drawer.Categories.ToArray();
        var labels = new[] { "Show all apps", "Add category", "Sort apps" }
            .Concat(categories.Select(c => c.Name)).ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Drawer categories");
        dialog.SetItems(labels, (_, e) => _ = choice.Accept(() =>
        {
            if (e.Which < 0 || e.Which >= labels.Length) return Task.CompletedTask;
            if (e.Which == 0) { _drawerCategoryId = null; _refreshDrawer?.Invoke(); }
            else if (e.Which == 1) AskOriginalDrawerName("Add category", "", expected, name => EditLayoutAsync(layout => LauncherLayoutEdits.AddDrawerCategory(layout, name), expected));
            else if (e.Which == 2) ShowDrawerSorting(expected);
            else ShowDrawerCategoryMenu(categories[e.Which - 3], expected);
            return Task.CompletedTask;
        }));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }

    private void ShowDrawerSorting(LauncherStoredLayout expected)
    {
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var options = new[] { "Name A–Z", "Name Z–A", "Category order" };
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Sort drawer apps");
        dialog.SetItems(options, (_, e) => _ = choice.Accept(() => EditLayoutAsync(layout => LauncherLayoutEdits.SetDrawerSort(layout, (LauncherDrawerSort)e.Which), expected)));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }

    private void ShowDrawerCategoryMenu(LauncherDrawerCategory category, LauncherStoredLayout expected)
    {
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(category.Name);
        dialog.SetItems(new[] { "Show category", "Rename", "Edit app order", "Remove category" }, (_, e) => _ = choice.Accept(() =>
        {
            if (e.Which < 0 || e.Which > 3) return Task.CompletedTask;
            if (e.Which == 0) { _drawerCategoryId = category.Id; _refreshDrawer?.Invoke(); }
            else if (e.Which == 1) AskOriginalDrawerName("Rename category", category.Name, expected,
                name => EditLayoutAsync(layout => LauncherLayoutEdits.RenameDrawerCategory(layout, category.Id, name), expected));
            else if (e.Which == 2) ShowDrawerCategoryOrder(category, expected);
            else
            {
                var confirmChoice = CaptureOriginalDrawerChoice(expected);
                var confirm = new AlertDialog.Builder(this); confirm.SetTitle("Remove category?");
                confirm.SetMessage("Applications and home shortcuts will stay in place.");
                confirm.SetPositiveButton("Remove", (_, _) => _ = confirmChoice.Accept(() => EditLayoutAsync(layout => LauncherLayoutEdits.RemoveDrawerCategory(layout, category.Id), expected)));
                confirm.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(confirm, confirmChoice);
            }
            return Task.CompletedTask;
        }));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }

    private void ShowDrawerCategoryOrder(LauncherDrawerCategory category, LauncherStoredLayout expected)
    {
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        if (category.Applications.Count == 0)
        { Toast.MakeText(this, "Long-press an app and choose Categories to add it.", ToastLength.Long)?.Show(); return; }
        var ids = category.Applications.ToArray();
        var labels = ids.Select(id => _apps.SingleOrDefault(a => a.ApplicationId == id)?.Label ?? "Unavailable application").ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Choose app to reorder");
        dialog.SetItems(labels, (_, e) => _ = choice.Accept(() =>
        {
            if (e.Which < 0 || e.Which >= ids.Length) return Task.CompletedTask;
            var id = ids[e.Which]; var actionChoice = CaptureOriginalDrawerChoice(expected); var actions = new AlertDialog.Builder(this); actions.SetTitle(labels[e.Which]);
            actions.SetItems(new[] { "Move earlier", "Move later", "Remove from category" }, (_, action) => _ = actionChoice.Accept(() =>
            {
                if (action.Which < 0 || action.Which > 2) return Task.CompletedTask;
                if (action.Which == 2) return EditLayoutAsync(layout => LauncherLayoutEdits.SetDrawerCategoryMembership(layout, category.Id, id, false), expected);
                else return EditLayoutAsync(layout => LauncherLayoutEdits.ReorderDrawerApplication(layout, category.Id, id, action.Which == 0 ? -1 : 1), expected);
            }));
            actions.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(actions, actionChoice);
            return Task.CompletedTask;
        }));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }

    private void ShowAppCategories(LauncherApp app, LauncherStoredLayout expected)
    {
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        if (!ReferenceEquals(_layout, expected)) return;
        var categories = expected.Current.Drawer?.Categories.ToArray() ?? [];
        if (categories.Length == 0) { ShowDrawerOrganization(); return; }
        var included = categories.Select(c => c.Applications.Contains(app.ApplicationId)).ToArray();
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(app.Label + " · " + app.ProfileLabel + " categories");
        dialog.SetMultiChoiceItems(categories.Select(c => c.Name).ToArray(), included, (_, e) => { if (choice.CanAccept() && e.Which >= 0 && e.Which < included.Length) included[e.Which] = e.IsChecked; });
        dialog.SetPositiveButton("Save", (_, _) => _ = choice.Accept(() => EditLayoutAsync(layout =>
        {
            for (var index = 0; index < categories.Length; index++)
                layout = LauncherLayoutEdits.SetDrawerCategoryMembership(layout, categories[index].Id, app.ApplicationId, included[index]);
            return layout;
        }, expected)));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }
}
