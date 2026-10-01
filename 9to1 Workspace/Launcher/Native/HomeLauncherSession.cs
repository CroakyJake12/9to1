using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Launcher;

/// <summary>An owning read snapshot, never a permission, platform binding or widget execution token.</summary>
public sealed class LauncherSessionSnapshot
{
    internal LauncherSessionSnapshot(AuthenticatedResourceActor actor, LauncherStoredLayout layout)
    { Actor = actor; Layout = layout; }
    internal AuthenticatedResourceActor Actor { get; }
    public LauncherStoredLayout Layout { get; }
}

/// <summary>Current persisted placement and optional verified definition metadata; no renderer or action grant.</summary>
public sealed record LauncherWidgetRead(LauncherWidgetPlacement Placement, HomeNativeWidgetResolution? NativeDefinition);

/// <summary>Uses the same Home actor, owned layout and central live widget registry for every read.
/// A profile/session/layout change invalidates the snapshot; stale UI cannot select another owner's binding.</summary>
public sealed class HomeLauncherSession(HomeLauncherLayoutStore layouts, IAuthenticatedResourceActorSource actors,
    HomeNativeWidgetRegistry widgets)
{
    public async Task<LauncherSessionSnapshot?> ReadAsync(CancellationToken ct = default)
    {
        var actor = await actors.GetCurrentAsync(ct);
        if (actor is null) return null;
        var layout = await layouts.ReadExistingAsync(ct);
        if (layout is null) return null;
        if (await actors.GetCurrentAsync(ct) != actor) throw new UnauthorizedAccessException("Home changed while opening Launcher.");
        return new(actor, layout);
    }
    public Task<LauncherStoredLayout> EditAsync(LauncherSessionSnapshot snapshot, Func<LauncherLayout, LauncherLayout> edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return layouts.EditAsActorAsync(snapshot.Layout, snapshot.Actor, edit, ct);
    }
    public async Task<bool> IsCurrentAsync(LauncherSessionSnapshot snapshot, CancellationToken ct = default)
        => await CurrentAsync(snapshot, ct) is not null;

    public async Task<LauncherWidgetRead?> ReadWidgetAsync(LauncherSessionSnapshot snapshot, Guid placementId,
        CancellationToken ct = default)
    {
        var current = await CurrentAsync(snapshot, ct);
        var placement = current?.Current.Widgets.SingleOrDefault(widget => widget.Id == placementId);
        if (placement is null) return null;
        var definition = placement.Native is { } reference ? await widgets.ResolveAsync(reference, ct) : null;
        if (await CurrentAsync(snapshot, ct) is null) return null;
        // Missing/revoked owners remain inert unavailable placements; reading never deletes state.
        return new(placement, definition);
    }
    private async Task<LauncherStoredLayout?> CurrentAsync(LauncherSessionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (await actors.GetCurrentAsync(ct) != snapshot.Actor) return null;
        var current = await layouts.ReadExistingAsync(ct);
        if (current is null || current.AuthorityId != snapshot.Layout.AuthorityId || current.Revision != snapshot.Layout.Revision ||
            await actors.GetCurrentAsync(ct) != snapshot.Actor) return null;
        return current;
    }
}
