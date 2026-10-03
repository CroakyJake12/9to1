using Haven.Application;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HavenOS.Home.Core;

namespace NineToOne.Launcher;

/// <summary>An owning read snapshot, never a permission, platform binding or widget execution token.</summary>
public sealed class LauncherSessionSnapshot
{
    internal LauncherSessionSnapshot(AuthenticatedResourceActor actor, LauncherStoredLayout layout)
    { Actor = actor; Layout = layout; }
    internal LauncherSessionSnapshot(AuthenticatedResourceActor actor, LauncherStoredLayout layout, HomeLauncherSession issuer)
        : this(actor, layout) { Issuer = issuer; OriginalLayoutJson = JsonSerializer.Serialize(layout); }
    internal HomeLauncherSession? Issuer { get; }
    internal string? OriginalLayoutJson { get; }
    internal AuthenticatedResourceActor Actor { get; }
    public LauncherStoredLayout Layout { get; }
}

/// <summary>Current persisted placement and optional verified definition metadata; no renderer or action grant.</summary>
public sealed record LauncherWidgetRead(LauncherWidgetPlacement Placement, HomeNativeWidgetResolution? NativeDefinition);

/// <summary>Uses the same Home actor, owned layout and central live widget registry for every read.
/// A profile/session/layout change invalidates the snapshot; stale UI cannot select another owner's binding.</summary>
public sealed class HomeLauncherSession
{
    private readonly HomeLauncherLayoutStore layouts;
    private readonly IAuthenticatedResourceActorSource actors;
    private readonly HomeNativeWidgetRegistry widgets;
    public HomeLauncherSession(HomeLauncherLayoutStore layouts, IAuthenticatedResourceActorSource actors,
        HomeNativeWidgetRegistry widgets)
    {
        ArgumentNullException.ThrowIfNull(layouts); ArgumentNullException.ThrowIfNull(actors); ArgumentNullException.ThrowIfNull(widgets);
        if (!layouts.IsBoundToActorSource(actors)) throw new UnauthorizedAccessException("Same canonical launcher actor source required.");
        this.layouts = layouts; this.actors = actors; this.widgets = widgets;
    }
    private readonly ConditionalWeakTable<LauncherSessionSnapshot, object> _snapshots = new();
    private LauncherSessionSnapshot Issue(AuthenticatedResourceActor actor, LauncherStoredLayout layout)
    {
        var snapshot = new LauncherSessionSnapshot(actor, layout, this);
        _snapshots.Add(snapshot, new object());
        return snapshot;
    }
    private sealed record EditedIssuance(LauncherSessionSnapshot Original, string ResultJson);
    private readonly ConditionalWeakTable<LauncherStoredLayout, EditedIssuance> _edits = new();
    private void RequireIssued(LauncherSessionSnapshot original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!ReferenceEquals(original.Issuer, this) || !_snapshots.TryGetValue(original, out _) || original.OriginalLayoutJson != JsonSerializer.Serialize(original.Layout))
            throw new UnauthorizedAccessException("The actual original launcher snapshot is unavailable.");
    }
    public async Task<LauncherSessionSnapshot?> ReadAsync(CancellationToken ct = default)
    {
        var actor = await actors.GetCurrentAsync(ct);
        if (actor is null) return null;
        return await ReadForActorAsync(actor, ct);
    }
    public async Task<LauncherSessionSnapshot?> ReadForActorAsync(AuthenticatedResourceActor originalActor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (await actors.GetCurrentAsync(ct) != originalActor) throw new UnauthorizedAccessException("The original Home launcher session changed.");
        var actor = originalActor;
        var layout = await layouts.ReadExistingForActorAsync(actor, ct);
        if (layout is null) return null;
        if (await actors.GetCurrentAsync(ct) != actor) throw new UnauthorizedAccessException("Home changed while opening Launcher.");
        return Issue(actor, layout);
    }
    // Identity data only, extracted from this issuer's exact still-current snapshot; never a copied actor grant.
    public async Task<AuthenticatedResourceActor> RequireOriginalActorAsync(LauncherSessionSnapshot original, CancellationToken ct = default)
    {
        RequireIssued(original);
        if (!await IsCurrentAsync(original, ct)) throw new UnauthorizedAccessException("The original displayed launcher session changed.");
        return original.Actor;
    }
    public Task<LauncherStoredLayout> EditAsync(LauncherSessionSnapshot snapshot, Func<LauncherLayout, LauncherLayout> edit,
        CancellationToken ct = default) => EditCoreAsync(snapshot, edit, null, ct);
    public Task<LauncherStoredLayout> EditForOriginalHostAsync(LauncherSessionSnapshot snapshot, Func<LauncherLayout, LauncherLayout> edit,
        Func<bool> originalHostCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(originalHostCurrent);
        return EditCoreAsync(snapshot, edit, originalHostCurrent, ct);
    }
    private async Task<LauncherStoredLayout> EditCoreAsync(LauncherSessionSnapshot snapshot, Func<LauncherLayout, LauncherLayout> edit,
        Func<bool>? originalHostCurrent, CancellationToken ct)
    {
        RequireIssued(snapshot);
        var result = originalHostCurrent is null
            ? await layouts.EditAsActorAsync(snapshot.Layout, snapshot.Actor, edit, ct)
            : await layouts.EditAsActorForOriginalHostAsync(snapshot.Layout, snapshot.Actor, edit, originalHostCurrent, ct);
        // Only this exact successful returned result can renew this exact original snapshot.
        // A lost return remains unavailable; no copied/current layout is treated as a receipt.
        _edits.Add(result, new(snapshot, JsonSerializer.Serialize(result)));
        return result;
    }
    public async Task<LauncherSessionSnapshot?> ReadAfterEditAsync(LauncherSessionSnapshot original,
        LauncherStoredLayout exactReturnedEdit, CancellationToken ct = default)
    {
        RequireIssued(original); ArgumentNullException.ThrowIfNull(exactReturnedEdit);
        if (!_edits.TryGetValue(exactReturnedEdit, out var issuance) || !ReferenceEquals(issuance.Original, original) ||
            issuance.ResultJson != JsonSerializer.Serialize(exactReturnedEdit) ||
            exactReturnedEdit.AuthorityId != original.Layout.AuthorityId || original.Layout.Revision == long.MaxValue ||
            exactReturnedEdit.Revision != original.Layout.Revision + 1)
            throw new UnauthorizedAccessException("An exact original returned launcher edit is required.");
        if (await actors.GetCurrentAsync(ct) != original.Actor) return null;
        var current = await layouts.ReadExistingForActorAsync(original.Actor, ct);
        ct.ThrowIfCancellationRequested();
        if (current is null || JsonSerializer.Serialize(current) != issuance.ResultJson ||
            await actors.GetCurrentAsync(ct) != original.Actor) return null;
        RequireIssued(original);
        ct.ThrowIfCancellationRequested();
        return Issue(original.Actor, current);
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
    /// <summary>Capture-only adapter for Home's same admitted owner registry. The result is data, not a renderer/action grant.</summary>
    public async Task<HomeNativeWidgetSurface?> CaptureNativeWidgetAsync(LauncherSessionSnapshot snapshot,
        Guid placementId, double viewportWidth, double viewportHeight, CancellationToken ct = default)
    {
        // Original actor is internal to this nonserializable snapshot; callers cannot supply a replacement actor string.
        if (placementId == Guid.Empty || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0 || viewportHeight <= 0 || viewportWidth > 16384 || viewportHeight > 16384) return null;
        var read = await ReadWidgetAsync(snapshot, placementId, ct);
        if (read?.Placement.Native is not { } reference || read.Placement.Android is not null ||
            read.NativeDefinition is not { } declared || declared.Reference != reference) return null;
        var capture = await widgets.CaptureAsync(reference, snapshot.Actor,
            new HomeNativeWidgetSize(read.Placement.ColumnSpan, read.Placement.RowSpan), viewportWidth, viewportHeight, ct);
        if (capture is null || capture.Reference != reference ||
            capture.SurfaceReference != declared.Definition.SurfaceReference) return null;
        var current = await CurrentAsync(snapshot, ct);
        if (current is null || current.Current.Widgets.SingleOrDefault(item => item.Id == placementId) != read.Placement)
            return null;
        return capture;
    }

    private async Task<LauncherStoredLayout?> CurrentAsync(LauncherSessionSnapshot snapshot, CancellationToken ct)
    {
        RequireIssued(snapshot);
        if (await actors.GetCurrentAsync(ct) != snapshot.Actor) return null;
        var current = await layouts.ReadExistingForActorAsync(snapshot.Actor, ct);
        if (current is null || current.AuthorityId != snapshot.Layout.AuthorityId || current.Revision != snapshot.Layout.Revision ||
            await actors.GetCurrentAsync(ct) != snapshot.Actor) return null;
        return current;
    }
}
