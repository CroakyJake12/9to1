// CUI Motion — ported from HavenUiMotion.cs.
// Original source: 9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenUiMotion.cs
//
// Canonical CUI motion durations used by code-created controls.

namespace CakeOS.Cui.Themes;

/// <summary>Canonical CUI motion durations used by code-created controls.</summary>
public static class CuiMotion
{
    public static readonly TimeSpan Instant = TimeSpan.Zero;
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan ButtonBounce = TimeSpan.FromMilliseconds(170);
    public static readonly TimeSpan TouchLongPress = TimeSpan.FromMilliseconds(550);
    public static readonly TimeSpan Layout = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan ScreenTransition = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan HoldToConfirm = TimeSpan.FromSeconds(5);
}
