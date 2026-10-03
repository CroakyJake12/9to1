namespace NineToOne.Launcher;

public enum LauncherGesture { SwipeUp, SwipeDown, SwipeLeft, SwipeRight, DoubleTap }
public enum LauncherCommand { None, OpenDrawer, PreviousPage, NextPage, OpenPageManager, OpenSettings }

/// <summary>Portable navigation bindings. Commands carry no platform identity or resource grant.</summary>
public sealed record LauncherGestures(LauncherCommand SwipeUp = LauncherCommand.OpenDrawer,
    LauncherCommand SwipeDown = LauncherCommand.None, LauncherCommand SwipeLeft = LauncherCommand.NextPage,
    LauncherCommand SwipeRight = LauncherCommand.PreviousPage, LauncherCommand DoubleTap = LauncherCommand.None)
{
    public void Validate()
    {
        if (!Enum.IsDefined(SwipeUp) || !Enum.IsDefined(SwipeDown) || !Enum.IsDefined(SwipeLeft) ||
            !Enum.IsDefined(SwipeRight) || !Enum.IsDefined(DoubleTap))
            throw new InvalidDataException("This gesture binding requires a compatible Launcher version.");
    }
    public LauncherCommand Resolve(LauncherGesture gesture) => gesture switch
    {
        LauncherGesture.SwipeUp => SwipeUp, LauncherGesture.SwipeDown => SwipeDown,
        LauncherGesture.SwipeLeft => SwipeLeft, LauncherGesture.SwipeRight => SwipeRight,
        LauncherGesture.DoubleTap => DoubleTap, _ => throw new ArgumentOutOfRangeException(nameof(gesture))
    };
    public LauncherGestures With(LauncherGesture gesture, LauncherCommand command)
    {
        var next = gesture switch
        {
            LauncherGesture.SwipeUp => this with { SwipeUp = command },
            LauncherGesture.SwipeDown => this with { SwipeDown = command },
            LauncherGesture.SwipeLeft => this with { SwipeLeft = command },
            LauncherGesture.SwipeRight => this with { SwipeRight = command },
            LauncherGesture.DoubleTap => this with { DoubleTap = command },
            _ => throw new ArgumentOutOfRangeException(nameof(gesture))
        };
        next.Validate(); return next;
    }
}

public static partial class LauncherLayoutEdits
{
    public static LauncherLayout SetGestures(LauncherLayout layout, LauncherGestures gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures); gestures.Validate();
        return Checked(layout with { Gestures = gestures });
    }
}

/// <summary>Single-pointer background gestures; cancellation and multi-touch cannot produce commands.</summary>
public sealed class LauncherGestureRecognizer(float swipeThreshold, float tapSlop, long doubleTapMilliseconds = 350)
{
    private readonly float _swipe = float.IsFinite(swipeThreshold) && swipeThreshold > 0 ? swipeThreshold : throw new ArgumentOutOfRangeException(nameof(swipeThreshold));
    private readonly float _slop = float.IsFinite(tapSlop) && tapSlop > 0 ? tapSlop : throw new ArgumentOutOfRangeException(nameof(tapSlop));
    private readonly long _doubleTap = doubleTapMilliseconds is > 0 and <= 1000 ? doubleTapMilliseconds : throw new ArgumentOutOfRangeException(nameof(doubleTapMilliseconds));
    private bool _active, _tapEligible;
    private float _x, _y, _lastX, _lastY;
    private long _down, _lastTap = -1;
    public bool WasTap { get; private set; }
    public void Cancel() { _active = false; _lastTap = -1; WasTap = false; }
    public void Down(float x, float y, long milliseconds, int pointers)
    {
        if (pointers != 1 || !float.IsFinite(x) || !float.IsFinite(y) || milliseconds < 0) { Cancel(); return; }
        _active = true; _tapEligible = true; _x = x; _y = y; _down = milliseconds;
    }
    public void Move(float x, float y, int pointers)
    {
        if (pointers != 1 || !float.IsFinite(x) || !float.IsFinite(y)) { Cancel(); return; }
        if (Math.Abs(x - _x) > _slop || Math.Abs(y - _y) > _slop) _tapEligible = false;
    }
    public LauncherGesture? Up(float x, float y, long milliseconds, int pointers)
    {
        WasTap = false;
        if (!_active || pointers != 1 || !float.IsFinite(x) || !float.IsFinite(y) || milliseconds < _down || milliseconds - _down > 1000)
        { Cancel(); return null; }
        _active = false;
        var dx = x - _x; var dy = y - _y;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) >= _swipe)
        {
            _lastTap = -1;
            return Math.Abs(dx) > Math.Abs(dy)
                ? dx < 0 ? LauncherGesture.SwipeLeft : LauncherGesture.SwipeRight
                : dy < 0 ? LauncherGesture.SwipeUp : LauncherGesture.SwipeDown;
        }
        if (!_tapEligible || Math.Abs(dx) > _slop || Math.Abs(dy) > _slop || milliseconds - _down > _doubleTap)
        { _lastTap = -1; return null; }
        WasTap = true;
        var doubleTap = _lastTap >= 0 && _down >= _lastTap && milliseconds - _lastTap <= _doubleTap &&
            Math.Abs(x - _lastX) <= _slop && Math.Abs(y - _lastY) <= _slop;
        _lastTap = doubleTap ? -1 : milliseconds; _lastX = x; _lastY = y;
        return doubleTap ? LauncherGesture.DoubleTap : null;
    }
}
