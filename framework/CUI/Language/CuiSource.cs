namespace CakeOS.Cui.Language;

public readonly record struct CuiSourcePosition(int Line, int Column)
{
    public static CuiSourcePosition Start => new(1, 1);

    public override string ToString() => $"{Line}:{Column}";
}

public readonly record struct CuiSourceSpan(
    string SourceName,
    CuiSourcePosition Start,
    CuiSourcePosition End)
{
    public static CuiSourceSpan At(string sourceName, int line, int column, int length = 1) =>
        new(sourceName, new(line, column), new(line, column + Math.Max(1, length)));

    public override string ToString() => $"{SourceName}:{Start.Line}:{Start.Column}";
}
