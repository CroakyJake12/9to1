using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application.Mathematics;
using Haven.Core.Forms;
using Haven.Core.Mathematics;

namespace Haven.Core.Tests;

public sealed class FormCanonicalMathematicsTests
{
    [Fact]
    public void Typed_numeric_answer_keeps_literal_identity_and_marking_after_checkpoint_restore()
    {
        var now = DateTimeOffset.UtcNow;
        var field = new FormField(Guid.NewGuid(), FormFieldKind.Mathematical, "Length", null,
            JsonSerializer.SerializeToElement(new { }), true, new(), Mathematics:new(Guid.NewGuid(), 1, "1+0.2"),
            Assessment:new(2, 1, [], Mathematics:new(Guid.NewGuid(), 1.2m, MathNumericComparison.Exact,
                SignificantFigures:3, Units:"m")));
        var project = Add(field, now);
        var optional = new FormResponseRuntime(Add(field with { Required = false }, now), Guid.NewGuid());
        Assert.True(optional.Answer(optional.Read().Revision, field.FieldID, JsonSerializer.SerializeToElement<object?>(null)).Success);
        Assert.True(optional.Submit(optional.Read().Revision).Success);
        Assert.Equal(0m, optional.Read().AwardedPoints);
        var runtime = new FormResponseRuntime(project, Guid.NewGuid());
        var value = new MathAnswer(Guid.NewGuid(), 1, new NumericMathAnswer("1.20", "m"));
        Assert.True(runtime.Answer(runtime.Read().Revision, field.FieldID, Element(value)).Success);
        var checkpoint = runtime.CaptureCheckpoint();
        var restored = FormResponseRuntime.Restore(FormProjectCodec.Decode(FormProjectCodec.Encode(project)), checkpoint);
        var read = restored.Read();
        var originalBytes = MathObjectCodec.Encode(value);
        Assert.Equal(originalBytes, MathObjectCodec.Encode(Read<MathAnswer>(Assert.Single(read.Answers).Value)));
        var forged = restored.Answer(read.Revision, field.FieldID, Element(value with { AnswerID = Guid.NewGuid(), Revision = 2 }));
        Assert.False(forged.Success); Assert.Equal("MathAnswerIdentityConflict", forged.Code);
        var stale = restored.Answer(read.Revision, field.FieldID, Element(value with { Value = new NumericMathAnswer("2.00", "m") }));
        Assert.False(stale.Success); Assert.Equal("RevisionConflict", stale.Code);
        var invalid = JsonSerializer.SerializeToElement(value with { Revision = 2,
            Value = new NumericMathAnswer("0.12345678901234567890123456789", "m") });
        var precision = restored.Answer(read.Revision, field.FieldID, invalid);
        Assert.False(precision.Success); Assert.Equal("MathParseError", precision.Code);
        Assert.Equal(read.Revision, restored.Read().Revision);
        Assert.Equal(originalBytes, MathObjectCodec.Encode(Read<MathAnswer>(Assert.Single(restored.Read().Answers).Value)));
        var submitted = restored.Submit(read.Revision);
        Assert.True(submitted.Success);
        Assert.Equal(FormMarkingOutcome.Correct, submitted.Response.ReleasedResults[field.FieldID].Outcome);
        Assert.Equal(2m, submitted.Response.AwardedPoints);
    }

    [Fact]
    public void Repeated_graph_answer_edits_bind_original_question_and_reopen_without_fabricated_targets()
    {
        var question = new GraphDefinition(Guid.NewGuid(), 7, new(-10, 10, -10, 10), [], [], [GraphResponseTool.PlacePoint]);
        var originalBytes = MathObjectCodec.Encode(question);
        var editor = new GraphResponseEditorSession(question);
        var first = editor.PlacePoint(editor.Snapshot().Revision, new(1, 1));
        var firstProjection = editor.Projection();
        var second = editor.PlacePoint(first.Revision, new(2, 3));
        var secondProjection = editor.Projection();
        Assert.Equal(first.ResponseID, second.ResponseID);
        Assert.Equal(first.Actions[0].Primitive.PrimitiveID, second.Actions[0].Primitive.PrimitiveID);
        Assert.Equal(question.GraphID, second.GraphID); Assert.Equal(question.Revision, second.GraphRevision);
        Assert.Equal(first.Revision + 1, second.Revision);
        Assert.NotEqual(question.GraphID, firstProjection.GraphID);
        Assert.Equal(firstProjection.GraphID, secondProjection.GraphID);
        Assert.Equal(first.Revision, firstProjection.Revision); Assert.Equal(second.Revision, secondProjection.Revision);
        Assert.NotEqual(MathObjectCodec.Encode(firstProjection), MathObjectCodec.Encode(secondProjection));
        Assert.Equal(originalBytes, MathObjectCodec.Encode(question));
        Assert.Throws<InvalidOperationException>(() => editor.PlacePoint(first.Revision, new(9, 9)));
        Assert.Throws<InvalidDataException>(() => editor.PlacePoint(second.Revision, new(20, 3)));
        Assert.Equal(MathObjectCodec.Encode(second), MathObjectCodec.Encode(editor.Snapshot()));
        var reopened = new GraphResponseEditorSession(MathObjectCodec.Capture(question), MathObjectCodec.Capture(second));
        Assert.Equal(MathObjectCodec.Encode(second), MathObjectCodec.Encode(reopened.Snapshot()));
        Assert.Equal(MathObjectCodec.Encode(secondProjection), MathObjectCodec.Encode(reopened.Projection()));
        Assert.Throws<InvalidOperationException>(() => GraphResponseProjection.Apply(question with { Revision = question.Revision + 1 }, second));
        Assert.Throws<InvalidOperationException>(() => GraphResponseProjection.Apply(question with { GraphID = Guid.NewGuid() }, second));
        var nextQuestion = question with { Revision = question.Revision + 1 };
        var nextVersionProjection = GraphResponseProjection.Apply(nextQuestion, second with { GraphRevision = nextQuestion.Revision });
        Assert.NotEqual(secondProjection.GraphID, nextVersionProjection.GraphID);
        Assert.Equal(secondProjection.Revision, nextVersionProjection.Revision);
        var maximumQuestion = question with { Revision = long.MaxValue };
        var maximumQuestionResponse = second with { GraphRevision = maximumQuestion.Revision };
        Assert.Equal(second.Revision, GraphResponseProjection.Apply(maximumQuestion, maximumQuestionResponse).Revision);
        var maximumQuestionEditor = new GraphEditorSession(maximumQuestion);
        Assert.Throws<OverflowException>(() => maximumQuestionEditor.Apply(maximumQuestionResponse));
        Assert.Equal(MathObjectCodec.Encode(maximumQuestion), MathObjectCodec.Encode(maximumQuestionEditor.Snapshot()));
        var target = new GraphPoint(Guid.NewGuid(), new(2, 3));
        var expected = question with { GraphID = Guid.NewGuid(), Revision = 1, Primitives = [target] };
        Assert.NotEqual(question.GraphID, expected.GraphID);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.Graph, "Place the point", null,
            JsonSerializer.SerializeToElement(new { }), true, new(), Graph:question,
            Assessment:new(3, 1, [], Graph:new(Guid.NewGuid(), target.PrimitiveID), ExpectedGraph:expected));
        var runtime = new FormResponseRuntime(Add(field, DateTimeOffset.UtcNow), Guid.NewGuid());
        Assert.True(runtime.Answer(runtime.Read().Revision, field.FieldID, Element(second)).Success);
        var checkpoint = runtime.CaptureCheckpoint();
        var bad = runtime.Answer(runtime.Read().Revision, field.FieldID, Element(second with { Revision = second.Revision + 1,
            GraphRevision = question.Revision + 1 }));
        Assert.False(bad.Success); Assert.Equal("GraphResponseInvalid", bad.Code);
        Assert.Equal(checkpoint.Revision, runtime.Read().Revision);
        var submitted = runtime.Submit(runtime.Read().Revision);
        Assert.True(submitted.Success); Assert.Equal(3m, submitted.Response.AwardedPoints);
        Assert.Equal(FormMarkingOutcome.Correct, submitted.Response.ReleasedResults[field.FieldID].Outcome);
        Assert.Throws<InvalidOperationException>(() => new GraphResponseEditorSession(question, second with { GraphID = Guid.NewGuid() }));
        var fabricated = Guid.NewGuid();
        Assert.Throws<KeyNotFoundException>(() => new GraphResponseEditorSession(
            question with { ResponseTools = [GraphResponseTool.PlacePoint, GraphResponseTool.ManipulatePrimitive] },
            second with { Actions = [new(GraphResponseTool.ManipulatePrimitive, new GraphPoint(fabricated, new(2, 3)), fabricated)] }));
    }

    [Fact]
    public void Existing_plain_form_bytes_decode_without_new_math_properties_and_keep_original_response_shape()
    {
        var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Name", null,
            JsonSerializer.SerializeToElement(new { }), false, new());
        var project = Add(field, DateTimeOffset.UtcNow);
        var json = JsonNode.Parse(FormProjectCodec.Encode(project))!;
        foreach (var item in json["Fields"]!.AsArray())
        { item!.AsObject().Remove("Mathematics"); item.AsObject().Remove("Graph"); }
        var decoded = FormProjectCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Equal(project.FormID, decoded.FormID); Assert.Equal(project.Revision, decoded.Revision);
        Assert.Null(decoded.Fields[0].Mathematics); Assert.Null(decoded.Fields[0].Graph);
        var runtime = new FormResponseRuntime(decoded, Guid.NewGuid());
        var answer = runtime.Answer(runtime.Read().Revision, field.FieldID, JsonSerializer.SerializeToElement("Legacy answer"));
        Assert.True(answer.Success); Assert.Equal("Legacy answer", Assert.Single(answer.Response.Answers).Value.GetString());
    }

    [Fact]
    public void Private_graph_grading_admits_only_the_exact_question_and_explicit_manipulation_target()
    {
        var first = new GraphPoint(Guid.NewGuid(), new(0, 0));
        var other = new GraphPoint(Guid.NewGuid(), new(1, 1));
        var question = new GraphDefinition(Guid.NewGuid(), 7, new(-10, 10, -10, 10), [], [first, other],
            [GraphResponseTool.PlacePoint, GraphResponseTool.ManipulatePrimitive]);
        var expectedPoint = new GraphPoint(Guid.NewGuid(), new(2, 3));
        var expected = new GraphDefinition(Guid.NewGuid(), 3, question.Axes, [], [expectedPoint], question.ResponseTools);
        var rule = new GraphCoordinateMarkingRule(Guid.NewGuid(), expectedPoint.PrimitiveID, QuestionPrimitiveID:first.PrimitiveID);
        var response = new GraphResponse(Guid.NewGuid(), 1, question.GraphID, question.Revision, [],
            [new(GraphResponseTool.ManipulatePrimitive, first with { Position = new(2, 3) }, first.PrimitiveID)]);
        var questionBytes = MathObjectCodec.Encode(question); var expectedBytes = MathObjectCodec.Encode(expected);
        var responseBytes = MathObjectCodec.Encode(response);
        Assert.Equal(MathMarkingOutcome.Correct, GraphMarking.Evaluate(question, expected, response, rule).Outcome);
        Assert.Throws<InvalidOperationException>(() => GraphMarking.Evaluate(question, expected, response with { GraphID = Guid.NewGuid() }, rule));
        Assert.Throws<InvalidOperationException>(() => GraphMarking.Evaluate(question, expected, response with { GraphRevision = question.Revision + 1 }, rule));
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(question, expected, response, rule with { QuestionPrimitiveID = null }).Outcome);
        var differentExistingTarget = response with { Actions = [new(GraphResponseTool.ManipulatePrimitive,
            other with { Position = new(2, 3) }, other.PrimitiveID)] };
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(question, expected, differentExistingTarget, rule).Outcome);
        var fabricatedID = Guid.NewGuid();
        var fabricated = response with { Actions = [new(GraphResponseTool.ManipulatePrimitive,
            new GraphPoint(fabricatedID, new(2, 3)), fabricatedID)] };
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(question, expected, fabricated, rule).Outcome);
        var contradictoryPlacement = response with { Actions = [new(GraphResponseTool.PlacePoint, new GraphPoint(Guid.NewGuid(), new(2, 3)))] };
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(question, expected, contradictoryPlacement, rule).Outcome);
        Assert.Throws<KeyNotFoundException>(() => GraphMarking.ValidatePolicy(question, expected, rule with { QuestionPrimitiveID = Guid.NewGuid() }));
        Assert.Throws<KeyNotFoundException>(() => GraphMarking.ValidatePolicy(question, expected, rule with { ExpectedPrimitiveID = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => GraphMarking.ValidatePolicy(question, expected with { GraphID = question.GraphID }, rule));
        Assert.Throws<InvalidDataException>(() => GraphMarking.ValidatePolicy(question,
            expected with { Primitives = [new GraphLine(expectedPoint.PrimitiveID, new(0, 0), new(2, 3))] }, rule));
        Assert.Throws<InvalidDataException>(() => GraphMarking.ValidatePolicy(question with { ResponseTools = [GraphResponseTool.PlacePoint] },
            expected with { Primitives = [new GraphLine(expectedPoint.PrimitiveID, new(0, 0), new(2, 3))] },
            rule with { QuestionPrimitiveID = null }));
        var field = new FormField(Guid.NewGuid(), FormFieldKind.Graph, "Move the first point", null,
            JsonSerializer.SerializeToElement(new { }), true, new(), Graph:question,
            Assessment:new(4, 1, [], Graph:rule, ExpectedGraph:expected));
        var project = FormProjectCodec.Capture(Add(field, DateTimeOffset.UtcNow));
        Assert.Equal(expected.GraphID, project.Fields[0].Assessment!.ExpectedGraph!.GraphID);
        Assert.Equal(expected.Revision, project.Fields[0].Assessment.ExpectedGraph.Revision);
        var runtime = new FormResponseRuntime(project, Guid.NewGuid());
        Assert.True(runtime.Answer(runtime.Read().Revision, field.FieldID, Element(response)).Success);
        Assert.True(runtime.Submit(runtime.Read().Revision).Success); Assert.Equal(4m, runtime.Read().AwardedPoints);
        Assert.Equal(questionBytes, MathObjectCodec.Encode(question));
        Assert.Equal(expectedBytes, MathObjectCodec.Encode(expected));
        Assert.Equal(responseBytes, MathObjectCodec.Encode(response));
    }

    [Fact]
    public void Private_expected_definition_configuration_changes_advance_only_its_own_revision_atomically()
    {
        var original = new GraphDefinition(Guid.NewGuid(), 3, new(-10, 10, -10, 10), [],
            [new GraphPoint(Guid.NewGuid(), new(2, 3))], [GraphResponseTool.PlacePoint]);
        var editor = new GraphEditorSession(original);
        Assert.Equal(MathObjectCodec.Encode(original), MathObjectCodec.Encode(editor.ReplaceDefinition(original.Revision, original)));
        var changed = editor.ReplaceDefinition(original.Revision, original with { Axes = original.Axes with { XMaximum = 20 } });
        Assert.Equal(original.GraphID, changed.GraphID); Assert.Equal(original.Revision + 1, changed.Revision);
        Assert.Equal(20m, changed.Axes.XMaximum);
        var bytes = MathObjectCodec.Encode(changed);
        Assert.Throws<InvalidOperationException>(() => editor.ReplaceDefinition(original.Revision, original));
        Assert.Throws<InvalidOperationException>(() => editor.ReplaceDefinition(changed.Revision, changed with { GraphID = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => editor.ReplaceDefinition(changed.Revision, changed with { Axes = changed.Axes with { XMaximum = -20 } }));
        Assert.Equal(bytes, MathObjectCodec.Encode(editor.Snapshot()));
        Assert.Equal(10m, original.Axes.XMaximum); Assert.Equal(3, original.Revision);
    }

    private static FormProject Add(FormField field, DateTimeOffset now)
    {
        var project = FormProjectEditor.Create("Canonical mathematical questions", FormModeKind.Form, now);
        return FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
    }
    private static JsonElement Element<T>(T value) where T : class
    { using var document = JsonDocument.Parse(MathObjectCodec.Encode(value)); return document.RootElement.Clone(); }
    private static T Read<T>(JsonElement value) where T : class =>
        MathObjectCodec.Decode<T>(System.Text.Encoding.UTF8.GetBytes(value.GetRawText()));
}
