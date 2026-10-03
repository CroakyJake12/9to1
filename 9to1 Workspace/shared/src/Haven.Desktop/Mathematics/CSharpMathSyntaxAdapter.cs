using CSharpMath.Atom;
using Haven.Application.Mathematics;
using Haven.Core.Mathematics;

namespace Haven.Desktop.Mathematics;

/// <summary>Reuse the maintained CSharpMath parser already shipped with Home NativeUI.
/// No command/macro registry mutation, file acquisition or symbolic-evaluation claim.</summary>
public sealed class CSharpMathSyntaxAdapter : IMathSyntaxAdapter
{
    public const string Implementation = "CSharpMath/1.0.0-pre.1/d6feac52d2cf4b186f1f275ca4c84fd7564cc470";
    public MathSyntaxResult Parse(string source, MathServiceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(limits); limits.Validate();
        if (string.IsNullOrWhiteSpace(source) || source.Length > limits.MaxSourceCharacters)
            return new(false, null, "MathSourceBudgetExceededOrEmpty", Implementation);
        var depth = 0; var commands = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var character = source[i];
            if (character == '\\')
            {
                if (++commands > limits.MaxSyntaxCommands) return new(false, null, "MathCommandBudgetExceeded", Implementation);
                if (i + 1 < source.Length && !char.IsLetter(source[i + 1])) i++;
            }
            else if (character == '{' && ++depth > limits.MaxSyntaxDepth)
                return new(false, null, "MathSyntaxDepthExceeded", Implementation);
            else if (character == '}' && --depth < 0) return new(false, null, "UnmatchedMathGroup", Implementation);
        }
        if (depth != 0) return new(false, null, "UnmatchedMathGroup", Implementation);
        return LaTeXParser.MathListFromLaTeX(source).Match(
            math => new MathSyntaxResult(true, LaTeXParser.MathListToLaTeX(math).ToString(), null, Implementation),
            error => new MathSyntaxResult(false, null, error[..Math.Min(error.Length, 1024)], Implementation));
    }
}
