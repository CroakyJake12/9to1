using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeHost;

/// <summary>A privately issued page of direct and inherited access grants, never an invitation or identity resolver.</summary>
public sealed class FilesNativeBrowserSharePage
{
    internal FilesNativeBrowserSharePage(FilesNativeBrowserPage page, HostedItemMetadata item,
        IReadOnlyList<FilesEmailShare> grants, string? next, FilesChangeCursor changeCursor)
    { Page = page; Item = item; Grants = grants; Next = next; ChangeCursor = changeCursor; }
    public FilesNativeBrowserPage Page { get; }
    public HostedItemMetadata Item { get; }
    public IReadOnlyList<FilesEmailShare> Grants { get; }
    public string? Next { get; }
    public FilesChangeCursor ChangeCursor { get; }
}

public sealed partial class FilesNativeBrowserService
{
    private sealed record OriginalShares(NativeFilesWorkspace Workspace, AuthenticatedResourceActor Actor,
        Func<bool> Lifetime);
    private sealed class ShareMutation(FilesNativeBrowserSharePage page, FilesEmailShare grant,
        FilesEmailShareRequest? request)
    {
        internal readonly FilesNativeBrowserSharePage Page = page;
        internal readonly FilesEmailShare Grant = grant;
        internal readonly FilesEmailShareRequest? Request = request;
        internal bool IsRevocation => Request is null;
        internal string ActionId => IsRevocation ? "9to1.Files.Share.Revoke" : "9to1.Files.Share.Grant";
        private FilesResult<FilesEmailShare>? _actual;
        internal FilesResult<FilesEmailShare>? Read() => Volatile.Read(ref _actual);
        internal void Capture(FilesResult<FilesEmailShare> actual)
        {
            if (Interlocked.CompareExchange(ref _actual, actual, null) is { } prior && !ReferenceEquals(prior, actual))
                throw new InvalidOperationException("The original returned sharing result cannot be replaced.");
        }
    }
    private readonly ConditionalWeakTable<FilesNativeBrowserSharePage, OriginalShares> _issuedSharePages = new();
    private readonly ConditionalWeakTable<PreparedMutation, ShareMutation> _issuedShareMutations = new();

    public bool CanReviewNativeShares(FilesNativeBrowserPage page, HostedItemMetadata item) =>
        !page.IsTrash && _originalPages.TryGetValue(page, out var original)
        && page.Items.Any(row => ReferenceEquals(row, item)) && item.CurrentRevisionId is not null
        && item.OwnerPrincipalId == original.Actor.ActorId
        && original.Workspace.Provider.Location.Capabilities.HasFlag(FilesProviderCapabilities.Sharing);

    /// <summary>Uses only the held Home/actor/configuration callback inside the Files read lease.
    /// Files page and revision checks deliberately happen outside that lease.</summary>
    public async Task<FilesNativeBrowserSharePage> ReadNativeSharesAsync(FilesNativeBrowserPage page,
        HostedItemMetadata item, AuthenticatedResourceActor actor, Func<bool> lifetime,
        FilesNativeBrowserSharePage? previous = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!lifetime() || !CanReviewNativeShares(page, item)
            || !_originalPages.TryGetValue(page, out var original) || original.Actor != actor)
            throw new UnauthorizedAccessException("Retain the original owned Files item to review access.");
        string? next = null;
        if (previous is not null)
        {
            DemandSharePage(previous, actor);
            if (!ReferenceEquals(previous.Page, page) || !ReferenceEquals(previous.Item, item) || previous.Next is null)
                throw new InvalidOperationException("Retain the original next access page.");
            next = previous.Next;
        }
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        var current = await CaptureOriginalPageReadCheckAsync(page, actor, lifetime, token).ConfigureAwait(false);
        var result = await ObserveOriginalAsync(original.Workspace.Provider.GetEmailSharesAsync(item.Id, next,
            new FilesCommitAuthorityGuard(actor.ActorId, current), token)).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
            throw new InvalidOperationException(result.Error?.Message ?? "Access details are unavailable.");
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        if (!lifetime() || !await current(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original access review retired.");
        if (result.Value.Items.Any(grant => grant.Origin == FilesGrantOrigin.Direct
                ? grant.Share.ItemId != item.Id || grant.InheritedFrom is not null
                : grant.Origin != FilesGrantOrigin.Inherited || grant.InheritedFrom != grant.Share.ItemId))
            throw new UnauthorizedAccessException("Access details contain a malformed direct or inherited grant.");
        var issued = new FilesNativeBrowserSharePage(page, item,
            Array.AsReadOnly(result.Value.Items.ToArray()), result.Value.NextPageToken, page.ChangeCursor);
        _issuedSharePages.Add(issued, new(original.Workspace, actor, lifetime));
        return issued;
    }

    /// <summary>Streams only from the selected original owner's durable cursor. The bound Home check is
    /// revalidated for each event; no historical full-feed scan is performed.</summary>
    public async IAsyncEnumerable<FilesChangeEvent> SubscribeOriginalChangesAsync(
        FilesNativeBrowserPage page, AuthenticatedResourceActor actor, Func<bool> lifetime,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        if (!_originalPages.TryGetValue(page, out var retained) || retained.Actor != actor || !lifetime())
            throw new UnauthorizedAccessException("Retain the same live original Files page to subscribe to changes.");
        var current = await CaptureOriginalPageReadCheckAsync(page, actor, lifetime, token).ConfigureAwait(false);
        var guard = new FilesCommitAuthorityGuard(actor.ActorId, current);
        await foreach (var change in retained.Workspace.Provider.SubscribeAsync(page.ChangeCursor, token)
            .WithCancellation(token).ConfigureAwait(false))
        {
            await guard.ValidateAsync(token).ConfigureAwait(false);
            yield return change;
        }
    }

    private OriginalShares DemandSharePage(FilesNativeBrowserSharePage page, AuthenticatedResourceActor actor)
    {
        if (!_issuedSharePages.TryGetValue(page, out var original) || original.Actor != actor
            || !original.Lifetime() || !CanReviewNativeShares(page.Page, page.Item))
            throw new UnauthorizedAccessException("Retain the same privately issued owner access page.");
        return original;
    }

    public Task<PreparedMutation> PrepareNativeShareRevocationAsync(FilesNativeBrowserSharePage page,
        FilesEmailShare grant, AuthenticatedResourceActor actor, Func<bool> lifetime,
        string sessionId, CancellationToken token = default) =>
        PrepareNativeShareMutationAsync(page, grant, null, null, actor, lifetime, sessionId, token);

    /// <summary>Changes only an existing source-issued recipient's Viewer/Editor role.
    /// Its canonical principal, address and expiry are retained; this never resolves a new recipient or sends mail.</summary>
    public Task<PreparedMutation> PrepareNativeShareRoleChangeAsync(FilesNativeBrowserSharePage page,
        FilesEmailShare grant, FilesAccessRole role, AuthenticatedResourceActor actor, Func<bool> lifetime,
        string sessionId, CancellationToken token = default) =>
        PrepareNativeShareMutationAsync(page, grant, role, grant.Share.ExpiresAt, actor, lifetime, sessionId, token);

    /// <summary>Changes only the original existing recipient's direct Viewer/Editor scope and
    /// expiry. Principal/email are source-owned and cannot be retargeted; no invitation is sent.</summary>
    public Task<PreparedMutation> PrepareNativeShareScopeChangeAsync(FilesNativeBrowserSharePage page,
        FilesEmailShare grant, FilesAccessRole role, DateTimeOffset? expiresAt, AuthenticatedResourceActor actor,
        Func<bool> lifetime, string sessionId, CancellationToken token = default) =>
        PrepareNativeShareMutationAsync(page, grant, role, expiresAt, actor, lifetime, sessionId, token);

    private async Task<PreparedMutation> PrepareNativeShareMutationAsync(FilesNativeBrowserSharePage page,
        FilesEmailShare grant, FilesAccessRole? requestedRole, DateTimeOffset? requestedExpiry,
        AuthenticatedResourceActor actor, Func<bool> lifetime,
        string sessionId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var original = DemandSharePage(page, actor);
        var broker = _mutationBroker ?? throw new NotSupportedException("Home-reviewed access changes are unavailable.");
        if (!lifetime() || string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 256
            || !page.Grants.Any(row => ReferenceEquals(row, grant)) || grant.Origin != FilesGrantOrigin.Direct || grant.Share.IsRevoked
            || grant.Share.ItemId != page.Item.Id || grant.Share.Role is not (FilesAccessRole.Viewer or FilesAccessRole.Editor))
            throw new InvalidOperationException("Choose an existing direct viewer or editor grant from this access page.");
        if (requestedRole is { } role && (role is not (FilesAccessRole.Viewer or FilesAccessRole.Editor)
            || role == grant.Share.Role && requestedExpiry == grant.Share.ExpiresAt
            || grant.Share.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow
            || requestedExpiry is { } selectedExpiry && selectedExpiry <= DateTimeOffset.UtcNow))
            throw new InvalidOperationException("Choose a changed Viewer/Editor role or a future expiry on an active direct grant.");
        await RevalidateAsync(page.Page, actor, token).ConfigureAwait(false);
        bool Alive() => lifetime() && original.Lifetime();
        var now = DateTimeOffset.UtcNow;
        var operation = requestedRole is null ? "Share.Revoke" : "Share.Grant";
        var intent = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, page.Item.Id, page.Item.ParentId,
            null, operation, page.Item.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
        var request = requestedRole is { } replacementRole ? new FilesEmailShareRequest(page.Page.StoreID,
            page.Item.Id, page.Item.CurrentRevisionId!.Value, grant.Share.PrincipalId, grant.RecipientEmail,
            replacementRole, requestedExpiry, intent.Id) : null;
        var issued = new ShareMutation(page, grant, request);
        var scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", page.Item.Id.ToString(),
            page.Item.CurrentRevisionId!.Value.ToString(), ResourceAccess.Write) });
        var arguments = request is null
            ? JsonSerializer.SerializeToElement(new { operationID = intent.Id.Value,
                storeID = page.Page.StoreID, expectedStoreRevision = page.Page.StoreRevision, itemID = page.Item.Id.Value,
                expectedRevision = page.Item.CurrentRevisionId.Value.ToString(), shareID = grant.Share.Id.Value,
                principalID = grant.Share.PrincipalId, recipientEmail = grant.RecipientEmail,
                role = grant.Share.Role.ToString(), expiresAt = grant.Share.ExpiresAt, operation })
            : JsonSerializer.SerializeToElement(new { operationID = intent.Id.Value,
                storeID = page.Page.StoreID, expectedStoreRevision = page.Page.StoreRevision, itemID = page.Item.Id.Value,
                expectedRevision = page.Item.CurrentRevisionId.Value.ToString(), shareID = grant.Share.Id.Value,
                principalID = grant.Share.PrincipalId, recipientEmail = grant.RecipientEmail,
                previousRole = grant.Share.Role.ToString(), role = request.Role.ToString(),
                previousExpiresAt = grant.Share.ExpiresAt, expiresAt = request.ExpiresAt, operation });
        var description = issued.IsRevocation
            ? $"Revoke {grant.Share.Role.ToString().ToLowerInvariant()} access for {grant.RecipientEmail} on ‘{page.Item.Name}’. "
                + "Future access through this grant will stop; previously copied content cannot be recalled."
            : $"Change {grant.RecipientEmail} from {grant.Share.Role} to {request!.Role} on ‘{page.Item.Name}’. "
                + (request.ExpiresAt == grant.Share.ExpiresAt
                    ? "The recorded recipient and selected expiry stay the same. "
                    : $"Expiry changes from {DescribeShareExpiry(grant.Share.ExpiresAt)} to {DescribeShareExpiry(request.ExpiresAt)}. The recorded recipient stays the same. ")
                + "This replaces this recipient’s previous direct grants on this item. "
                + "Inherited folder access may still apply. Viewer permits metadata browsing; Editor also permits scoped renaming. "
                + "Document and content edits require their separate owning-app permissions. No invitation will be sent.";
        var approval = await ObserveOriginalAsync(broker.AuthorizeForActorAsync(actor, "files", issued.ActionId,
            scopes, arguments, description, null, sessionId, token)).ConfigureAwait(false);
        await RevalidateAsync(page.Page, actor, token).ConfigureAwait(false);
        DemandSharePage(page, actor);
        if (!Alive()) throw new ObjectDisposedException("Original Files access review");
        var prepared = new PreparedMutation(this, original.Workspace, page.Page, actor, intent,
            grant.RecipientEmail, scopes, Array.Empty<FilesItemRevisionPrecondition>(), arguments, Alive, approval);
        // Same original review/task/retirement/audit custody; neither sharing action dispatches a structural mutation.
        _issuedNativeMutations.Add(prepared, new ActualOperationObservation());
        _issuedShareMutations.Add(prepared, issued);
        return prepared;
    }

    public bool IsNativeShareMutation(PreparedMutation original) =>
        ReferenceEquals(original.Issuer, this) && _issuedShareMutations.TryGetValue(original, out _);

    public bool IsNativeShareRevocation(PreparedMutation original) =>
        ReferenceEquals(original.Issuer, this) && _issuedShareMutations.TryGetValue(original, out var issued) && issued.IsRevocation;

    private ShareMutation DemandIssuedShareMutation(PreparedMutation original)
    {
        DemandNativeMutation(original);
        return _issuedShareMutations.TryGetValue(original, out var issued) ? issued
            : throw new UnauthorizedAccessException("Retain the original access change.");
    }

    private ShareMutation DemandIssuedShareRevocation(PreparedMutation original)
    {
        var issued = DemandIssuedShareMutation(original);
        return issued.IsRevocation ? issued
            : throw new UnauthorizedAccessException("The original access change is not a revocation.");
    }

    public string DescribeNativeShareMutation(PreparedMutation original)
    {
        var issued = DemandIssuedShareMutation(original);
        return issued.IsRevocation
            ? $"Revoke direct access for {issued.Grant.RecipientEmail} on ‘{issued.Page.Item.Name}’"
            : $"Change {issued.Grant.RecipientEmail} from {issued.Grant.Share.Role} to {issued.Request!.Role} on ‘{issued.Page.Item.Name}’"
                + (issued.Request.ExpiresAt == issued.Grant.Share.ExpiresAt ? ""
                    : $" · expiry {DescribeShareExpiry(issued.Grant.Share.ExpiresAt)} → {DescribeShareExpiry(issued.Request.ExpiresAt)}");
    }

    public string DescribeNativeShareRevocation(PreparedMutation original)
    {
        DemandIssuedShareRevocation(original);
        return DescribeNativeShareMutation(original);
    }

    public FilesResult<FilesEmailShare>? ReadObservedNativeShareMutation(PreparedMutation original) =>
        DemandIssuedShareMutation(original).Read();

    public FilesResult<FilesEmailShare>? ReadObservedNativeShareRevocation(PreparedMutation original) =>
        DemandIssuedShareRevocation(original).Read();

    private static bool ShareMutationCommitted(ShareMutation issued, string actorId, FilesResult<FilesEmailShare>? result)
    {
        var returned = result?.Value;
        return result?.IsSuccess == true && returned is not null
            && returned.Share.ItemId == issued.Grant.Share.ItemId && returned.Share.PrincipalId == issued.Grant.Share.PrincipalId
            && (issued.IsRevocation
                ? returned.Share.IsRevoked && returned.Share.Id == issued.Grant.Share.Id
                : !returned.Share.IsRevoked && returned.Share.Id.Value != Guid.Empty && returned.Share.Id != issued.Grant.Share.Id
                    && returned.Share.Role == issued.Request!.Role && returned.RecipientEmail == issued.Request.RecipientEmail
                    && returned.Share.ExpiresAt == issued.Request.ExpiresAt && returned.GrantedByPrincipalId == actorId);
    }

    /// <summary>Validates the exact captured provider ACK against the issued access decision.
    /// It conveys no replay authority and does not claim that Home recorded the audit.</summary>
    public bool WasNativeShareMutationCommitted(PreparedMutation original)
    {
        var issued = DemandIssuedShareMutation(original);
        return ShareMutationCommitted(issued, original.Actor.ActorId, issued.Read());
    }

    public Task<FilesNativeBrowserMutationOutcome> ApplyNativeShareRevocationAsync(PreparedMutation original,
        CancellationToken token = default)
    {
        DemandIssuedShareRevocation(original);
        return ApplyNativeShareMutationAsync(original, token);
    }

    public Task<FilesNativeBrowserMutationOutcome> ApplyNativeShareMutationAsync(PreparedMutation original,
        CancellationToken token = default)
    {
        var issued = DemandIssuedShareMutation(original);
        lock (original.Gate)
        {
            if (original.ActualApply is { } prior && !(prior.IsCompletedSuccessfully && prior.Result.AwaitingHomeReview)) return prior;
            if (original.Retired || original.Capability is not null || original.ReviewAttempts++ >= 32)
                throw new InvalidOperationException("Inspect the original access outcome; another effect admission is unavailable.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = original.ActualApply = ApplyShareAfterPublicationAsync(start.Task, original, issued, token);
            start.SetResult(); return actual;
        }
    }

    private async Task<FilesNativeBrowserMutationOutcome> ApplyShareAfterPublicationAsync(Task start,
        PreparedMutation original, ShareMutation issued, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var broker = _mutationBroker!;
        bool Alive() => !original.Retired && original.Lifetime();
        if (!Alive()) throw new ObjectDisposedException("Original Files access review");
        DemandSharePage(issued.Page, original.Actor);
        await RevalidateAsync(original.Page, original.Actor, token).ConfigureAwait(false);
        var capability = await ObserveOriginalAsync(broker.BeginExecutionCapabilityAsync(original.Approval.RequestId,
            original.Arguments, token)).ConfigureAwait(false);
        if (capability is null) return new(null, true, false, "Home has not admitted this access change. Review its decision before choosing Apply.");
        lock (original.Gate) original.Capability = capability;
        HomeClaimedResourceCommitFence? fence = null;
        FilesResult<FilesEmailShare>? result = null;
        var claimed = false;
        List<Exception> failures = [];
        try
        {
            if (!Alive()) throw new ObjectDisposedException("Original Files access review");
            DemandSharePage(issued.Page, original.Actor);
            await RevalidateAsync(original.Page, original.Actor, token).ConfigureAwait(false);
            var claim = await ObserveOriginalAsync(broker.ClaimExecutionObservedAsync(capability, "files",
                issued.ActionId, original.Scopes, original.Arguments, token)).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != original.Actor)
                throw new UnauthorizedAccessException("Home did not claim the same original access change.");
            claimed = true;
            fence = await ObserveOriginalAsync(workspaces.CaptureBrowserCommitFenceAsync(original.Workspace, broker,
                capability, Alive, token).AsTask()).ConfigureAwait(false);
            var guard = new FilesCommitAuthorityGuard(original.Actor.ActorId, fence.ValidateAsync);
            var actual = issued.Request is { } grantRequest
                ? original.Workspace.Provider.GrantEmailAccessAsync(grantRequest, guard, token)
                : original.Workspace.Provider.RevokeEmailAccessAsync(issued.Grant.Share.Id,
                    original.OriginalOperation.BaseRevisionId!.Value, original.OriginalOperation.Id, guard, token);
            result = await ObserveOriginalAsync(actual).ConfigureAwait(false);
            issued.Capture(result); // Retain actual provider ACK before fence cleanup or Home audit.
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            if (fence is not null)
                try { await ObserveOriginalAsync(fence.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
        }
        if (!claimed)
        {
            try
            {
                var audit = await ObserveOriginalAsync(broker.AbortUnclaimedExecutionAsync(capability, CancellationToken.None)).ConfigureAwait(false);
                if (!audit.Succeeded && audit.Code != "HOME_EXECUTION_ABORT_NOT_OWNED") failures.Add(new InvalidOperationException(audit.Message));
            }
            catch (Exception error) { failures.Add(error); }
            throw new AggregateException("Original access admission refused; no replacement effect is available.", failures);
        }
        var committed = ShareMutationCommitted(issued, original.Actor.ActorId, result);
        var expiryChanged = !issued.IsRevocation && issued.Request!.ExpiresAt != issued.Grant.Share.ExpiresAt;
        var completedCode = issued.IsRevocation ? "FILES_SHARE_REVOKED" : expiryChanged ? "FILES_SHARE_SCOPE_CHANGED" : "FILES_SHARE_ROLE_CHANGED";
        var completedMessage = issued.IsRevocation ? "The original direct access grant was revoked."
            : expiryChanged ? "The existing recipient's direct role/expiry scope changed; principal and address were retained."
            : "The existing recipient's role was changed; recipient and expiry were retained.";
        var outcome = new HomeExecutionOutcome(committed ? HomePermissionRequestState.Succeeded
            : result is { IsSuccess: false } ? HomePermissionRequestState.Failed : HomePermissionRequestState.PartiallyCompleted,
            committed ? completedCode : result?.Error?.Code.ToString() ?? "FILES_SHARE_OUTCOME_UNKNOWN",
            committed ? completedMessage : result?.Error?.Message
                ?? "The original access commit or cleanup failed; inspect its retained outcome before further work.",
            committed ? [new("files.item", original.OriginalOperation.ItemId.ToString())] : []);
        lock (original.Gate) original.OriginalOutcome = outcome;
        HomePermissionOperationResult? recorded = null;
        try { recorded = await ObserveOriginalAsync(broker.CompleteExecutionAsync(capability, outcome, CancellationToken.None)).ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Original access commit/audit/cleanup failed; it cannot replay.", failures);
        var message = issued.IsRevocation
            ? recorded?.Succeeded == true ? "Access revoked. Other direct or inherited grants may still allow access."
                : "Access revoked; choose Retry audit to record the Home outcome without revoking again."
            : recorded?.Succeeded == true ? expiryChanged
                ? "Direct access role/expiry changed. Recipient is unchanged; inherited access may still apply."
                : "Direct access role changed. Recipient and selected expiry are unchanged; inherited access may still apply."
                : "Access scope changed; choose Retry audit to record the Home outcome without granting again.";
        return new(null, false, recorded?.Succeeded == true, committed ? message
            : result?.Error?.Message ?? "Inspect the original access outcome.");
    }
    private static string DescribeShareExpiry(DateTimeOffset? expiry) => expiry is { } instant
        ? instant.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) : "no expiry";

}
