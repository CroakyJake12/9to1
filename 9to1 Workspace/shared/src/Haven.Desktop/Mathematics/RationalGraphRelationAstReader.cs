using CSharpMath.Atom;
using Haven.Core.Mathematics;
using Atoms = CSharpMath.Atom.Atoms;

namespace Haven.Desktop.Mathematics;

/// <summary>A restricted explicit-y relation read from the maintained MathList,
/// not a string tokenizer or a general implicit-equation solver.</summary>
internal static class RationalGraphRelationAstReader
{
    internal sealed record Reading(string RightHandLaTeX, bool IncludeBoundary, bool? ShadeAbove);
    public static Reading Read(MathExpression source, bool inequality,
        bool declaredBoundary, MathRationalEvaluationLimits limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.LaTeX.Length > limits.MaxSourceCharacters)
            throw new RationalAdmissionException("MathSourceBudgetExceeded");
        var syntax = new CSharpMathSyntaxAdapter().Parse(source.LaTeX,
            new MathServiceLimits(MaxSourceCharacters: limits.MaxSourceCharacters, MaxSyntaxDepth: limits.MaxAstDepth));
        if (!syntax.Succeeded) throw new RationalAdmissionException("MathParseError");
        var list = LaTeXParser.MathListFromLaTeX(source.LaTeX).Match(value => value.Clone(true),
            _ => throw new RationalAdmissionException("MathParseError"));
        var atoms = list.Where(atom => atom is not Atoms.Space).ToArray();
        if (atoms.Length < 3 || atoms.Count(atom => atom is Atoms.Relation) != 1 ||
            atoms[0] is not Atoms.Variable y || y.Nucleus != "y" || y.FontStyle == FontStyle.Roman ||
            y.Superscript.Count != 0 || y.Subscript.Count != 0 || atoms[1] is not Atoms.Relation relation ||
            relation.Superscript.Count != 0 || relation.Subscript.Count != 0)
            throw new RationalAdmissionException("GraphExplicitYRelationUnsupported");
        bool include; bool? above;
        if (!inequality)
        {
            if (relation.Nucleus != "=") throw new RationalAdmissionException("GraphEquationRelationUnsupported");
            include = true; above = null;
        }
        else
        {
            (include, above) = relation.Nucleus switch
            {
                "<" => (false, false), "≤" => (true, false),
                ">" => (false, true), "≥" => (true, true),
                _ => throw new RationalAdmissionException("GraphInequalityRelationUnsupported")
            };
            if (include != declaredBoundary)
                throw new RationalAdmissionException("GraphInequalityBoundaryMismatch");
        }
        var right = new MathList(); foreach (var atom in atoms.Skip(2)) right.Add(atom);
        token.ThrowIfCancellationRequested();
        // Maintained serialization preserves supported RHS atom structure; the
        // exact rational bridge then owns admission and the maintained parser.
        return new(LaTeXParser.MathListToLaTeX(right).ToString(), include, above);
    }
}
