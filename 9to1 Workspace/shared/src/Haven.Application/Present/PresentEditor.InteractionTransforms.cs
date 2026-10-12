using Haven.Core;

namespace Haven.Application;

/// <summary>Owner-space transform projection shared by the editor and its transient preview.</summary>
public static class PresentInteractionTransforms
{
    public readonly record struct Geometry(double X, double Y, double Width, double Height, double RotationDegrees);

    public static Geometry ProjectChild(PresentElement group, PresentElement child, double deltaX, double deltaY,
        double deltaWidth, double deltaHeight, double deltaRotation, double slideAspectRatio)
    {
        var x = Math.Clamp(group.X + deltaX, 0, .99); var y = Math.Clamp(group.Y + deltaY, 0, .99);
        var width = Math.Clamp(group.Width + deltaWidth, .01, 1 - x);
        var height = Math.Clamp(group.Height + deltaHeight, .01, 1 - y);
        var scaleX = width / group.Width; var scaleY = height / group.Height;
        // Rotation operates in physical slide space, not anisotropic normalised axes.
        var aspect = double.IsFinite(slideAspectRatio) && slideAspectRatio > 0 ? slideAspectRatio : 16d / 9;
        var offsetX = (child.X + child.Width / 2 - group.X - group.Width / 2) * aspect;
        var offsetY = child.Y + child.Height / 2 - group.Y - group.Height / 2;
        var radians = group.RotationDegrees * Math.PI / 180;
        var localX = (Math.Cos(radians) * offsetX + Math.Sin(radians) * offsetY) * scaleX;
        var localY = (-Math.Sin(radians) * offsetX + Math.Cos(radians) * offsetY) * scaleY;
        radians += deltaRotation * Math.PI / 180;
        var childWidth = child.Width * scaleX; var childHeight = child.Height * scaleY;
        return new(x + width / 2 + (Math.Cos(radians) * localX - Math.Sin(radians) * localY) / aspect - childWidth / 2,
            y + height / 2 + Math.Sin(radians) * localX + Math.Cos(radians) * localY - childHeight / 2,
            childWidth, childHeight, child.RotationDegrees + deltaRotation);
    }
}

public sealed partial class PresentEditor
{
    private IReadOnlyList<PresentElement> InteractionRoots()
    {
        var selected = EditableSelection();
        var ids = selected.Select(element => element.Id).ToHashSet();
        return selected.Where(element => !Ancestors(element).Any(parent => ids.Contains(parent.Id))).ToArray();
    }

    private IEnumerable<PresentElement> Ancestors(PresentElement element)
    {
        var seen = new HashSet<Guid>();
        while (element.ParentGroupId is { } parentId && seen.Add(parentId)
            && SelectedSlide.Elements.FirstOrDefault(item => item.Id == parentId && item.Kind == PresentElementKind.Group) is { } parent)
        { yield return parent; element = parent; }
    }

    private IReadOnlyList<PresentElement> InteractionMembers(IReadOnlyList<PresentElement> roots)
    {
        var ids = roots.Select(element => element.Id).ToHashSet();
        return SelectedSlide.Elements.Where(element => ids.Contains(element.Id) || Ancestors(element).Any(parent => ids.Contains(parent.Id))).ToArray();
    }

    private void ApplyInteractionTransform(PresentElement root, double dx, double dy, double dw, double dh, double rotation)
    {
        // Project all descendants from the same unchanged group snapshot before
        // editing the root. A nested child is transformed once, never twice.
        var descendants = root.Kind == PresentElementKind.Group
            ? SelectedSlide.Elements.Where(element => Ancestors(element).Any(parent => parent.Id == root.Id)).ToArray() : [];
        var projected = descendants.Select(element => (Element: element, Geometry: PresentInteractionTransforms.ProjectChild(root,
            element, dx, dy, dw, dh, rotation, Document.SlideSize.WidthInches / Document.SlideSize.HeightInches))).ToArray();
        var x = Math.Clamp(root.X + dx, 0, .99); var y = Math.Clamp(root.Y + dy, 0, .99);
        root.Width = Math.Clamp(root.Width + dw, .01, 1 - x);
        root.Height = Math.Clamp(root.Height + dh, .01, 1 - y);
        root.X = x; root.Y = y; root.RotationDegrees += rotation;
        foreach (var (element, geometry) in projected)
        {
            element.X = geometry.X; element.Y = geometry.Y; element.Width = geometry.Width;
            element.Height = geometry.Height; element.RotationDegrees = geometry.RotationDegrees;
        }
    }
}
