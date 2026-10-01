using System.Runtime.CompilerServices;
using NineToOne.Launcher;

namespace Haven.Android;

/// <summary>Associates each displayed layout instance with the exact Home session that read it.
/// A later refresh at the same revision cannot rebind an older dialog to a newer actor.</summary>
public sealed class AndroidLauncherLayoutSessions(HomeLauncherSession sessions)
{
    private readonly ConditionalWeakTable<LauncherStoredLayout, LauncherSessionSnapshot> _displayed = new();
    public LauncherStoredLayout Bind(LauncherSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _displayed.GetValue(snapshot.Layout, _ => snapshot);
        return snapshot.Layout;
    }
    public LauncherSessionSnapshot Require(LauncherStoredLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return _displayed.TryGetValue(layout, out var snapshot) ? snapshot
            : throw new UnauthorizedAccessException("Reload this launcher layout before editing it.");
    }
    public Task<LauncherStoredLayout> EditAsync(LauncherStoredLayout layout, Func<LauncherLayout, LauncherLayout> edit,
        CancellationToken ct = default) => sessions.EditAsync(Require(layout), edit, ct);
}
