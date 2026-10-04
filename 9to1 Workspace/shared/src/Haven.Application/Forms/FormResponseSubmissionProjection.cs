using System.Globalization;
using System.Text.Json;
using Haven.Core.Forms;

namespace Haven.Application;

/// <summary>Projects a completed canonical runtime response into the existing Forms/Data response model.
/// This conversion grants no storage permission and does not execute linked Data writes.</summary>
public static class FormResponseSubmissionProjection
{
    public static FormsSubmission Create(FormProject project, FormResponse response)
    {
        var captured = FormProjectCodec.Capture(project);
        ArgumentNullException.ThrowIfNull(response);
        if (response.FormID != captured.FormID || response.ProjectRevision != captured.Revision || response.FormVersionID == Guid.Empty
            || response.ResponseID == Guid.Empty || response.State != FormResponseState.Submitted || response.SubmittedAt is null
            || response.Revision < 1 || response.Revision > int.MaxValue || response.StartedAt == default || response.SubmittedAt < response.StartedAt)
            throw new InvalidDataException("The submitted response does not match the pinned canonical form/version.");
        if (response.Answers.Count > FormsSubmissionLogic.MaxAnswersPerSubmission || response.Answers.Select(answer => answer.FieldID).Distinct().Count() != response.Answers.Count)
            throw new InvalidDataException("Response exceeds the configured Data response storage capability or repeats field identity.");
        var fields = captured.Fields.ToDictionary(field => field.FieldID);
        var answers = new Dictionary<string, FormsAnswer>(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var answer in response.Answers)
        {
            if (!fields.TryGetValue(answer.FieldID, out var field) || FormAnswerValidation.Validate(field, answer.Value) is not null)
                throw new InvalidDataException("The answer does not match its published field schema.");
            var key = answer.FieldID.ToString("D");
            var binding = field.DataBindingID is { } bindingID ? captured.DataBindings.Single(item => item.BindingID == bindingID) : null;
            answers.Add(key, new(key, field.Kind.ToString(), answer.Value.Clone(), binding?.WorkbookID, binding?.TableID, binding?.ColumnID.ToString("D")));
            // Legacy display cells are secondary projections; typed JSON remains the authoritative answer.
            values.Add(key, answer.Value.ValueKind == JsonValueKind.String ? answer.Value.GetString()! : answer.Value.GetRawText());
        }
        if (captured.Fields.Any(field => field.Required && !answers.ContainsKey(field.FieldID.ToString("D"))))
            throw new InvalidDataException("Required canonical answers are missing.");
        var writes = captured.DataBindings.Where(binding => binding.Kind != FormDataBindingKind.Lookup).ToArray();
        var single = writes.Length == 1 ? writes[0] : null;
        return FormsSubmissionLogic.Normalise(new FormsSubmission(response.ResponseID.ToString("D"), captured.FormID.ToString("D"), captured.Title, values, response.SubmittedAt.Value)
        {
            FormVersionId = response.FormVersionID.ToString("D"), StartedAt = response.StartedAt, Answers = answers,
            Revision = checked((int)response.Revision), DataBindingId = single?.BindingID.ToString("D"),
            DataTargetWorkbookId = single?.WorkbookID, DataTargetTableId = single?.TableID,
            DataWriteStatus = writes.Length > 0 ? FormsDataWriteStatus.Pending : FormsDataWriteStatus.NotBound
        });
    }
}
