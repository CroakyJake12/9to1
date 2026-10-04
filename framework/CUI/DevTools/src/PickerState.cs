namespace Haven.CUI.DevTools;

public enum ElementPickerMode
{
    Inactive,
    Armed,
    Hovering,
    Picked,
    Cancelled
}

public sealed record ElementPickerSnapshot(
    ElementPickerMode Mode,
    ElementId? HoveredElementId,
    ElementId? PickedElementId,
    CuiPoint? PointerPosition,
    CuiRect? HighlightBounds,
    string? Status);

public sealed class ElementPickerState
{
    public ElementPickerSnapshot Current { get; private set; } =
        new(ElementPickerMode.Inactive, null, null, null, null, null);

    public void Arm() => Current = new(ElementPickerMode.Armed, null, null, null, null, "Pick an element; press Escape to cancel.");

    public bool Hover(ElementId elementId, CuiPoint pointerPosition, CuiRect bounds, bool isInspectable)
    {
        if (Current.Mode is not (ElementPickerMode.Armed or ElementPickerMode.Hovering) || !isInspectable)
            return false;

        Current = new(ElementPickerMode.Hovering, elementId, null, pointerPosition, bounds, "Click to select this element.");
        return true;
    }

    public ElementId? Pick()
    {
        if (Current.Mode != ElementPickerMode.Hovering || Current.HoveredElementId is not { } elementId)
            return null;

        Current = Current with
        {
            Mode = ElementPickerMode.Picked,
            PickedElementId = elementId,
            Status = "Element selected."
        };
        return elementId;
    }

    public void Cancel(string? reason = null) =>
        Current = new(ElementPickerMode.Cancelled, null, null, null, null, reason ?? "Element picking cancelled.");

    public void Reset() => Current = new(ElementPickerMode.Inactive, null, null, null, null, null);

    public void RemoveStaleElements(CuiElementTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if ((Current.HoveredElementId is { } hovered && !tree.Contains(hovered))
            || (Current.PickedElementId is { } picked && !tree.Contains(picked)))
        {
            Cancel("The inspected element no longer exists.");
        }
    }
}
