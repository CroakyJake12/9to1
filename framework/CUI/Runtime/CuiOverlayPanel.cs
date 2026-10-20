using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
            cell.Measure(availableSize);
            if (IsFloatingCell(cell)) continue;   // out-of-flow: excluded from the outer desired size
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
            if (!IsFloatingCell(cell))
            {
                cell.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                continue;
            }
            cell.Arrange(Place(cell, viewport));
        }
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

    private Rect Place(Control cell, Rect viewport)
    {
        var host = FloatingHost(cell);
        if (host is null) return Unplaced();
        var target = ResolveTarget(cell, host);
        if (target is null) return Unplaced();
        var anchor = TranslateToPanel(target);
        if (anchor is null) return Unplaced();

        // Fail closed on an unsupported placement in this floating relationship: never silently treat an
        // unrelated placement symbol as Above.
        var side = SideOf(host);
        if (side is null) return Unplaced();

        // Clamp the usable width BEFORE measuring the floating content, then remeasure at that width so
        // wrap-based heights reflect the ACTUAL available width rather than a wider earlier measure.
        var usableWidth = Math.Max(0, viewport.Width);
        cell.Measure(new Size(usableWidth, double.PositiveInfinity));
        var size = new Size(
            Math.Max(0, Math.Min(cell.DesiredSize.Width, usableWidth)),
            Math.Max(0, cell.DesiredSize.Height));
        if (size.Width <= 0 || size.Height <= 0) return Unplaced();

        // Try the authored side first, then its opposite, then the two perpendicular sides. A candidate
        // is acceptable only when it fits ENTIRELY inside the usable owner viewport AND does not intersect
        // the protected composer body (the authored target). If no side offers genuinely safe space, fall
        // back to an explicit bounded unplaced/degraded state rather than covering editing/actions.
        foreach (var candidateSide in OrderedSides(side))
        {
            var rect = Preferred(candidateSide, anchor.Value, size);
            if (Fits(rect, viewport, anchor.Value)) return rect;
        }
        return Unplaced();
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
    /// rect and leave the floating cell unplaced. Fallback becomes a live hidden state, never a
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
        foreach (var cell in Children)
        {
            if (!IsFloatingCell(cell)) continue;
            var host = FloatingHost(cell);
            if (host is null) continue;
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
    }

    private void DisposeRelationshipObservers()
    {
        foreach (var (host, handler) in _relationshipHandlers) host.RelationshipChanged -= handler;
        _relationshipHandlers.Clear();
        _hostCells.Clear();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeObservers();
        DisposeRelationshipObservers();
        _targets.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void DisposeObservers()
    {
        foreach (var (target, handler) in _observed) target.PropertyChanged -= handler;
        _observed.Clear();
    }

    public void Dispose()
    {
        DisposeObservers();
        DisposeRelationshipObservers();
    }
}
