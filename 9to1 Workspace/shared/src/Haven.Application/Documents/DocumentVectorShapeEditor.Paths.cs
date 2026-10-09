using Haven.Core;

namespace Haven.Application;

public sealed partial class DocumentVectorShapeEditor
{
    public void SetSubpathClosed(Guid pathId, Guid subpathId, bool closed, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        if (!Enum.IsDefined(origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        var current = RequireOriginalScopedSubpath(Shape, pathId, subpathId);
        if (current.Closed == closed) return;
        Edit(closed ? "Close vector subpath" : "Open vector subpath", origin,
            sameShape => RequireOriginalScopedSubpath(sameShape, pathId, subpathId).Closed = closed);
    }
    private static DocumentVectorSubpath RequireOriginalScopedSubpath(DocumentVectorShape shape, Guid pathId, Guid subpathId)
    {
        var path = shape.Paths.SingleOrDefault(path => path.Id == pathId) ?? throw new ArgumentOutOfRangeException(nameof(pathId));
        return path.Subpaths.SingleOrDefault(subpath => subpath.Id == subpathId) ?? throw new ArgumentOutOfRangeException(nameof(subpathId));
    }
}
