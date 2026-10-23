using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeHost;

/// <summary>A privately issued page of persisted direct grants, never an invitation or an identity resolver.</summary>
public sealed class FilesNativeBrowserSharePage
{
    internal FilesNativeBrowserSharePage(FilesNativeBrowserPage page, HostedItemMetadata item,
        IReadOnlyList<FilesEmailShare> grants, string? next)
    { Page = page; Item = item; Grants = grants; Next = next; }
    public FilesNativeBrowserPage Page { get; }
    public HostedItemMetadata Item { get; }
    public IReadOnlyList<FilesEmailShare> Grants { get; }
    public string? Next { get; }
}

public sealed partial class FilesNativeBrowserService
{
    private sealed record OriginalShares(NativeFilesWorkspace Workspace, AuthenticatedResourceActor Actor,
        Func<bool> Lifetime);
    private sealed class ShareRevocation(FilesNativeBrowserSharePage page, FilesEmailShare grant)
    {
        internal readonly FilesNativeBrowserSharePage Page = page;
        internal readonly FilesEmailShare Grant = grant;
        private FilesResult<FilesEmailShare>? _actual;
        internal FilesResult<FilesEmailShare>? Read() => Volatile.Read(ref _actual);
        internal void Capture(FilesResult<FilesEmailShare> actual)
        {
            if (Interlocked.CompareExchange(ref _actual, actual, null) is { } prior && !ReferenceEquals(prior, actual))
                throw new InvalidOperationException("The original returned sharing result cannot be replaced.");
        }
    }
    private readonly ConditionalWeakTable<FilesNativeBrowserSharePage, OriginalShares> _issuedSharePages = new();
    private readonly ConditionalWeakTable<PreparedMutation, ShareRevocation> _issuedShareRevocations = new();

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
        if (result.Value.Items.Any(grant => grant.Share.ItemId != item.Id))
            throw new UnauthorizedAccessException("Access details do not belong to the selected item.");
        var issued = new FilesNativeBrowserSharePage(page, item,
            Array.AsReadOnly(result.Value.Items.ToArray()), result.Value.NextPageToken);
        _issuedSharePages.Add(issued, new(original.Workspace, actor, lifetime));
        return issued;
    }

    private OriginalShares DemandSharePage(FilesNativeBrowserSharePage page, AuthenticatedResourceActor actor)
    {
        if (!_issuedSharePages.TryGetValue(page, out var original) || original.Actor != actor
            || !original.Lifetime() || !CanReviewNativeShares(page.Page, page.Item))
            throw new UnauthorizedAccessException("Retain the same privately issued owner access page.");
        return original;
    }

    public async Task<PreparedMutation> PrepareNativeShareRevocationAsync(FilesNativeBrowserSharePage page,
        FilesEmailShare grant, AuthenticatedResourceActor actor, Func<bool> lifetime,
        string sessionId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var original = DemandSharePage(page, actor);
        var broker = _mutationBroker ?? throw new NotSupportedException("Home-reviewed access changes are unavailable.");
        if (!lifetime() || string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 256
            || !page.Grants.Any(row => ReferenceEquals(row, grant)) || grant.Share.IsRevoked
            || grant.Share.ItemId != page.Item.Id || grant.Share.Role is not (FilesAccessRole.Viewer or FilesAccessRole.Editor))
            throw new InvalidOperationException("Choose an existing direct viewer or editor grant from this access page.");
        await RevalidateAsync(page.Page, actor, token).ConfigureAwait(false);
        bool Alive() => lifetime() && original.Lifetime();
        var now = DateTimeOffset.UtcNow;
        var intent = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, page.Item.Id, page.Item.ParentId,
            null, "Share.Revoke", page.Item.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
        var scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", page.Item.Id.ToString(),
            page.Item.CurrentRevisionId!.Value.ToString(), ResourceAccess.Write) });
        var arguments = JsonSerializer.SerializeToElement(new { operationID = intent.Id.Value,
            storeID = page.Page.StoreID, expectedStoreRevision = page.Page.StoreRevision, itemID = page.Item.Id.Value,
            expectedRevision = page.Item.CurrentRevisionId.Value.ToString(), shareID = grant.Share.Id.Value,
            principalID = grant.Share.PrincipalId, recipientEmail = grant.RecipientEmail,
            role = grant.Share.Role.ToString(), expiresAt = grant.Share.ExpiresAt, operation = "Share.Revoke" });
        var description = $"Revoke {grant.Share.Role.ToString().ToLowerInvariant()} access for {grant.RecipientEmail} on ‘{page.Item.Name}’. "
            + "Future access through this grant will stop; previously copied content cannot be recalled.";
        var approval = await ObserveOriginalAsync(broker.AuthorizeForActorAsync(actor, "files", "9to1.Files.Share.Revoke",
            scopes, arguments, description, null, sessionId, token)).ConfigureAwait(false);
        await RevalidateAsync(page.Page, actor, token).ConfigureAwait(false);
        DemandSharePage(page, actor);
        if (!Alive()) throw new ObjectDisposedException("Original Files access review");
        var prepared = new PreparedMutation(this, original.Workspace, page.Page, actor, intent,
            grant.RecipientEmail, scopes, Array.Empty<FilesItemRevisionPrecondition>(), arguments, Alive, approval);
        // Reuse the existing original review, task, retirement and audit custody; no structural action is dispatched.
        _issuedNativeMutations.Add(prepared, new ActualOperationObservation());
        _issuedShareRevocations.Add(prepared, new(page, grant));
        return prepared;
    }

    public bool IsNativeShareRevocation(PreparedMutation original) =>
        ReferenceEquals(original.Issuer, this) && _issuedShareRevocations.TryGetValue(original, out _);

    public string DescribeNativeShareRevocation(PreparedMutation original)
    {
        DemandNativeMutation(original);
        return _issuedShareRevocations.TryGetValue(original, out var issued)
            ? $"Revoke direct access for {issued.Grant.RecipientEmail} on ‘{issued.Page.Item.Name}’"
            : throw new UnauthorizedAccessException("Retain the original access change.");
    }

    public FilesResult<FilesEmailShare>? ReadObservedNativeShareRevocation(PreparedMutation original)
    {
        DemandNativeMutation(original);
        return _issuedShareRevocations.TryGetValue(original, out var issued) ? issued.Read()
            : throw new UnauthorizedAccessException("Retain the original access change.");
    }

    public Task<FilesNativeBrowserMutationOutcome> ApplyNativeShareRevocationAsync(PreparedMutation original,
        CancellationToken token = default)
    {
        DemandNativeMutation(original);
        if (!_issuedShareRevocations.TryGetValue(original, out var issued))
            throw new UnauthorizedAccessException("Retain the original access change.");
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
        PreparedMutation original, ShareRevocation issued, CancellationToken token)
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
                "9to1.Files.Share.Revoke", original.Scopes, original.Arguments, token)).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != original.Actor)
                throw new UnauthorizedAccessException("Home did not claim the same original access change.");
            claimed = true;
            fence = await ObserveOriginalAsync(workspaces.CaptureBrowserCommitFenceAsync(original.Workspace, broker,
                capability, Alive, token).AsTask()).ConfigureAwait(false);
            var actual = original.Workspace.Provider.RevokeEmailAccessAsync(issued.Grant.Share.Id,
                original.OriginalOperation.BaseRevisionId!.Value, original.OriginalOperation.Id,
                new FilesCommitAuthorityGuard(original.Actor.ActorId, fence.ValidateAsync), token);
            result = await ObserveOriginalAsync(actual).ConfigureAwait(false);
            issued.Capture(result); // Capture the actual provider ACK before fence cleanup or Home audit.
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
        var returned = result?.Value;
        var committed = result?.IsSuccess == true && returned is not null && returned.Share.IsRevoked
            && returned.Share.Id == issued.Grant.Share.Id && returned.Share.ItemId == issued.Grant.Share.ItemId
            && returned.Share.PrincipalId == issued.Grant.Share.PrincipalId;
        var outcome = new HomeExecutionOutcome(committed ? HomePermissionRequestState.Succeeded
            : result is { IsSuccess: false } ? HomePermissionRequestState.Failed : HomePermissionRequestState.PartiallyCompleted,
            committed ? "FILES_SHARE_REVOKED" : result?.Error?.Code.ToString() ?? "FILES_SHARE_OUTCOME_UNKNOWN",
            committed ? "The original direct access grant was revoked." : result?.Error?.Message
                ?? "The original access commit or cleanup failed; inspect its retained outcome before further work.",
            committed ? [new("files.item", original.OriginalOperation.ItemId.ToString())] : []);
        lock (original.Gate) original.OriginalOutcome = outcome;
        HomePermissionOperationResult? recorded = null;
        try { recorded = await ObserveOriginalAsync(broker.CompleteExecutionAsync(capability, outcome, CancellationToken.None)).ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Original access commit/audit/cleanup failed; it cannot replay.", failures);
        return new(null, false, recorded?.Succeeded == true, committed
            ? recorded?.Succeeded == true ? "Access revoked. Other direct or inherited grants may still allow access."
                : "Access revoked; choose Retry audit to record the Home outcome without revoking again."
            : result?.Error?.Message ?? "Inspect the original access outcome.");
    }
}
