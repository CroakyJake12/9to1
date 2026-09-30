using System.Text.Json;
using Haven.Core;

namespace HavenOS.Home.Core;

/// <summary>Lossless style definitions from the owning Notes document, retaining document-local IDs.
/// Version 1 describes this shared projection schema; freshness uses the exact Files and owning revision tokens.</summary>
public static class HomeNotesStyleProjection
{
    public static IReadOnlyList<HomeProductivityStyle> Project(NotesDocument document, HomeProductivityContext context,
        Guid backingFileId, Guid filesRevisionId, string owningRevision)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        if (document.Id == Guid.Empty || !Guid.TryParse(context.ArtifactId, out var artifactId) || artifactId != document.Id ||
            backingFileId == Guid.Empty || filesRevisionId == Guid.Empty || string.IsNullOrWhiteSpace(owningRevision) ||
            document.Styles is null || document.Styles.Count > 500 || document.Styles.Any(style => style is null ||
                string.IsNullOrWhiteSpace(style.Id) || style.Character is null || style.Paragraph is null) ||
            document.Styles.Select(style => style.Id).Distinct(StringComparer.Ordinal).Count() != document.Styles.Count)
            throw new InvalidDataException("Notes styles require exact owning document/File identities, revisions and unique local style IDs.");
        return Array.AsReadOnly(document.Styles.Select(style => new HomeProductivityStyle(style.Id, 1, style.Name,
            HomeProductivityEngine.StyleScope(context), JsonSerializer.SerializeToElement(style))
        {
            BasedOnStyleIds = string.IsNullOrWhiteSpace(style.BasedOn) ? [] : Array.AsReadOnly(new[] { style.BasedOn }),
            Source = new(context.AppId, context.ArtifactId, backingFileId, filesRevisionId, owningRevision)
        }).ToArray());
    }
}
