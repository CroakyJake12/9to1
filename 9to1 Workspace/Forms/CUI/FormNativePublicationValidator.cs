using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;

namespace HavenOS.Forms;

/// <summary>Publication capabilities of the actual owning native renderer and canonical runtime.
/// Home authority remains independently mandatory; declared project policy never grants respondent access.</summary>
public sealed class FormNativePublicationValidator(IFormNativeMathematicsProvider? mathematics = null) : IFormProjectPublicationValidator
{
    public void Validate(Guid formID, JsonElement canonicalProject) => _ = Decode(formID, canonicalProject);

    public void ValidateForPublication(Guid formID, JsonElement canonicalProject)
    {
        var project = Decode(formID, canonicalProject);
        if (project.AccessPolicy.Respondents != FormRespondentAccess.OwnerOnly || project.AccessPolicy.AudienceIDs.Count != 0)
            throw new NotSupportedException("CapabilityUnavailable: a registered shared respondent authority is required.");
        if (project.DataBindings.Count != 0)
            throw new NotSupportedException("CapabilityUnavailable: a registered typed Data commit coordinator is required.");
        if (project.MarkingScheme.Release is FormResultRelease.AfterReview or FormResultRelease.ConfiguredState
            || project.Fields.Any(field => field.Assessment?.Release is FormResultRelease.AfterReview or FormResultRelease.ConfiguredState))
            throw new NotSupportedException("CapabilityUnavailable: a registered review or state release workflow is required.");
        // This invokes the same renderer admission and typed runtime used by interactive preview,
        // rather than maintaining a separate optimistic list of renderable fields.
        using var preview = mathematics is null ? new FormNativePreview(project) : new FormNativePreview(project, mathematics);
    }

    private static FormProject Decode(Guid formID, JsonElement canonicalProject)
    {
        var project = FormProjectCodec.Decode(Encoding.UTF8.GetBytes(canonicalProject.GetRawText()));
        if (project.FormID != formID) throw new InvalidDataException("Form identity does not match its publication.");
        return project;
    }
}
