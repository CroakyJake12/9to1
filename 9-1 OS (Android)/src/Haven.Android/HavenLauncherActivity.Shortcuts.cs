using Android.App;
using Android.Widget;
using Haven.Desktop;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private async Task ShowApplicationShortcutsAsync(LauncherApp app)
    {
        try
        {
            var owner = (App.Services ?? throw new InvalidOperationException("Home is unavailable."))
                .GetRequiredService<AndroidInstalledApplicationShortcuts>();
            var shortcuts = await owner.QueryAsync(app.ApplicationId, app.RegistryRevision, _launcherLifetime.Token);
            if (_launcherLifetime.IsCancellationRequested || IsFinishing || IsDestroyed) return;
            if (shortcuts.Count == 0)
            { Toast.MakeText(this, "This app has no available shortcuts in its current profile.", ToastLength.Short)?.Show(); return; }
            var dialog = new AlertDialog.Builder(this); dialog.SetTitle(app.Label + " · " + app.ProfileLabel + " shortcuts");
            dialog.SetItems(shortcuts.Select(shortcut => shortcut.Label).ToArray(), (_, e) => _ = InvokeApplicationShortcutAsync(owner, shortcuts[e.Which]));
            dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or global::Java.Lang.SecurityException or global::Android.Content.ActivityNotFoundException or global::Java.Lang.IllegalStateException or global::Java.Lang.IllegalArgumentException)
        { if (!_launcherLifetime.IsCancellationRequested) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
    }

    private async Task InvokeApplicationShortcutAsync(AndroidInstalledApplicationShortcuts owner, AndroidApplicationShortcut shortcut)
    {
        try { await owner.InvokeAsync(shortcut, _launcherLifetime.Token); }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or global::Java.Lang.SecurityException or global::Android.Content.ActivityNotFoundException or global::Java.Lang.IllegalStateException or global::Java.Lang.IllegalArgumentException)
        { if (!_launcherLifetime.IsCancellationRequested) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
    }
}
