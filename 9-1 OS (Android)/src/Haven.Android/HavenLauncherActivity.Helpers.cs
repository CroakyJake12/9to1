using NineToOne.Launcher;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private GradientDrawable MagicalBackground(int radius)
    {
        var background = HavenNativeAccentPalette.Launcher.Primary.Create(radius);
        background.SetStroke(Dp(1), Color.Argb(210, 213, 165, 255));
        return background;
    }

    private static GradientDrawable RoundedBackground(Color color, int radius)
    {
        var background = new GradientDrawable();
        background.SetColor(color);
        background.SetCornerRadius(radius);
        return background;
    }

    private int Dp(int value)
        => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1f));

    private sealed record LauncherApp(
        string Label,
        string PackageName,
        string ActivityName,
        Drawable? Icon,
        Guid ApplicationId,
        long RegistryRevision,
        string PlatformProfileId,
        string ProfileLabel,
        bool IsCurrentProfile,
        bool Available)
    {
        public string Key => ApplicationId.ToString("D");
        public string LegacyPersonalKey => PackageName + "/" + ActivityName;
    }

    private sealed class SwipeTouchListener(float swipeThresholdPixels, float tapSlopPixels,
        Func<Func<bool>?> captureOriginal, Action<LauncherGesture> dispatch) : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly AndroidLauncherGestureInput _input = new(swipeThresholdPixels, tapSlopPixels, captureOriginal);
        public bool OnTouch(View? view, MotionEvent? e)
        {
            if (e is null) { _input.Cancel(); return false; }
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _input.Down(e.RawX, e.RawY, e.EventTime, e.PointerCount); break;
                case MotionEventActions.Move:
                    _input.Move(e.RawX, e.RawY, e.PointerCount); break;
                case MotionEventActions.Up:
                    var gesture = _input.Up(e.RawX, e.RawY, e.EventTime, e.PointerCount);
                    if (gesture is { } value) dispatch(value); else if (_input.WasTap) view?.PerformClick();
                    break;
                case MotionEventActions.Cancel:
                case MotionEventActions.PointerDown:
                case MotionEventActions.PointerUp:
                case MotionEventActions.Outside:
                    _input.Cancel(); break;
            }
            return true;
        }
    }
}
