using Android.Content;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        // A genuine HOME navigation closes transient surfaces, never adopts an actor or executes an app.
        if (intent?.Action != global::Android.Content.Intent.ActionMain || !intent.HasCategory(global::Android.Content.Intent.CategoryHome) ||
            _launcherLifetime.IsCancellationRequested || IsDestroyed || IsFinishing) return;
#if ASTRA_ANDROID_NATIVE_INPUT_PROBE
        // Android singleTask OnNewIntent does not replace Activity.Intent. Retain only this diagnostic
        // request bit from the actual framework HOME intent, without adopting any actor or command.
        _nativeControllerProbeRequested = intent.GetBooleanExtra("astra_native_controller_probe", false);
#endif
        RetireOriginalWidgetSelection();
        CloseLayoutDocumentDialogs();
        CloseAppDrawer();
        _folderDialog?.Dismiss();
        CloseGestureDialogs();
        CloseLauncherSettingsDialog();
        CloseApplicationShortcutDialog();
        ClosePlacementMenu();
        CloseOriginalDrawerDialogs();
        CloseWidgetDialogs();
        PauseLauncherDulche();
        _movingPlacementId = null;
        // Invalidate an already queued page transition before it can restore the previous move state.
        var generation = Interlocked.Increment(ref _widgetRenderEpoch);
        if (!_homeReady || !_activityStarted) return;
        _grid?.Post(() =>
        {
            if (_activityStarted && !_launcherLifetime.IsCancellationRequested && generation == _widgetRenderEpoch)
                RenderPage();
        });
    }
}
