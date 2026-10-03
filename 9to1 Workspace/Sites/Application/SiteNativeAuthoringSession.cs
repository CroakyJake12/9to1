using System.Globalization;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Sites.Application;

public sealed record SiteAuthoringView(IReadOnlyList<SiteProject> Projects, SiteProject? Project,
    Guid? PageID, SitePreviewSnapshot? Preview, string Message, SiteNativeWorkspaceBinding? WorkspaceBinding = null);

/// <summary>One native view over the current canonical Files binding. UI snapshots and cached HTML grant no authority.</summary>
public sealed class SiteNativeAuthoringSession(ISiteNativeWorkspaceAuthority workspace,
    ResourceAuthorizationService authorization, HomeResourceOperationBroker home,
    SiteNativeWriteCoordinator writes, Func<string, AuthenticatedResourceActor, CancellationToken, Task> reviewHomeRequest) : IDisposable
{
    private readonly object gate = new();
    private long generation;
    private bool disposed;
    private int writing;
    private readonly string sessionID = Guid.NewGuid().ToString("N");
    private SiteNativeWorkspaceBinding? binding;
    private SitePreviewService? preview;
    private SiteAuthoringView view = new([], null, null, null, "Open a project or create a website.");
    private SiteNativeWriteAuditPendingException? auditPending;
    private SiteNativeAdmissionAuditPendingException? admissionAuditPending;

    public bool HasPendingAudit { get { lock (gate) return auditPending is not null || admissionAuditPending is not null; } }

    public async Task<SiteAuthoringView> RefreshAsync(Guid? siteID = null, Guid? pageID = null,
        CancellationToken cancellationToken = default)
    {
        long selected;
        lock (gate) { ThrowIfDisposed(); selected = ++generation; }
        try
        {
            var currentBinding = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (currentBinding is null) return Disconnect(selected, "Choose a current Sites folder in Files.");
            async Task CheckBinding(CancellationToken ct)
            {
                if (await workspace.GetCurrentAsync(ct).ConfigureAwait(false) != currentBinding)
                    throw new UnauthorizedAccessException("The canonical Sites folder or local profile changed.");
            }
            var store = new FileSiteWorkspaceStore(currentBinding.RootDirectory, CheckBinding);
            var projects = new SiteProjectService(store);
            var candidates = await store.ReadAsync(state => state.Projects.ToArray(), cancellationToken).ConfigureAwait(false);
            var allowed = new List<SiteProject>();
            foreach (var project in candidates)
            {
                if (project.Source.FilesDirectoryId != currentBinding.FilesFolderId) continue;
                var actor = await authorization.AuthorizeAsync("sites.project.read", [ReadScope(project)], cancellationToken).ConfigureAwait(false);
                if (actor is not null && Matches(actor, currentBinding)) allowed.Add(project);
            }
            await CheckBinding(cancellationToken).ConfigureAwait(false);
            SitePreviewService activePreview;
            Guid? preferredSite;
            Guid? preferredPage;
            lock (gate)
            {
                if (disposed || selected != generation) return Empty("A newer Sites view replaced this refresh.");
                if (binding != currentBinding) { preview?.Clear(); preview = null; view = Empty("The source changed."); }
                binding = currentBinding;
                activePreview = preview ??= new SitePreviewService(projects, authorization, new SiteDocumentRenderer());
                preferredSite = siteID ?? view.Project?.SiteId;
                preferredPage = pageID ?? view.PageID;
            }
            var ordered = allowed.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase).ThenBy(project => project.SiteId).ToArray();
            var current = preferredSite is { } explicitSite
                ? ordered.SingleOrDefault(project => project.SiteId == explicitSite) : ordered.FirstOrDefault();
            SitePreviewSnapshot? rendered = null;
            Guid? selectedPage = null;
            if (current is not null)
            {
                var page = current.Pages.SingleOrDefault(page => page.PageId == preferredPage) ?? current.Pages.FirstOrDefault();
                selectedPage = page?.PageId;
                if (page is not null)
                    rendered = await activePreview.RefreshAsync(new(current.SiteId, page.PageId, current.Revision), cancellationToken).ConfigureAwait(false);
                else activePreview.Clear();
            }
            else activePreview.Clear();
            await CheckBinding(cancellationToken).ConfigureAwait(false);
            // Recheck the exact current owning record before exposing project names/structure to the UI.
            foreach (var project in ordered)
                if (await authorization.AuthorizeAsync("sites.project.read", [ReadScope(project)], cancellationToken).ConfigureAwait(false) is not { } actor || !Matches(actor, currentBinding))
                    return Disconnect(selected, "Project read access changed. Refresh Sites.");
            await CheckBinding(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (disposed || selected != generation) return Empty("A newer Sites view replaced this refresh.");
                return view = new(ordered, current, selectedPage, rendered,
                    current is null ? "Create a website to begin." : selectedPage is null ? "Add a page to preview this website." : Describe(rendered), currentBinding);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Disconnect(selected, "The Sites refresh was cancelled."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SiteOperationException)
        { return Disconnect(selected, "The current canonical Sites source or read access is unavailable."); }
    }

    /// <summary>Call immediately before displaying cached output, including while no edit is occurring.</summary>
    public async Task<SiteAuthoringView> ReadCurrentAsync(CancellationToken cancellationToken = default)
    {
        SiteNativeWorkspaceBinding? capturedBinding; SitePreviewService? capturedPreview; SiteAuthoringView captured; long selected;
        lock (gate) { ThrowIfDisposed(); capturedBinding = binding; capturedPreview = preview; captured = view; selected = generation; }
        if (capturedBinding is null || await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != capturedBinding)
            return Disconnect(selected, "The Sites folder or local profile changed.");
        foreach (var project in captured.Projects)
            if (await authorization.AuthorizeAsync("sites.project.read", [ReadScope(project)], cancellationToken).ConfigureAwait(false) is not { } actor || !Matches(actor, capturedBinding))
                return Disconnect(selected, "Project read access changed. Refresh Sites.");
        var currentPreview = capturedPreview is null ? null : await capturedPreview.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (currentPreview?.State == SitePreviewState.Disconnected) return Disconnect(selected, "Project read access changed. Refresh Sites.");
        if (await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != capturedBinding)
            return Disconnect(selected, "The Sites folder or local profile changed.");
        lock (gate)
        {
            if (disposed || selected != generation || view != captured) return Empty("A newer Sites view replaced this refresh.");
            return view = captured with { Preview = currentPreview };
        }
    }

    public Task<SiteAuthoringView> CreateProjectAsync(string name, string relativePath, CancellationToken ct = default)
        => WriteAsync(current => SiteNativeWriteIntent.Create(current, name, "9to1-native", relativePath), "Create website “" + name + "” in the current Sites folder.", ct);

    public Task<SiteAuthoringView> CreatePageAsync(string name, string path, CancellationToken ct = default)
        => WriteAsync(current => { var project = SelectedProject(); return SiteNativeWriteIntent.CreatePage(current, project.SiteId, project.Revision, name, path); }, "Add page “" + name + "” to this website.", ct);

    public Task<SiteAuthoringView> AddComponentAsync(string type, IReadOnlyDictionary<string, JsonElement> properties, CancellationToken ct = default)
        => WriteAsync(current => { var project = SelectedProject(); Guid page; lock (gate) page = view.PageID ?? throw new InvalidOperationException("Select a page first."); return SiteNativeWriteIntent.AddComponent(current, project.SiteId, project.Revision, page, null, type, properties); }, "Add a " + type + " component to the selected page.", ct);

    public Task<SiteAuthoringView> UpdatePropertiesAsync(Guid componentID, IReadOnlyDictionary<string, JsonElement> properties, CancellationToken ct = default)
        => WriteAsync(current => { var project = SelectedProject(); if (!project.Components.Any(component => component.ComponentId == componentID)) throw new InvalidOperationException("Select a current component first."); return SiteNativeWriteIntent.UpdateProperties(current, project.SiteId, project.Revision, componentID, properties); }, "Save the selected component properties.", ct);

    private SiteProject SelectedProject() { lock (gate) { ThrowIfDisposed(); return view.Project ?? throw new InvalidOperationException("Select a website first."); } }

    private async Task<SiteAuthoringView> WriteAsync(Func<SiteNativeWorkspaceBinding, SiteNativeWriteIntent> capture, string impact, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref writing, 1, 0) != 0) throw new InvalidOperationException("A Sites write is already awaiting completion.");
        try
        {
            lock (gate) { ThrowIfDisposed(); if (auditPending is not null || admissionAuditPending is not null) throw new InvalidOperationException("Recover the prior Home audit before making another edit."); }
            var current = await workspace.GetCurrentAsync(ct).ConfigureAwait(false) ?? throw new UnauthorizedAccessException("The current Sites folder is unavailable.");
            lock (gate) if (binding is not null && binding != current) throw new UnauthorizedAccessException("Refresh the changed Sites folder before saving.");
            var intent = capture(current);
            var originalActor = new AuthenticatedResourceActor(current.ActorId, current.ProfileId, null, null, current.AuthenticationRevision);
            var approval = await home.AuthorizeForActorAsync(originalActor, "sites", intent.ActionId, intent.Scopes, intent.Arguments, impact, null, sessionID, ct).ConfigureAwait(false);
            if (approval.State == HomePermissionRequestState.PendingApproval)
                await reviewHomeRequest(approval.RequestId, originalActor, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            lock (gate) ThrowIfDisposed();
            var capability = await home.BeginExecutionCapabilityAsync(approval.RequestId, intent.Arguments, ct).ConfigureAwait(false);
            if (capability is null) throw new UnauthorizedAccessException("Home has not approved this exact current Sites edit.");
            SiteApiResult<SiteProject> result;
            try { result = await writes.ExecuteAsync(intent, capability, ct).ConfigureAwait(false); }
            catch (SiteNativeWriteAuditPendingException pending) { lock (gate) auditPending = pending; throw; }
            catch (SiteNativeAdmissionAuditPendingException pending) { lock (gate) admissionAuditPending = pending; throw; }
            if (!result.IsSuccess) throw new SiteOperationException(result.Error!);
            return await RefreshAsync(result.Value!.SiteId, cancellationToken: ct).ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref writing, 0); }
    }

    /// <summary>Owner-held recovery retries only the already observed audit; never repeats a mutation.</summary>
    public async Task<SiteAuthoringView> RetryAuditAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref writing, 1, 0) != 0) throw new InvalidOperationException("A Sites operation is already pending.");
        try
        {
            SiteNativeWriteAuditPendingException? pending; SiteNativeAdmissionAuditPendingException? rejected;
            lock (gate) { ThrowIfDisposed(); pending = auditPending; rejected = admissionAuditPending; }
            if (pending is not null)
            {
                try { await writes.RetryAuditAsync(pending, ct).ConfigureAwait(false); }
                catch (SiteNativeWriteAuditPendingException) { throw; }
                catch { lock (gate) auditPending = null; throw; }
                lock (gate) auditPending = null;
            }
            else if (rejected is not null)
            {
                try { await writes.RetryAdmissionAuditAsync(rejected, ct).ConfigureAwait(false); }
                catch (SiteNativeAdmissionAuditPendingException) { throw; }
                catch { lock (gate) admissionAuditPending = null; throw; }
                lock (gate) admissionAuditPending = null;
            }
            return await RefreshAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref writing, 0); }
    }

    public void SuspendPreview() { lock (gate) { generation++; view = view with { Preview = null }; } }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; generation++; preview?.Clear(); preview = null; view = Empty("Sites is closed."); } }
    private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(SiteNativeAuthoringSession)); }
    private SiteAuthoringView Disconnect(long selected, string message)
    {
        lock (gate) { if (selected != generation || disposed) return Empty("The Sites view changed."); preview?.Clear(); preview = null; binding = null; return view = Empty(message); }
    }
    private static SiteAuthoringView Empty(string message) => new([], null, null, null, message);
    private static bool Matches(AuthenticatedResourceActor actor, SiteNativeWorkspaceBinding expected) => actor.AccountId is null && actor.OrganisationId is null && actor.ActorId == expected.ActorId && actor.ProfileId == expected.ProfileId && actor.AuthenticationRevision == expected.AuthenticationRevision;
    private static ResourceScope ReadScope(SiteProject project) => new("sites.project", project.SiteId.ToString("D"), project.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read);
    private static string Describe(SitePreviewSnapshot? snapshot) => snapshot?.State switch
    {
        SitePreviewState.Live => "Preview is current.",
        SitePreviewState.BuildFailed when snapshot.IsRetainedLastValid => "The edit needs attention. Showing the last valid preview.",
        SitePreviewState.BuildFailed => "The edit needs attention before it can be previewed.",
        _ => "Preview is unavailable."
    };
}
