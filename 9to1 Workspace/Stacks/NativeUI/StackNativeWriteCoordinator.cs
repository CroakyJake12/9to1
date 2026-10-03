using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Stacks.NativeUI;

public sealed record StackNativeWriteResult(Guid ProjectId, Guid DomainId, Guid? RevisionId, string Operation, bool CanonicalWriteAcknowledged);

/// <summary>Consumes the exact Home approval at the canonical Stack owner. Audit retry never replays an owner edit.</summary>
public sealed class StackNativeWriteCoordinator(IStackNativeWorkspaceAuthority workspace, HomeResourceOperationBroker home)
{
    public async Task<StackNativeWriteResult> ExecuteAsync(StackNativeWriteIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        async Task CheckBinding(CancellationToken ct)
        {
            if (await workspace.GetCurrentAsync(intent.Binding.ProjectId, ct).ConfigureAwait(false) != intent.Binding)
                throw new UnauthorizedAccessException("The canonical Stack Files/profile binding changed.");
        }
        try { await CheckBinding(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { await RejectAdmissionAsync(capability, error, StackNativeAdmissionAuditKind.UnclaimedAbort).ConfigureAwait(false); throw; }
        AuthenticatedResourceActor? actor;
        try { actor = await home.ClaimExecutionAsync(capability, "stacks", intent.ActionId, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { await RejectAdmissionAsync(capability, error, StackNativeAdmissionAuditKind.RejectedClaim).ConfigureAwait(false); throw; }
        if (actor is null)
            await RejectAdmissionAsync(capability, new UnauthorizedAccessException("Home did not authorize this exact Stack owner edit."), StackNativeAdmissionAuditKind.RejectedClaim).ConfigureAwait(false);
        ExceptionDispatchInfo? failure = null;
        StackNativeWriteResult? result = null;
        var noEffects = true;
        try
        {
            if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null ||
                actor.ActorId != intent.Binding.ActorId || actor.ProfileId != intent.Binding.ProfileId ||
                actor.AuthenticationRevision != intent.Binding.AuthenticationRevision)
                throw new UnauthorizedAccessException("The claimed Home actor does not match the current Stack binding.");
            var store = new JsonFileStackProjectStore(intent.Binding.ProjectDirectory, CheckBinding);
            var engine = new StackEngine(store);
            var main = await engine.OpenProjectAsync(cancellationToken).ConfigureAwait(false);
            if (main.ProjectId != intent.Binding.ProjectId || store.LoadedRevisionToken != intent.ProjectRevision)
                throw new StackFailureException(StackFailureCode.RevisionConflict, "The reviewed canonical Stack project changed before owner admission.", intent.Binding.ProjectId.ToString("D"), recoverable: true);
            var domain = (await engine.GetLineageAsync(cancellationToken).ConfigureAwait(false)).SingleOrDefault(item => item.Id == intent.DomainId);
            if (domain is null || (domain.HeadRevisionId ?? domain.BaseRevisionId) != intent.HeadRevisionId)
                throw new StackFailureException(StackFailureCode.RevisionConflict, "The reviewed canonical source domain changed before owner admission.", intent.DomainId.ToString("D"), recoverable: true);
            var capabilities = new HashSet<StackCapability> { StackCapability.ViewSource };
            if (intent.Operation == "create-domain") capabilities.Add(StackCapability.CreateDomain);
            if (intent.Operation == "commit") capabilities.Add(StackCapability.Contribute);
            var ownerActor = new StackActor(actor.ActorId, capabilities);
            // From here, an unexpected failure may follow a storage effect. Only the
            // owning store's pre-write CAS conflict is known to have no effect.
            noEffects = false;
            switch (intent.Operation)
            {
                case "create-domain":
                    var created = await engine.CreateDomainAsync(domain.Id, intent.Text, ownerActor, cancellationToken).ConfigureAwait(false);
                    result = new(main.ProjectId, created.Id, created.HeadRevisionId ?? created.BaseRevisionId, intent.Operation, true); break;
                case "set-active":
                    await engine.SetActiveDomainAsync(domain.Id, ownerActor, cancellationToken).ConfigureAwait(false);
                    result = new(main.ProjectId, domain.Id, domain.HeadRevisionId ?? domain.BaseRevisionId, intent.Operation, true); break;
                case "commit":
                    var revision = await engine.CreateCommitAsync(domain.Id, intent.Text, ownerActor, intent.Paths, cancellationToken).ConfigureAwait(false);
                    result = new(main.ProjectId, domain.Id, revision.Id, intent.Operation, true); break;
                default: throw new InvalidOperationException("Unknown captured Stack owner edit.");
            }
        }
        catch (StackFailureException error) when (error.Code == StackFailureCode.RevisionConflict)
        { noEffects = true; failure = ExceptionDispatchInfo.Capture(error); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { failure = ExceptionDispatchInfo.Capture(error); }
        var outcome = new HomeExecutionOutcome(failure is null ? HomePermissionRequestState.Succeeded :
            noEffects ? HomePermissionRequestState.Failed : HomePermissionRequestState.PartiallyCompleted,
            failure is null ? "StackCanonicalWriteCommitted" : noEffects ? "StackOwnerEditRejected" : "StackOwnerEditNeedsRecovery",
            failure is null ? "The canonical Stack owner acknowledged the edit." : noEffects ? "The Stack edit stopped before any owner effect." :
                "The Stack owner outcome requires inspection. Do not repeat the edit.",
            Array.AsReadOnly(capability.Scopes.Select(scope => new HomeObjectReference(scope.Kind, scope.Id)).ToArray()));
        return await RecordCompletionAsync(new(this, capability, outcome, result, failure)).ConfigureAwait(false);
    }

    private async Task<StackNativeWriteResult> RecordCompletionAsync(StackNativeWriteAuditPendingException pending)
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

    private async Task RejectAdmissionAsync(HomeResourceExecutionCapability capability, Exception error, StackNativeAdmissionAuditKind kind)
    {
        var pending = new StackNativeAdmissionAuditPendingException(this, capability, ExceptionDispatchInfo.Capture(error), kind);
        await RetryAdmissionAuditAsync(pending, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Finishes only the retained negative admission audit; never claims, resolves or writes a project.</summary>
    public async Task RetryAdmissionAuditAsync(StackNativeAdmissionAuditPendingException pending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (!ReferenceEquals(pending.Owner, this)) throw new UnauthorizedAccessException("Stacks admission recovery belongs to another coordinator.");
        try
        {
            var audited = pending.Kind == StackNativeAdmissionAuditKind.UnclaimedAbort
                ? await home.AbortUnclaimedExecutionAsync(pending.Capability, cancellationToken).ConfigureAwait(false)
                : await home.RetryRejectedClaimAuditAsync(pending.Capability, cancellationToken).ConfigureAwait(false);
            if (audited.Code == "HOME_CLAIM_REJECTION_NOT_OWNED" && pending.Kind == StackNativeAdmissionAuditKind.RejectedClaim)
            {
                // An exact-operation mismatch may leave the handle unclaimed. Consume it as an abort,
                // without turning a foreign or already claimed handle into a new completion right.
                pending = new(this, pending.Capability, pending.Failure, StackNativeAdmissionAuditKind.UnclaimedAbort);
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
    public async Task<StackNativeWriteResult> RetryAuditAsync(StackNativeWriteAuditPendingException pending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (!ReferenceEquals(pending.Owner, this)) throw new UnauthorizedAccessException("Stacks audit recovery belongs to another coordinator.");
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

internal enum StackNativeAdmissionAuditKind { UnclaimedAbort, RejectedClaim }

/// <summary>Owner-held negative audit recovery; it is never a resource execution grant.</summary>
public sealed class StackNativeAdmissionAuditPendingException : Exception
{
    internal StackNativeAdmissionAuditPendingException(StackNativeWriteCoordinator owner, HomeResourceExecutionCapability capability,
        ExceptionDispatchInfo failure, StackNativeAdmissionAuditKind kind)
        : base("The Stacks write was stopped before owner admission, but its Home audit requires recovery. Do not repeat the write.")
    { Owner = owner; Capability = capability; Failure = failure; Kind = kind; }
    internal StackNativeWriteCoordinator Owner { get; }
    internal HomeResourceExecutionCapability Capability { get; }
    internal ExceptionDispatchInfo Failure { get; }
    internal StackNativeAdmissionAuditKind Kind { get; }
    public string RequestId => Capability.RequestId;
}

/// <summary>In-process owner-held audit recovery, not a serializable execution grant.</summary>
public sealed class StackNativeWriteAuditPendingException : Exception
{
    internal StackNativeWriteAuditPendingException(StackNativeWriteCoordinator owner, HomeResourceExecutionCapability capability,
        HomeExecutionOutcome outcome, StackNativeWriteResult? result, ExceptionDispatchInfo? failure)
        : base("The Stacks operation has finished but its Home audit requires recovery. Do not repeat the write.")
    { Owner = owner; Capability = capability; Outcome = outcome; Result = result; Failure = failure; }
    internal StackNativeWriteCoordinator Owner { get; }
    internal HomeResourceExecutionCapability Capability { get; }
    internal HomeExecutionOutcome Outcome { get; }
    internal ExceptionDispatchInfo? Failure { get; }
    public StackNativeWriteResult? Result { get; }
    public bool CommitAcknowledged => Result?.CanonicalWriteAcknowledged == true;
    public string RequestId => Capability.RequestId;
}
