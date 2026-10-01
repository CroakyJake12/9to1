using System.Text.Json;
using Haven.Core;
using Haven.Core.Forms;

namespace Haven.Application;

/// <summary>Prepares one exact existing-record edit from a pinned submitted response. This is a
/// conversion, not respondent authority, publication admission, execution or a persisted write receipt.
/// The owning coordinator must load the authoritative response and obtain Home approval for Intent.
/// By default one mutation per response/workbook uses ResponseID; a multi-target coordinator must
/// persist a separate operation ID for each mutation before preparing it.</summary>
public sealed record FormDataRecordUpdatePlan(Guid FormID, Guid FormVersionID, Guid ResponseID,
    long ResponseRevision, IReadOnlyList<Guid> BindingIDs, DataRecordUpdateIntent Intent);

public static class FormDataRecordUpdateProjection
{
    public static FormDataRecordUpdatePlan Prepare(FormProject pinned, FormResponse response,
        Guid storeID, DataWorkbook target, Guid tableID, Guid recordID, Guid? operationID = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var project = FormProjectCodec.Capture(pinned);
        var submission = FormResponseSubmissionProjection.Create(project, response);
        var bindings = project.DataBindings.Where(binding => binding.WorkbookID == target.Id && binding.TableID == tableID
            && binding.Kind != FormDataBindingKind.Lookup).ToArray();
        if (bindings.Length == 0) throw new InvalidOperationException("DataBindingNotFound");
        if (bindings.Any(binding => binding.Kind != FormDataBindingKind.UpdateRecord || binding.ParentBindingID is not null
            || binding.SourceColumnID is not null)) throw new NotSupportedException("DataBindingRequiresStructuredMutation");
        var fields = project.Fields.ToDictionary(field => field.FieldID);
        var values = new Dictionary<Guid, DataScalarRecordValue>();
        foreach (var binding in bindings)
        {
            var field = fields[binding.FieldID];
            if (field.DataBindingID != binding.BindingID) throw new InvalidDataException("DataBindingIsNotAttached");
            // Unanswered optional fields leave the target untouched; explicit null requires a nullable Data capability.
            if (!submission.Answers.TryGetValue(field.FieldID.ToString("D"), out var answer)) continue;
            var kind = field.Kind switch
            {
                FormFieldKind.ShortText or FormFieldKind.LongText or FormFieldKind.Email or FormFieldKind.Phone => DataCellKind.Text,
                FormFieldKind.Number or FormFieldKind.Decimal or FormFieldKind.Currency or FormFieldKind.Rating => DataCellKind.Number,
                FormFieldKind.Date or FormFieldKind.DateTime => DataCellKind.Date,
                _ => throw new NotSupportedException("DataBindingScalarTypeUnavailable")
            };
            if (!values.TryAdd(binding.ColumnID, DataRecordEdits.Capture(new(kind, answer.Value))))
                throw new InvalidDataException("DataBindingTargetCollision");
        }
        var intent = DataRecordUpdateIntent.Capture(storeID, target.Id, target.Version, target.RevisionId, tableID, recordID, values, operationID ?? response.ResponseID,
            new(project.FormID, response.FormVersionID, response.ResponseID, response.Revision));
        // Exercise actual canonical IDs, formula protection and validation on a detached workbook only.
        var detached = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.SerializeToUtf8Bytes(target))
            ?? throw new InvalidDataException("Data workbook snapshot is unavailable.");
        detached.Normalize();
        DataRecordEdits.UpdateRecord(detached, tableID, recordID, intent.Values);
        return new(project.FormID, response.FormVersionID, response.ResponseID, response.Revision,
            Array.AsReadOnly(bindings.Select(binding => binding.BindingID).ToArray()), intent);
    }
}
