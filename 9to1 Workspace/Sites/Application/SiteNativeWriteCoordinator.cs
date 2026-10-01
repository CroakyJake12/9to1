using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Sites.Application;

/// <summary>Captured typed edit; no caller field supplies actor identity or grants authority.</summary>
public sealed class SiteNativeWriteIntent
{
    public const string TargetAppId = "sites";
    private readonly JsonElement _arguments;
    private readonly JsonElement _payload;
    private SiteNativeWriteIntent(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, string operation, JsonElement payload)
    {
        if (binding.FilesFolderId == Guid.Empty || string.IsNullOrWhiteSpace(binding.FolderRevision)) throw new ArgumentException("A current Files folder is required.");
        if (operation != "create" && (siteID == Guid.Empty || revision < 1)) throw new ArgumentException("The current Sites identity and revision are required.");
        Binding = binding; SiteID = siteID; Revision = revision; Operation = operation; _payload = payload.Clone();
        ActionId = operation == "create" ? "sites.project.create" : "sites.project.save";
        var scopes = new List<ResourceScope> { new("files.item", binding.FilesFolderId.ToString(), binding.FolderRevision, ResourceAccess.Write) };
        if (operation != "create") scopes.Add(new("sites.project", siteID.ToString(), revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write));
        Scopes = scopes.AsReadOnly();
        _arguments = JsonSerializer.SerializeToElement(new { operation, siteID, revision, filesFolderID = binding.FilesFolderId, folderRevision = binding.FolderRevision, payload = _payload });
    }
    internal SiteNativeWorkspaceBinding Binding { get; }
    internal JsonElement Payload => _payload;
    public Guid SiteID { get; }
    public long Revision { get; }
    public string Operation { get; }
    public string ActionId { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static SiteNativeWriteIntent Create(SiteNativeWorkspaceBinding binding, string name, string frameworkID, string relativePath)
        => new(binding, Guid.Empty, 0, "create", JsonSerializer.SerializeToElement(new { name, frameworkID, relativePath }));
    public static SiteNativeWriteIntent Rename(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, string name)
        => new(binding, siteID, revision, "rename", JsonSerializer.SerializeToElement(new { name }));
    public static SiteNativeWriteIntent CreatePage(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, string name, string path)
        => new(binding, siteID, revision, "page.create", JsonSerializer.SerializeToElement(new { name, path }));
    public static SiteNativeWriteIntent AddComponent(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid pageID, Guid? parentID, string type, IReadOnlyDictionary<string, JsonElement> properties)
        => new(binding, siteID, revision, "component.add", JsonSerializer.SerializeToElement(new { pageID, parentID, type, properties }));
    public static SiteNativeWriteIntent UpdateProperties(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid componentID, IReadOnlyDictionary<string, JsonElement> properties)
        => new(binding, siteID, revision, "component.properties", JsonSerializer.SerializeToElement(new { componentID, properties }));
    public static SiteNativeWriteIntent MoveComponent(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid componentID, Guid pageID, Guid? parentID, int position)
        => new(binding, siteID, revision, "component.move", JsonSerializer.SerializeToElement(new { componentID, pageID, parentID, position }));
    public static SiteNativeWriteIntent SetResponsiveOverride(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid componentID, string breakpoint, IReadOnlyDictionary<string, JsonElement>? properties)
        => new(binding, siteID, revision, "component.responsive", JsonSerializer.SerializeToElement(new { componentID, breakpoint, properties }));
}

/// <summary>Home approval is consumed by the owning Sites write, followed by its atomic revision transaction.
/// The canonical Files/profile binding is rechecked inside the store lock before the commit.</summary>
public sealed class SiteNativeWriteCoordinator(ISiteNativeWorkspaceAuthority workspace, HomeResourceOperationBroker home)
{
    public async Task<SiteApiResult<SiteProject>> ExecuteAsync(SiteNativeWriteIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        async Task CheckBinding(CancellationToken ct)
        {
            if (await workspace.GetCurrentAsync(ct).ConfigureAwait(false) != intent.Binding)
                throw new UnauthorizedAccessException("The canonical Sites Files/profile binding changed.");
        }
        try { await CheckBinding(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await RejectAdmissionAsync(capability, error, SiteNativeAdmissionAuditKind.UnclaimedAbort).ConfigureAwait(false);
            throw;
        }
        AuthenticatedResourceActor? actor;
        try
        {
            actor = await home.ClaimExecutionAsync(capability, SiteNativeWriteIntent.TargetAppId, intent.ActionId, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await RejectAdmissionAsync(capability, error, SiteNativeAdmissionAuditKind.RejectedClaim).ConfigureAwait(false);
            throw;
        }
        if (actor is null)
            await RejectAdmissionAsync(capability, new UnauthorizedAccessException("Home did not authorise this exact current Sites write."), SiteNativeAdmissionAuditKind.RejectedClaim).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null || actor.ActorId != intent.Binding.ActorId ||
            actor.ProfileId != intent.Binding.ProfileId || actor.AuthenticationRevision != intent.Binding.AuthenticationRevision)
        {
            var admissionFailure = ExceptionDispatchInfo.Capture(new UnauthorizedAccessException("Home did not authorise this exact current Sites write."));
            var rejected = new SiteNativeWriteAuditPendingException(this, capability,
                new(HomePermissionRequestState.Failed, "SitesAdmissionRejected", "The owning Sites binding rejected the claimed operation before any mutation.", []), null, admissionFailure);
            await RecordCompletionAsync(rejected).ConfigureAwait(false);
            admissionFailure.Throw();
        }
        var projects = new SiteProjectService(new FileSiteWorkspaceStore(intent.Binding.RootDirectory, CheckBinding));
        var authoring = new SiteAuthoringService(projects);
        var payload = intent.Payload;
        string Text(string name) => payload.GetProperty(name).GetString() ?? "";
        SiteApiResult<SiteProject>? result = null;
        ExceptionDispatchInfo? failure = null;
        try
        {
            result = await MutateAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An unexpected owner failure is not proof that no effect occurred.
            failure = ExceptionDispatchInfo.Capture(error);
        }
        var outcome = new HomeExecutionOutcome(
            failure is not null ? HomePermissionRequestState.PartiallyCompleted : result!.IsSuccess
                ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
            failure is not null ? "SitesWriteNeedsRecovery" : result!.IsSuccess ? "SitesWriteCommitted" : "SitesWriteRejected",
            failure is not null ? "The Sites write outcome requires inspection; do not repeat the mutation."
                : result!.IsSuccess ? "The canonical Sites revision was committed." : "The owning Sites write was rejected.",
            Array.AsReadOnly(capability.Scopes.Select(scope => new HomeObjectReference(scope.Kind, scope.Id)).ToArray()));
        var recovery = new SiteNativeWriteAuditPendingException(this, capability, outcome, result, failure);
        return await RecordCompletionAsync(recovery).ConfigureAwait(false);

        async Task<SiteApiResult<SiteProject>> MutateAsync() => intent.Operation switch
        {
            "create" => await projects.CreateProjectAsync(new(intent.Binding.FilesFolderId, null, null, intent.Binding.FolderRevision,
                Text("name"), Text("frameworkID"), Text("relativePath")), cancellationToken).ConfigureAwait(false),
            "rename" => await projects.UpdateProjectAsync(intent.SiteID, intent.Revision, project =>
                string.IsNullOrWhiteSpace(Text("name")) || Text("name").Trim().Length > 120
                    ? throw new SiteOperationException(new("InvalidInput", "Project name must have 1–120 characters.", "name", false))
                    : project with { Name = Text("name").Trim() }, cancellationToken).ConfigureAwait(false),
            "page.create" => await authoring.CreatePageAsync(intent.SiteID, intent.Revision, Text("name"), Text("path"), cancellationToken).ConfigureAwait(false),
            "component.add" => await authoring.AddComponentAsync(intent.SiteID, intent.Revision, payload.GetProperty("pageID").GetGuid(),
                payload.GetProperty("parentID").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("parentID").GetGuid(), Text("type"),
                payload.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()), cancellationToken).ConfigureAwait(false),
            "component.properties" => await authoring.UpdateComponentAsync(intent.SiteID, intent.Revision, payload.GetProperty("componentID").GetGuid(),
                component => component with { Properties = payload.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) }, cancellationToken).ConfigureAwait(false),
            "component.move" => await authoring.MoveComponentAsync(intent.SiteID, intent.Revision, payload.GetProperty("componentID").GetGuid(), payload.GetProperty("pageID").GetGuid(),
                payload.GetProperty("parentID").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("parentID").GetGuid(), payload.GetProperty("position").GetInt32(), cancellationToken).ConfigureAwait(false),
            "component.responsive" => await authoring.SetResponsiveOverrideAsync(intent.SiteID, intent.Revision, payload.GetProperty("componentID").GetGuid(), Text("breakpoint"),
                payload.GetProperty("properties").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()), cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Unknown captured Sites operation.")
        };
    }

    private async Task<SiteApiResult<SiteProject>> RecordCompletionAsync(SiteNativeWriteAuditPendingException pending)
    {
        try
        {
            var audited = await home.CompleteExecutionAsync(pending.Capability, pending.Outcome, CancellationToken.None).ConfigureAwait(false);
            if (!audited.Succeeded) throw pending;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { throw pending; }
        pending.Failure?.Throw();
        return pending.Result!;
    }

    private async Task RejectAdmissionAsync(HomeResourceExecutionCapability capability, Exception error, SiteNativeAdmissionAuditKind kind)
    {
        var pending = new SiteNativeAdmissionAuditPendingException(this, capability, ExceptionDispatchInfo.Capture(error), kind);
        await RetryAdmissionAuditAsync(pending, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Finishes only the retained negative admission audit; never claims, resolves or writes a project.</summary>
    public async Task RetryAdmissionAuditAsync(SiteNativeAdmissionAuditPendingException pending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (!ReferenceEquals(pending.Owner, this)) throw new UnauthorizedAccessException("Sites admission recovery belongs to another coordinator.");
        try
        {
            var audited = pending.Kind == SiteNativeAdmissionAuditKind.UnclaimedAbort
                ? await home.AbortUnclaimedExecutionAsync(pending.Capability, cancellationToken).ConfigureAwait(false)
                : await home.RetryRejectedClaimAuditAsync(pending.Capability, cancellationToken).ConfigureAwait(false);
            if (audited.Code == "HOME_CLAIM_REJECTION_NOT_OWNED" && pending.Kind == SiteNativeAdmissionAuditKind.RejectedClaim)
            {
                // An exact-operation mismatch may leave the handle unclaimed. Consume it as an abort,
                // without turning a foreign or already claimed handle into a new completion right.
                pending = new(this, pending.Capability, pending.Failure, SiteNativeAdmissionAuditKind.UnclaimedAbort);
                audited = await home.AbortUnclaimedExecutionAsync(pending.Capability, cancellationToken).ConfigureAwait(false);
            }
            if (audited.Code == "HOME_EXECUTION_ABORT_NOT_OWNED") pending.Failure.Throw();
            if (!audited.Succeeded)
            {
                // A different negative Home decision can already have stopped the request.
                // Observe its actual issuer-bound record; do not report our proposed audit
                // as recorded or manufacture another claim/completion right.
                var decision = await home.GetExecutionDecisionAsync(pending.Capability, cancellationToken).ConfigureAwait(false);
                if (decision is { Code: not "HOME_PERMISSION_REQUEST_NOT_FOUND",
                    State: HomePermissionRequestState.Failed or HomePermissionRequestState.Cancelled or
                        HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked })
                    pending.Failure.Throw();
                throw pending;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            // NOT_OWNED must surface the original denial, not manufacture an audit recovery grant.
            if (ReferenceEquals(error, pending.Failure.SourceException)) throw;
            throw pending;
        }
        pending.Failure.Throw();
    }

    /// <summary>Retries only the exact retained Home audit. Never claims or executes another write.</summary>
    public async Task<SiteApiResult<SiteProject>> RetryAuditAsync(SiteNativeWriteAuditPendingException pending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (!ReferenceEquals(pending.Owner, this)) throw new UnauthorizedAccessException("Sites audit recovery belongs to another coordinator.");
        try
        {
            var audited = await home.RetryCompletionAuditAsync(pending.Capability, cancellationToken).ConfigureAwait(false);
            if (!audited.Succeeded) throw pending;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            throw pending;
        }
        pending.Failure?.Throw();
        return pending.Result!;
    }
}

internal enum SiteNativeAdmissionAuditKind { UnclaimedAbort, RejectedClaim }

/// <summary>Owner-held negative audit recovery; it is never a resource execution grant.</summary>
public sealed class SiteNativeAdmissionAuditPendingException : Exception
{
    internal SiteNativeAdmissionAuditPendingException(SiteNativeWriteCoordinator owner, HomeResourceExecutionCapability capability,
        ExceptionDispatchInfo failure, SiteNativeAdmissionAuditKind kind)
        : base("The Sites write was stopped before owner admission, but its Home audit requires recovery. Do not repeat the write.")
    { Owner = owner; Capability = capability; Failure = failure; Kind = kind; }
    internal SiteNativeWriteCoordinator Owner { get; }
    internal HomeResourceExecutionCapability Capability { get; }
    internal ExceptionDispatchInfo Failure { get; }
    internal SiteNativeAdmissionAuditKind Kind { get; }
    public string RequestId => Capability.RequestId;
}

/// <summary>In-process owner-held audit recovery, not a serializable execution grant.</summary>
public sealed class SiteNativeWriteAuditPendingException : Exception
{
    internal SiteNativeWriteAuditPendingException(SiteNativeWriteCoordinator owner, HomeResourceExecutionCapability capability,
        HomeExecutionOutcome outcome, SiteApiResult<SiteProject>? result, ExceptionDispatchInfo? failure)
        : base("The Sites operation has finished but its Home audit requires recovery. Do not repeat the write.")
    { Owner = owner; Capability = capability; Outcome = outcome; Result = result; Failure = failure; }
    internal SiteNativeWriteCoordinator Owner { get; }
    internal HomeResourceExecutionCapability Capability { get; }
    internal HomeExecutionOutcome Outcome { get; }
    internal ExceptionDispatchInfo? Failure { get; }
    public SiteApiResult<SiteProject>? Result { get; }
    public bool CommitAcknowledged => Result?.IsSuccess == true;
    public string RequestId => Capability.RequestId;
}
