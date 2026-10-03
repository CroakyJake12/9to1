using System.Text;
using System.Text.Json;
using Haven.Core.Mathematics;

namespace Haven.Core.Forms;

/// <summary>Forms owns question policy and released scores, while the shared canonical
/// codec/response projection/marking engine owns mathematical objects. No client score,
/// display graph or guessed symbolic equivalence is accepted as authoritative.</summary>
public static class FormMathematics
{
    public static string? Validate(FormField field, JsonElement value)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(value.GetRawText());
            if (field.Kind == FormFieldKind.Mathematical)
            {
                var answer = MathObjectCodec.Decode<MathAnswer>(bytes);
                if (answer.Value is not NumericMathAnswer numeric) return "CapabilityUnavailable";
                _ = MathNumericLiteral.Read(numeric.Literal);
                return null;
            }
            if (field.Kind == FormFieldKind.Graph)
            {
                var answer = MathObjectCodec.Decode<GraphResponse>(bytes);
                _ = GraphResponseProjection.Apply(field.Graph!, answer);
                return null;
            }
            throw new ArgumentException("Not a mathematical field.", nameof(field));
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or
            InvalidOperationException or KeyNotFoundException or OverflowException)
        { return field.Kind == FormFieldKind.Graph ? "GraphResponseInvalid" : "MathParseError"; }
    }
    public static string? ValidateReplacement(FormField field, JsonElement previous, JsonElement next)
    {
        if (field.Kind is not (FormFieldKind.Mathematical or FormFieldKind.Graph)
            || previous.ValueKind == JsonValueKind.Null || next.ValueKind == JsonValueKind.Null) return null;
        Guid beforeID, afterID; long beforeRevision, afterRevision; bool unchanged;
        if (field.Kind == FormFieldKind.Mathematical)
        {
            var before = MathObjectCodec.Decode<MathAnswer>(Encoding.UTF8.GetBytes(previous.GetRawText()));
            var after = MathObjectCodec.Decode<MathAnswer>(Encoding.UTF8.GetBytes(next.GetRawText()));
            beforeID = before.AnswerID; afterID = after.AnswerID; beforeRevision = before.Revision; afterRevision = after.Revision;
            unchanged = MathObjectCodec.Encode(before).SequenceEqual(MathObjectCodec.Encode(after));
        }
        else
        {
            var before = MathObjectCodec.Decode<GraphResponse>(Encoding.UTF8.GetBytes(previous.GetRawText()));
            var after = MathObjectCodec.Decode<GraphResponse>(Encoding.UTF8.GetBytes(next.GetRawText()));
            beforeID = before.ResponseID; afterID = after.ResponseID; beforeRevision = before.Revision; afterRevision = after.Revision;
            unchanged = MathObjectCodec.Encode(before).SequenceEqual(MathObjectCodec.Encode(after));
        }
        return beforeID != afterID ? "MathAnswerIdentityConflict"
            : afterRevision < beforeRevision || afterRevision == beforeRevision && !unchanged ? "RevisionConflict" : null;
    }

    public static FormMarkingResult Evaluate(FormField field, JsonElement value, FormFieldAssessment assessment)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(FormMarkingOutcome.Incorrect, 0, assessment.MaximumPoints, []);
        MathMarkingResult result;
        var bytes = Encoding.UTF8.GetBytes(value.GetRawText());
        if (assessment.Mathematics is { } numeric)
            result = MathMarking.Evaluate(MathObjectCodec.Decode<MathAnswer>(bytes), numeric);
        else if (assessment.Graph is { } graph)
            result = GraphMarking.Evaluate(field.Graph!, assessment.ExpectedGraph!, MathObjectCodec.Decode<GraphResponse>(bytes), graph);
        else return FormMarking.Evaluate(value, assessment.Rules, assessment.MaximumPoints);
        var outcome = result.Outcome switch
        {
            MathMarkingOutcome.Correct => FormMarkingOutcome.Correct,
            MathMarkingOutcome.Incorrect => FormMarkingOutcome.Incorrect,
            _ => FormMarkingOutcome.NeedsReview
        };
        return new(outcome, outcome == FormMarkingOutcome.Correct ? assessment.MaximumPoints : 0,
            assessment.MaximumPoints, outcome == FormMarkingOutcome.Correct ? [result.RuleID] : [],
            result.Diagnostic ?? result.Provenance);
    }
}
