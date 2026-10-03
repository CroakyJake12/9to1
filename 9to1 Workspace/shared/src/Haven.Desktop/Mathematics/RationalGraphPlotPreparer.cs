using System.Globalization;
using System.Numerics;
using AngouriMath;
using Haven.Application.Mathematics;
using Haven.Core.Mathematics;
using PeterO.Numbers;
using Rational = AngouriMath.Entity.Number.Rational;
using Integer = AngouriMath.Entity.Number.Integer;

namespace Haven.Desktop.Mathematics;

/// <summary>Prepare bounded display paths from the maintained mathematical AST.
/// Exact original divisors are checked before coordinates are rounded for display.
/// Supported zero sets are constants, affine expressions and their products or
/// integer powers. Unclassified denominator zero sets refuse the primitive;
/// samples cannot prove continuity, domains, equivalence or grading.</summary>
public sealed class RationalGraphPlotPreparer : IGraphPlotPreparer
{
    public const string Implementation = "CSharpMath/1.0.0-pre.1+AngouriMath/2.5.0/original-rational-display-v1";
    public async ValueTask<PreparedGraphPlot> PrepareAsync(GraphDefinition graph,
        GraphPlotPreparationPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(policy);
        policy.Validate(); cancellationToken.ThrowIfCancellationRequested();
        var snapshot = MathObjectCodec.Capture(graph);
        // This original task is awaited through settlement. Cooperative admission
        // and checks do not claim a hard parser CPU, memory or wall-time boundary.
        return await Task.Run(() => Prepare(snapshot, policy, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static PreparedGraphPlot Prepare(GraphDefinition graph,
        GraphPlotPreparationPolicy policy, CancellationToken token)
    {
        var segments = new List<GraphPlotDisplaySegment>();
        var exclusions = new List<GraphPlotOriginalExclusion>();
        var regions = new List<GraphPlotDisplayRegion>();
        var refusals = new List<GraphPlotPreparationRefusal>();
        var operationBudget = new OperationBudget(policy.MaxOperations, token);
        foreach (var primitive in graph.Primitives)
        {
            token.ThrowIfCancellationRequested();
            var reference = primitive switch
            {
                GraphFunction value => (MathExpressionReference?)value.Expression,
                GraphEquation value => value.Expression, GraphInequality value => value.Expression, _ => null
            };
            if (reference is null) continue;
            var expression = graph.Expressions.Single(value =>
                value.ExpressionID == reference.ExpressionID && value.Revision == reference.Revision);
            try
            {
                var function = primitive as GraphFunction ?? new GraphFunction(primitive.PrimitiveID, reference);
                var source = expression.LaTeX; var includeBoundary = true; bool? shadeAbove = null;
                if (primitive is GraphEquation or GraphInequality)
                {
                    var relation = RationalGraphRelationAstReader.Read(expression, primitive is GraphInequality,
                        primitive is not GraphInequality inequality || inequality.IncludeBoundary,
                        policy.EffectiveExpressionLimits, token);
                    source = relation.RightHandLaTeX; includeBoundary = relation.IncludeBoundary; shadeAbove = relation.ShadeAbove;
                }
                var reading = new MathExpressionEvaluationPolicy(Variable: function.Variable,
                    Limits: policy.EffectiveExpressionLimits); reading.Validate();
                var syntax = new CSharpMathRationalAstBridge(reading, token).Read(source);
                var preparation = new FunctionPreparation(policy, token, operationBudget);
                var result = preparation.Read(function, syntax, graph.Axes, includeBoundary, shadeAbove);
                if (segments.Count + result.Segments.Count + regions.Count + result.Regions.Count > policy.MaxSegments ||
                    segments.Sum(segment => segment.Points.Count) + result.Segments.Sum(segment => segment.Points.Count) +
                    regions.Sum(region => region.Boundary.Count) + result.Regions.Sum(region => region.Boundary.Count) > policy.MaxTotalCoordinates ||
                    exclusions.Count + result.Exclusions.Count > policy.MaxOriginalExclusions)
                    throw new RationalAdmissionException("GraphPlotPreparedBudgetExceeded");
                // A function is published only after its entire preparation succeeds.
                segments.AddRange(result.Segments); exclusions.AddRange(result.Exclusions); regions.AddRange(result.Regions);
            }
            catch (RationalAdmissionException error) { refusals.Add(new(primitive.PrimitiveID, error.Diagnostic)); }
            catch (AngouriMath.Core.Exceptions.ParseException) { refusals.Add(new(primitive.PrimitiveID, "MathParseError")); }
            catch (OverflowException) { refusals.Add(new(primitive.PrimitiveID, "GraphPlotArithmeticBudgetExceeded")); }
        }
        token.ThrowIfCancellationRequested();
        return new(graph, policy, segments, exclusions, refusals, Implementation, regions);
    }

    private sealed class OperationBudget(int maximum, CancellationToken token)
    {
        private int _operations;
        public void Check()
        {
            token.ThrowIfCancellationRequested();
            if (++_operations > maximum) throw new RationalAdmissionException("GraphOperationBudgetExceeded");
        }
    }

    private sealed class FunctionPreparation(GraphPlotPreparationPolicy policy, CancellationToken token,
        OperationBudget operationBudget)
    {
        private readonly MathRationalEvaluationLimits _limits = policy.EffectiveExpressionLimits;
        private readonly List<ERational> _roots = [];
        private int _nodes;

        public (List<GraphPlotDisplaySegment> Segments, List<GraphPlotOriginalExclusion> Exclusions, List<GraphPlotDisplayRegion> Regions)
            Read(GraphFunction function, Entity original, GraphAxes axes, bool includeBoundary, bool? shadeAbove)
        {
            AdmitOriginal(original, 0);
            var minimum = Decimal(axes.XMinimum); var maximum = Decimal(axes.XMaximum);
            if (function.Domain is { } domain)
            {
                var declaredMinimum = Decimal(domain.Minimum); var declaredMaximum = Decimal(domain.Maximum);
                if (declaredMinimum.CompareTo(minimum) > 0) minimum = declaredMinimum;
                if (declaredMaximum.CompareTo(maximum) < 0) maximum = declaredMaximum;
            }
            if (minimum.CompareTo(maximum) >= 0)
                throw new RationalAdmissionException("GraphPlotVisibleDomainEmpty");
            var leftDisplay = Display(minimum); var rightDisplay = Display(maximum);
            if (leftDisplay >= rightDisplay)
                throw new RationalAdmissionException("GraphRangeBelowDisplayPrecision");
            var visibleRoots = _roots.Where(root => root.CompareTo(minimum) >= 0 && root.CompareTo(maximum) <= 0)
                .OrderBy(root => root, Comparer<ERational>.Create((left, right) => left.CompareTo(right))).ToArray();
            var excluded = visibleRoots.Select(root => new GraphPlotOriginalExclusion(function.PrimitiveID,
                function.Expression, ExactText(root), Display(root))).ToList();
            var boundaries = new List<ERational> { minimum };
            boundaries.AddRange(visibleRoots.Where(root => root.CompareTo(minimum) > 0 && root.CompareTo(maximum) < 0));
            boundaries.Add(maximum);
            var segments = new List<GraphPlotDisplaySegment>();
            var regions = new List<GraphPlotDisplayRegion>();
            var totalPoints = 0;
            var minimumY = Decimal(axes.YMinimum); var maximumY = Decimal(axes.YMaximum);
            for (var interval = 0; interval < boundaries.Count - 1; interval++)
            {
                var left = boundaries[interval]; var right = boundaries[interval + 1];
                var points = new List<GraphPlotDisplayPoint>();
                var fillPoints = new List<GraphPlotDisplayPoint>();
                void FinishPath()
                {
                    if (points.Count >= 2)
                    {
                        totalPoints = checked(totalPoints + points.Count);
                        if (segments.Count + regions.Count >= policy.MaxSegments || totalPoints > policy.MaxTotalCoordinates)
                            throw new RationalAdmissionException("GraphPlotPreparedBudgetExceeded");
                        segments.Add(new(function.PrimitiveID, function.Expression, points, includeBoundary));
                    }
                    points.Clear();
                }
                void FinishFill()
                {
                    if (shadeAbove is null) return;
                    var edge = (double)(shadeAbove.Value ? axes.YMaximum : axes.YMinimum);
                    if (fillPoints.Count >= 2 && fillPoints.Any(point => point.Y != edge))
                    {
                        var boundary = new List<GraphPlotDisplayPoint> { new(fillPoints[0].X, edge) };
                        boundary.AddRange(fillPoints); boundary.Add(new(fillPoints[^1].X, edge));
                        totalPoints = checked(totalPoints + boundary.Count);
                        if (segments.Count + regions.Count >= policy.MaxSegments || totalPoints > policy.MaxTotalCoordinates)
                            throw new RationalAdmissionException("GraphPlotPreparedBudgetExceeded");
                        regions.Add(new(function.PrimitiveID, function.Expression, includeBoundary, boundary));
                    }
                    fillPoints.Clear();
                }
                // Each classified interval is an independent path, including when
                // the pole lies between regular sample positions. No NaN sentinel.
                for (var sample = 0; sample <= policy.SamplesPerInterval; sample++)
                {
                    Check();
                    var fraction = ERational.Create(EInteger.FromInt32(sample), EInteger.FromInt32(policy.SamplesPerInterval));
                    var x = Bound(left.Add(right.Subtract(left).Multiply(fraction)));
                    if (_roots.Any(root => root.CompareTo(x) == 0)) { FinishPath(); FinishFill(); continue; }
                    var y = Evaluate(original, x);
                    var displayX = x.ToDouble(); var displayY = y.ToDouble();
                    if (shadeAbove is not null)
                    {
                        var clamped = y.CompareTo(minimumY) < 0 ? minimumY : y.CompareTo(maximumY) > 0 ? maximumY : y;
                        var displayFillY = Display(clamped);
                        if (fillPoints.Count != 0 && displayX <= fillPoints[^1].X)
                            throw new RationalAdmissionException("GraphSampleRangeBelowDisplayPrecision");
                        fillPoints.Add(new(displayX, displayFillY));
                    }
                    if (!double.IsFinite(displayX) || !double.IsFinite(displayY) ||
                        Math.Abs(displayX) > policy.MaxDisplayMagnitude || Math.Abs(displayY) > policy.MaxDisplayMagnitude ||
                        y.CompareTo(minimumY) < 0 || y.CompareTo(maximumY) > 0)
                    { FinishPath(); continue; }
                    if (points.Count != 0 && displayX <= points[^1].X)
                        throw new RationalAdmissionException("GraphSampleRangeBelowDisplayPrecision");
                    points.Add(new(displayX, displayY));
                }
                FinishPath(); FinishFill();
            }
            return (segments, excluded, regions);
        }

        private void AdmitOriginal(Entity node, int depth)
        {
            Check();
            if (++_nodes > _limits.MaxNormalizedNodes || depth > _limits.MaxAstDepth * 4)
                throw new RationalAdmissionException("GraphOriginalTreeBudgetExceeded");
            switch (node)
            {
                case Rational number: Bound(number.ERational); return;
                case Entity.Variable variable when IsVariable(variable): return;
                case Entity.Sumf(var left, var right):
                    AdmitOriginal(left, depth + 1); AdmitOriginal(right, depth + 1); return;
                case Entity.Minusf(var left, var right):
                    AdmitOriginal(left, depth + 1); AdmitOriginal(right, depth + 1); return;
                case Entity.Mulf(var left, var right):
                    AdmitOriginal(left, depth + 1); AdmitOriginal(right, depth + 1); return;
                case Entity.Divf(var left, var right):
                    AdmitOriginal(left, depth + 1); AdmitOriginal(right, depth + 1);
                    ExcludeZeros(right); return;
                case Entity.Powf(var basis, Integer exponent):
                    var power = Power(exponent); AdmitOriginal(basis, depth + 1);
                    if (power <= 0) ExcludeZeros(basis); return;
                default: throw new RationalAdmissionException("GraphRationalEntityUnsupported");
            }
        }

        private void ExcludeZeros(Entity node)
        {
            Check();
            if (TryAffine(node, out var slope, out var intercept))
            {
                if (slope.Numerator.IsZero)
                {
                    if (intercept.Numerator.IsZero)
                        throw new RationalAdmissionException("MathOriginalDomainEmptyOrIndeterminate");
                    return;
                }
                var root = Bound(ERational.Zero.Subtract(intercept).Divide(slope));
                if (!_roots.Any(value => value.CompareTo(root) == 0)) _roots.Add(root);
                if (_roots.Count > policy.MaxOriginalExclusions)
                    throw new RationalAdmissionException("GraphOriginalDomainBudgetExceeded");
                return;
            }
            switch (node)
            {
                case Entity.Mulf(var left, var right): ExcludeZeros(left); ExcludeZeros(right); return;
                case Entity.Divf(var numerator, _): ExcludeZeros(numerator); return;
                case Entity.Powf(var basis, Integer exponent):
                    if (Power(exponent) > 0) ExcludeZeros(basis);
                    return;
                default: throw new RationalAdmissionException("GraphOriginalZeroSetUnclassified");
            }
        }

        private bool TryAffine(Entity node, out ERational slope, out ERational intercept)
        {
            Check(); slope = ERational.Zero; intercept = ERational.Zero;
            if (node is Rational number) { intercept = Bound(number.ERational); return true; }
            if (node is Entity.Variable variable && IsVariable(variable)) { slope = ERational.One; return true; }
            Entity left, right; char operation;
            switch (node)
            {
                case Entity.Sumf(var a, var b): left = a; right = b; operation = '+'; break;
                case Entity.Minusf(var a, var b): left = a; right = b; operation = '-'; break;
                case Entity.Mulf(var a, var b): left = a; right = b; operation = '*'; break;
                case Entity.Divf(var a, var b): left = a; right = b; operation = '/'; break;
                case Entity.Powf(var basis, Integer exponent) when Power(exponent) == 1:
                    return TryAffine(basis, out slope, out intercept);
                default: return false;
            }
            if (!TryAffine(left, out var aSlope, out var aIntercept) ||
                !TryAffine(right, out var bSlope, out var bIntercept)) return false;
            switch (operation)
            {
                case '+': slope = Bound(aSlope.Add(bSlope)); intercept = Bound(aIntercept.Add(bIntercept)); return true;
                case '-': slope = Bound(aSlope.Subtract(bSlope)); intercept = Bound(aIntercept.Subtract(bIntercept)); return true;
                case '*' when aSlope.Numerator.IsZero:
                    slope = Bound(aIntercept.Multiply(bSlope)); intercept = Bound(aIntercept.Multiply(bIntercept)); return true;
                case '*' when bSlope.Numerator.IsZero:
                    slope = Bound(bIntercept.Multiply(aSlope)); intercept = Bound(bIntercept.Multiply(aIntercept)); return true;
                case '/' when bSlope.Numerator.IsZero && !bIntercept.Numerator.IsZero:
                    slope = Bound(aSlope.Divide(bIntercept)); intercept = Bound(aIntercept.Divide(bIntercept)); return true;
                default: return false;
            }
        }

        private ERational Evaluate(Entity node, ERational x)
        {
            Check();
            ERational result;
            switch (node)
            {
                case Rational number: result = number.ERational; break;
                case Entity.Variable variable when IsVariable(variable): result = x; break;
                case Entity.Sumf(var a, var b): result = Evaluate(a, x).Add(Evaluate(b, x)); break;
                case Entity.Minusf(var a, var b): result = Evaluate(a, x).Subtract(Evaluate(b, x)); break;
                case Entity.Mulf(var a, var b): result = Evaluate(a, x).Multiply(Evaluate(b, x)); break;
                case Entity.Divf(var a, var b):
                    var dividend = Evaluate(a, x); var divisor = Evaluate(b, x);
                    if (divisor.Numerator.IsZero) throw new RationalAdmissionException("GraphUnclassifiedOriginalDomainHole");
                    result = dividend.Divide(divisor); break;
                case Entity.Powf(var basis, Integer exponent):
                    var power = Power(exponent); var value = Evaluate(basis, x);
                    if (power <= 0 && value.Numerator.IsZero)
                        throw new RationalAdmissionException("GraphUnclassifiedOriginalDomainHole");
                    result = ERational.One;
                    for (var i = 0; i < Math.Abs(power); i++) { Check(); result = Bound(result.Multiply(value)); }
                    if (power < 0) result = ERational.One.Divide(result);
                    break;
                default: throw new RationalAdmissionException("GraphRationalEntityUnsupported");
            }
            return Bound(result);
        }
        private static bool IsVariable(Entity.Variable variable) =>
            variable.GetType() == typeof(Entity.Variable) && variable.Name == CSharpMathRationalAstBridge.ParserVariableName;
        private int Power(Integer exponent)
        {
            Check();
            if (exponent.EInteger.Abs().CompareTo(EInteger.FromInt32(_limits.MaxAbsoluteIntegerPower)) > 0)
                throw new RationalAdmissionException("RationalIntegerPowerUnsupported");
            return exponent.EInteger.ToInt32Checked();
        }
        private ERational Bound(ERational value)
        {
            Check();
            if (BigInteger.Abs(BigInteger.Parse(value.Numerator.ToString(), CultureInfo.InvariantCulture)).GetBitLength() > _limits.MaxCoefficientBits ||
                BigInteger.Parse(value.Denominator.ToString(), CultureInfo.InvariantCulture).GetBitLength() > _limits.MaxCoefficientBits)
                throw new RationalAdmissionException("GraphCoefficientBudgetExceeded");
            return Rational.Create(value).ERational;
        }
        private void Check()
        {
            token.ThrowIfCancellationRequested();
            operationBudget.Check();
        }
        private double Display(ERational exact)
        {
            var value = exact.ToDouble();
            if (!double.IsFinite(value) || Math.Abs(value) > policy.MaxDisplayMagnitude)
                throw new RationalAdmissionException("GraphRangeBeyondDisplayPolicy");
            return value;
        }
        private static string ExactText(ERational value) => value.Numerator + "/" + value.Denominator;
        private static ERational Decimal(decimal value)
        {
            var bits = decimal.GetBits(value);
            var numerator = (new BigInteger((uint)bits[2]) << 64) +
                (new BigInteger((uint)bits[1]) << 32) + (uint)bits[0];
            if ((bits[3] & int.MinValue) != 0) numerator = -numerator;
            var scale = (bits[3] >> 16) & 0x7f;
            return ERational.Create(EInteger.FromString(numerator.ToString(CultureInfo.InvariantCulture)),
                EInteger.FromString(BigInteger.Pow(10, scale).ToString(CultureInfo.InvariantCulture)));
        }
    }
}
