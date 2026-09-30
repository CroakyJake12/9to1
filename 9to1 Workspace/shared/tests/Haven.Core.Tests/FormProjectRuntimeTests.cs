using System.Text.Json;
using Haven.Core.Forms;

namespace Haven.Core.Tests;

public sealed class FormProjectRuntimeTests
{
    [Theory]
    [InlineData(FormFieldKind.SingleChoice, true)]
    [InlineData(FormFieldKind.SingleChoice, false)]
    [InlineData(FormFieldKind.Dropdown, true)]
    [InlineData(FormFieldKind.Dropdown, false)]
    public void Scalar_choice_assessment_uses_option_identity_and_retains_typed_answer(FormFieldKind kind, bool correct)
    {
        var accepted = new FormChoiceOption(Guid.NewGuid(), "Same label");
        var rejected = new FormChoiceOption(Guid.NewGuid(), "Same label");
        var field = Text("Select an option") with
        {
            Kind = kind, Options = [accepted, rejected], Assessment = new(2, 1,
                [new(Guid.NewGuid(), FormMarkingRuleKind.ChoiceSet, 2, ChoiceIDs: [accepted.OptionID.ToString("N")])])
        };
        var project = WithField(FormModeKind.Test, field);
        var runtime = new FormResponseRuntime(project, Guid.NewGuid());
        var selected = correct ? accepted.OptionID : rejected.OptionID;
        var answer = runtime.Answer(1, field.FieldID, Json(selected));
        Assert.True(answer.Success);
        Assert.Empty(answer.Response.ReleasedResults);
        var restored = FormResponseRuntime.Restore(project, runtime.CaptureCheckpoint());
        var submitted = restored.Submit(answer.Response.Revision);
        Assert.True(submitted.Success);
        Assert.Equal(selected, Assert.Single(submitted.Response.Answers).Value.GetGuid());
        Assert.Equal(correct ? 2 : 0, submitted.Response.AwardedPoints);
        Assert.Equal(correct ? FormMarkingOutcome.Correct : FormMarkingOutcome.Incorrect,
            Assert.Single(submitted.Response.ReleasedResults).Value.Outcome);
    }

    [Fact]
    public void Checkpoint_roundtrip_retains_question_lock_hidden_marks_version_and_original_timer()
    {
        var field = Text("Processor", Assessment(FormResultRelease.AfterQuestion));
        var project = WithField(FormModeKind.Quiz, field) with { RuntimeSettings = new(TimeLimit: TimeSpan.FromMinutes(2)) };
        var clock = new CheckpointClock { Now = Now };
        var runtime = new FormResponseRuntime(project, Guid.NewGuid(), clock);
        var answer = runtime.Answer(1, field.FieldID, Json("CPU"));
        var checkpoint = JsonSerializer.Deserialize<FormResponseCheckpoint>(JsonSerializer.Serialize(runtime.CaptureCheckpoint()))!;
        var restored = FormResponseRuntime.Restore(project, checkpoint, clock);
        Assert.Equal(answer.Response.ResponseID, restored.Read().ResponseID);
        Assert.Equal(answer.Response.FormVersionID, restored.Read().FormVersionID);
        Assert.Empty(restored.Read().ReleasedResults);
        var advanced = restored.Advance(checkpoint.Revision);
        Assert.Single(advanced.Response.ReleasedResults);
        var locked = FormResponseRuntime.Restore(project, restored.CaptureCheckpoint(), clock);
        Assert.Equal("IntegrityPolicyViolation", locked.Answer(advanced.Response.Revision, field.FieldID, Json("wrong")).Code);
        clock.Now = Now.AddMinutes(3);
        Assert.Equal("IntegrityPolicyViolation", locked.Submit(advanced.Response.Revision).Code);
        Assert.Throws<InvalidDataException>(() => FormResponseRuntime.Restore(project with { Revision = project.Revision + 1 }, checkpoint, clock));
        Assert.Throws<InvalidDataException>(() => FormResponseRuntime.Restore(project, checkpoint with { Answers = [] , QuestionIndex = 1 }, clock));
    }

    [Fact]
    public void Preset_cannot_silently_ignore_a_configured_state_graph()
    {
        var project = WithField(FormModeKind.Test, Text("Question"));
        project = project with { ModeDefinition = project.ModeDefinition with { StateGraphID = Guid.NewGuid(), StartNodeID = Guid.NewGuid() } };
        Assert.Throws<NotSupportedException>(() => new FormResponseRuntime(project, Guid.NewGuid()));
    }

    private sealed class CheckpointClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
    private static FormField Text(string label, FormFieldAssessment? assessment = null) =>
        new(Guid.NewGuid(), FormFieldKind.ShortText, label, null, Json(new { }), true, new(), Assessment: assessment);
    private static FormProject WithField(FormModeKind mode, FormField field)
    {
        var project = FormProjectEditor.Create("Assessment", mode, Now);
        return FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, Now);
    }
    private static FormFieldAssessment Assessment(FormResultRelease release) => new(2, 3,
        [new(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 2, AcceptedTexts: ["CPU", "Central Processing Unit"],
            Normalisation: new(IgnoreCase: true, Trim: true))], release);

    [Fact]
    public void Authoring_move_preserves_identity_and_binding_and_roundtrips_without_aliases()
    {
        var choices = new List<FormChoiceOption> { new(Guid.NewGuid(), "Original") };
        var field = Text("Question") with { Kind = FormFieldKind.SingleChoice, Options = choices };
        var project = WithField(FormModeKind.Form, field);
        var binding = new FormDataBinding(Guid.NewGuid(), field.FieldID, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FormDataBindingKind.AppendRecord);
        project = FormProjectEditor.BindData(project, project.Revision, binding, Now);
        var second = new FormPage(Guid.NewGuid(), "Second", [], new());
        project = FormProjectEditor.AddPage(project, project.Revision, second, Now);
        var moved = FormProjectEditor.MoveField(project, project.Revision, field.FieldID, second.PageID, 0, Now);
        choices.Clear();
        var reopened = FormProjectCodec.Decode(FormProjectCodec.Encode(moved));
        Assert.Empty(reopened.Pages[0].Children);
        Assert.Equal(field.FieldID, Assert.Single(reopened.Pages[1].Children).ID);
        Assert.Equal(binding.BindingID, Assert.Single(reopened.Fields).DataBindingID);
        Assert.Equal("Original", Assert.Single(reopened.Fields[0].Options!).Label);
        Assert.Single(project.Pages[0].Children);
        Assert.Throws<InvalidOperationException>(() => FormProjectEditor.SetTheme(moved, project.Revision, new("another"), Now));
    }

    [Theory]
    [InlineData(FormModeKind.Form)]
    [InlineData(FormModeKind.Test)]
    [InlineData(FormModeKind.Quiz)]
    public void Presets_share_typed_answers_and_marking_but_respect_result_release(FormModeKind mode)
    {
        var field = Text("Processor", Assessment(mode == FormModeKind.Quiz ? FormResultRelease.AfterQuestion : FormResultRelease.AfterSubmission));
        var project = WithField(mode, field);
        var version = Guid.NewGuid();
        var runtime = new FormResponseRuntime(project, version);
        var initial = runtime.Read();
        Assert.False(runtime.Submit(initial.Revision).Success);
        Assert.False(runtime.Answer(initial.Revision, field.FieldID, Json(42)).Success);
        var answered = runtime.Answer(initial.Revision, field.FieldID, Json(" cpu "));
        Assert.True(answered.Success);
        Assert.Empty(answered.Response.ReleasedResults);
        Assert.Null(answered.Response.AwardedPoints);
        Assert.Equal("RevisionConflict", runtime.Answer(initial.Revision, field.FieldID, Json("wrong")).Code);
        if (mode == FormModeKind.Quiz)
        {
            answered = runtime.Advance(answered.Response.Revision);
            Assert.True(answered.Success);
            Assert.Single(answered.Response.ReleasedResults);
        }
        var submitted = runtime.Submit(answered.Response.Revision);
        Assert.True(submitted.Success);
        Assert.Equal(initial.ResponseID, submitted.Response.ResponseID);
        Assert.Equal(version, submitted.Response.FormVersionID);
        Assert.Equal(project.Revision, submitted.Response.ProjectRevision);
        Assert.Equal(6, submitted.Response.AwardedPoints);
        Assert.Equal(6, submitted.Response.MaximumPoints);
        Assert.Equal("ResponseClosed", runtime.Answer(submitted.Response.Revision, field.FieldID, Json("other")).Code);
    }

    [Fact]
    public void Plain_form_has_no_invented_correctness_and_quiz_locks_previous_answer()
    {
        var first = Text("Name"); var second = Text("Other");
        var project = WithField(FormModeKind.Quiz, first);
        project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, second, Now);
        var runtime = new FormResponseRuntime(project, Guid.NewGuid());
        Assert.Equal("IntegrityPolicyViolation", runtime.Answer(1, second.FieldID, Json("early")).Code);
        var answered = runtime.Answer(1, first.FieldID, Json("Name"));
        Assert.Empty(answered.Response.ReleasedResults);
        var advanced = runtime.Advance(answered.Response.Revision);
        Assert.True(advanced.Success);
        Assert.Equal(second.FieldID, advanced.Response.CurrentFieldID);
        Assert.Equal("IntegrityPolicyViolation", runtime.Answer(advanced.Response.Revision, first.FieldID, Json("changed")).Code);
        var done = runtime.Answer(advanced.Response.Revision, second.FieldID, Json("OK"));
        var submitted = runtime.Submit(done.Response.Revision);
        Assert.True(submitted.Success);
        Assert.Null(submitted.Response.MaximumPoints);
    }

    [Fact]
    public void Review_release_does_not_leak_score_and_server_clock_blocks_expired_changes()
    {
        var clock = new Clock(Now);
        var field = Text("Processor", Assessment(FormResultRelease.AfterReview));
        var project = WithField(FormModeKind.Test, field) with { RuntimeSettings = new(TimeLimit: TimeSpan.FromSeconds(10)) };
        var runtime = new FormResponseRuntime(project, Guid.NewGuid(), clock);
        var answer = runtime.Answer(1, field.FieldID, Json("CPU"));
        var submitted = runtime.Submit(answer.Response.Revision);
        Assert.Empty(submitted.Response.ReleasedResults);
        Assert.Null(submitted.Response.AwardedPoints);
        var expired = new FormResponseRuntime(project, Guid.NewGuid(), clock);
        clock.Now += TimeSpan.FromSeconds(10);
        Assert.Equal("IntegrityPolicyViolation", expired.Answer(1, field.FieldID, Json("CPU")).Code);
        Assert.Empty(expired.Read().Answers);
    }

    [Fact]
    public void AfterQuestion_hides_marks_until_question_completion_and_then_locks_reanswers()
    {
        var field = Text("Processor", Assessment(FormResultRelease.AfterQuestion));
        var runtime = new FormResponseRuntime(WithField(FormModeKind.Quiz, field), Guid.NewGuid());
        var first = runtime.Answer(1, field.FieldID, Json("CPU"));
        Assert.Empty(first.Response.ReleasedResults);
        var edited = runtime.Answer(first.Response.Revision, field.FieldID, Json("GPU"));
        Assert.Empty(edited.Response.ReleasedResults);
        Assert.Null(edited.Response.AwardedPoints);
        var completed = runtime.Advance(edited.Response.Revision);
        Assert.True(completed.Success);
        Assert.Equal(FormMarkingOutcome.Incorrect, Assert.Single(completed.Response.ReleasedResults).Value.Outcome);
        var denied = runtime.Answer(completed.Response.Revision, field.FieldID, Json("CPU"));
        Assert.Equal("IntegrityPolicyViolation", denied.Code);
        Assert.Equal("GPU", Assert.Single(denied.Response.Answers).Value.GetString());
    }

    [Fact]
    public void Typed_choice_ids_and_numeric_constraints_cannot_be_bypassed_with_text()
    {
        var option = new FormChoiceOption(Guid.NewGuid(), "Duplicate label");
        var field = Text("Choice") with { Kind = FormFieldKind.SingleChoice, Options = [option] };
        Assert.Null(FormAnswerValidation.Validate(field, Json(option.OptionID)));
        Assert.Equal("ValidationFailed", FormAnswerValidation.Validate(field, Json(option.Label)));
        var number = Text("Number") with { Kind = FormFieldKind.Number, ResponseSchema = Json(new { minimum = 2, maximum = 4 }) };
        Assert.Null(FormAnswerValidation.Validate(number, Json(3)));
        Assert.Equal("ValidationFailed", FormAnswerValidation.Validate(number, Json("3")));
        Assert.Equal("ValidationFailed", FormAnswerValidation.Validate(number, Json(5)));
        Assert.Throws<NotSupportedException>(() => FormAnswerValidation.RequireSupported(number with { ResponseSchema = Json(new { ignoredConstraint = true }) }));
    }

    [Fact]
    public void Unknown_schema_and_custom_runtime_fail_explicitly_preserving_source()
    {
        var project = WithField(FormModeKind.Form, Text("Name"));
        var bytes = FormProjectCodec.Encode(project);
        var unknown = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2"));
        Assert.Throws<InvalidDataException>(() => FormProjectCodec.Decode(unknown));
        Assert.Equal(project.FormID, FormProjectCodec.Decode(bytes).FormID);
        var custom = FormProjectEditor.SetMode(project, project.Revision, new(Guid.NewGuid(), FormModeKind.Custom, Guid.NewGuid(), Guid.NewGuid()), Now);
        Assert.Throws<NotSupportedException>(() => new FormResponseRuntime(custom, Guid.NewGuid()));
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
