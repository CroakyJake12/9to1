using System.Text.Json;

namespace Haven.Core.Forms;

/// <summary>An immutable publication envelope over the canonical authored project's typed JSON projection.
/// It preserves the full project, including newer component configuration, rather than a runtime-only copy.</summary>
public sealed record FormPublishedVersion(Guid FormID, Guid FormVersionID, long SourceRevision,
    DateTimeOffset PublishedAt, JsonElement Project);
public enum FormPublicationState { Draft, Published, Closed }
public sealed record FormPublication(Guid FormID, long Revision, JsonElement Draft,
    IReadOnlyList<FormPublishedVersion> Versions, Guid? ActiveVersionID, FormPublicationState State, int SchemaVersion = 1);

public static class FormPublicationValidation
{
    public static void Validate(FormPublication publication)
    {
        if (publication.SchemaVersion != 1 || publication.FormID == Guid.Empty || publication.Revision < 0 || !Enum.IsDefined(publication.State)
            || publication.Versions is null || publication.Versions.Count > 10000)
            throw new InvalidDataException("Invalid form publication identity, revision or versions.");
        ValidateProject(publication.FormID, publication.Draft);
        var ids = new HashSet<Guid>();
        foreach (var version in publication.Versions)
        {
            if (version is null || version.FormID != publication.FormID || version.FormVersionID == Guid.Empty
                || !ids.Add(version.FormVersionID) || version.SourceRevision < 0 || version.SourceRevision >= publication.Revision
                || version.PublishedAt == default)
                throw new InvalidDataException("Invalid immutable form version.");
            ValidateProject(publication.FormID, version.Project);
        }
        if (publication.ActiveVersionID is { } active && !ids.Contains(active)
            || publication.State == FormPublicationState.Published && publication.ActiveVersionID is null)
            throw new InvalidDataException("Invalid active form version.");
    }

    public static void ValidateProject(Guid formID, JsonElement project)
    {
        if (project.ValueKind != JsonValueKind.Object || !project.TryGetProperty("FormID", out var identity)
            || identity.ValueKind != JsonValueKind.String || !identity.TryGetGuid(out var actual) || actual != formID)
            throw new InvalidDataException("Project projection must retain its canonical FormID.");
    }
}
