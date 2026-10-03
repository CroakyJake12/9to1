using System.Globalization;
using System.Text;
using Haven.Core.Mathematics;
using System.Text.Json;

namespace Haven.Core.Forms;

public enum FormResponseState { InProgress, Submitted }
public enum FormResponseDataWriteState { NotRequested, Pending }
public sealed record FormAnswer(Guid FieldID, JsonElement Value);
public sealed record FormResponse(Guid ResponseID, Guid FormID, Guid FormVersionID, long ProjectRevision,
    long Revision, FormResponseState State, DateTimeOffset StartedAt, DateTimeOffset? SubmittedAt,
    IReadOnlyList<FormAnswer> Answers, IReadOnlyDictionary<Guid, FormMarkingResult> ReleasedResults,
    Guid? CurrentFieldID, decimal? AwardedPoints, decimal? MaximumPoints, Guid? GradeID,
    FormResponseDataWriteState DataWriteState);
public sealed record FormResponseOperation(bool Success, string? Code, FormResponse Response);
/// <summary>Authoritative server persistence only; never accept a respondent-supplied checkpoint.
/// Marks are recomputed from the pinned authored version rather than trusting stored client scores.</summary>
public sealed record FormResponseCheckpoint(int SchemaVersion, Guid ResponseID, Guid FormID, Guid FormVersionID,
    long ProjectRevision, long Revision, DateTimeOffset StartedAt, DateTimeOffset? SubmittedAt,
    int QuestionIndex, IReadOnlyList<FormAnswer> Answers);

/// <summary>One deterministic session engine for native Form/Test/Quiz presets and interactive preview.
/// The host owns respondent authorisation, durable response CAS, attempts and Data commits. This instance is
/// server-side state: respondent views contain released marks only, never authored marking rules.</summary>
public sealed class FormResponseRuntime
{
    private readonly FormProject _project;
    private readonly Guid _version;
    private readonly Guid _id;
    private readonly DateTimeOffset _started;
    private readonly TimeProvider _clock;
    private readonly Guid[] _order;
    private readonly Dictionary<Guid, JsonElement> _answers = [];
    private readonly Dictionary<Guid, FormMarkingResult> _marks = [];
    private readonly object _gate = new();
    private long _revision = 1;
    private int _index;
    private DateTimeOffset? _submitted;

    public FormResponseRuntime(FormProject project, Guid versionID, TimeProvider? clock = null)
    {
        _project = FormProjectCodec.Capture(project);
        if (versionID == Guid.Empty) throw new ArgumentException("VersionNotFound", nameof(versionID));
        if (_project.ModeDefinition.Kind == FormModeKind.Custom || _project.ModeDefinition.StateGraphID is not null || _project.LogicGraphID is not null
            || _project.Pages.Any(page => page.VisibilityNodeID is not null) || _project.Fields.Any(field => field.ValidationNodeID is not null)
            || _project.RuntimeSettings.ShuffleChoices || _project.RuntimeSettings.ShuffleQuestions)
            throw new NotSupportedException("CapabilityUnavailable: configured graph or randomisation runtime is not registered.");
        _version = versionID; _id = Guid.NewGuid(); _clock = clock ?? TimeProvider.System; _started = _clock.GetUtcNow();
        _order = _project.Pages.SelectMany(page => page.Children).SelectMany(child => child.Kind == FormChildKind.Field
            ? new[] { child.ID } : _project.Components.Single(component => component.ComponentID == child.ID).ChildFieldIDs)
            .Distinct().ToArray();
        if (_order.Length != _project.Fields.Count)
            throw new InvalidDataException("UnreachableFields: each field must be reachable from an authored page.");
        foreach (var field in _project.Fields) FormAnswerValidation.RequireSupported(field);
    }

    public FormResponse Read() { lock (_gate) return Snapshot(); }

    public FormResponseCheckpoint CaptureCheckpoint()
    {
        lock (_gate) return new(1, _id, _project.FormID, _version, _project.Revision, _revision, _started, _submitted,
            _index, _answers.Select(answer => new FormAnswer(answer.Key, answer.Value.Clone())).ToArray());
    }

    public static FormResponseRuntime Restore(FormProject project, FormResponseCheckpoint checkpoint, TimeProvider? clock = null) =>
        new(project, checkpoint, clock);

    private FormResponseRuntime(FormProject project, FormResponseCheckpoint checkpoint, TimeProvider? clock)
        : this(project, checkpoint.FormVersionID, clock)
    {
        if (checkpoint.SchemaVersion != 1 || checkpoint.ResponseID == Guid.Empty || checkpoint.FormID != _project.FormID
            || checkpoint.ProjectRevision != _project.Revision || checkpoint.Revision < 1 || checkpoint.StartedAt == default
            || checkpoint.SubmittedAt < checkpoint.StartedAt || checkpoint.QuestionIndex < 0 || checkpoint.QuestionIndex > _order.Length
            || _project.ModeDefinition.Kind != FormModeKind.Quiz && checkpoint.QuestionIndex != 0
            || checkpoint.Answers is null || checkpoint.Answers.Count > _project.Fields.Count)
            throw new InvalidDataException("Invalid authoritative response checkpoint.");
        _id = checkpoint.ResponseID; _started = checkpoint.StartedAt; _revision = checkpoint.Revision;
        _index = checkpoint.QuestionIndex; _submitted = checkpoint.SubmittedAt;
        foreach (var answer in checkpoint.Answers)
        {
            var field = answer is null ? null : _project.Fields.SingleOrDefault(field => field.FieldID == answer.FieldID);
            if (field is null || _answers.ContainsKey(field.FieldID) || answer!.Value.ValueKind == JsonValueKind.Undefined
                || FormAnswerValidation.Validate(field, answer.Value) is not null
                || _project.ModeDefinition.Kind == FormModeKind.Quiz && _submitted is null && Array.IndexOf(_order, field.FieldID) > _index)
                throw new InvalidDataException("Invalid checkpoint answer or question progression.");
            _answers[field.FieldID] = answer.Value.Clone();
            if (field.Assessment is { } assessment)
                _marks[field.FieldID] = FormMathematics.Evaluate(field, answer.Value, assessment);
        }
        if (_revision < 1L + _answers.Count + _index + (_submitted is null ? 0 : 1)
            || _project.Fields.Any(field => field.Required && !_answers.ContainsKey(field.FieldID)
                && (_submitted is not null || _project.ModeDefinition.Kind == FormModeKind.Quiz && Array.IndexOf(_order, field.FieldID) < _index)))
            throw new InvalidDataException("Checkpoint cannot represent a valid completed transition.");
        if (_submitted is not null)
            foreach (var field in _project.Fields.Where(field => field.Assessment is not null && !_marks.ContainsKey(field.FieldID)))
                _marks[field.FieldID] = new(FormMarkingOutcome.Incorrect, 0, field.Assessment!.MaximumPoints, []);
    }

    public FormResponseOperation Answer(long expectedRevision, Guid fieldID, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Answer schema is undefined.", nameof(value));
        var encoded = value.GetRawText();
        if (encoded.Length > FormProjectCodec.MaximumBytes || System.Text.Encoding.UTF8.GetByteCount(encoded) > FormProjectCodec.MaximumBytes)
            throw new ArgumentException("Answer exceeds the response byte limit.", nameof(value));
        var captured = value.Clone();
        lock (_gate)
        {
            var rejected = Check(expectedRevision);
            if (rejected is not null) return new(false, rejected, Snapshot());
            var field = _project.Fields.SingleOrDefault(field => field.FieldID == fieldID);
            if (field is null) return new(false, "FieldNotFound", Snapshot());
            if (_project.ModeDefinition.Kind == FormModeKind.Quiz && (_index >= _order.Length || _order[_index] != fieldID))
                return new(false, "IntegrityPolicyViolation", Snapshot());
            var error = FormAnswerValidation.Validate(field, captured);
            if (error is not null) return new(false, error, Snapshot());
            if (_answers.TryGetValue(fieldID, out var previous))
            {
                error = FormMathematics.ValidateReplacement(field, previous, captured);
                if (error is not null) return new(false, error, Snapshot());
            }
            _answers[fieldID] = captured;
            if (field.Assessment is { } assessment)
                _marks[fieldID] = FormMathematics.Evaluate(field, captured, assessment);
            _revision = checked(_revision + 1);
            return new(true, null, Snapshot());
        }
    }

    public FormResponseOperation Advance(long expectedRevision)
    {
        lock (_gate)
        {
            var rejected = Check(expectedRevision);
            if (rejected is not null) return new(false, rejected, Snapshot());
            if (_project.ModeDefinition.Kind != FormModeKind.Quiz || _index >= _order.Length)
                return new(false, "InvalidState", Snapshot());
            var field = _project.Fields.Single(field => field.FieldID == _order[_index]);
            if (field.Required && !_answers.ContainsKey(field.FieldID)) return new(false, "ValidationFailed", Snapshot());
            _index++; _revision = checked(_revision + 1);
            return new(true, null, Snapshot());
        }
    }

    public FormResponseOperation Submit(long expectedRevision)
    {
        lock (_gate)
        {
            var rejected = Check(expectedRevision);
            if (rejected is not null) return new(false, rejected, Snapshot());
            if (_project.Fields.Any(field => field.Required && !_answers.ContainsKey(field.FieldID)))
                return new(false, "ValidationFailed", Snapshot());
            // Unanswered optional assessable questions contribute zero, preserving the configured denominator.
            foreach (var field in _project.Fields.Where(field => field.Assessment is not null && !_marks.ContainsKey(field.FieldID)))
                _marks[field.FieldID] = new(FormMarkingOutcome.Incorrect, 0, field.Assessment!.MaximumPoints, []);
            _submitted = _clock.GetUtcNow(); _revision = checked(_revision + 1);
            return new(true, null, Snapshot());
        }
    }

    private string? Check(long expected) => expected != _revision ? "RevisionConflict" : _submitted is not null ? "ResponseClosed"
        : _project.RuntimeSettings.TimeLimit is { } limit && _clock.GetUtcNow() - _started >= limit ? "IntegrityPolicyViolation" : null;

    private FormResponse Snapshot()
    {
        // Answer edits are drafts. Advance completes and locks the current Quiz question;
        // AfterQuestion must not turn answer edits into a correctness oracle before that boundary.
        bool Release(FormResultRelease policy, Guid? fieldID = null) => policy == FormResultRelease.Immediate
            || policy == FormResultRelease.AfterQuestion && (_submitted is not null
                || _project.ModeDefinition.Kind == FormModeKind.Quiz && fieldID is { } id && Array.IndexOf(_order, id) < _index)
            || policy == FormResultRelease.AfterSubmission && _submitted is not null;
        var released = _marks.Where(mark => Release(_project.Fields.Single(field => field.FieldID == mark.Key).Assessment!.Release, mark.Key))
            .ToDictionary(mark => mark.Key, mark => mark.Value with { MatchedRuleIDs = mark.Value.MatchedRuleIDs.ToArray() });
        var graded = _submitted is not null && _marks.Count > 0 && Release(_project.MarkingScheme.Release)
            && _project.Fields.Where(field => field.Assessment is not null).All(field => Release(field.Assessment!.Release, field.FieldID))
            && _marks.Values.All(mark => mark.Outcome != FormMarkingOutcome.NeedsReview);
        decimal? points = graded ? _project.Fields.Where(field => field.Assessment is not null)
            .Sum(field => _marks[field.FieldID].AwardedPoints * field.Assessment!.Weight) : null;
        decimal? maximum = graded ? _project.Fields.Where(field => field.Assessment is not null)
            .Sum(field => field.Assessment!.MaximumPoints * field.Assessment.Weight) : null;
        Guid? grade = maximum is > 0 ? _project.MarkingScheme.Grades.OrderByDescending(band => band.MinimumPercentage)
            .FirstOrDefault(band => points / maximum * 100 >= band.MinimumPercentage)?.GradeID : null;
        return new(_id, _project.FormID, _version, _project.Revision, _revision,
            _submitted is null ? FormResponseState.InProgress : FormResponseState.Submitted, _started, _submitted,
            _answers.Select(answer => new FormAnswer(answer.Key, answer.Value.Clone())).ToArray(), released,
            _project.ModeDefinition.Kind == FormModeKind.Quiz && _index < _order.Length ? _order[_index] : null,
            points, maximum, grade, _submitted is not null && _project.DataBindings.Any(binding => binding.Kind != FormDataBindingKind.Lookup)
                ? FormResponseDataWriteState.Pending : FormResponseDataWriteState.NotRequested);
    }
}

/// <summary>Typed answer checks shared by session preview and native runtime. Unsupported schema keywords
/// are explicit capability errors rather than silently ignored constraints.</summary>
public static class FormAnswerValidation
{
    public static void RequireSupported(FormField field)
    {
        if (field.Kind is FormFieldKind.Mathematical or FormFieldKind.Graph)
        {
            if (field.ResponseSchema.EnumerateObject().Any())
                throw new NotSupportedException("CapabilityUnavailable: mathematical response schema keyword.");
            if (field.Kind == FormFieldKind.Mathematical && field.Mathematics is null ||
                field.Kind == FormFieldKind.Graph && field.Graph is null)
                throw new NotSupportedException("CapabilityUnavailable: canonical mathematical question is not configured.");
            return;
        }
        if (field.Kind is not (FormFieldKind.ShortText or FormFieldKind.LongText or FormFieldKind.Number
            or FormFieldKind.Decimal or FormFieldKind.Currency or FormFieldKind.Email or FormFieldKind.Phone
            or FormFieldKind.Date or FormFieldKind.Time or FormFieldKind.DateTime or FormFieldKind.Duration
            or FormFieldKind.SingleChoice or FormFieldKind.MultipleChoice or FormFieldKind.Dropdown or FormFieldKind.CheckboxSet
            or FormFieldKind.Rating or FormFieldKind.Ranking or FormFieldKind.TableInput))
            throw new NotSupportedException("CapabilityUnavailable: typed answer provider is not registered for " + field.Kind);
        var numeric = field.Kind is FormFieldKind.Number or FormFieldKind.Decimal or FormFieldKind.Currency or FormFieldKind.Rating;
        var text = field.Kind is FormFieldKind.ShortText or FormFieldKind.LongText or FormFieldKind.Email or FormFieldKind.Phone
            or FormFieldKind.Date or FormFieldKind.Time or FormFieldKind.DateTime or FormFieldKind.Duration;
        if (field.ResponseSchema.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Response constraints must be an object.");
        decimal? minimum = null, maximum = null; int? minLength = null, maxLength = null;
        var keywords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in field.ResponseSchema.EnumerateObject())
        {
            if (!keywords.Add(property.Name)) throw new InvalidDataException("Duplicate response constraint.");
            if (property.Name is "minimum" or "maximum")
            {
                if (!numeric || property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDecimal(out _)) throw new InvalidDataException("Invalid numeric response constraint.");
                if (property.Name == "minimum") minimum = property.Value.GetDecimal(); else maximum = property.Value.GetDecimal();
            }
            else if (property.Name is "minLength" or "maxLength")
            {
                if (!text || property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var length) || length is < 0 or > 65536) throw new InvalidDataException("Invalid string response constraint.");
                if (property.Name == "minLength") minLength = property.Value.GetInt32(); else maxLength = property.Value.GetInt32();
            }
            else throw new NotSupportedException("CapabilityUnavailable: response schema keyword " + property.Name);
        }
        if (minimum > maximum || minLength > maxLength) throw new InvalidDataException("Response constraint bounds are inverted.");
    }

    public static string? Validate(FormField field, JsonElement value)
    {
        RequireSupported(field);
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return field.Required ? "ValidationFailed" : null;
        if (field.Kind is FormFieldKind.Mathematical or FormFieldKind.Graph)
            return FormMathematics.Validate(field, value);
        bool String() => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 65536
            && (!field.Required || !string.IsNullOrWhiteSpace(value.GetString()));
        bool Choice(JsonElement item) => item.ValueKind == JsonValueKind.String && item.TryGetGuid(out var id)
            && field.Options?.Any(option => option.OptionID == id) == true;
        bool valid = field.Kind switch
        {
            FormFieldKind.ShortText or FormFieldKind.LongText or FormFieldKind.Phone => String(),
            FormFieldKind.Email => String() && System.Net.Mail.MailAddress.TryCreate(value.GetString(), out var address)
                && address.Address == value.GetString(),
            FormFieldKind.Number or FormFieldKind.Decimal or FormFieldKind.Currency or FormFieldKind.Rating => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            FormFieldKind.Date => String() && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            FormFieldKind.Time => String() && TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            FormFieldKind.DateTime => String() && DateTimeOffset.TryParseExact(value.GetString(), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            FormFieldKind.Duration => String() && TimeSpan.TryParseExact(value.GetString(), "c", CultureInfo.InvariantCulture, out var duration) && duration >= TimeSpan.Zero,
            FormFieldKind.SingleChoice or FormFieldKind.Dropdown => Choice(value),
            FormFieldKind.MultipleChoice or FormFieldKind.CheckboxSet or FormFieldKind.Ranking => value.ValueKind == JsonValueKind.Array
                && value.GetArrayLength() <= 4096 && (!field.Required || value.GetArrayLength() > 0)
                && value.EnumerateArray().All(Choice) && value.EnumerateArray().Select(item => item.GetGuid()).Distinct().Count() == value.GetArrayLength(),
            FormFieldKind.TableInput => ValidTable(field, value),
            _ => false
        };
        if (!valid) return "ValidationFailed";
        foreach (var property in field.ResponseSchema.EnumerateObject())
        {
            if (property.Name is "minimum" or "maximum")
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)
                    || (property.Name == "minimum" ? number < property.Value.GetDecimal() : number > property.Value.GetDecimal())) return "ValidationFailed";
            }
            if (property.Name is "minLength" or "maxLength")
            {
                if (value.ValueKind != JsonValueKind.String || (property.Name == "minLength"
                    ? value.GetString()!.Length < property.Value.GetInt32() : value.GetString()!.Length > property.Value.GetInt32())) return "ValidationFailed";
            }
        }
        return null;
    }

    private static bool ValidTable(FormField field, JsonElement value)
    {
        try
        {
            var response = value.Deserialize<FormTableInputResponse>();
            return response is { Rows: not null } && response.Rows.All(row => row is { Cells: not null })
                && FormTableInput.Validate(field.Table!, response).Count == 0;
        }
        catch (JsonException) { return false; }
    }
}
