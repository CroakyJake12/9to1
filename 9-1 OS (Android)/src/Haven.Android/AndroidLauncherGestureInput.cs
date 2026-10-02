using NineToOne.Launcher;

namespace Haven.Android;

/// <summary>Existing recognizer with retained original-view lifetime, not a navigation or resource grant.</summary>
public sealed class AndroidLauncherGestureInput(float threshold, float slop, Func<Func<bool>?> captureOriginalView)
{
    private readonly LauncherGestureRecognizer input = new(threshold, slop);
    private Func<bool>? original;
    public bool WasTap => original?.Invoke() == true && input.WasTap;
    public void Cancel() { input.Cancel(); original = null; }
    public void Down(float x, float y, long milliseconds, int pointers)
    {
        // A double tap may join only the same still-current original view as its first tap.
        if (original?.Invoke() != true) input.Cancel();
        original = captureOriginalView();
        if (original?.Invoke() != true) { Cancel(); return; }
        input.Down(x, y, milliseconds, pointers);
    }
    public void Move(float x, float y, int pointers)
    {
        if (original?.Invoke() != true) { Cancel(); return; }
        input.Move(x, y, pointers);
    }
    public LauncherGesture? Up(float x, float y, long milliseconds, int pointers)
    {
        if (original?.Invoke() != true) { Cancel(); return null; }
        return input.Up(x, y, milliseconds, pointers);
    }
}
