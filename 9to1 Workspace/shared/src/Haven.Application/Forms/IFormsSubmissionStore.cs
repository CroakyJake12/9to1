using System.Text.Json;

namespace Haven.Application;

/// <summary>One typed form answer. Data references use stable IDs, never sheet coordinates or display names.</summary>
public sealed record FormsAnswer(
    string FieldId,
    string ValueType,
    JsonElement Value,
    Guid? DataWorkbookId = null,
    Guid? DataTableId = null,
    string? DataFieldId = null,
    string? DataRecordId = null);

public enum FormsDataWriteStatus
{
    NotBound = 0,
    Pending = 1,
    Succeeded = 2,
    RetryableFailure = 3,
    Conflict = 4,
    PartialFailure = 5
}

public sealed record FormsSubmissionCursor(DateTimeOffset SubmittedAt, string ResponseId);
public sealed record FormsSubmissionPageRequest(int PageSize = 50, FormsSubmissionCursor? After = null, string? FormId = null);
public sealed record FormsSubmissionPage(IReadOnlyList<FormsSubmission> Items, FormsSubmissionCursor? Next);

/// <summary>A locally retained response submitted to a named Haven form.</summary>
/// <param name="Id">Stable response identifier.</param>
/// <param name="FormId">Stable key identifying the form.</param>
/// <param name="FormTitle">Human-facing form title captured at submission time.</param>
/// <param name="Values">Submitted field values keyed by the form's stable field ids.</param>
/// <param name="SubmittedAt">Submission timestamp.</param>
public sealed record FormsSubmission(
    string Id,
    string FormId,
    string FormTitle,
    IReadOnlyDictionary<string, string> Values,
    DateTimeOffset SubmittedAt)
{
    /// <summary>Published form version/schema revision answered; legacy local submissions are marked explicitly.</summary>
    public string FormVersionId { get; init; } = string.Empty;

    /// <summary>Typed answers keyed by the stable Form FieldID.</summary>
    public IReadOnlyDictionary<string, FormsAnswer> Answers { get; init; } = new Dictionary<string, FormsAnswer>(StringComparer.Ordinal);

    public DateTimeOffset? StartedAt { get; init; }
    public string? DataBindingId { get; init; }
    public Guid? DataTargetWorkbookId { get; init; }
    public Guid? DataTargetTableId { get; init; }
    public FormsDataWriteStatus DataWriteStatus { get; init; } = FormsDataWriteStatus.NotBound;
    public int Revision { get; init; } = 1;
}

/// <summary>Stable response identity was reused for a different submitted result.</summary>
public sealed class FormsSubmissionConflictException(string responseId)
    : InvalidOperationException($"Response id '{responseId}' is already stored with different submission data.")
{
    public string Code => "FormsResponseIdConflict";
    public string ResponseId { get; } = responseId;
    public bool CanRetry => false;
}

/// <summary>Stores local form submissions, newest first, without publishing them to a service.</summary>
public interface IFormsSubmissionStore
{
    /// <summary>Data workbook id containing the same locally stored responses.</summary>
    Guid DataWorkbookId { get; }

    Task<IReadOnlyList<FormsSubmission>> GetLatestAsync(CancellationToken cancellationToken);
    Task SaveAsync(FormsSubmission submission, CancellationToken cancellationToken);

    async Task<FormsSubmissionPage> GetPageAsync(FormsSubmissionPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PageSize is < 1 or > FormsSubmissionLogic.MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(request), $"Page size must be between 1 and {FormsSubmissionLogic.MaxPageSize}.");

        var submissions = FormsSubmissionLogic.OrderPage(
            await GetLatestAsync(cancellationToken).ConfigureAwait(false), request);
        var page = submissions.Take(request.PageSize + 1).ToArray();
        var hasMore = page.Length > request.PageSize;
        var items = page.Take(request.PageSize).ToArray();
        var next = hasMore && items.Length > 0
            ? new FormsSubmissionCursor(items[^1].SubmittedAt, items[^1].Id)
            : null;
        return new FormsSubmissionPage(items, next);
    }
}

/// <summary>Shared bounds and ordering for local form response stores.</summary>
public static class FormsSubmissionLogic
{
    public const int MaxPageSize = 200;
    public const int MaxFieldsPerSubmission = 32;
    public const int MaxAnswersPerSubmission = 256;
    public const int MaxFieldIdLength = 64;
    public const int MaxTextLength = 4000;
    public const int MaxFormTitleLength = 120;
    public const int MaxAnswerTypeLength = 64;
    public const int MaxTypedAnswerJsonLength = 1_048_576;

    public static IReadOnlyList<FormsSubmission> Normalise(IEnumerable<FormsSubmission?>? submissions)
    {
        if (submissions is null) return [];
        var latestById = new Dictionary<string, FormsSubmission>(StringComparer.Ordinal);
        foreach (var candidate in submissions)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.Id) || string.IsNullOrWhiteSpace(candidate.FormId)) continue;
            var normalised = Normalise(candidate);
            if (latestById.TryGetValue(normalised.Id, out var existing))
            {
                if (!AreEquivalent(existing, normalised))
                    throw new FormsSubmissionConflictException(normalised.Id);
                continue;
            }
            latestById[normalised.Id] = normalised;
        }
        return latestById.Values
            .OrderByDescending(item => item.SubmittedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static FormsSubmission Normalise(FormsSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in submission.Values ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > MaxFieldIdLength || values.Count >= MaxFieldsPerSubmission) continue;
            var text = (value ?? string.Empty).Trim();
            if (values.ContainsKey(key.Trim()))
                throw new ArgumentException($"Form field id '{key.Trim()}' appears more than once after normalisation.", nameof(submission));
            values[key.Trim()] = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        }

        var answers = new Dictionary<string, FormsAnswer>(StringComparer.Ordinal);
        foreach (var (key, answer) in submission.Answers ?? new Dictionary<string, FormsAnswer>(StringComparer.Ordinal))
        {
            if (answers.Count >= MaxAnswersPerSubmission)
                throw new ArgumentException($"A response cannot contain more than {MaxAnswersPerSubmission} typed answers.", nameof(submission));
            if (answer is null) throw new ArgumentException("A typed form answer cannot be null.", nameof(submission));
            var fieldId = (answer.FieldId ?? key ?? string.Empty).Trim();
            var valueType = (answer.ValueType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fieldId) || fieldId.Length > MaxFieldIdLength)
                throw new ArgumentException("Typed form answers need a field id within the supported length.", nameof(submission));
            if (string.IsNullOrWhiteSpace(valueType) || valueType.Length > MaxAnswerTypeLength || valueType.Any(char.IsControl))
                throw new ArgumentException($"Typed answer '{fieldId}' needs a printable value type within the supported length.", nameof(submission));
            if (answer.Value.ValueKind == JsonValueKind.Undefined)
                throw new ArgumentException($"Typed answer '{fieldId}' has no JSON value.", nameof(submission));
            if (answer.DataRecordId is not null && (answer.DataWorkbookId is null || answer.DataTableId is null))
                throw new ArgumentException($"Typed answer '{fieldId}' references a record without stable workbook and table ids.", nameof(submission));
            if (answer.DataFieldId is not null && answer.DataTableId is null)
                throw new ArgumentException($"Typed answer '{fieldId}' references a field without a stable Data table id.", nameof(submission));

            var normalizedAnswer = answer with { FieldId = fieldId, ValueType = valueType, Value = answer.Value.Clone() };
            var encodedLength = JsonSerializer.Serialize(normalizedAnswer).Length;
            if (encodedLength > MaxTypedAnswerJsonLength)
                throw new ArgumentException($"Typed answer '{fieldId}' exceeds the {MaxTypedAnswerJsonLength}-character storage limit.", nameof(submission));
            if (!answers.TryAdd(fieldId, normalizedAnswer))
                throw new ArgumentException($"Form field id '{fieldId}' appears more than once after normalisation.", nameof(submission));
        }

        if (answers.Count == 0)
        {
            foreach (var (fieldId, text) in values)
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(text));
                answers[fieldId] = new FormsAnswer(fieldId, "text", document.RootElement.Clone());
            }
        }
        else if (values.Count == 0)
        {
            foreach (var (fieldId, answer) in answers)
                values[fieldId] = ToDisplayValue(answer.Value);
        }
        var id = submission.Id.Trim();
        var formId = submission.FormId.Trim();
        if (id.Length > 256 || id.Any(char.IsControl) || formId.Length > 256 || formId.Any(char.IsControl))
            throw new ArgumentException("Response and form ids must be at most 256 printable characters.", nameof(submission));
        var title = (submission.FormTitle ?? string.Empty).Trim();
        if (title.Length > MaxFormTitleLength) title = title[..MaxFormTitleLength];
        var versionId = (submission.FormVersionId ?? string.Empty).Trim();
        if (versionId.Length > 128 || versionId.Any(char.IsControl))
            throw new ArgumentException("Form version id must be at most 128 printable characters.", nameof(submission));
        if (submission.StartedAt is { } startedAt && startedAt > submission.SubmittedAt)
            throw new ArgumentException("A response cannot start after its submitted timestamp.", nameof(submission));
        if (!Enum.IsDefined(submission.DataWriteStatus))
            throw new ArgumentException("Form response has an unknown Data-write status.", nameof(submission));
        var bindingId = string.IsNullOrWhiteSpace(submission.DataBindingId) ? null : submission.DataBindingId.Trim();
        if (bindingId is { Length: > 128 } || bindingId?.Any(char.IsControl) == true)
            throw new ArgumentException("Data binding id must be at most 128 printable characters.", nameof(submission));
        return submission with
        {
            Id = id,
            FormId = formId,
            FormTitle = string.IsNullOrWhiteSpace(title) ? formId : title,
            Values = values,
            Answers = answers,
            FormVersionId = string.IsNullOrWhiteSpace(versionId) ? "legacy-unversioned" : versionId,
            DataBindingId = bindingId,
            Revision = submission.Revision < 1 ? 1 : submission.Revision
        };
    }

    public static IEnumerable<FormsSubmission> OrderPage(
        IEnumerable<FormsSubmission> submissions,
        FormsSubmissionPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(request);
        var formId = string.IsNullOrWhiteSpace(request.FormId) ? null : request.FormId.Trim();
        return submissions
            .Where(item => formId is null || item.FormId.Equals(formId, StringComparison.Ordinal))
            .Where(item => request.After is null
                || item.SubmittedAt < request.After.SubmittedAt
                || (item.SubmittedAt == request.After.SubmittedAt
                    && string.CompareOrdinal(item.Id, request.After.ResponseId) > 0))
            .OrderByDescending(item => item.SubmittedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal);
    }

    public static bool AreEquivalent(FormsSubmission left, FormsSubmission right) =>
        left.Id.Equals(right.Id, StringComparison.Ordinal)
        && left.FormId.Equals(right.FormId, StringComparison.Ordinal)
        && left.FormTitle.Equals(right.FormTitle, StringComparison.Ordinal)
        && left.FormVersionId.Equals(right.FormVersionId, StringComparison.Ordinal)
        && left.SubmittedAt.Equals(right.SubmittedAt)
        && left.StartedAt.Equals(right.StartedAt)
        && left.DataBindingId == right.DataBindingId
        && left.DataTargetWorkbookId == right.DataTargetWorkbookId
        && left.DataTargetTableId == right.DataTargetTableId
        && left.DataWriteStatus == right.DataWriteStatus
        && left.Revision == right.Revision
        && left.Values.Count == right.Values.Count
        && left.Values.All(pair => right.Values.TryGetValue(pair.Key, out var value)
            && pair.Value.Equals(value, StringComparison.Ordinal))
        && left.Answers.Count == right.Answers.Count
        && left.Answers.All(pair => right.Answers.TryGetValue(pair.Key, out var answer)
            && pair.Value.ValueType.Equals(answer.ValueType, StringComparison.Ordinal)
            && pair.Value.DataWorkbookId == answer.DataWorkbookId
            && pair.Value.DataTableId == answer.DataTableId
            && pair.Value.DataFieldId == answer.DataFieldId
            && pair.Value.DataRecordId == answer.DataRecordId
            && pair.Value.Value.GetRawText().Equals(answer.Value.GetRawText(), StringComparison.Ordinal));

    private static string ToDisplayValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText()
    };
}
