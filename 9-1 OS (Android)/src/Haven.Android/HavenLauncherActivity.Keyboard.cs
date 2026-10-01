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
        if (keyCode == Keycode.Escape && _movingPlacementId is not null)
        {
            _movingPlacementId = null; RenderPage(); return true;
        }
        if (keyCode == Keycode.Menu)
        {
            if (e.RepeatCount == 0) ShowPagesMenu();
            return true;
        }
        return base.OnKeyDown(keyCode, e);
    }
}
