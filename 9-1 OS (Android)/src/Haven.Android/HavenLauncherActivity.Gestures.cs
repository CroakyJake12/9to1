using Android.App;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private AlertDialog? _gestureDialog, _gestureChoiceDialog;
    private void CloseGestureDialogs()
    {
        _gestureChoiceDialog?.Dismiss(); _gestureChoiceDialog = null;
        _gestureDialog?.Dismiss(); _gestureDialog = null;
    }
    private Func<bool>? CaptureOriginalGestureView()
    {
        if (!_homeReady || !_activityStarted || _launcherLifetime.IsCancellationRequested || _layout is null || _root is null) return null;
        var expected = _layout; var root = _root; var epoch = _widgetRenderEpoch;
        return () => _homeReady && _activityStarted && !_launcherLifetime.IsCancellationRequested &&
            ReferenceEquals(_layout, expected) && ReferenceEquals(_root, root) && epoch == _widgetRenderEpoch && root.IsAttachedToWindow;
    }
    private void RunGesture(LauncherGesture gesture)
    {
        if (!_homeReady || !_activityStarted || _launcherLifetime.IsCancellationRequested || IsFinishing || IsDestroyed || _layout is null) return;
        RunLauncherCommand((_layout.Current.Gestures ?? new()).Resolve(gesture));
    }

    private void RunLauncherCommand(LauncherCommand command)
    {
        if (!_homeReady || !_activityStarted || _launcherLifetime.IsCancellationRequested || IsFinishing || IsDestroyed) return;
        switch (command)
        {
            case LauncherCommand.None: break;
            case LauncherCommand.OpenDrawer: ShowAppDrawer(); break;
            case LauncherCommand.PreviousPage: ChangePage(-1); break;
            case LauncherCommand.NextPage: ChangePage(1); break;
            case LauncherCommand.OpenPageManager: ShowPagesMenu(); break;
            case LauncherCommand.OpenSettings: ShowLauncherSettings(); break;
            default: throw new InvalidOperationException("This Launcher command is unavailable.");
        }
    }

    private void ShowGestureSettings()
    {
        var expected = _layout; if (expected is null) return;
        CloseGestureDialogs();
        var draft = expected.Current.Gestures ?? new();
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        var names = new[] { "Swipe up", "Swipe down", "Swipe left", "Swipe right", "Double-tap background" };
        var commands = Enum.GetValues<LauncherCommand>();
        var labels = new[] { "No action", "Open app drawer", "Previous page", "Next page", "Manage pages", "Launcher settings" };
        foreach (var gesture in Enum.GetValues<LauncherGesture>())
        {
            var button = new Button(this);
            void Refresh() => button.Text = names[(int)gesture] + ": " + labels[Array.IndexOf(commands, draft.Resolve(gesture))];
            Refresh();
            button.Click += (_, _) =>
            {
                var picker = new AlertDialog.Builder(this); picker.SetTitle(names[(int)gesture]);
                picker.SetSingleChoiceItems(labels, Array.IndexOf(commands, draft.Resolve(gesture)), (sender, args) =>
                {
                    draft = draft.With(gesture, commands[args.Which]); Refresh();
                    if (sender is AlertDialog choiceDialog) choiceDialog.Dismiss();
                });
                picker.SetNegativeButton("Cancel", (_, _) => { });
                _gestureChoiceDialog?.Dismiss(); _gestureChoiceDialog = picker.Show();
            };
            panel.AddView(button);
        }
        var scroll = new ScrollView(this); scroll.AddView(panel);
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Background gestures"); dialog.SetView(scroll);
        dialog.SetPositiveButton("Save", (_, _) => _ = EditLayoutAsync(layout => LauncherLayoutEdits.SetGestures(layout, draft), expected));
        dialog.SetNeutralButton("Defaults", (_, _) => ShowGestureSettingsDefaults(expected));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); _gestureDialog = dialog.Show();
    }

    private void ShowGestureSettingsDefaults(LauncherStoredLayout expected)
    {
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Restore default gestures?");
        dialog.SetMessage("Swipe up opens the app drawer. Left and right change pages. Swipe down and double-tap have no action.");
        dialog.SetPositiveButton("Restore", (_, _) => _ = EditLayoutAsync(layout => LauncherLayoutEdits.SetGestures(layout, new()), expected));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); _gestureDialog = dialog.Show();
    }
}
