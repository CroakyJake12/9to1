namespace Haven.Core.Mathematics;

/// <summary>The shared, revisioned expression. Draft text belongs to an editor session;
/// a successfully parsed canonical expression is the only value committed here.</summary>
public sealed record MathExpression(Guid ExpressionID, long Revision, string LaTeX,
    string AccessibleDescription = "", int SchemaVersion = 1);

public sealed record MathExpressionReference(Guid ExpressionID, long Revision);

/// <summary>Explicit local service limits. Parser, graph and storage consumers use the same
/// configured instance; these bounds do not imply provider pricing or hosted quotas.</summary>
/// <param name="MaxSyntaxDepth">Maximum explicit LaTeX brace-group nesting. Command
/// recursion is separately bounded by MaxSyntaxCommands; this is not a parser stack counter.</param>
public sealed record MathServiceLimits(int MaxSourceCharacters = 8192, int MaxSyntaxDepth = 64,
    int MaxSyntaxCommands = 256, int MaxAnswerItems = 256, int MaxMatrixCells = 1024,
    int MaxGraphPrimitives = 256, int MaxGraphPoints = 4096,
    int MaxSerializedBytes = 4 * 1024 * 1024, int MaxObjectDepth = 32, int MaxGraphTickCount = 256)
{
    public void Validate()
    {
        if (MaxSourceCharacters < 1 || MaxSyntaxDepth < 1 || MaxSyntaxCommands < 1 ||
            MaxAnswerItems < 1 || MaxMatrixCells < 1 || MaxGraphPrimitives < 1 ||
            MaxGraphPoints < 2 || MaxSerializedBytes < 1 || MaxObjectDepth is < 1 or > 128 || MaxGraphTickCount < 4)
            throw new ArgumentOutOfRangeException(nameof(MathServiceLimits));
    }
}
