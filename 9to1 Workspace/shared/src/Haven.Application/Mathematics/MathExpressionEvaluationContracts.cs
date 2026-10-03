using Haven.Core.Mathematics;

namespace Haven.Application.Mathematics;

/// <summary>Evaluate the held canonical versions without rewriting either ID or
/// revision. Equivalent requires independently retained original-domain evidence
/// and a supported exact rational proof; finite numerical sampling is insufficient.
/// Cancellation propagates from the actual operation. In-process cancellation or
/// Task.WaitAsync alone is not a hard wall/CPU/memory bound.</summary>
public interface IMathExpressionEvaluator
{
    ValueTask<MathExpressionEquivalenceResult> CompareAsync(MathExpression expected,
        MathExpression candidate, MathExpressionEvaluationPolicy policy,
        CancellationToken cancellationToken = default);
}
