using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Overlay layout that supports genuine out-of-flow floating children. Regular children are measured
/// and arranged exactly as in a plain <see cref="Panel"/> (fill the available area). Floating
/// children (cells whose direct child is a <see cref="CuiFloatingLayer"/>) are excluded from this
/// panel's desired size (so they never inflate the owning container's outer measure) and are
/// arranged at their content size relative to an explicitly authored target — the WHOLE protected
/// body/placement anchor — on the authored side, within the real owner scene viewport. The anchor
/// identity and side are authored CUI (Target + Placement) carried by the <see cref="CuiContainerControl"/>
/// wrapping the floating layer; they are never inferred from a first-TextBox traversal or an
/// element-id heuristic in framework code.
/// </summary>
/// <remarks>
/// Live tracking is scoped and lifetime-owned: the panel, its coordinate ancestors up to the owning
/// top-level, the authored target and the target's intermediate coordinate ancestors are observed
/// with bounded per-owner subscriptions disposed on detach/close. A target that is not attached, not
/// effectively visible, outside the same owner subtree, or whose translation to this panel fails is
/// reported truthfully — the floating cell is NOT parked at origin (0,0) on a failed TranslatePoint.
/// </remarks>
internal sealed class CuiOverlayPanel : Panel, IDisposable
{
    private const double Gap = 8;

    private readonly List<(Control Observed, EventHandler<AvaloniaPropertyChangedEventArgs> Handler)> _observed = [];
    private readonly Dictionary<Control, Control?> _targets = [];
    private readonly Dictionary<CuiContainerControl, EventHandler> _relationshipHandlers = [];
    private readonly Dictionary<CuiContainerControl, Control> _hostCells = [];
    // Escape-portal state: hosts whose resolved rect must escape this panel's own physical bounds are
    // re-parented into a top-level popup portal; their in-tree cell stays in Children (so the visual
    // structure is never mutated mid-arrange) but is tracked here and excluded from flow.
    private readonly Dictionary<CuiContainerControl, CuiFloatingPortal> _portals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Control> _movedCells = new(ReferenceEqualityComparer.Instance);
    // Portal popup logical adoptions/releases are queued and applied only AFTER the Measure/Arrange
    // Children iteration completes (mutating Children during that enumeration would invalidate the
    // enumerator). These are host-only logical members, excluded from flow.
    private readonly List<Popup> _pendingAdoptions = [];
    private readonly List<Popup> _pendingReleases = [];
    // Each portal's authored host is registered with its owning compound-focus scope so genuine focus
    // on the portaled (physically removed) toolbar content keeps the surface's expanded state and Aa
    // selection alive. Resolved at engagement; unregistered on retirement/detach/dispose.
    private readonly Dictionary<CuiContainerControl, CuiCompoundFocusScope?> _portalFocusScopes = new(ReferenceEqualityComparer.Instance);

    public CuiOverlayPanel() => UseLayoutRounding = false;

    private static bool IsFloatingCell(Control cell) =>
        cell is Border { Child: CuiContainerControl { Layout: CuiFloatingLayer } };

    private static CuiContainerControl? FloatingHost(Control cell) =>
        (cell as Border)?.Child as CuiContainerControl;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        foreach (var cell in Children)
        {
            // Popups adopted by the portal are host-only logical/visual members, not layout cells.
            if (cell is Popup) continue;
            cell.Measure(availableSize);
            // Out-of-flow: floating cells and cells whose authored host currently lives in an escape
            // portal are excluded from the outer desired size.
            if (IsFloatingCell(cell) || _movedCells.Contains(cell)) continue;
            width = Math.Max(width, cell.DesiredSize.Width);
            height = Math.Max(height, cell.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureRelationshipObservers();
        Observe();
        var viewport = OwnerViewport(finalSize);
        foreach (var cell in Children)
        {
            // Popups adopted by the portal are host-only members, never arranged as layout cells.
            if (cell is Popup) continue;
            // Portaled cells (host currently in an escape portal) are handled by the portal lane
            // regardless of whether the cell still reports as floating (its authored host has been
            // moved out of the in-tree Border, so IsFloatingCell is false while the portal is live).
            if (_movedCells.Contains(cell))
            {
                if (TryGetPortalByCell(cell, out var portal))
                    ApplyPortaledPlacement(portal, cell, viewport, finalSize);
                continue;
            }

            if (!IsFloatingCell(cell))
            {
                cell.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                continue;
            }
            ApplyFloatingPlacement(cell, viewport, finalSize);
        }
        FlushPortalLogicalChanges();
        return finalSize;
    }

    /// <summary>The real usable scene viewport in this panel's coordinate space. When no owning
    /// top-level is available the panel's own bounds are used; this is a degraded fallback, never a
    /// claim that the local leaf equals the complete scene.</summary>
    private Rect OwnerViewport(Size finalSize)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return new Rect(0, 0, finalSize.Width, finalSize.Height);
        var origin = topLevel.TranslatePoint(default, this);
        if (origin is null) return new Rect(0, 0, finalSize.Width, finalSize.Height);
        return new Rect(origin.Value.X, origin.Value.Y,
            Math.Max(0, topLevel.Bounds.Width), Math.Max(0, topLevel.Bounds.Height));
    }

    private Rect Place(Control cell, CuiContainerControl host, Rect viewport)
    {
        var target = ResolveTarget(cell, host);
        if (target is null) return Unplaced();
        var anchor = TranslateToPanel(target);
        if (anchor is null) return Unplaced();

        // Fail closed on an unsupported placement in this floating relationship: never silently treat an
        // unrelated placement symbol as Above.
        var side = SideOf(host);
        if (side is null) return Unplaced();

        // Clamp the usable width BEFORE measuring the floating content, then remeasure at that width so
        // wrap-based heights reflect the ACTUAL available width rather than a wider earlier measure. We
        // measure the RETAINED AUTHORED floating host (the validated CUI container inside the wrapper),
        // NOT the suppressed wrapper cell: once the wrapper is hidden the cell's own DesiredSize is
        // reset to zero by the framework, so measuring it would permanently report zero and make
        // recovery unreachable. The host is authored-visible regardless of the panel-private wrapper
        // state, so its measure reflects real content and keeps the placement decision truthful.
        var usableWidth = Math.Max(0, viewport.Width);
        var size = MeasureHost(host, usableWidth);
        if (size.Width <= 0 || size.Height <= 0) return Unplaced();

        // Try the authored side first, then its opposite, then the two perpendicular sides. A candidate
        // is acceptable only when it fits ENTIRELY inside the usable owner viewport AND does not intersect
        // the protected composer body (the authored target). If no side offers genuinely safe space, fall
        // back to an explicit bounded unplaced/degraded state rather than covering editing/actions.
        return FitPlacement(host, anchor.Value, size, viewport) ?? Unplaced();
    }

    /// <summary>Measure the retained authored floating host (not the suppressible wrapper cell) at the
    /// usable viewport width and report its bounded content size.</summary>
    private static Size MeasureHost(CuiContainerControl host, double usableWidth)
    {
        host.Measure(new Size(usableWidth, double.PositiveInfinity));
        return new Size(
            Math.Max(0, Math.Min(host.DesiredSize.Width, usableWidth)),
            Math.Max(0, host.DesiredSize.Height));
    }

    /// <summary>Place a portaled host (whose authored content already lives in an escape portal) against
    /// the current resolved target, re-measuring the CURRENT retained visible content at the current
    /// viewport width. We never reuse a cached earlier size, so toggling the authored toolbar off (or a
    /// narrow wrap after resize) reflects the live content and retires the portal when nothing visible
    /// remains.</summary>
    private Rect PlacePortaled(CuiContainerControl host, Control cell, Rect viewport)
    {
        var target = ResolveTarget(cell, host);
        if (target is null) return Unplaced();
        var anchor = TranslateToPanel(target);
        if (anchor is null) return Unplaced();
        var side = SideOf(host);
        if (side is null) return Unplaced();
        var usableWidth = Math.Max(0, viewport.Width);
        var size = MeasureHost(host, usableWidth);
        if (size.Width <= 0 || size.Height <= 0) return Unplaced();
        return FitPlacement(host, anchor.Value, size, viewport) ?? Unplaced();
    }

    /// <summary>Choose the first candidate side that fits entirely inside the owner viewport and does not
    /// intersect the protected authored target. Returns null when no side offers genuinely safe space.</summary>
    private static Rect? FitPlacement(CuiContainerControl host, Rect anchor, Size size, Rect viewport)
    {
        var side = SideOf(host);
        if (side is null) return null;
        foreach (var candidateSide in OrderedSides(side))
        {
            var rect = Preferred(candidateSide, anchor, size);
            if (Fits(rect, viewport, anchor)) return rect;
        }
        return null;
    }

    /// <summary>
    /// Apply the resolved placement to a floating cell. A solved (non-empty) rect is arranged at that
    /// rect with the wrapper visibility restored to the authored child's effective visibility. When no
    /// safe rect exists the cell enters a genuine UNPLACED state: it is taken out of the render/input/
    /// focus/tab path entirely (wrapper hidden) rather than merely arranged at a zero rect, which would
    /// still let positive-sized descendants (e.g. MinWidth/MinHeight toolbar buttons) paint/hit/focus
    /// un-clipped near the origin. The authored FormattingOpen/IsVisible state is never overwritten —
    /// only the panel-private cell wrapper visibility is driven — and when safe space returns the next
    /// arrange restores placement, so recovery is automatic. Hidden unplaced cells remain in Children so
    /// their relationship/placement observers keep living and re-placement is re-evaluated on every pass.
    /// </summary>
    private void ApplyFloatingPlacement(Control cell, Rect viewport, Size finalSize)
    {
        // Portaled lane: the authored host currently lives in an escape portal; reposition/restore it.
        if (_movedCells.Contains(cell) && TryGetPortalByCell(cell, out var portal))
        {
            ApplyPortaledPlacement(portal, cell, viewport, finalSize);
            return;
        }

        // Validate the floating-cell contract through the existing typed helper (a Border whose direct
        // child is a CuiContainerControl with a CuiFloatingLayer layout). We never read an unvalidated
        // Control.Child — the wrapper is the panel-private Border; the authored content is the host.
        var host = FloatingHost(cell);
        if (host is null)
        {
            if (cell.IsVisible) cell.IsVisible = false;
            return;
        }

        var rect = Place(cell, host, viewport);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            // Genuine unplaced state: suppress the whole subtree (paint/hit/focus/tab stops) WITHOUT
            // touching the authored child's visibility binding. Idempotent; recovery is handled by the
            // next arrange once safe space returns. Place() measures the authored host independently of
            // this suppressed wrapper, so the next pass still sees real content size and can restore.
            DismissPortalFor(host);
            if (cell.IsVisible) cell.IsVisible = false;
            return;
        }

        // Escape engagement rule: when the resolved rect must leave this panel's own physical bounds
        // (e.g. the composer leaf is clipped by a native host), the in-tree cell cannot paint/hit-test
        // there. Engage the top-level portal. When the rect fits within the panel, the in-tree path is
        // used unchanged, preserving the existing owner-window behavior.
        var ownBounds = new Rect(0, 0, Math.Max(0, finalSize.Width), Math.Max(0, finalSize.Height));
        if (!ownBounds.Contains(rect))
        {
            EnsurePortalFor(host, (Border)cell, rect);
            return;
        }

        DismissPortalFor(host);
        // Restore the wrapper to the authored host's effective visibility (mirrors authored state; the
        // wrapper is panel-private, never the authored binding).
        var authoredVisible = host.IsVisible;
        if (cell.IsVisible != authoredVisible) cell.IsVisible = authoredVisible;
        cell.Arrange(rect);
    }

    /// <summary>Re-position an already-portaled host. When safe space returns so the rect fits inside the
    /// panel again, the portal is retired and the SAME retained authored host is restored to the in-tree
    /// cell (recovery). When no safe rect remains, the portal is dismissed and the cell suppressed.</summary>
    private void ApplyPortaledPlacement(CuiFloatingPortal portal, Control cell, Rect viewport, Size finalSize)
    {
        var host = portal.Host;
        var rect = PlacePortaled(host, portal.Cell, viewport);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            DismissPortalFor(host);
            if (cell.IsVisible) cell.IsVisible = false;
            return;
        }

        var ownBounds = new Rect(0, 0, Math.Max(0, finalSize.Width), Math.Max(0, finalSize.Height));
        if (ownBounds.Contains(rect))
        {
            DismissPortalFor(host);
            var authoredVisible = host.IsVisible;
            if (cell.IsVisible != authoredVisible) cell.IsVisible = authoredVisible;
            cell.Arrange(rect);
            return;
        }

        portal.Show(rect);
    }

    private bool TryGetPortalByCell(Control cell, out CuiFloatingPortal portal)
    {
        foreach (var (host, candidate) in _portals)
        {
            if (ReferenceEquals(candidate.Cell, cell)) { portal = candidate; return true; }
        }
        portal = null!;
        return false;
    }

    private void EnsurePortalFor(CuiContainerControl host, Border cell, Rect rect)
    {
        var isNew = false;
        if (!_portals.TryGetValue(host, out var portal))
        {
            isNew = true;
            portal = new CuiFloatingPortal(this, host, cell);
            _portals[host] = portal;
        }
        _movedCells.Add(cell);
        portal.Show(rect);
        if (cell.IsVisible) cell.IsVisible = false;
        if (isNew)
        {
            // Register the retained authored host with its owning compound-focus scope so focus on the
            // portaled toolbar content keeps the surface expanded (Aa selected). Resolved from the cell,
            // which stays inside the authored subtree even while the host is physically portaled, so it
            // reaches exactly the same scope the composer input observes. Registration happens after the
            // host is moved into the portal so the scope can also observe the portal's popup top level.
            var scope = ResolveFocusScope(cell);
            _portalFocusScopes[host] = scope;
            scope?.RegisterExternalMember(host);
        }
    }

    /// <summary>Resolve the owning compound-focus scope for a floating cell by walking its visual
    /// ancestors to the nearest compound-focus-enabled authored container (mirrors the composer input's
    /// own scope discovery). Returns null when no compound scope exists (degraded surface).</summary>
    private static CuiCompoundFocusScope? ResolveFocusScope(Control cell)
    {
        Visual? current = cell;
        while (current is not null)
        {
            if (current is CuiContainerControl { CompoundFocusEnabled: true } container && container.CompoundFocusScope is { } scope)
                return scope;
            current = current.GetVisualParent();
        }
        return null;
    }

    /// <summary>Retire the portal for a host: close it, restore the SAME authored host to its cell and
    /// drop all tracking. Idempotent.</summary>
    private void DismissPortalFor(CuiContainerControl host)
    {
        if (!_portals.TryGetValue(host, out var portal)) return;
        // Restore physical custody FIRST: any synchronous callback during retirement (focus change,
        // UnregisterExternalMember -> Recompute, layout re-eval, or AuthoredChildren queries) must see
        // the authored child through the normal cell.Child path — never through a transient active-
        // portal-only recovery. The _portals map is still present here, so both recovery paths agree
        // until tracking is dropped afterward.
        portal.Dismiss();
        _portals.Remove(host);
        _movedCells.Remove(portal.Cell);
        if (_portalFocusScopes.Remove(host, out var scope)) scope?.UnregisterExternalMember(host);
        portal.Dispose();
    }

    /// <summary>Called by a portal when its popup closes unexpectedly; drop tracking without double-restoring.</summary>
    internal void OnPortalClosed(CuiFloatingPortal portal)
    {
        if (_portals.Remove(portal.Host, out var known) && ReferenceEquals(known, portal))
        {
            _movedCells.Remove(portal.Cell);
            if (_portalFocusScopes.Remove(portal.Host, out var scope)) scope?.UnregisterExternalMember(portal.Host);
        }
    }

    /// <summary>Live escape portals (test/static diagnostic; the panel owns their lifetime).</summary>
    internal IReadOnlyCollection<CuiFloatingPortal> ActivePortals => _portals.Values;

    /// <summary>The authored hosts currently physically portaled (typed identity, independent of the
    /// cells' physical Child references). Used by the owning container to reconcile removal/replacement.</summary>
    internal IReadOnlyCollection<CuiContainerControl> PortaledHosts => _portals.Keys;

    /// <summary>Adopt a portal popup as a host-only logical/visual member of this panel (resource/
    /// accessibility/command ancestry). The panel excludes Popup children from flow. Idempotent.</summary>
    internal void AdoptPortalLogically(Popup popup)
    {
        if (popup is null || _pendingAdoptions.Contains(popup)) return;
        _pendingAdoptions.Add(popup);
    }

    /// <summary>Release a previously adopted portal popup from this panel's tree. Idempotent. Release is
    /// queued and applied once layout iteration completes — never during the Children enumeration.</summary>
    internal void ReleasePortalLogically(Popup popup)
    {
        if (popup is null || _pendingReleases.Contains(popup)) return;
        _pendingReleases.Add(popup);
    }

    /// <summary>Apply queued portal logical adoptions/releases. Mutating the Children collection during
    /// Measure/Arrange iteration would invalidate the enumerator, so additions/removals are queued and
    /// flushed here, after the arrangement loop completes.</summary>
    private void FlushPortalLogicalChanges()
    {
        foreach (var popup in _pendingReleases)
        {
            if (Children.Contains(popup)) Children.Remove(popup);
            _pendingAdoptions.Remove(popup);
        }
        _pendingReleases.Clear();
        foreach (var popup in _pendingAdoptions)
        {
            if (!Children.Contains(popup)) Children.Add(popup);
        }
        _pendingAdoptions.Clear();
    }

    /// <summary>Force-retire the portal for a host (used by the owning container on hide/remove/
    /// replacement/reload): retain, unregister focus and restore the SAME host to its in-tree cell.</summary>
    internal void ForceRetirePortaledHost(CuiContainerControl host)
    {
        if (host is null) return;
        DismissPortalFor(host);
    }

    /// <summary>Resolve the authored host a cell currently holds physically (in an escape portal) when the
    /// cell's own Child is detached. Used by the owning container to keep authored identity stable across
    /// traversal/order/remove/replacement regardless of physical portal placement.</summary>
    internal bool TryGetPortaledHostForCell(Border cell, out CuiContainerControl host)
    {
        foreach (var portal in _portals.Values)
        {
            if (ReferenceEquals(portal.Cell, cell)) { host = portal.Host; return true; }
        }
        host = null!;
        return false;
    }

    /// <summary>A candidate is usable only when it is fully inside the owner viewport AND avoids the
    /// protected composer body. A positive rectangle alone does not mean usable space exists.</summary>
    private static bool Fits(Rect rect, Rect viewport, Rect anchor) =>
        viewport.Contains(rect) && !rect.Intersects(anchor);

    private static IEnumerable<string> OrderedSides(string side)
    {
        yield return side;
        yield return Opposite(side);
        if (side is "Above" or "Under")
        {
            yield return "LeftOf";
            yield return "RightOf";
        }
        else
        {
            yield return "Above";
            yield return "Under";
        }
    }

    /// <summary>A failed relationship must not park the bar at (0,0): return an empty (invisible)
    /// rect as the sentinel for the unplaced state. The caller (<see cref="ApplyFloatingPlacement"/>)
    /// additionally suppresses the cell's render/input/focus/tab path so a zero rect by itself never
    /// lets overflow descendants paint near the origin. Fallback is a live hidden state, never a
    /// silently misplaced origin-anchored surface.</summary>
    private static Rect Unplaced() => new(0, 0, 0, 0);

    /// <summary>Translate an authored descendant target's current bounds into this panel's coordinate
    /// space. Returns null when the target is disconnected or translation fails (never (0,0)).</summary>
    private Rect? TranslateToPanel(Control control)
    {
        if (!control.IsAttachedToVisualTree()) return null;
        var origin = control.TranslatePoint(default, this);
        if (origin is null) return null;
        return new Rect(origin.Value.X, origin.Value.Y, control.Bounds.Width, control.Bounds.Height);
    }

    private Control? ResolveTarget(Control cell, CuiContainerControl host)
    {
        if (_targets.TryGetValue(cell, out var cached) && cached is not null && ValidTarget(cached))
            return cached;
        var target = ResolveFromId(host.AuthoredTargetId);
        _targets[cell] = target;
        return target;
    }

    private Control? ResolveFromId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var target = this.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => string.Equals(AutomationProperties.GetAutomationId(c), id, StringComparison.Ordinal));
        return target is not null && ValidTarget(target) ? target : null;
    }

    /// <summary>The target must be inside THIS owner subtree (a descendant of this overlay panel) and
    /// currently attached and effectively visible. It is never accepted merely because it is attached
    /// somewhere else.</summary>
    private bool ValidTarget(Control target) =>
        (ReferenceEquals(target, this) || this.IsVisualAncestorOf(target))
        && target.IsAttachedToVisualTree() && target.IsEffectivelyVisible;

    private static string? SideOf(CuiContainerControl host) => host.AuthoredPlacement?.Trim() switch
    {
        "Above" => "Above",
        "Under" => "Under",
        "LeftOf" => "LeftOf",
        "RightOf" => "RightOf",
        _ => null,
    };

    private static string Opposite(string side) => side switch
    {
        "Under" => "Above",
        "LeftOf" => "RightOf",
        "RightOf" => "LeftOf",
        _ => "Under",
    };

    private static Rect Preferred(string side, Rect anchor, Size size) => side switch
    {
        "Under" => new Rect(anchor.Center.X - size.Width / 2, anchor.Bottom + Gap, size.Width, size.Height),
        "LeftOf" => new Rect(anchor.Left - Gap - size.Width, anchor.Center.Y - size.Height / 2, size.Width, size.Height),
        "RightOf" => new Rect(anchor.Right + Gap, anchor.Center.Y - size.Height / 2, size.Width, size.Height),
        _ => new Rect(anchor.Center.X - size.Width / 2, anchor.Top - Gap - size.Height, size.Width, size.Height),
    };

    /// <summary>Observe the panel, its coordinate ancestors up to the owning top-level, each authored
    /// target and the target's intermediate coordinate ancestors. Movement of any intermediate regular-flow
    /// ancestor can shift the translated anchor even when the target's local Bounds are unchanged, so those
    /// ancestors are observed too. Subscriptions are bounded and lifetime-owned (disposed on detach/close).</summary>
    private void Observe()
    {
        var targets = new List<Control> { this };
        foreach (var ancestor in this.GetVisualAncestors().OfType<Control>())
            if (!targets.Contains(ancestor)) targets.Add(ancestor);

        foreach (var cell in Children)
        {
            if (!IsFloatingCell(cell)) continue;
            var host = FloatingHost(cell);
            if (host is null) continue;
            var target = ResolveTarget(cell, host);
            if (target is null) continue;
            if (!targets.Contains(target)) targets.Add(target);
            foreach (var ancestor in target.GetVisualAncestors().OfType<Control>())
                if (!targets.Contains(ancestor)) targets.Add(ancestor);
        }

        // Portaled floating hosts' authored targets are observed identically so live target/ancestor
        // movement re-positions the portal.
        foreach (var (host, portal) in _portals)
        {
            var target = ResolveTarget(portal.Cell, host);
            if (target is null) continue;
            if (!targets.Contains(target)) targets.Add(target);
            foreach (var ancestor in target.GetVisualAncestors().OfType<Control>())
                if (!targets.Contains(ancestor)) targets.Add(ancestor);
        }

        if (_observed.Count == targets.Count
            && _observed.Zip(targets, (stored, expected) => ReferenceEquals(stored.Observed, expected)).All(match => match))
            return;

        DisposeObservers();
        foreach (var target in targets)
        {
            EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
            {
                if (change.Property == BoundsProperty || change.Property == IsVisibleProperty) InvalidateMeasure();
            };
            target.PropertyChanged += handler;
            _observed.Add((target, handler));
        }
    }

    /// <summary>Observe each floating host's authored Target/Placement relationship so a live authored
    /// Target change invalidates the cached target and the next arrange re-resolves against the NEW id
    /// and swaps observers to the new target. Subscriptions are bounded and lifetime-owned.</summary>
    private void EnsureRelationshipObservers()
    {
        // Hosts currently present as floating cells in Children. Any previously observed host not in
        // this set is retired and must have its handler subscription, host→cell mapping and any stale
        // cached target pruned (not only on whole-panel detach).
        var present = new HashSet<CuiContainerControl>(ReferenceEqualityComparer.Instance);
        foreach (var cell in Children)
        {
            if (!IsFloatingCell(cell)) continue;
            if (FloatingHost(cell) is not { } host) continue;
            present.Add(host);
            if (!_hostCells.TryGetValue(host, out var knownCell) || !ReferenceEquals(knownCell, cell))
                _hostCells[host] = cell;
            if (_relationshipHandlers.ContainsKey(host)) continue;
            EventHandler handler = (_, _) =>
            {
                // The authored relationship changed: drop the stale cached target so the next arrange
                // re-resolves against the NEW id (and rebinds observers to the new target).
                if (_hostCells.TryGetValue(host, out var relatedCell)) _targets.Remove(relatedCell);
                InvalidateMeasure();
            };
            host.RelationshipChanged += handler;
            _relationshipHandlers[host] = handler;
        }

        // Portaled hosts are also live authored floating relationships; keep them observed and map
        // their host→cell so a live Target change still drops the stale cached anchor.
        foreach (var (host, portal) in _portals)
        {
            present.Add(host);
            if (!_hostCells.TryGetValue(host, out var knownCell) || !ReferenceEquals(knownCell, portal.Cell))
                _hostCells[host] = portal.Cell;
            if (_relationshipHandlers.ContainsKey(host)) continue;
            EventHandler handler = (_, _) =>
            {
                if (_hostCells.TryGetValue(host, out var relatedCell)) _targets.Remove(relatedCell);
                InvalidateMeasure();
            };
            host.RelationshipChanged += handler;
            _relationshipHandlers[host] = handler;
        }

        if (_relationshipHandlers.Count <= present.Count) return;
        foreach (var host in _relationshipHandlers.Keys.Where(host => !present.Contains(host)).ToArray())
        {
            host.RelationshipChanged -= _relationshipHandlers[host];
            _relationshipHandlers.Remove(host);
            if (_hostCells.Remove(host, out var retiredCell)) _targets.Remove(retiredCell);
        }
    }

    private void DisposeRelationshipObservers()
    {
        foreach (var (host, handler) in _relationshipHandlers) host.RelationshipChanged -= handler;
        _relationshipHandlers.Clear();
        _hostCells.Clear();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposePortals();
        DisposeObservers();
        DisposeRelationshipObservers();
        _targets.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void DisposePortals()
    {
        foreach (var portal in _portals.Values.ToArray())
        {
            if (_portalFocusScopes.Remove(portal.Host, out var scope)) scope?.UnregisterExternalMember(portal.Host);
            portal.Dismiss();
            portal.Dispose();
        }
        _portals.Clear();
        _movedCells.Clear();
        _portalFocusScopes.Clear();
    }

    private void DisposeObservers()
    {
        foreach (var (target, handler) in _observed) target.PropertyChanged -= handler;
        _observed.Clear();
    }

    public void Dispose()
    {
        DisposePortals();
        DisposeObservers();
        DisposeRelationshipObservers();
    }
}
