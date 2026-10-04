using System.Text.Json.Serialization;

namespace Haven.Core.Mathematics;

public sealed record MathAnswer(Guid AnswerID, long Revision, MathAnswerValue Value, int SchemaVersion = 1);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NumericMathAnswer), "numeric")]
[JsonDerivedType(typeof(ExpressionMathAnswer), "expression")]
[JsonDerivedType(typeof(EquationMathAnswer), "equation")]
[JsonDerivedType(typeof(InequalityMathAnswer), "inequality")]
[JsonDerivedType(typeof(IntervalMathAnswer), "interval")]
[JsonDerivedType(typeof(CoordinateMathAnswer), "coordinate")]
[JsonDerivedType(typeof(VectorMathAnswer), "vector")]
[JsonDerivedType(typeof(MatrixMathAnswer), "matrix")]
[JsonDerivedType(typeof(SetMathAnswer), "set")]
[JsonDerivedType(typeof(MultipleExpressionsMathAnswer), "multipleExpressions")]
[JsonDerivedType(typeof(FreeWorkingMathAnswer), "freeWorking")]
public abstract record MathAnswerValue;

/// <summary>Keep the actual invariant decimal literal: 1.20 and 1.2 have equal value
/// but distinct submitted precision. Scientific notation is retained, not guessed.</summary>
public sealed record NumericMathAnswer(string Literal, string? Units = null) : MathAnswerValue;
public sealed record ExpressionMathAnswer(MathExpression Expression) : MathAnswerValue;
public sealed record EquationMathAnswer(MathExpression Left, MathExpression Right) : MathAnswerValue;
public enum MathRelation { LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual }
public sealed record InequalityMathAnswer(MathExpression Left, MathRelation Relation, MathExpression Right) : MathAnswerValue;
/// <summary>A null lower/upper endpoint explicitly denotes negative/positive infinity.</summary>
public sealed record IntervalMathAnswer(decimal? Lower, bool IncludeLower, decimal? Upper, bool IncludeUpper) : MathAnswerValue;
public sealed record CoordinateMathAnswer(decimal[] Coordinates) : MathAnswerValue;
public sealed record VectorMathAnswer(MathExpression[] Components) : MathAnswerValue;
public sealed record MatrixMathAnswer(MathExpression[][] Rows) : MathAnswerValue;
public sealed record SetMathAnswer(MathExpression[] Elements) : MathAnswerValue;
public sealed record MultipleExpressionsMathAnswer(MathExpression[] Expressions) : MathAnswerValue;
public sealed record FreeWorkingMathAnswer(string Text, MathExpression[] Expressions) : MathAnswerValue;
