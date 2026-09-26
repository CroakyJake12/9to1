namespace Haven.Application;

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
    DateTimeOffset SubmittedAt);

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
}

/// <summary>Shared bounds and ordering for local form response stores.</summary>
public static class FormsSubmissionLogic
{
    public const int MaxSubmissions = 500;
    public const int MaxFieldsPerSubmission = 32;
    public const int MaxFieldIdLength = 64;
    public const int MaxTextLength = 4000;
    public const int MaxFormTitleLength = 120;

    public static IReadOnlyList<FormsSubmission> Normalise(IEnumerable<FormsSubmission?>? submissions)
    {
        if (submissions is null) return [];
        var latestById = new Dictionary<string, FormsSubmission>(StringComparer.Ordinal);
        foreach (var candidate in submissions)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.Id) || string.IsNullOrWhiteSpace(candidate.FormId)) continue;
            var normalised = Normalise(candidate);
            if (!latestById.TryGetValue(normalised.Id, out var existing) || normalised.SubmittedAt > existing.SubmittedAt)
                latestById[normalised.Id] = normalised;
        }
        return latestById.Values
            .OrderByDescending(item => item.SubmittedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(MaxSubmissions)
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
            values[key.Trim()] = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        }
        var id = submission.Id.Trim();
        var formId = submission.FormId.Trim();
        var title = (submission.FormTitle ?? string.Empty).Trim();
        if (title.Length > MaxFormTitleLength) title = title[..MaxFormTitleLength];
        return submission with
        {
            Id = id,
            FormId = formId,
            FormTitle = string.IsNullOrWhiteSpace(title) ? formId : title,
            Values = values
        };
    }
}
