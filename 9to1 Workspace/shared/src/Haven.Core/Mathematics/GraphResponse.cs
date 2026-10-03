namespace Haven.Core.Mathematics;

public enum GraphResponseTool
{
    PlacePoint, DrawLine, DrawCurve, SubmitCoordinates,
    PlotFunction, PlotEquation, PlotInequality, IdentifyRegion, ManipulatePrimitive
}

/// <summary>One atomic response to an exact graph version, with inspectable typed actions.
/// The owning product also checks responder identity and current permissions.</summary>
public sealed record GraphResponse(Guid ResponseID, long Revision, Guid GraphID,
    long GraphRevision, MathExpression[] Expressions, GraphResponseAction[] Actions,
    int SchemaVersion = 1);
public sealed record GraphResponseAction(GraphResponseTool Tool, GraphPrimitive Primitive,
    Guid? TargetPrimitiveID = null);
