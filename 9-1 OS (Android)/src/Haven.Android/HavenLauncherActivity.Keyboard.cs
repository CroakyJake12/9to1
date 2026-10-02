using Android.Views;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (!_homeReady || !_activityStarted || _layout is null || e is null)
            return base.OnKeyDown(keyCode, e);
        if (e.IsCtrlPressed && (keyCode is Keycode.PageUp or Keycode.PageDown))
        {
            if (e.RepeatCount == 0) ChangePage(keyCode == Keycode.PageUp ? -1 : 1);
            return true;
        }
        if (e.IsCtrlPressed && keyCode == Keycode.Space)
        {
            if (e.RepeatCount == 0) ShowAppDrawer();
            return true;
        }
        if (keyCode is Keycode.ButtonL1 or Keycode.ButtonR1)
        {
            if (e.RepeatCount == 0)
            {
#if ASTRA_ANDROID_NATIVE_INPUT_PROBE
                BeginActualNativeControllerProbe(keyCode, e);
                try { ChangePage(keyCode == Keycode.ButtonL1 ? -1 : 1); }
                finally { _nativeInputDispatching = false; }
#else
                ChangePage(keyCode == Keycode.ButtonL1 ? -1 : 1);
#endif
            }
            return true;
        }
        if ((keyCode is Keycode.Escape or Keycode.ButtonB) && _movingPlacementId is not null)
        {
            _movingPlacementId = null; RenderPage(); return true;
        }
        if (keyCode is Keycode.Menu or Keycode.ButtonY)
        {
            if (e.RepeatCount != 0) return true;
            // Use the real focused tile's existing owner callback, never a reconstructed app command.
            var focused = CurrentFocus;
            if (focused is not null && focused.IsAttachedToWindow && focused.LongClickable && IsCurrentLauncherDescendant(focused) &&
                focused.PerformLongClick()) return true;
            if (keyCode == Keycode.Menu) { ShowPagesMenu(); return true; }
        }
        return base.OnKeyDown(keyCode, e);
    }
    private bool IsCurrentLauncherDescendant(View view)
    {
        for (View? current = view; current is not null; current = current.Parent as View)
            if (ReferenceEquals(current, _root)) return true;
        return false;
    }
}
