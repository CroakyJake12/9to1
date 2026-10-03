using Android.App;
using Android.Widget;
using Haven.Desktop;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private AlertDialog? _applicationShortcutDialog;
    private long _applicationShortcutGeneration;
    private void CloseApplicationShortcutDialog()
    { ++_applicationShortcutGeneration; _applicationShortcutDialog?.Dismiss(); _applicationShortcutDialog = null; }
    private async Task ShowApplicationShortcutsAsync(LauncherApp app, LauncherStoredLayout expected)
    {
        CloseApplicationShortcutDialog(); var originalGeneration = _applicationShortcutGeneration;
        var originalEpoch = _widgetRenderEpoch; var originalRoot = _root; var ct = _launcherLifetime.Token;
        bool Current() => _activityStarted && _homeReady && !ct.IsCancellationRequested && !IsFinishing && !IsDestroyed &&
            originalRoot is not null && originalRoot.IsAttachedToWindow && originalGeneration == _applicationShortcutGeneration &&
            ReferenceEquals(_layout, expected) && ReferenceEquals(_root, originalRoot) && originalEpoch == _widgetRenderEpoch;
        try
        {
            if (!Current()) return;
            var original = DisplayedLayouts.Require(expected);
            var originalActor = await WidgetSessions.RequireOriginalActorAsync(original, ct);
            if (!Current()) return;
            var owner = (App.Services ?? throw new InvalidOperationException("Home is unavailable."))
                .GetRequiredService<AndroidInstalledApplicationShortcuts>();
            var shortcuts = await owner.QueryForActorAsync(app.ApplicationId, app.RegistryRevision, originalActor, ct);
            if (!Current()) return;
            var originalSessionCurrent = await WidgetSessions.IsCurrentAsync(original, ct);
#if ASTRA_ANDROID_CONTEXT_PROBE
            await TryHoldShortcutPublicationResultAsync(app, expected, originalSessionCurrent, originalGeneration);
#endif
            if (!originalSessionCurrent || !Current()) return;
            if (shortcuts.Count == 0)
            { if (Current()) Toast.MakeText(this, "This app has no available shortcuts in its current profile.", ToastLength.Short)?.Show(); return; }
            var dialog = new AlertDialog.Builder(this); dialog.SetTitle(app.Label + " · " + app.ProfileLabel + " shortcuts");
            dialog.SetItems(shortcuts.Select(shortcut => shortcut.Label).ToArray(), (_, e) =>
            { if (Current() && e.Which >= 0 && e.Which < shortcuts.Count) _ = InvokeApplicationShortcutAsync(owner, shortcuts[e.Which], Current); });
            dialog.SetNegativeButton("Cancel", (_, _) => { });
            var shown = dialog.Create() ?? throw new InvalidOperationException("Android could not create the original shortcut dialog.");
            // This continuation runs on the Android UI thread. Do not publish across an await.
            if (!Current()) { shown.Dismiss(); return; }
            _applicationShortcutDialog = shown;
            shown.DismissEvent += (_, _) => { if (ReferenceEquals(_applicationShortcutDialog, shown)) _applicationShortcutDialog = null; };
            if (!Current()) { shown.Dismiss(); return; } shown.Show();
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or global::Java.Lang.SecurityException or global::Android.Content.ActivityNotFoundException or global::Java.Lang.IllegalStateException or global::Java.Lang.IllegalArgumentException)
        { if (Current()) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
#if ASTRA_ANDROID_CONTEXT_PROBE
        finally { VerifyShortcutPublicationAfterOuterContinuation(originalGeneration); }
#endif
    }

#if ASTRA_ANDROID_CONTEXT_PROBE
    private bool _shortcutPublicationProbeStarted;
    private long _shortcutPublicationProbeHeldGeneration;
    private AlertDialog? _shortcutPublicationProbeNewerDialog;
    private async Task TryHoldShortcutPublicationResultAsync(LauncherApp app, LauncherStoredLayout expected, bool actualHomeCurrent, long heldGeneration)
    {
        if (_shortcutPublicationProbeStarted || Intent?.GetBooleanExtra("astra_shortcut_publication_probe", false) != true) return;
        _shortcutPublicationProbeStarted = true;
        if (!actualHomeCurrent)
        { global::Android.Util.Log.Error("AstraShortcutPublicationProbe", "ActualOriginalHomeSessionUnavailable"); return; }
        // Hold this actual Home result continuation while a later real dialog generation publishes.
        await ShowApplicationShortcutsAsync(app, expected);
        var newer = _applicationShortcutDialog;
        if (newer is null || !newer.IsShowing)
        { global::Android.Util.Log.Error("AstraShortcutPublicationProbe", "ActualAvailableInstalledShortcutDialogRequired"); return; }
        _shortcutPublicationProbeHeldGeneration = heldGeneration;
        _shortcutPublicationProbeNewerDialog = newer;
    }
    private void VerifyShortcutPublicationAfterOuterContinuation(long completedGeneration)
    {
        var newer = _shortcutPublicationProbeNewerDialog;
        if (newer is null || completedGeneration != _shortcutPublicationProbeHeldGeneration) return;
        _shortcutPublicationProbeNewerDialog = null;
            if (!ReferenceEquals(_applicationShortcutDialog, newer) || !newer.IsShowing)
            { global::Android.Util.Log.Error("AstraShortcutPublicationProbe", "ActualNewerShortcutDialogOwnershipLost"); return; }
            CloseApplicationShortcutDialog();
            if (_applicationShortcutDialog is not null || newer.IsShowing)
            { global::Android.Util.Log.Error("AstraShortcutPublicationProbe", "ActualNewerShortcutDialogLifecycleCloseFailed"); return; }
            global::Android.Util.Log.Info("AstraShortcutPublicationProbe", "ActualHeldHomeResultNewerNativeDialogPreservedAndClosed");
    }
#endif

    private async Task InvokeApplicationShortcutAsync(AndroidInstalledApplicationShortcuts owner, AndroidOriginalShortcutSelection shortcut, Func<bool> originalHostCurrent)
    {
        try { await owner.InvokeOriginalAsync(shortcut, originalHostCurrent, _launcherLifetime.Token); }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or global::Java.Lang.SecurityException or global::Android.Content.ActivityNotFoundException or global::Java.Lang.IllegalStateException or global::Java.Lang.IllegalArgumentException)
        { if (!_launcherLifetime.IsCancellationRequested) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
    }
}
