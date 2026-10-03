using System.Globalization;
using Haven.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Runtime;

namespace HavenOS.Apps.Sites.Application;

public enum SitePreviewState { Building, Live, BuildFailed, Disconnected, Cancelled, Superseded }

public sealed record SitePreviewRequest(Guid SiteID, Guid PageID, long ExpectedProjectRevision);

/// <summary>Rendered revision remains explicit when current diagnostics accompany an older valid document.</summary>
public sealed record SitePreviewSnapshot(Guid SiteID, Guid? ProjectID, Guid PageID,
    long RequestedProjectRevision, long? ProjectRevision, string? SourceRevision, SitePreviewState State,
    string? Document, long? RenderedProjectRevision, IReadOnlyList<SiteDiagnostic> Diagnostics,
    bool IsRetainedLastValid);

/// <summary>Reads canonical projects, never a divergent preview project or generated artifact.</summary>
public interface ISitePreviewProjectSource
{
    Task<SiteApiResult<SiteProject>> ReadAsync(Guid siteID, CancellationToken cancellationToken);
}

public sealed class CanonicalSitePreviewProjectSource(SiteProjectService projects) : ISitePreviewProjectSource
{
    public Task<SiteApiResult<SiteProject>> ReadAsync(Guid siteID, CancellationToken cancellationToken)
        => projects.GetProjectAsync(siteID, cancellationToken);
}

/// <summary>
/// One viewing session over the canonical Sites renderer. Cached output never grants access:
/// current ambient actor and owning read authority are checked before rendering, publication and retrieval.
/// The host disposes/clears this derived state on close; no preview state is canonical persistence.
/// </summary>
public sealed class SitePreviewService(ISitePreviewProjectSource projects,
    ResourceAuthorizationService authorization, SiteDocumentRenderer renderer)
{
    public SitePreviewService(SiteProjectService projects, ResourceAuthorizationService authorization,
        SiteDocumentRenderer renderer) : this(new CanonicalSitePreviewProjectSource(projects), authorization, renderer) { }

    private sealed record PreviewIdentity(AuthenticatedResourceActor Actor, Guid SiteID, Guid ProjectID,
        Guid PageID, SiteSourceBinding Source);
    private sealed record ValidPreview(PreviewIdentity Identity, string Document, long ProjectRevision);
    private sealed record CurrentPreview(PreviewIdentity Identity, ResourceScope Scope, SitePreviewSnapshot Snapshot);

    private readonly object gate = new();
    private long generation;
    private ValidPreview? lastValid;
    private CurrentPreview? current;

    public void Clear()
    {
        lock (gate) { generation++; current = null; lastValid = null; }
    }

    /// <summary>Rechecks access even when only displaying a previously rendered document.</summary>
    public async Task<SitePreviewSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        CurrentPreview? captured;
        long selected;
        lock (gate) { captured = current; selected = generation; }
        if (captured is null) return null;
        var actor = await authorization.AuthorizeForActorAsync(captured.Identity.Actor, "sites.project.read",
            [captured.Scope], cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            if (selected != generation || current != captured) return null;
            if (actor is null)
            {
                lastValid = null;
                current = null;
                return WithoutDocument(captured.Snapshot, SitePreviewState.Disconnected,
                    "PreviewAccessUnavailable", "Current project read access is unavailable.");
            }
            return captured.Snapshot;
        }
    }

    public async Task<SitePreviewSnapshot> RefreshAsync(SitePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SiteID == Guid.Empty || request.PageID == Guid.Empty || request.ExpectedProjectRevision < 1)
            throw new ArgumentException("Preview requires stable SiteID/PageID and a positive project revision.", nameof(request));
        long selected;
        lock (gate) { selected = ++generation; current = null; }
        var empty = new SitePreviewSnapshot(request.SiteID, null, request.PageID,
            request.ExpectedProjectRevision, null, null, SitePreviewState.Building, null, null, [], false);
        try
        {
            var found = await projects.ReadAsync(request.SiteID, cancellationToken).ConfigureAwait(false);
            if (!IsSelected(selected)) return Superseded(empty);
            if (found.Error is not null || found.Value is not { } project || project.SiteId != request.SiteID ||
                project.ProjectId == Guid.Empty || project.Source.FilesDirectoryId == Guid.Empty)
                return Disconnect(selected, empty, "PreviewSourceUnavailable", "The canonical preview project is unavailable.");

            var scope = new ResourceScope("sites.project", project.SiteId.ToString("D"),
                project.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read);
            var actor = await authorization.AuthorizeAsync("sites.project.read", [scope], cancellationToken).ConfigureAwait(false);
            if (!IsSelected(selected)) return Superseded(empty);
            if (actor is null)
                return Disconnect(selected, empty, "PreviewAccessUnavailable", "Current project read access is unavailable.");

            var identity = new PreviewIdentity(actor, project.SiteId, project.ProjectId, request.PageID, project.Source);
            SitePreviewSnapshot building;
            lock (gate)
            {
                if (selected != generation) return Superseded(empty);
                if (lastValid?.Identity != identity) lastValid = null;
                building = empty with { ProjectID = project.ProjectId, ProjectRevision = project.Revision,
                    SourceRevision = project.Source.SourceRevision, Document = lastValid?.Document,
                    RenderedProjectRevision = lastValid?.ProjectRevision, IsRetainedLastValid = lastValid is not null };
                current = new(identity, scope, building);
            }

            SiteRenderResult? rendered = null;
            IReadOnlyList<SiteDiagnostic> diagnostics;
            if (project.Revision != request.ExpectedProjectRevision)
                diagnostics = [new("RevisionConflict", "The canonical project changed; refresh the selected revision.", request.PageID, null, true)];
            else if (!string.Equals(project.Source.FrameworkId, "9to1-native", StringComparison.Ordinal))
                diagnostics = [new("PreviewProviderUnavailable", "This preview provider supports Sites-native canonical projects.", request.PageID, null, true)];
            else
            {
                try
                {
                    rendered = renderer.Render(project, request.PageID, SiteRenderContext.PagePreview);
                    diagnostics = rendered.Diagnostics.ToArray();
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException)
                {
                    diagnostics = [new("PreviewRenderFailed", "The canonical page could not be rendered. Review its structure and source.", request.PageID, null, true)];
                }
            }

            // The renderer runs from the loaded snapshot; publication still requires the same current actor/revision.
            var stillAllowed = await authorization.AuthorizeForActorAsync(actor, "sites.project.read", [scope],
                cancellationToken).ConfigureAwait(false);
            if (!IsSelected(selected)) return Superseded(building);
            if (stillAllowed is null)
                return Disconnect(selected, building, "PreviewAccessUnavailable", "Project read access or its canonical revision changed.");
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (selected != generation) return Superseded(building);
                var failed = rendered is null || diagnostics.Any(item => item.IsError);
                if (!failed) lastValid = new(identity, rendered!.Document, project.Revision);
                var result = building with { State = failed ? SitePreviewState.BuildFailed : SitePreviewState.Live,
                    Document = lastValid?.Document, RenderedProjectRevision = lastValid?.ProjectRevision,
                    Diagnostics = diagnostics, IsRetainedLastValid = failed && lastValid is not null };
                current = new(identity, scope, result);
                return result;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Disconnect(selected, empty, "PreviewCancelled", "The preview refresh was cancelled.", SitePreviewState.Cancelled);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SiteOperationException)
        {
            return Disconnect(selected, empty, "PreviewSourceUnavailable", "The canonical source or its current read authority is unavailable.");
        }
    }

    private bool IsSelected(long selected) { lock (gate) return generation == selected; }

    private SitePreviewSnapshot Disconnect(long selected, SitePreviewSnapshot snapshot, string code, string message,
        SitePreviewState state = SitePreviewState.Disconnected)
    {
        lock (gate)
        {
            if (generation != selected) return Superseded(snapshot);
            lastValid = null;
            current = null;
            return WithoutDocument(snapshot, state, code, message);
        }
    }

    private static SitePreviewSnapshot Superseded(SitePreviewSnapshot snapshot)
        => snapshot with { State = SitePreviewState.Superseded, Document = null, RenderedProjectRevision = null,
            Diagnostics = [], IsRetainedLastValid = false };

    private static SitePreviewSnapshot WithoutDocument(SitePreviewSnapshot snapshot, SitePreviewState state,
        string code, string message)
        => snapshot with { State = state, Document = null, RenderedProjectRevision = null,
            Diagnostics = [new(code, message, snapshot.PageID, null, true)], IsRetainedLastValid = false };
}
