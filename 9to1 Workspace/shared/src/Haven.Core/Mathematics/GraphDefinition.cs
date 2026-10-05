using System.Text.Json.Serialization;

namespace Haven.Core.Mathematics;

public readonly record struct GraphCoordinate(decimal X, decimal Y);
public sealed record GraphAxes(decimal XMinimum, decimal XMaximum, decimal YMinimum,
    decimal YMaximum, bool ShowAxes = true, bool ShowGrid = true,
    decimal XGridSpacing = 1, decimal YGridSpacing = 1,
    string XLabel = "x", string YLabel = "y");
public sealed record GraphDomain(decimal Minimum, decimal Maximum);

/// <summary>Mathematical graph data, separate from the shared executable Node Graph.
/// Expression references bind exact stable identity and revision within this snapshot.</summary>
public sealed record GraphDefinition(Guid GraphID, long Revision, GraphAxes Axes,
    MathExpression[] Expressions, GraphPrimitive[] Primitives,
    GraphResponseTool[] ResponseTools, string AccessibleDescription = "", int SchemaVersion = 1);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GraphFunction), "function")]
[JsonDerivedType(typeof(GraphEquation), "equation")]
[JsonDerivedType(typeof(GraphInequality), "inequality")]
[JsonDerivedType(typeof(GraphPoint), "point")]
[JsonDerivedType(typeof(GraphCoordinateTable), "coordinateTable")]
[JsonDerivedType(typeof(GraphLine), "line")]
[JsonDerivedType(typeof(GraphCurve), "curve")]
[JsonDerivedType(typeof(GraphRegion), "region")]
public abstract record GraphPrimitive(Guid PrimitiveID);
public sealed record GraphFunction(Guid PrimitiveID, MathExpressionReference Expression,
    string Variable = "x", GraphDomain? Domain = null) : GraphPrimitive(PrimitiveID);
public sealed record GraphEquation(Guid PrimitiveID, MathExpressionReference Expression) : GraphPrimitive(PrimitiveID);
public sealed record GraphInequality(Guid PrimitiveID, MathExpressionReference Expression,
    bool IncludeBoundary = true) : GraphPrimitive(PrimitiveID);
public sealed record GraphPoint(Guid PrimitiveID, GraphCoordinate Position) : GraphPrimitive(PrimitiveID);
public sealed record GraphCoordinateTable(Guid PrimitiveID, GraphCoordinate[] Points) : GraphPrimitive(PrimitiveID);
public sealed record GraphLine(Guid PrimitiveID, GraphCoordinate Start, GraphCoordinate End) : GraphPrimitive(PrimitiveID);
public sealed record GraphCurve(Guid PrimitiveID, GraphCoordinate[] Points) : GraphPrimitive(PrimitiveID);
public sealed record GraphRegion(Guid PrimitiveID, GraphCoordinate[] Boundary) : GraphPrimitive(PrimitiveID);
