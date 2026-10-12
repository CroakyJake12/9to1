using Haven.Application;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal enum PresentVectorHandleKind { Node = 0, Control1 = 1, Control2 = 2 }

internal sealed partial class PresentSlideCanvas : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenCursorInputTarget, IHavenHoverInputTarget, IHavenKeyboardInputTarget, IHavenTextInputTarget, IHavenClipboardInputTarget
{
    private PresentDocument? _document;
    private PresentSlide? _slide;
    private HashSet<Guid> _selectedIds = [];
    private Guid? _dragElementId;
    private VectorHandleHit? _dragVectorHandle;
    private Guid? _vectorPointEditingElementId;
    private HavenPoint _pointerStart;
    private HavenPoint _pointerCurrent;
    private double _viewScale = 1;
    internal double? ManualScale { get; set; }
    internal HavenRect PaperBounds => SlideRectLocal();
    public double ViewScale
    {
        get => _viewScale;
        set { var next = double.IsFinite(value) ? Math.Clamp(value, .1, 1) : 1; if (_viewScale == next) return; _viewScale = next; InvalidateInteraction(); }
    }

    public PresentSlideCanvas()
    {
        Name = "Present.Slide.Canvas"; Accessibility.Role = HavenAccessibleRole.Image; Accessibility.Focusable = true; Accessibility.AccessibleName = "Editable presentation slide canvas"; Accessibility.Description = "Select and drag slide objects.";
        SetValue(HavenProperties.Width, HavenLength.Percent(100)); SetValue(HavenProperties.MinHeight, HavenLength.Px(420)); SetValue(HavenProperties.Background, "Surface"); SetValue(HavenProperties.BorderColor, "Border"); SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12))); SetValue(HavenProperties.Clip, true);
    }

    public event Action<Guid?>? SelectionRequested;
    public event Action<double, double>? MoveSelectionRequested;
    public event Action<Guid, Guid, PresentVectorHandleKind, double, double>? VectorHandleMoveRequested;

    public void PrepareContext(HavenPoint local)
    {
        if (IsTextEditing && TryEditingRect(out var rect, out var rotation)
            && rect.Contains(InverseRotatePoint(local, rect, rotation))) return;
        CommitTextEdit(); ResetDirectGesture();
        var hit = HitElement(local);
        if (hit is null) SelectionSetRequested?.Invoke(Array.Empty<Guid>());
        else if (!_selectedIds.Contains(hit.Id)) SelectionSetRequested?.Invoke(new[] { hit.Id });
        InvalidateInteraction();
    }

    public void SetSlide(PresentDocument document, PresentSlide slide, IEnumerable<Guid> selectedIds)
    {
        if (_slide?.Id != slide.Id) { CancelInteraction(); ClearTextEditState(); _hoveredElementId = null; }
        ResetDirectGesture();
        if (_textEditElementId is { } textId && (!selectedIds.Contains(textId) || slide.Elements.All(item => item.Id != textId))) ClearTextEditState();
        _document = document ?? throw new ArgumentNullException(nameof(document)); _slide = slide ?? throw new ArgumentNullException(nameof(slide)); _selectedIds = selectedIds?.ToHashSet() ?? []; _vectorPointEditingElementId = _vectorPointEditingElementId is { } editingId && _selectedIds.Contains(editingId) && slide.Elements.Any(element => element.Id == editingId && !element.Locked && element.VectorShape is not null) ? editingId : null; _dragElementId = null; _dragVectorHandle = null; _pointerStart = _pointerCurrent = default; Accessibility.Description = $"Slide {slide.Order + 1}. {slide.Elements.Count} editable objects."; InvalidateInteraction();
    }

    internal Guid? VectorPointEditingElementId => _vectorPointEditingElementId;

    internal void SetVectorPointEditing(Guid? elementId)
    {
        if (elementId is { } id
            && (_slide is null
                || !_selectedIds.Contains(id)
                || _slide.Elements.All(element => element.Id != id || element.Locked || element.VectorShape is null)))
            elementId = null;
        if (_vectorPointEditingElementId == elementId) return;
        _vectorPointEditingElementId = elementId;
        _dragVectorHandle = null;
        _activeTransformHandle = PresentTransformHandle.None;
        _marqueeSelecting = false;
        InvalidateInteraction();
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        if (_slide is null || _document is null || input.Button != HavenPointerButton.Primary) return false;
        if (IsTextEditing)
        {
            // Handles and the selection border remain manipulation targets while
            // editing. Only the interior captures text-selection gestures.
            if (HitTransformHandle(input.LocalPosition) is null && !HitEditingBorder(input.LocalPosition)
                && TryHandleTextPointerPress(input)) return true;
            CommitTextEdit();
        }
        ResetDirectGesture();
        _pointerStart = _pointerCurrent = input.LocalPosition;

        if (TryBeginInsertion(input)) return true;

        if (HitVectorHandle(input.LocalPosition) is { } vectorHandle)
        {
            SelectionRequested?.Invoke(vectorHandle.ElementId);
            _pointerStart = _pointerCurrent = input.LocalPosition;
            _dragVectorHandle = vectorHandle;
            InvalidateInteraction();
            return true;
        }

        if (HitTransformHandle(input.LocalPosition) is { } transformHandle)
        {
            _pointerStart = _pointerCurrent = input.LocalPosition;
            _activeTransformHandle = transformHandle;
            InvalidateInteraction();
            return true;
        }

        var hit = HitElement(input.LocalPosition);
        if (hit is not null)
        {
            if (input.ClickCount == 2 && !input.Modifiers.HasFlag(HavenKeyModifiers.Shift)
                && (hit.Kind is PresentElementKind.Text or PresentElementKind.Shape) && !hit.Locked && _selectedIds.Contains(hit.Id))
            {
                BeginElementTextEdit(hit, input.LocalPosition, ToInputModifiers(input.Modifiers));
                return true;
            }
            var selected = _selectedIds.ToHashSet();
            if (input.Modifiers.HasFlag(HavenKeyModifiers.Shift))
            {
                if (!selected.Add(hit.Id)) selected.Remove(hit.Id);
            }
            else if (!selected.Contains(hit.Id))
            {
                selected.Clear();
                selected.Add(hit.Id);
            }
            SelectionSetRequested?.Invoke(selected);
            _pointerStart = _pointerCurrent = input.LocalPosition;
            _selectedIds = selected;
            if (selected.Contains(hit.Id) && !hit.Locked) _dragElementId = hit.Id;
            InvalidateInteraction();
            return true;
        }

        if (TryBeginTitleEdit(input.LocalPosition, ToInputModifiers(input.Modifiers))) return true;
        _marqueeBaseSelection = input.Modifiers.HasFlag(HavenKeyModifiers.Shift) ? _selectedIds.ToHashSet() : [];
        if (!input.Modifiers.HasFlag(HavenKeyModifiers.Shift)) SelectionSetRequested?.Invoke(Array.Empty<Guid>());
        _pointerStart = _pointerCurrent = input.LocalPosition;
        _marqueeSelecting = true;
        InvalidateInteraction();
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (MoveInsertion(input)) return true;
        if (MoveTextPointer(input)) return true;
        if (_dragVectorHandle is null && _dragElementId is null && _activeTransformHandle == PresentTransformHandle.None && !_marqueeSelecting) return false;
        _pointerCurrent = input.LocalPosition;
        _gestureModifiers = input.Modifiers;
        if (!_gestureActivated && DistanceSquared(_pointerStart, _pointerCurrent) >= DragThresholdSquared) _gestureActivated = true;
        InvalidateInteraction();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (ReleaseInsertion(input)) return true;
        if (ReleaseTextPointer()) return true;
        if (!HasDirectGesture) return false;
        if (!_gestureActivated) { ResetDirectGesture(); InvalidateInteraction(); return true; }
        _pointerCurrent = input.LocalPosition;
        if (_dragVectorHandle is { } vectorHandle)
        {
            if (DistanceSquared(_pointerStart, _pointerCurrent) > 0.25
                && TryLocalToVectorPoint(vectorHandle.ElementId, _pointerCurrent, out var point))
                VectorHandleMoveRequested?.Invoke(vectorHandle.ElementId, vectorHandle.NodeId, vectorHandle.Kind, point.X, point.Y);
            ResetDirectGesture();
            InvalidateInteraction();
            return true;
        }

        var slide = SlideRectLocal();
        var dx = slide.Width <= 0 ? 0 : (_pointerCurrent.X - _pointerStart.X) / slide.Width;
        var dy = slide.Height <= 0 ? 0 : (_pointerCurrent.Y - _pointerStart.Y) / slide.Height;

        if (_activeTransformHandle != PresentTransformHandle.None)
        {
            var transform = BuildDirectTransform(_activeTransformHandle, dx, dy);
            if (Math.Abs(transform.DeltaX) > .0005 || Math.Abs(transform.DeltaY) > .0005
                || Math.Abs(transform.DeltaWidth) > .0005 || Math.Abs(transform.DeltaHeight) > .0005
                || Math.Abs(transform.DeltaRotation) > .05)
                TransformSelectionRequested?.Invoke(transform.DeltaX, transform.DeltaY, transform.DeltaWidth, transform.DeltaHeight, transform.DeltaRotation);
            ResetDirectGesture();
            InvalidateInteraction();
            return true;
        }

        if (_marqueeSelecting)
        {
            SelectionSetRequested?.Invoke(_marqueeBaseSelection.Union(HitElementsInMarquee(_pointerStart, _pointerCurrent)).ToArray());
            ResetDirectGesture();
            InvalidateInteraction();
            return true;
        }

        if (_dragElementId is null) return false;
        var move = PreviewMoveDelta();
        if (Math.Abs(move.X) > 0.0000001 || Math.Abs(move.Y) > 0.0000001) MoveSelectionRequested?.Invoke(move.X, move.Y);
        ResetDirectGesture();
        InvalidateInteraction();
        return true;
    }

    public bool PointerCancelled(HavenPointerInput input)
    {
        ResetDirectGesture();
        _insertionDragging = false;
        _textPointerSelecting = false;
        InvalidateInteraction();
        return true;
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (_document is null || _slide is null || Bounds.Width <= 1 || Bounds.Height <= 1) return; var slideRect = SlideRectAbsolute();
        context.Add(new HavenShadowCommand(slideRect, new HavenShadow(new HavenSolidBrush(70, 0, 0, 0), 18, 0, 5, 0, .3), 4)); context.Add(new HavenFillRoundedRectCommand(slideRect, BackgroundBrush(_document, _slide), 4, opacity)); context.Add(new HavenStrokeRoundedRectCommand(slideRect, new HavenPen(new HavenTokenBrush("Border"), 1), 4, opacity));
        DrawSlideContent(context, slideRect, opacity);
        DrawHover(context, slideRect, opacity);
        var showHandles = _selectedIds.Count == 1;
        foreach (var element in _slide.Elements.Where(value => _selectedIds.Contains(value.Id) && value.Visible))
            DrawDirectSelection(context, element, ElementRect(element, slideRect, _gestureActivated && _dragElementId is not null && IsSelectedForTransform(element)), opacity, showHandles);
        DrawMarquee(context, opacity);
        DrawInsertion(context, opacity);
    }

    internal void DrawPreview(HavenDrawingContext context, PresentDocument document, PresentSlide slide, HavenRect target, double opacity)
    {
        _document = document; _slide = slide;
        context.Add(new HavenFillRoundedRectCommand(target, BackgroundBrush(document, slide), 7, opacity));
        DrawSlideContent(context, target, opacity);
    }

    private void DrawSlideContent(HavenDrawingContext context, HavenRect target, double opacity)
    {
        if (_document is null || _slide is null) return;
        var logical = new HavenRect(0, 0, _document.SlideSize.WidthInches * 96, _document.SlideSize.HeightInches * 96);
        context.Add(new HavenPushClipCommand(target));
        context.Add(new HavenPushTransformCommand(target,
            new HavenTransform(target.Width / logical.Width, target.Height / logical.Height, TranslateX: target.X, TranslateY: target.Y), new HavenPoint(0, 0)));
        DrawSlideTitle(context, logical, opacity);
        foreach (var element in _slide.Elements.Where(value => IsEffectivelyVisible(value) && value.Kind != PresentElementKind.Group).OrderBy(value => SelectionRoot(value).Order).ThenBy(value => value.Order))
            DrawElement(context, element, logical, opacity);
        context.Add(new HavenPopTransformCommand(target));
        context.Add(new HavenPopClipCommand(target));
    }

    private void DrawElement(HavenDrawingContext context, PresentElement element, HavenRect slideRect, double opacity)
    {
        var rect = ElementRect(element, slideRect, _gestureActivated && _dragElementId is not null && IsSelectedForTransform(element));
        var rotation = PreviewRotation(element);
        if (Math.Abs(rotation) > .001) context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: rotation), new HavenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
        if (element.Kind == PresentElementKind.Shape && element.VectorShape is null
            && element.Style.Shadow && element.Style.ShadowStyle is { } shadow)
        {
            var radians = shadow.RotateWithShape ? 0 : -rotation * Math.PI / 180;
            var offsetX = shadow.OffsetX * Math.Cos(radians) - shadow.OffsetY * Math.Sin(radians);
            var offsetY = shadow.OffsetX * Math.Sin(radians) + shadow.OffsetY * Math.Cos(radians);
            context.Add(new HavenShadowCommand(rect, new HavenShadow(Brush(shadow.Color, "#59000000"),
                shadow.Blur, offsetX, offsetY, Opacity: opacity * element.Opacity), Math.Max(0, rect.Width * element.Style.CornerRadius)));
        }
        switch (element.Kind)
        {
            case PresentElementKind.Text:
                DrawEditableElementText(context, element, rect, opacity);
                break;
            case PresentElementKind.Shape when element.VectorShape is { } vector:
                var renderedVector = BuildPreviewVector(element, vector);
                DrawVectorShape(context, renderedVector, rect, opacity * element.Opacity);
                DrawEditableElementText(context, element, rect, opacity);
                if (_vectorPointEditingElementId == element.Id)
                    DrawVectorHandles(context, renderedVector, rect, opacity);
                break;
            case PresentElementKind.Shape:
                context.Add(new HavenFillRoundedRectCommand(rect, Brush(element.Style.FillColor, "AccentSubtle"), Math.Max(0, rect.Width * element.Style.CornerRadius), opacity * element.Opacity));
                if (element.Style.StrokeWidth > 0)
                    context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(Brush(element.Style.StrokeColor, "Accent"), element.Style.StrokeWidth), Math.Max(0, rect.Width * element.Style.CornerRadius), opacity * element.Opacity));
                DrawEditableElementText(context, element, rect, opacity);
                break;
            case PresentElementKind.Table:
                DrawTable(context, element.ReadTable(), rect, opacity * element.Opacity);
                break;
            case PresentElementKind.Chart:
                DrawChart(context, element.ReadChart(), rect, opacity * element.Opacity);
                break;
            case PresentElementKind.Image:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 6, opacity * element.Opacity));
                context.Add(new HavenTextCommand(rect, new HavenTextLayout(string.IsNullOrWhiteSpace(element.AlternativeText) ? "Image" : element.AlternativeText, "Segoe UI", 14, 600, rect.Width, true), new HavenTokenBrush("TextSecondary"), opacity));
                break;
            case PresentElementKind.GenUi:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 8, opacity * element.Opacity)); context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Accent"), 1.5), 8, opacity));
                context.Add(new HavenTextCommand(rect, new HavenTextLayout("GenUI · " + (string.IsNullOrWhiteSpace(element.AlternativeText) ? "interactive object" : element.AlternativeText), "Segoe UI", 13, 600, rect.Width, true), new HavenTokenBrush("TextPrimary"), opacity));
                break;
            case PresentElementKind.Media:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 8, opacity * element.Opacity)); context.Add(new HavenTextCommand(rect, new HavenTextLayout("Media", "Segoe UI", 14, 600, rect.Width, true), new HavenTokenBrush("TextSecondary"), opacity));
                break;
        }
        if (Math.Abs(rotation) > .001) context.Add(new HavenPopTransformCommand(rect));
    }

    private static void DrawVectorShape(HavenDrawingContext context, DocumentVectorShape shape, HavenRect rect, double opacity)
    {
        foreach (var path in shape.Paths)
        {
            var figures = new List<HavenPathFigure>();
            foreach (var subpath in path.Subpaths.Where(value => value.Nodes.Count > 0))
            {
                var first = DocumentVectorShapes.TransformPoint(shape, subpath.Nodes[0].Point); var segments = new List<HavenPathSegment>();
                for (var index = 1; index < subpath.Nodes.Count; index++)
                {
                    var node = subpath.Nodes[index]; var end = DocumentVectorShapes.TransformPoint(shape, node.Point);
                    segments.Add(node.IncomingSegment switch
                    {
                        DocumentVectorSegmentKind.Quadratic when node.Control1 is { } c1 => new HavenQuadraticBezierSegment(ToHaven(DocumentVectorShapes.TransformPoint(shape, c1)), ToHaven(end)),
                        DocumentVectorSegmentKind.Cubic when node.Control1 is { } c1 && node.Control2 is { } c2 => new HavenCubicBezierSegment(ToHaven(DocumentVectorShapes.TransformPoint(shape, c1)), ToHaven(DocumentVectorShapes.TransformPoint(shape, c2)), ToHaven(end)),
                        _ => new HavenLineSegment(ToHaven(end))
                    });
                }
                figures.Add(new HavenPathFigure(ToHaven(first), segments, subpath.Closed));
            }
            if (figures.Count == 0) continue;
            var geometry = new HavenGeometry(new HavenPath(figures, path.FillRule == DocumentVectorFillRule.NonZero ? HavenFillRule.NonZero : HavenFillRule.EvenOdd), new HavenRect(shape.ViewBox.X, shape.ViewBox.Y, shape.ViewBox.Width, shape.ViewBox.Height));
            HavenBrush? fill = path.Fill.Kind == DocumentVectorFillKind.None ? null : Brush(path.Fill.Color, "AccentSubtle", path.Fill.Opacity); var stroke = path.Stroke.Enabled && path.Stroke.Width > 0 ? new HavenPen(Brush(path.Stroke.Color, "Accent", path.Stroke.Opacity), path.Stroke.Width) : null; context.Add(new HavenGeometryCommand(rect, geometry, fill, stroke, opacity * path.Opacity));
        }
    }
    private DocumentVectorShape BuildPreviewVector(PresentElement element, DocumentVectorShape source)
    {
        if (_dragVectorHandle is not { } handle || handle.ElementId != element.Id
            || !TryLocalToVectorPoint(element.Id, _pointerCurrent, out var point)) return source;
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(source));
        if (handle.Kind == PresentVectorHandleKind.Node) editor.MoveNode(handle.NodeId, point.X, point.Y);
        else editor.MoveControlPoint(handle.NodeId, handle.Kind == PresentVectorHandleKind.Control1 ? 1 : 2, point.X, point.Y);
        return editor.Shape;
    }

    private static void DrawVectorHandles(HavenDrawingContext context, DocumentVectorShape shape, HavenRect rect, double opacity)
    {
        var guidePen = new HavenPen(new HavenTokenBrush("Accent"), 1);
        foreach (var subpath in shape.Paths.SelectMany(path => path.Subpaths))
        {
            for (var index = 0; index < subpath.Nodes.Count; index++)
            {
                var node = subpath.Nodes[index];
                var nodePoint = VectorPointToScreen(shape, node.Point, rect);
                context.Add(new HavenEllipseCommand(new HavenRect(nodePoint.X - 4, nodePoint.Y - 4, 8, 8), new HavenTokenBrush("Accent"), null, opacity));
                if (index == 0) continue;
                var previous = VectorPointToScreen(shape, subpath.Nodes[index - 1].Point, rect);
                if (node.Control1 is { } control1 && node.IncomingSegment != DocumentVectorSegmentKind.Line)
                {
                    var firstControl = VectorPointToScreen(shape, control1, rect);
                    context.Add(new HavenLineCommand(previous, firstControl, guidePen, opacity));
                    if (node.IncomingSegment == DocumentVectorSegmentKind.Quadratic) context.Add(new HavenLineCommand(firstControl, nodePoint, guidePen, opacity));
                    context.Add(new HavenEllipseCommand(new HavenRect(firstControl.X - 3, firstControl.Y - 3, 6, 6), new HavenTokenBrush("SurfaceRaised"), guidePen, opacity));
                }
                if (node.Control2 is { } control2 && node.IncomingSegment == DocumentVectorSegmentKind.Cubic)
                {
                    var secondControl = VectorPointToScreen(shape, control2, rect);
                    context.Add(new HavenLineCommand(nodePoint, secondControl, guidePen, opacity));
                    context.Add(new HavenEllipseCommand(new HavenRect(secondControl.X - 3, secondControl.Y - 3, 6, 6), new HavenTokenBrush("SurfaceRaised"), guidePen, opacity));
                }
            }
        }
    }

    private VectorHandleHit? HitVectorHandle(HavenPoint local)
    {
        if (_slide is null || _selectedIds.Count != 1) return null;
        var element = _slide.Elements.FirstOrDefault(value => _selectedIds.Contains(value.Id) && !value.Locked && value.VectorShape is not null
            && value.Id == _vectorPointEditingElementId);
        if (element?.VectorShape is not { } shape) return null;
        var rect = ElementRect(element, SlideRectLocal(), false);
        var point = InverseRotatePoint(local, rect, element.RotationDegrees);
        VectorHandleHit? best = null;
        var bestDistance = 100d;
        void Consider(Guid nodeId, PresentVectorHandleKind kind, DocumentVectorPoint source)
        {
            var screen = VectorPointToScreen(shape, source, rect);
            var distance = DistanceSquared(point, screen);
            if (distance >= bestDistance) return;
            bestDistance = distance;
            best = new VectorHandleHit(element.Id, nodeId, kind);
        }
        foreach (var node in shape.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes))
        {
            Consider(node.Id, PresentVectorHandleKind.Node, node.Point);
            if (node.Control1 is { } control1 && node.IncomingSegment != DocumentVectorSegmentKind.Line) Consider(node.Id, PresentVectorHandleKind.Control1, control1);
            if (node.Control2 is { } control2 && node.IncomingSegment == DocumentVectorSegmentKind.Cubic) Consider(node.Id, PresentVectorHandleKind.Control2, control2);
        }
        return best;
    }

    private bool TryLocalToVectorPoint(Guid elementId, HavenPoint local, out DocumentVectorPoint point)
    {
        point = new DocumentVectorPoint(0, 0);
        if (_slide?.Elements.FirstOrDefault(value => value.Id == elementId) is not { VectorShape: { } shape } element) return false;
        var rect = ElementRect(element, SlideRectLocal(), false);
        if (rect.Width <= 0.0001 || rect.Height <= 0.0001) return false;
        var unrotated = InverseRotatePoint(local, rect, element.RotationDegrees);
        var transformed = new DocumentVectorPoint(
            shape.ViewBox.X + (unrotated.X - rect.X) / rect.Width * shape.ViewBox.Width,
            shape.ViewBox.Y + (unrotated.Y - rect.Y) / rect.Height * shape.ViewBox.Height);
        point = DocumentVectorShapes.InverseTransformPoint(shape, transformed);
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    private static HavenPoint VectorPointToScreen(DocumentVectorShape shape, DocumentVectorPoint point, HavenRect rect)
    {
        var transformed = DocumentVectorShapes.TransformPoint(shape, point);
        return new HavenPoint(
            rect.X + (transformed.X - shape.ViewBox.X) / shape.ViewBox.Width * rect.Width,
            rect.Y + (transformed.Y - shape.ViewBox.Y) / shape.ViewBox.Height * rect.Height);
    }

    private static HavenPoint InverseRotatePoint(HavenPoint point, HavenRect rect, double degrees)
    {
        if (Math.Abs(degrees) < 0.001) return point;
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var radians = -degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = point.X - centerX;
        var y = point.Y - centerY;
        return new HavenPoint(x * cos - y * sin + centerX, x * sin + y * cos + centerY);
    }

    private static double DistanceSquared(HavenPoint left, HavenPoint right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return x * x + y * y;
    }

    private readonly record struct VectorHandleHit(Guid ElementId, Guid NodeId, PresentVectorHandleKind Kind);

    private PresentElement? HitElement(HavenPoint local)
    {
        if (_slide is null) return null;
        var slideRect = SlideRectLocal();
        if (!slideRect.Contains(local)) return null;
        foreach (var element in _slide.Elements.Where(element => IsEffectivelyVisible(element) && element.Kind != PresentElementKind.Group).OrderByDescending(element => SelectionRoot(element).Order).ThenByDescending(element => element.Order))
        {
            var rect = ElementRect(element, slideRect, false);
            if (rect.Contains(InverseRotatePoint(local, rect, element.RotationDegrees))) return SelectionRoot(element);
        }
        return null;
    }

    private HavenRect SlideRectAbsolute() { var local = SlideRectLocal(); return new HavenRect(Bounds.X + local.X, Bounds.Y + local.Y, local.Width, local.Height); }
    private HavenRect SlideRectLocal()
    {
        var ratio = _document?.SlideSize is { HeightInches: > 0 } size ? size.WidthInches / size.HeightInches : 16d / 9d;
        var availableWidth = Math.Max(1, Bounds.Width - 32);
        var availableHeight = Math.Max(1, Bounds.Height - 32);
        var width = ManualScale is { } scale && _document is not null
            ? _document.SlideSize.WidthInches * 96 * scale
            : Math.Min(availableWidth, availableHeight * ratio) * _viewScale;
        var height = width / ratio;
        return new HavenRect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
    }

    private HavenRect ElementRect(PresentElement element, HavenRect slideRect, bool preview)
    {
        var measured = SlideRectLocal();
        if (_gestureActivated && _activeTransformHandle != PresentTransformHandle.None && IsSelectedForTransform(element))
        {
            var transform = BuildDirectTransform(_activeTransformHandle,
                (_pointerCurrent.X - _pointerStart.X) / measured.Width,
                (_pointerCurrent.Y - _pointerStart.Y) / measured.Height);
            // Match the owner's constraints so the release cannot jump away from
            // the exact dimensions previewed during a captured gesture.
            var projected = ProjectElementTransform(element, transform);
            var x = Math.Clamp(projected.X, 0, .99);
            var y = Math.Clamp(projected.Y, 0, .99);
            var width = Math.Clamp(projected.Width, .01, 1 - x);
            var height = Math.Clamp(projected.Height, .01, 1 - y);
            return new HavenRect(slideRect.X + x * slideRect.Width, slideRect.Y + y * slideRect.Height,
                width * slideRect.Width, height * slideRect.Height);
        }
        var delta = preview ? PreviewMoveDelta() : default;
        var dx = delta.X * slideRect.Width;
        var dy = delta.Y * slideRect.Height;
        return new HavenRect(slideRect.X + element.X * slideRect.Width + dx, slideRect.Y + element.Y * slideRect.Height + dy, element.Width * slideRect.Width, element.Height * slideRect.Height);
    }

    private static HavenPoint ToHaven(DocumentVectorPoint point) => new(point.X, point.Y);
    private static HavenBrush BackgroundBrush(PresentDocument document, PresentSlide slide)
    {
        var themeColor = document.Theme.Colors.Background;
        if (slide.Background.Kind == PresentBackgroundKind.Solid) return Brush(slide.Background.Color, themeColor);
        if (slide.Background.Kind == PresentBackgroundKind.Theme && document.Theme.Background.Kind == PresentBackgroundKind.Solid)
            return Brush(document.Theme.Background.Color, themeColor);
        return Brush(themeColor, "Surface");
    }

    private static HavenBrush Brush(string? value, string fallback, double opacity = 1)
    {
        // Accept exactly the literal colour forms accepted by the editor (including
        // #RGB, #ARGB and named colours). A literal fallback must never become a theme key.
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (Avalonia.Media.Color.TryParse(text, out var color) || Avalonia.Media.Color.TryParse(fallback, out color))
            return new HavenSolidBrush((byte)Math.Clamp(color.A * opacity, 0, 255), color.R, color.G, color.B);
        return new HavenTokenBrush(fallback);
    }
}
