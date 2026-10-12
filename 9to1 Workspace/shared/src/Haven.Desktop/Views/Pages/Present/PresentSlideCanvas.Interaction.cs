using Haven.Application;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

public enum PresentInteractionState
{
    Idle, HoveringObject, ObjectSelected, TextEditing, ObjectDragging, ObjectResizing,
    ObjectRotating, MultipleObjectsSelected, InsertionMode, MarqueeSelecting, CanvasPanning, CanvasZooming
}

public sealed record PresentObjectInteractionGeometry(Guid ElementId, double X, double Y, double Width, double Height, double RotationDegrees);

internal sealed partial class PresentSlideCanvas
{
    private const double DragThresholdSquared = 9;
    private Guid? _hoveredElementId;
    private bool _gestureActivated;
    private HavenKeyModifiers _gestureModifiers;
    private HashSet<Guid> _marqueeBaseSelection = [];
    private string? _insertionPreset;
    private DocumentVectorShape? _insertionPreview;
    private bool _insertionDragging;
    private PresentInteractionState _reportedState;
    private Guid? _reportedHover;

    public event EventHandler? InteractionChanged;
    public event EventHandler? GeometryPreviewed;
    public event Action<string, double, double, double, double>? ShapeInsertionRequested;
    public Guid? HoveredObjectId => _hoveredElementId;
    private bool HasDirectGesture => _dragElementId is not null || _dragVectorHandle is not null
        || _activeTransformHandle != PresentTransformHandle.None || _marqueeSelecting;
    public PresentInteractionState InteractionState => _insertionPreset is not null ? PresentInteractionState.InsertionMode
        : IsTextEditing ? PresentInteractionState.TextEditing
        : _gestureActivated && _activeTransformHandle == PresentTransformHandle.Rotate ? PresentInteractionState.ObjectRotating
        : _gestureActivated && (_activeTransformHandle != PresentTransformHandle.None || _dragVectorHandle is not null) ? PresentInteractionState.ObjectResizing
        : _gestureActivated && _dragElementId is not null ? PresentInteractionState.ObjectDragging
        : _gestureActivated && _marqueeSelecting ? PresentInteractionState.MarqueeSelecting
        : _selectedIds.Count > 1 ? PresentInteractionState.MultipleObjectsSelected
        : _selectedIds.Count == 1 ? PresentInteractionState.ObjectSelected
        : _hoveredElementId is not null ? PresentInteractionState.HoveringObject : PresentInteractionState.Idle;

    public IReadOnlyList<PresentObjectInteractionGeometry> LiveGeometry => _slide is null ? [] : _slide.Elements
        .Where(IsSelectedForTransform).Select(element =>
        {
            var paper = SlideRectLocal();
            var rect = ElementRect(element, paper, _gestureActivated && _dragElementId is not null);
            return new PresentObjectInteractionGeometry(element.Id, (rect.X - paper.X) / paper.Width,
                (rect.Y - paper.Y) / paper.Height, rect.Width / paper.Width, rect.Height / paper.Height, PreviewRotation(element));
        }).ToArray();

    private void InvalidateInteraction()
    {
        // Object geometry and caret/hover live inside a specialised document
        // renderer. Pointer samples do not change its CUI tree, style or measure.
        Invalidate(HavenInvalidationKinds.Paint);
        var state = InteractionState;
        if (state != _reportedState || _reportedHover != _hoveredElementId)
        {
            _reportedState = state; _reportedHover = _hoveredElementId;
            InteractionChanged?.Invoke(this, EventArgs.Empty);
        }
        if (_gestureActivated) GeometryPreviewed?.Invoke(this, EventArgs.Empty);
    }

    public void PointerHovered(HavenPoint? localPosition)
    {
        var hovered = localPosition is { } point && !HasDirectGesture && !IsTextEditing && _insertionPreset is null
            ? HitElement(point)?.Id : null;
        if (_hoveredElementId == hovered) return;
        _hoveredElementId = hovered; InvalidateInteraction();
    }

    private void DrawHover(HavenDrawingContext context, HavenRect paper, double opacity)
    {
        if (_hoveredElementId is not { } id || _selectedIds.Contains(id)
            || _slide?.Elements.FirstOrDefault(element => element.Id == id) is not { } element) return;
        var rect = ElementRect(element, paper, false);
        if (Math.Abs(element.RotationDegrees) > .001)
            context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: element.RotationDegrees), Center(rect)));
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Accent"), 1), 2, opacity * .55));
        if (Math.Abs(element.RotationDegrees) > .001) context.Add(new HavenPopTransformCommand(rect));
    }

    public void BeginShapeInsertion(string presetId, DocumentVectorShape preview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentNullException.ThrowIfNull(preview);
        CommitTextEdit(); ResetDirectGesture();
        _insertionPreset = presetId;
        _insertionPreview = DocumentVectorShapes.Clone(preview);
        _insertionDragging = false; _hoveredElementId = null;
        InvalidateInteraction();
    }

    public void CancelInteraction()
    {
        ResetDirectGesture(); _textPointerSelecting = false;
        _insertionPreset = null; _insertionPreview = null; _insertionDragging = false;
        InvalidateInteraction();
    }

    private bool TryBeginInsertion(HavenPointerInput input)
    {
        if (_insertionPreset is null) return false;
        if (!SlideRectLocal().Contains(input.LocalPosition)) return true;
        _pointerStart = _pointerCurrent = input.LocalPosition;
        _insertionDragging = true; _gestureModifiers = input.Modifiers;
        InvalidateInteraction(); return true;
    }

    private bool MoveInsertion(HavenPointerInput input)
    {
        if (!_insertionDragging) return false;
        _pointerCurrent = input.LocalPosition; _gestureModifiers = input.Modifiers;
        InvalidateInteraction(); return true;
    }

    private bool ReleaseInsertion(HavenPointerInput input)
    {
        if (_insertionPreset is null) return false;
        if (!_insertionDragging) return true;
        _pointerCurrent = input.LocalPosition;
        var preset = _insertionPreset; var rect = InsertionRect();
        CancelInteraction();
        ShapeInsertionRequested?.Invoke(preset, rect.X, rect.Y, rect.Width, rect.Height);
        return true;
    }

    private HavenRect InsertionRect()
    {
        var paper = SlideRectLocal();
        var start = new HavenPoint(Math.Clamp((_pointerStart.X - paper.X) / paper.Width, 0, 1), Math.Clamp((_pointerStart.Y - paper.Y) / paper.Height, 0, 1));
        var end = new HavenPoint(Math.Clamp((_pointerCurrent.X - paper.X) / paper.Width, 0, 1), Math.Clamp((_pointerCurrent.Y - paper.Y) / paper.Height, 0, 1));
        if (DistanceSquared(_pointerStart, _pointerCurrent) < DragThresholdSquared)
            return new HavenRect(Math.Clamp(start.X - .125, 0, .75), Math.Clamp(start.Y - .125, 0, .75), .25, .25);
        var rect = NormalizeRect(start, end);
        var width = Math.Max(.01, rect.Width); var height = Math.Max(.01, rect.Height);
        if (_gestureModifiers.HasFlag(HavenKeyModifiers.Shift))
        {
            // Preserve the selected preset's view box proportions in slide pixels.
            var ratio = (_insertionPreview?.ViewBox.Width ?? 1) / Math.Max(.0001, _insertionPreview?.ViewBox.Height ?? 1);
            height = width * paper.Width / paper.Height / ratio;
        }
        width = Math.Min(1, width); height = Math.Min(1, height);
        return new HavenRect(Math.Min(rect.X, 1 - width), Math.Min(rect.Y, 1 - height), width, height);
    }

    private void DrawInsertion(HavenDrawingContext context, double opacity)
    {
        if (!_insertionDragging || _insertionPreview is null) return;
        var rect = InsertionRect(); var paper = SlideRectAbsolute();
        var target = new HavenRect(paper.X + rect.X * paper.Width, paper.Y + rect.Y * paper.Height, rect.Width * paper.Width, rect.Height * paper.Height);
        DrawVectorShape(context, _insertionPreview, target, opacity * .75);
        context.Add(new HavenStrokeRoundedRectCommand(target, new HavenPen(new HavenTokenBrush("Accent"), 1), 0, opacity));
    }

    private PresentElement SelectionRoot(PresentElement element)
    {
        var seen = new HashSet<Guid>();
        while (_slide is not null && element.ParentGroupId is { } groupId && seen.Add(groupId)
            && _slide.Elements.FirstOrDefault(item => item.Id == groupId && item.Kind == PresentElementKind.Group) is { } group)
            element = group;
        return element;
    }

    private bool IsEffectivelyVisible(PresentElement element)
    {
        var seen = new HashSet<Guid>();
        while (true)
        {
            if (!element.Visible) return false;
            if (_slide is null || element.ParentGroupId is not { } id || !seen.Add(id)
                || _slide.Elements.FirstOrDefault(item => item.Id == id) is not { } parent) return true;
            element = parent;
        }
    }

    private bool IsSelectedForTransform(PresentElement element)
    {
        var root = SelectionRoot(element);
        return !root.Locked && (_selectedIds.Contains(element.Id) || _selectedIds.Contains(root.Id));
    }

    private HavenPoint PreviewMoveDelta()
    {
        if (_slide is null) return default;
        var elements = _slide.Elements.Where(IsSelectedForTransform).ToArray();
        if (elements.Length == 0) return default;
        var paper = SlideRectLocal();
        var dx = (_pointerCurrent.X - _pointerStart.X) / paper.Width;
        var dy = (_pointerCurrent.Y - _pointerStart.Y) / paper.Height;
        return new HavenPoint(Math.Clamp(dx, -elements.Min(item => item.X), Math.Max(0, 1 - elements.Max(item => item.X + item.Width))),
            Math.Clamp(dy, -elements.Min(item => item.Y), Math.Max(0, 1 - elements.Max(item => item.Y + item.Height))));
    }

    private PresentObjectInteractionGeometry ProjectElementTransform(PresentElement element, DirectTransform transform)
    {
        var root = SelectionRoot(element);
        if (root.Id != element.Id && _selectedIds.Contains(root.Id))
        {
            var projection = PresentInteractionTransforms.ProjectChild(root, element, transform.DeltaX, transform.DeltaY,
                transform.DeltaWidth, transform.DeltaHeight, transform.DeltaRotation, _document!.SlideSize.WidthInches / _document.SlideSize.HeightInches);
            return new(element.Id, projection.X, projection.Y, projection.Width, projection.Height, projection.RotationDegrees);
        }
        return new(element.Id, element.X + transform.DeltaX, element.Y + transform.DeltaY,
            element.Width + transform.DeltaWidth, element.Height + transform.DeltaHeight, element.RotationDegrees + transform.DeltaRotation);
    }
}
