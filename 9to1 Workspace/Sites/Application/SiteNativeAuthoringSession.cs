using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Sites.Application;

public sealed record SiteNativeAuthoringPage(IReadOnlyList<SiteProject> Projects);
public sealed record SiteNativeAuthoringOutcome(SiteApiResult<SiteProject>? Result,
    bool AwaitingHomeReview, bool AuditRecorded, string Message);

/// <summary>Native semantic authoring over the SAME explicit Files workspace and Home broker.
/// Displayed records are observations; only actual Home admission and the maintained Sites
/// coordinator may write. No caller root, identity, provider or replacement intent is accepted.</summary>
public sealed class SiteNativeAuthoringSession(ISiteNativeWorkspaceAuthority workspaces,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
    HomeResourceOperationBroker broker)
{
    private sealed record IssuedPage(SiteNativeWorkspaceBinding Binding, AuthenticatedResourceActor Actor,
        IReadOnlyDictionary<Guid, string> OriginalProjectJson);
    private readonly ConditionalWeakTable<SiteNativeAuthoringPage, IssuedPage> _pages = new();
    private readonly ConditionalWeakTable<PreparedEdit, object> _edits = new();

    public Task<SiteNativeAuthoringPage> ReadAsync(AuthenticatedResourceActor originalActor,
        CancellationToken token = default) => PublishAsync(() => ReadCoreAsync(originalActor, token));

    private async Task<SiteNativeAuthoringPage> ReadCoreAsync(AuthenticatedResourceActor actor, CancellationToken token)
    {
        var binding = await RequireBindingAsync(actor, token).ConfigureAwait(false);
        if (await resources.AuthorizeForActorAsync(actor, "files.browser.read",
            [new("files.item", binding.FilesFolderId.ToString(), binding.FolderRevision, ResourceAccess.Read)], token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The original actor cannot read the configured Sites folder.");
        var store = new FileSiteWorkspaceStore(binding.RootDirectory);
        var projects = await ObserveAsync(store.ReadAsync(state => state.Projects.ToArray(), token)).ConfigureAwait(false);
        if (projects.Length > 1000) throw new InvalidOperationException("The Sites index exceeds this native view's bounded capacity.");
        var visible = new List<SiteProject>();
        foreach (var project in projects)
        {
            if (project.Source.FilesDirectoryId == binding.FilesFolderId &&
                await resources.AuthorizeForActorAsync(actor, "sites.project.read",
                    [ProjectReadScope(project)], token).ConfigureAwait(false) == actor)
                visible.Add(project);
        }
        await RequireSameBindingAsync(binding, actor, token).ConfigureAwait(false);
        var page = new SiteNativeAuthoringPage(Array.AsReadOnly(visible.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.SiteId).ToArray()));
        _pages.Add(page, new(binding, actor, page.Projects.ToDictionary(item => item.SiteId, Json)));
        return page;
    }

    public Task RevalidateAsync(SiteNativeAuthoringPage samePage, AuthenticatedResourceActor originalActor,
        CancellationToken token = default) => RevalidateCoreAsync(samePage, originalActor, token);

    private async Task RevalidateCoreAsync(SiteNativeAuthoringPage page, AuthenticatedResourceActor actor, CancellationToken token)
    {
        var original = RequirePage(page, actor);
        await RequireSameBindingAsync(original.Binding, actor, token).ConfigureAwait(false);
        if (page.Projects.Count != original.OriginalProjectJson.Count || page.Projects.Any(project =>
            !original.OriginalProjectJson.TryGetValue(project.SiteId, out var json) || Json(project) != json))
            throw new UnauthorizedAccessException("The privately issued Sites observation was copied or changed.");
        var projects = new SiteProjectService(new FileSiteWorkspaceStore(original.Binding.RootDirectory));
        foreach (var displayed in page.Projects)
        {
            var current = await ObserveAsync(projects.GetProjectAsync(displayed.SiteId, token)).ConfigureAwait(false);
            if (!current.IsSuccess || current.Value is null || Json(current.Value) != original.OriginalProjectJson[displayed.SiteId]
                || await resources.AuthorizeForActorAsync(actor, "sites.project.read", [ProjectReadScope(displayed)], token).ConfigureAwait(false) != actor)
                throw new InvalidOperationException("The displayed Sites revision changed. Refresh before editing.");
        }
        await RequireSameBindingAsync(original.Binding, actor, token).ConfigureAwait(false);
    }

    public sealed class PreparedEdit
    {
        internal readonly SiteNativeAuthoringSession Issuer;
        internal readonly SiteNativeAuthoringPage Page;
        internal readonly AuthenticatedResourceActor Actor;
        internal readonly SiteNativeWriteIntent Intent;
        internal readonly Func<bool> Lifetime;
        internal readonly SiteNativeWriteCoordinator Coordinator;
        internal readonly object Gate = new();
        internal Task<SiteNativeAuthoringOutcome>? ActualApply;
        internal HomeResourceExecutionCapability? Capability;
        internal SiteNativeWriteAuditPendingException? TerminalAudit;
        internal SiteNativeAdmissionAuditPendingException? AdmissionAudit;
        internal SiteApiResult<SiteProject>? ActualResult;
        internal bool Retired;
        internal int Attempts;
        internal PreparedEdit(SiteNativeAuthoringSession issuer, SiteNativeAuthoringPage page,
            AuthenticatedResourceActor actor, SiteNativeWriteIntent intent, Func<bool> lifetime,
            SiteNativeWriteCoordinator coordinator, HomePermissionAuthorization approval, string description)
        { Issuer = issuer; Page = page; Actor = actor; Intent = intent; Lifetime = lifetime;
            Coordinator = coordinator; Approval = approval; Description = description; }
        public HomePermissionAuthorization Approval { get; }
        public string Description { get; }
        public SiteApiResult<SiteProject>? ObservedResult { get { lock (Gate) return ActualResult; } }
        public bool HasAuditRecovery { get { lock (Gate) return TerminalAudit is not null || AdmissionAudit is not null; } }
    }

    public Task<PreparedEdit> PrepareAsync(SiteNativeAuthoringPage samePage, SiteProject? sameProject,
        SitePage? sameSitePage, SiteComponent? sameComponent, AuthenticatedResourceActor originalActor,
        string operation, string name, string path, string text, Func<bool> originalLifetime, string sessionId,
        CancellationToken token = default) => PublishAsync(() => PrepareCoreAsync(samePage, sameProject, sameSitePage,
            sameComponent, originalActor, operation, name, path, text, originalLifetime, sessionId, token));

    private async Task<PreparedEdit> PrepareCoreAsync(SiteNativeAuthoringPage page, SiteProject? project,
        SitePage? selectedPage, SiteComponent? component, AuthenticatedResourceActor actor,
        string operation, string name, string path, string text, Func<bool> lifetime, string sessionId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!lifetime() || string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 256 || text.Length > 65536
            || name.Length > 120 || path.Length > 256)
            throw new ArgumentException("Retain the live Sites view and bounded original edit.");
        var source = RequirePage(page, actor);
        await RevalidateCoreAsync(page, actor, token).ConfigureAwait(false);
        if (operation != "create" && (project is null || !page.Projects.Any(item => ReferenceEquals(item, project))))
            throw new UnauthorizedAccessException("Select a project from the SAME privately issued Sites page.");
        if ((operation is "heading" or "text") && (selectedPage is null || !project!.Pages.Any(item => ReferenceEquals(item, selectedPage))))
            throw new UnauthorizedAccessException("Select an original page in this project.");
        if (operation == "save-text" && (component is null || !project!.Components.Any(item => ReferenceEquals(item, component))
            || component.ComponentType is not ("heading" or "text")))
            throw new UnauthorizedAccessException("Select an original heading or text component.");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["text"] = JsonSerializer.SerializeToElement(text) };
        if (operation == "heading") properties["level"] = JsonSerializer.SerializeToElement(2);
        SiteNativeWriteIntent intent = operation switch
        {
            "create" => SiteNativeWriteIntent.Create(source.Binding, name, "9to1-native", path),
            "page" => SiteNativeWriteIntent.CreatePage(source.Binding, project!.SiteId, project.Revision, name, path),
            "heading" or "text" => SiteNativeWriteIntent.AddComponent(source.Binding, project!.SiteId,
                project.Revision, selectedPage!.PageId, null, operation == "heading" ? "heading" : "text", properties),
            "save-text" => SiteNativeWriteIntent.UpdateProperties(source.Binding, project!.SiteId, project.Revision,
                component!.ComponentId, component.Properties.ToDictionary(pair => pair.Key,
                    pair => pair.Key == "text" ? JsonSerializer.SerializeToElement(text) : pair.Value.Clone(), StringComparer.Ordinal)
                    .AppendIfMissingText(text)),
            _ => throw new ArgumentException("Choose an implemented Sites edit.")
        };
        var description = operation switch { "create" => "Create project: " + name, "page" => "Create page: " + name,
            "save-text" => "Save selected text", _ => "Add " + operation };
        var approval = await ObserveAsync(broker.AuthorizeForActorAsync(actor, "sites", intent.ActionId,
            intent.Scopes, intent.Arguments, description, null, sessionId, token)).ConfigureAwait(false);
        await RevalidateCoreAsync(page, actor, token).ConfigureAwait(false);
        if (!lifetime()) throw new ObjectDisposedException("Original Sites view");
        var coordinator = new SiteNativeWriteCoordinator(new OriginalWorkspace(this, source.Binding, actor, lifetime), broker);
        var prepared = new PreparedEdit(this, page, actor, intent, lifetime, coordinator, approval, description);
        _edits.Add(prepared, new object()); return prepared;
    }

    public Task<SiteNativeAuthoringOutcome> ApplyAsync(PreparedEdit sameOriginal, CancellationToken token = default)
    {
        DemandEdit(sameOriginal);
        lock (sameOriginal.Gate)
        {
            if (sameOriginal.ActualApply is { } previous && !(previous.IsCompletedSuccessfully && previous.Result.AwaitingHomeReview)) return previous;
            if (sameOriginal.Retired || sameOriginal.Capability is not null || sameOriginal.Attempts++ >= 32)
                throw new InvalidOperationException("The original Sites edit cannot acquire another effect admission.");
            return sameOriginal.ActualApply = PublishAsync(() => ApplyCoreAsync(sameOriginal, token));
        }
    }

    private async Task<SiteNativeAuthoringOutcome> ApplyCoreAsync(PreparedEdit original, CancellationToken token)
    {
        if (!original.Lifetime() || original.Retired) throw new ObjectDisposedException("Original Sites view");
        await RevalidateCoreAsync(original.Page, original.Actor, token).ConfigureAwait(false);
        var capability = await ObserveAsync(broker.BeginExecutionCapabilityAsync(original.Approval.RequestId,
            original.Intent.Arguments, token)).ConfigureAwait(false);
        if (capability is null) return new(null, true, false, "Home has not admitted this edit. Check its decision before Apply.");
        lock (original.Gate) original.Capability = capability;
        var actual = original.Coordinator.ExecuteAsync(original.Intent, capability, token);
        try
        {
            var result = await actual.ConfigureAwait(false);
            lock (original.Gate) original.ActualResult = result;
            return new(result, false, true, result.IsSuccess ? "Sites saved." : result.Error?.Message ?? "The Sites edit was rejected.");
        }
        catch when (actual.IsFaulted)
        {
            var faults = actual.Exception!.Flatten().InnerExceptions;
            lock (original.Gate)
            {
                var terminal = faults.OfType<SiteNativeWriteAuditPendingException>().Distinct().ToArray();
                var admission = faults.OfType<SiteNativeAdmissionAuditPendingException>().Distinct().ToArray();
                original.TerminalAudit = terminal.Length == 1 && admission.Length == 0 ? terminal[0] : null;
                original.AdmissionAudit = admission.Length == 1 && terminal.Length == 0 ? admission[0] : null;
                // This typed ACK belongs to the SAME actual coordinator task, before its audit failed.
                if (original.TerminalAudit?.Result is { } result) original.ActualResult = result;
            }
            throw actual.Exception!;
        }
    }

    /// <summary>Only the SAME actual coordinator's retained audit; no owner lookup or effect replay.</summary>
    public Task<SiteApiResult<SiteProject>?> RetryAuditAsync(PreparedEdit sameOriginal, CancellationToken token = default)
    {
        DemandEdit(sameOriginal);
        return PublishAsync(async () =>
        {
            SiteNativeWriteAuditPendingException? terminal; SiteNativeAdmissionAuditPendingException? admission;
            lock (sameOriginal.Gate) { terminal = sameOriginal.TerminalAudit; admission = sameOriginal.AdmissionAudit; }
            if (terminal is not null)
            {
                var result = await ObserveAsync(sameOriginal.Coordinator.RetryAuditAsync(terminal, token)).ConfigureAwait(false);
                lock (sameOriginal.Gate) sameOriginal.TerminalAudit = null;
                return result;
            }
            if (admission is not null)
            {
                await ObserveAsync(sameOriginal.Coordinator.RetryAdmissionAuditAsync(admission, token)).ConfigureAwait(false);
                return null; // Its preserved original admission failure normally throws.
            }
            throw new InvalidOperationException("No actual Sites audit recovery is retained.");
        });
    }

    public void RetireReview(PreparedEdit sameOriginal)
    {
        DemandEdit(sameOriginal);
        lock (sameOriginal.Gate)
        {
            if (sameOriginal.Capability is not null || sameOriginal.ActualApply is { IsCompleted: false })
                throw new InvalidOperationException("The original execution must settle before review retirement.");
            sameOriginal.Retired = true;
        }
    }

    public Task<SiteRenderResult> PreviewAsync(SiteNativeAuthoringPage samePage, SiteProject sameProject,
        SitePage sameSitePage, AuthenticatedResourceActor actor, CancellationToken token = default) => PublishAsync(async () =>
    {
        await RevalidateCoreAsync(samePage, actor, token).ConfigureAwait(false);
        if (!samePage.Projects.Any(item => ReferenceEquals(item, sameProject)) || !sameProject.Pages.Any(item => ReferenceEquals(item, sameSitePage)))
            throw new UnauthorizedAccessException("Retain the original project and page for preview.");
        return new SiteDocumentRenderer().Render(sameProject, sameSitePage.PageId, SiteRenderContext.PagePreview);
    });

    private IssuedPage RequirePage(SiteNativeAuthoringPage page, AuthenticatedResourceActor actor) =>
        _pages.TryGetValue(page, out var original) && original.Actor == actor ? original
            : throw new UnauthorizedAccessException("Retain the SAME privately issued Sites page and original actor.");
    private void DemandEdit(PreparedEdit original)
    {
        if (!ReferenceEquals(original.Issuer, this) || !_edits.TryGetValue(original, out _))
            throw new UnauthorizedAccessException("Only the SAME privately prepared Sites edit is available.");
    }
    private async Task<SiteNativeWorkspaceBinding> RequireBindingAsync(AuthenticatedResourceActor actor, CancellationToken token)
    {
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The original Sites actor changed.");
        var current = await ObserveAsync(workspaces.GetCurrentAsync(token)).ConfigureAwait(false);
        if (current is null || current.ProfileId != actor.ProfileId || current.ActorId != actor.ActorId
            || current.AuthenticationRevision != actor.AuthenticationRevision || current.FilesFolderId == Guid.Empty
            || !Directory.Exists(current.RootDirectory) || await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("Set up the actual Sites folder in Home before opening Sites.");
        return current;
    }
    private async Task RequireSameBindingAsync(SiteNativeWorkspaceBinding original, AuthenticatedResourceActor actor, CancellationToken token)
    { if (await RequireBindingAsync(actor, token).ConfigureAwait(false) != original) throw new UnauthorizedAccessException("The original Files/Sites binding changed."); }
    private sealed class OriginalWorkspace(SiteNativeAuthoringSession issuer, SiteNativeWorkspaceBinding original,
        AuthenticatedResourceActor actor, Func<bool> lifetime) : ISiteNativeWorkspaceAuthority
    {
        public async Task<SiteNativeWorkspaceBinding?> GetCurrentAsync(CancellationToken token = default)
        {
            if (!lifetime()) return null;
            var current = await issuer.RequireBindingAsync(actor, token).ConfigureAwait(false);
            return lifetime() && current == original ? current : null;
        }
    }
    private static ResourceScope ProjectReadScope(SiteProject project) => new("sites.project", project.SiteId.ToString(),
        project.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Read);
    private static string Json(SiteProject project) => JsonSerializer.Serialize(project);
    private static Task<T> PublishAsync<T>(Func<Task<T>> operation)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = RunAsync(start.Task, operation); start.SetResult(); return actual;
        static async Task<T> RunAsync(Task start, Func<Task<T>> operation)
        { await start.ConfigureAwait(false); return await ObserveAsync(operation()).ConfigureAwait(false); }
    }
    private static async Task<T> ObserveAsync<T>(Task<T> actual)
    { try { return await actual.ConfigureAwait(false); } catch when (actual.IsFaulted) { throw actual.Exception!; } }
    private static async Task ObserveAsync(Task actual)
    { try { await actual.ConfigureAwait(false); } catch when (actual.IsFaulted) { throw actual.Exception!; } }
}

internal static class SiteNativeTextProperties
{
    internal static IReadOnlyDictionary<string, JsonElement> AppendIfMissingText(this Dictionary<string, JsonElement> properties, string text)
    { properties["text"] = JsonSerializer.SerializeToElement(text); return properties; }
}
