#if ASTRA_ANDROID_NATIVE_INPUT_PROBE
using Android.Views;
using NineToOne.Launcher;
using System.Text.Json;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private bool _nativeInputProbeStarted, _nativeInputDispatching, _nativeControllerProbeRequested;
    private Task? _nativeInputCompletion;
    private LauncherSessionSnapshot? _nativeInputOriginal;
    private Guid _nativeInputTarget;
    private long _nativeInputDownTime, _nativeInputEventTime;
    private int _nativeInputDevice, _nativeInputSource;
    private void BeginActualNativeControllerProbe(Keycode keyCode, KeyEvent native)
    {
        if (_nativeInputProbeStarted || keyCode != Keycode.ButtonR1 || (!_nativeControllerProbeRequested && Intent?.GetBooleanExtra("astra_native_controller_probe", false) != true)) return;
        _nativeInputProbeStarted = true;
        try
        {
            var role = ObserveDefaultHomeRole();
            if (!role.ResolvedHere || (role.Supported && !role.Held) || !HasWindowFocus || _root?.IsAttachedToWindow != true)
                throw new InvalidOperationException("Actual default HOME role, resolved component and focused attached native window required.");
            var layout = _layout ?? throw new InvalidOperationException("Actual Home layout required.");
            if (_page < 0 || _page + 1 >= layout.Current.Pages.Count) throw new InvalidOperationException("An actual next Home page is required; no synthetic page is supplied.");
            _nativeInputOriginal = DisplayedLayouts.Require(layout);
            _nativeInputTarget = layout.Current.Pages[_page + 1].Id;
            // Freeze actual framework event scalars before any awaited owner action; retain no mutable native event.
            _nativeInputDownTime = native.DownTime; _nativeInputEventTime = native.EventTime;
            _nativeInputDevice = native.DeviceId; _nativeInputSource = (int)native.Source;
            _nativeInputDispatching = true;
        }
        catch (Exception error) { LogNativeInputFailure(error.Message); }
    }
    public override bool OnKeyUp(Keycode keyCode, KeyEvent? native)
    {
        if (keyCode == Keycode.ButtonR1 && native is not null && _nativeInputOriginal is not null && native.DownTime == _nativeInputDownTime)
        {
            var original = _nativeInputOriginal; _nativeInputOriginal = null;
            var completion = _nativeInputCompletion;
            var upTime = native.EventTime; var upDevice = native.DeviceId; var upSource = (int)native.Source;
            _ = VerifyActualNativeControllerAsync(original, completion, upTime, upDevice, upSource);
        }
        return base.OnKeyUp(keyCode, native);
    }
    private async Task VerifyActualNativeControllerAsync(LauncherSessionSnapshot original, Task? completion, long upTime, int upDevice, int upSource)
    {
        try
        {
            if (completion is null || upDevice != _nativeInputDevice || upSource != _nativeInputSource || upTime < _nativeInputEventTime)
                throw new InvalidOperationException("Matching actual native Down/Up and exact production page task required.");
            await completion.WaitAsync(TimeSpan.FromSeconds(10));
            var role = ObserveDefaultHomeRole();
            if (!role.ResolvedHere || (role.Supported && !role.Held) || !_activityStarted || !HasWindowFocus)
                throw new InvalidOperationException("Actual default HOME/window retired before owning proof.");
            var displayed = _layout ?? throw new InvalidOperationException("Actual renewed Home view unavailable.");
            var root = _root; var epoch = _widgetRenderEpoch;
            var current = DisplayedLayouts.Require(displayed);
            var actor = await WidgetSessions.RequireOriginalActorAsync(current, _launcherLifetime.Token);
            if (actor != original.Actor) throw new UnauthorizedAccessException("Native input changed original Home actor.");
            var persisted = await LayoutStore.ReadExistingForActorAsync(actor, _launcherLifetime.Token)
                ?? throw new IOException("Actual persisted Home record unavailable.");
            var finalRole = ObserveDefaultHomeRole();
            if (!finalRole.ResolvedHere || (finalRole.Supported && !finalRole.Held) || !_activityStarted || !HasWindowFocus ||
                root?.IsAttachedToWindow != true || !ReferenceEquals(root, _root) || epoch != _widgetRenderEpoch || !ReferenceEquals(displayed, _layout))
                throw new UnauthorizedAccessException("Actual default HOME original rendered view retired during owner read.");
            if (persisted.AuthorityId != original.Layout.AuthorityId || persisted.Revision != original.Layout.Revision + 1 ||
                persisted.Current.ActivePageId != _nativeInputTarget || current.Layout.Revision != persisted.Revision ||
                current.Layout.Current.ActivePageId != _nativeInputTarget)
                throw new IOException("Actual native input did not produce exact next-page durable Home receipt and renewed view.");
            global::Android.Util.Log.Info("AstraNativeInputProbe", JsonSerializer.Serialize(new
            { success = true, code = "ActualDefaultHomeNativeControllerPageSaved", nativeKey = "BUTTON_R1", _nativeInputDevice, _nativeInputSource,
                downTime = _nativeInputDownTime, downEventTime = _nativeInputEventTime, upTime,
                beforeRevision = original.Layout.Revision, savedRevision = persisted.Revision, pageId = _nativeInputTarget,
                authorityId = persisted.AuthorityId, processId = global::Android.OS.Process.MyPid(), nativeUid = global::Android.OS.Process.MyUid(),
                defaultHomeRole = true, physicalHardware = false, inputOriginRequiresRunnerEvidence = true }));
        }
        catch (Exception error) { LogNativeInputFailure(error.Message); }
    }
    private static void LogNativeInputFailure(string reason) => global::Android.Util.Log.Error("AstraNativeInputProbe",
        JsonSerializer.Serialize(new { success = false, code = "ActualDefaultHomeNativeControllerPageSaved", reason, physicalHardware = false }));
}
#endif
