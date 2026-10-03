using Haven.Core.Mathematics;

namespace Haven.Application.Mathematics;

/// <summary>One respondent answer to a pinned question. Response identity/revision changes
/// independently of the authored question revision; every edit validates the full candidate
/// against that original question, preserving a usable answer after stale or invalid edits.</summary>
public sealed class GraphResponseEditorSession
{
    private readonly object _gate = new();
    private readonly GraphDefinition _question;
    private readonly MathServiceLimits _limits;
    private GraphResponse _response;
    public GraphResponseEditorSession(GraphDefinition question, GraphResponse? initial = null,
        MathServiceLimits? limits = null)
    {
        _limits = limits ?? new();
        _question = MathObjectCodec.Capture(question, _limits);
        _response = MathObjectCodec.Capture(initial ?? new(Guid.NewGuid(), 1,
            _question.GraphID, _question.Revision, [], []), _limits);
        Validate(_response);
    }
    public GraphDefinition Question => MathObjectCodec.Capture(_question, _limits);
    public GraphResponse Snapshot()
    { lock (_gate) return MathObjectCodec.Capture(_response, _limits); }
    public GraphDefinition Projection()
    {
        lock (_gate) return _response.Actions.Length == 0 ? Question
            : GraphResponseProjection.Apply(_question, _response, _limits);
    }
    public GraphResponse Replace(long expectedRevision, MathExpression[] expressions, GraphResponseAction[] actions)
    {
        lock (_gate)
        {
            if (expectedRevision != _response.Revision) throw new InvalidOperationException("RevisionConflict");
            var candidate = MathObjectCodec.Capture(_response with { Revision = checked(_response.Revision + 1),
                Expressions = expressions, Actions = actions }, _limits);
            Validate(candidate);
            _response = candidate;
            return Snapshot();
        }
    }
    public GraphResponse PlacePoint(long expectedRevision, GraphCoordinate point)
    {
        lock (_gate)
        {
            if (expectedRevision != _response.Revision) throw new InvalidOperationException("RevisionConflict");
            if (!_question.ResponseTools.Contains(GraphResponseTool.PlacePoint))
                throw new InvalidOperationException("GraphToolNotDeclared");
            if (point.X < _question.Axes.XMinimum || point.X > _question.Axes.XMaximum ||
                point.Y < _question.Axes.YMinimum || point.Y > _question.Axes.YMaximum)
                throw new InvalidDataException("GraphCoordinateOutsideRange");
            if (_response.Actions.Length > 1 || _response.Actions.Any(x => x.Tool != GraphResponseTool.PlacePoint))
                throw new NotSupportedException("SinglePointEditorUnavailableForThisResponse");
            var pointID = _response.Actions.SingleOrDefault()?.Primitive.PrimitiveID ?? Guid.NewGuid();
            return Replace(expectedRevision, [], [new(GraphResponseTool.PlacePoint, new GraphPoint(pointID, point))]);
        }
    }
    private void Validate(GraphResponse response)
    {
        if (response.GraphID != _question.GraphID || response.GraphRevision != _question.Revision)
            throw new InvalidOperationException("RevisionConflict");
        if (response.Actions.Length != 0) _ = GraphResponseProjection.Apply(_question, response, _limits);
        else if (response.Expressions.Length != 0) throw new InvalidDataException("ExpressionsWithoutGraphResponse");
    }
}
