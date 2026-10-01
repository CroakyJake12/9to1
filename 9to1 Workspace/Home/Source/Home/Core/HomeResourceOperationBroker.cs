using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

internal enum HomeResourceRejectionKind { Claim = -1, Begin = -3, UnclaimedAbort = -5 }

/// <summary>Shared native/web operation bridge; resource ACLs intersect Home consent rather than being replaced by it.</summary>
public sealed class HomeResourceOperationBroker(ResourceAuthorizationService resources, HomePermissionTrustService permissions)
{
    private sealed record Binding(AuthenticatedResourceActor Actor, string TargetAppId, string ActionId, ResourceScope[] Scopes, string Digest);
    private readonly ConcurrentDictionary<HomeResourceExecutionCapability, Binding> _executions = new();
    private readonly ConcurrentDictionary<string, Binding> _bindings = new();
    private readonly ConcurrentDictionary<string, HomeResourceExecutionCapability> _rejectedBegins = new();

    public async Task<HomePermissionAuthorization> AuthorizeAsync(string targetAppId, string actionId,
        IReadOnlyList<ResourceScope> scopes, JsonElement arguments, string preview, string? backupId,
        string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (_bindings.Count + _rejectedBegins.Count >= 1024) throw new InvalidOperationException("Too many outstanding resource approval requests.");
        var scopeSnapshot = scopes.ToArray();
        var actor = await resources.AuthorizeAsync(actionId, scopeSnapshot, cancellationToken).ConfigureAwait(false);
        if (actor is null) throw new UnauthorizedAccessException("The authenticated actor lacks current canonical resource access.");
        var objects = scopeSnapshot.Select(scope => new HomeObjectReference(scope.Kind, scope.Id)).ToArray();
        var digest = Digest(arguments);
        var caller = new HomePermissionCallerIdentity(actor.ActorId, actor.ActorId, actor.ProfileId, actor.AuthenticationRevision, true);
        var result = await permissions.AuthorizeAsync(new(null, caller, sessionId, new(targetAppId, actionId, objects),
            new(objects.Select(item => item.ObjectType).Distinct().ToArray(), objects.Length, objects, false, preview, backupId, digest)), cancellationToken).ConfigureAwait(false);
        if (result.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
            _bindings[result.RequestId] = new(actor, targetAppId, actionId, scopeSnapshot, digest);
        return result;
    }

    public async Task<bool> BeginExecutionAsync(string requestId, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var capability = await BeginExecutionCapabilityAsync(requestId, arguments, cancellationToken).ConfigureAwait(false);
        // Legacy callers consume directly and do not leave a second reusable owner capability.
        return capability is not null && _executions.TryRemove(capability, out _);
    }

    /// <summary>Consumes Home approval once. The owner must separately claim this issuer-bound capability immediately
    /// before its expected-revision transaction; discovery, signatures and copied request fields cannot mint it.</summary>
    public async Task<HomeResourceExecutionCapability?> BeginExecutionCapabilityAsync(string requestId, JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (_executions.Count >= 1024 || !_bindings.TryGetValue(requestId, out var binding) || binding.Digest != Digest(arguments)) return null;
        var current = await resources.AuthorizeAsync(binding.ActionId, binding.Scopes, cancellationToken).ConfigureAwait(false);
        if (current != binding.Actor) return null;
        var approval = await permissions.GetAuthorizationAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (!approval.IsAllowed || !_bindings.TryRemove(requestId, out var consumed) || consumed != binding) return null;
        var capability = new HomeResourceExecutionCapability(this, requestId, binding.TargetAppId, binding.ActionId,
            Array.AsReadOnly(binding.Scopes.ToArray()));
        try
        {
            if (!(await permissions.BeginExecutionAsync(requestId, cancellationToken).ConfigureAwait(false)).IsAllowed) return null;
        }
        catch
        {
            // No capability crossed the owner boundary. Even a post-publication storage exception
            // cannot turn this consumed intent into a second dispatch attempt.
            capability.MarkRejected(this, HomeResourceRejectionKind.Begin);
            _rejectedBegins[requestId] = capability;
            try { await RetryRejectedBeginAuditAsync(requestId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception auditFailure) when (auditFailure is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            throw;
        }
        return _executions.TryAdd(capability, binding) ? capability : null;
    }

    /// <summary>Originating host recovery for a failed dispatch admission. This request ID identifies
    /// only an already-consumed local negative outcome; it cannot reconstruct a capability or invoke an owner.</summary>
    public async Task<HomePermissionOperationResult> RetryRejectedBeginAuditAsync(string requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId) || !_rejectedBegins.TryGetValue(requestId, out var capability))
            return new(false, "HOME_BEGIN_AUDIT_NOT_OWNED", "No failed dispatch admission is retained by this host.");
        var result = await capability.AuditRejectedAsync(this, () => permissions.RecordExecutionAsync(requestId,
            new(HomePermissionRequestState.Failed, "HOME_RESOURCE_BEGIN_REJECTED",
                "Dispatch admission failed; no execution capability was issued to the owner.", []), cancellationToken), cancellationToken, HomeResourceRejectionKind.Begin).ConfigureAwait(false);
        if (result.Succeeded) _rejectedBegins.TryRemove(requestId, out _);
        return result;
    }

    /// <summary>Atomically consumes this issuer's still-unclaimed handle, then records a fixed negative
    /// outcome. A concurrent owner claim wins or this abort wins, never both. Retry this same handle only
    /// to finish its abort audit. NOT_OWNED means no abort right; other false results or storage exceptions
    /// retain the negative handle for audit recovery and must not be treated as successful recording.</summary>
    public async Task<HomePermissionOperationResult> AbortUnclaimedExecutionAsync(HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability);
        cancellationToken.ThrowIfCancellationRequested();
        HomePermissionOperationResult NotOwned() => new(false, "HOME_EXECUTION_ABORT_NOT_OWNED",
            "This host cannot abort a foreign, already claimed, or differently rejected execution handle.");
        if (_executions.TryRemove(capability, out _) && !capability.MarkRejected(this, HomeResourceRejectionKind.UnclaimedAbort))
            return NotOwned();
        var result = await capability.AuditRejectedAsync(this, () => permissions.RecordExecutionAsync(capability.RequestId,
            new(HomePermissionRequestState.Failed, "HOME_RESOURCE_EXECUTION_ABORTED",
                "The owning operation stopped before any resource execution claim was issued.", []), cancellationToken),
            cancellationToken, HomeResourceRejectionKind.UnclaimedAbort).ConfigureAwait(false);
        return result.Code == "HOME_CLAIM_REJECTION_NOT_OWNED" ? NotOwned() : result;
    }

    /// <summary>Fresh actor, owner ACL and exact operation check followed by one-use claim. This is not a transaction
    /// implementation: the owner still must enforce its canonical revision and atomically persist all intended targets.</summary>
    public async Task<AuthenticatedResourceActor?> ClaimExecutionAsync(HomeResourceExecutionCapability capability,
        string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes, JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(scopes);
        var scopeSnapshot = scopes.ToArray();
        if (!_executions.TryGetValue(capability, out var binding) || binding.TargetAppId != targetAppId ||
            binding.ActionId != actionId || binding.Digest != Digest(arguments) || !binding.Scopes.SequenceEqual(scopeSnapshot)) return null;
        if (!_executions.TryRemove(capability, out var consumed) || consumed != binding) return null;
        try
        {
            var current = await resources.AuthorizeAsync(binding.ActionId, binding.Scopes, cancellationToken).ConfigureAwait(false);
            if (current == binding.Actor && await permissions.IsExecutionCurrentAsync(capability.RequestId, cancellationToken).ConfigureAwait(false) &&
                capability.MarkClaimed(this)) return current;
        }
        catch
        {
            capability.MarkRejected(this);
            try { await RetryRejectedClaimAuditAsync(capability, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception auditFailure) when (auditFailure is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            // Preserve resolver/cancellation failure. No claim was issued and the consumed handle is
            // never restored. If audit storage failed, the same handle can retry only rejection audit.
            throw;
        }
        capability.MarkRejected(this);
        await RetryRejectedClaimAuditAsync(capability, CancellationToken.None).ConfigureAwait(false);
        return null;
    }

    /// <summary>Audit-only recovery for this issuer's consumed, rejected claim. Cannot grant an owner
    /// claim, change the outcome, or overwrite an already revoked/terminal permission decision.</summary>
    public Task<HomePermissionOperationResult> RetryRejectedClaimAuditAsync(HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return capability.AuditRejectedAsync(this, () => permissions.RecordExecutionAsync(capability.RequestId,
            new(HomePermissionRequestState.Failed, "HOME_RESOURCE_CLAIM_REJECTED",
                "The resource authority could not confirm the owning operation; no owner claim was issued.", []), cancellationToken), cancellationToken);
    }

    /// <summary>Records the canonical owner's observed terminal outcome after a successful claim, once.
    /// This is an audit transition, never a new execution grant. Current ACL/actor changes cannot erase a
    /// previously committed effect. The caller must report actual failure/partial outcome, never infer success
    /// from approval or claim. If recording fails, retain the owning result and surface audit recovery;
    /// do not repeat the owning mutation. The same exact outcome may retry audit recording idempotently.
    /// No serializable request ID can manufacture this completion right.</summary>
    public async Task<HomePermissionOperationResult> CompleteExecutionAsync(HomeResourceExecutionCapability capability,
        HomeExecutionOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(outcome);
        try { outcome.Validate(); }
        catch (ArgumentException) { return new(false, "HOME_EXECUTION_OUTCOME_INVALID", "The owner must supply a valid terminal execution outcome."); }
        outcome = outcome with { AffectedObjects = Array.AsReadOnly(outcome.AffectedObjects.Take(10001).ToArray()) };
        if (outcome.Code.Length > 128 || outcome.Message is null || outcome.Message.Length > 4096 ||
            outcome.AffectedObjects.Count > 10000 || outcome.AffectedObjects.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.ObjectType) || item.ObjectType.Length > 256 ||
                string.IsNullOrWhiteSpace(item.ObjectId) || item.ObjectId.Length > 1024))
            return new(false, "HOME_EXECUTION_OUTCOME_INVALID", "The owner outcome exceeds supported bounds.");
        cancellationToken.ThrowIfCancellationRequested();
        return await capability.CompleteAsync(this, Digest(JsonSerializer.SerializeToElement(outcome)),
            () => permissions.RecordExecutionAsync(capability.RequestId, outcome, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static string Digest(JsonElement arguments) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText())));
}

/// <summary>Non-serializable issuer-bound handle. Its public metadata is descriptive and never independently grants access.</summary>
public sealed class HomeResourceExecutionCapability
{
    private readonly HomeResourceOperationBroker _issuer;
    private int _claimState;
    private readonly SemaphoreSlim _completionGate = new(1, 1);
    private string? _outcomeDigest;
    private HomePermissionOperationResult? _completed;
    internal HomeResourceExecutionCapability(HomeResourceOperationBroker issuer, string requestId, string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes)
    { _issuer = issuer; RequestId = requestId; TargetAppId = targetAppId; ActionId = actionId; Scopes = scopes; }
    internal bool MarkClaimed(HomeResourceOperationBroker issuer) =>
        ReferenceEquals(_issuer, issuer) && Interlocked.CompareExchange(ref _claimState, 1, 0) == 0;
    internal bool MarkRejected(HomeResourceOperationBroker issuer, HomeResourceRejectionKind kind = HomeResourceRejectionKind.Claim) =>
        ReferenceEquals(_issuer, issuer) && Enum.IsDefined(kind) && Interlocked.CompareExchange(ref _claimState, (int)kind, 0) == 0;
    internal async Task<HomePermissionOperationResult> AuditRejectedAsync(HomeResourceOperationBroker issuer,
        Func<Task<HomePermissionOperationResult>> record, CancellationToken ct, HomeResourceRejectionKind kind = HomeResourceRejectionKind.Claim)
    {
        var state = Volatile.Read(ref _claimState);
        if (!ReferenceEquals(_issuer, issuer) || !Enum.IsDefined(kind) || (state != (int)kind && state != (int)kind - 1))
            return new(false, "HOME_CLAIM_REJECTION_NOT_OWNED", "This issuer has no rejected claim to audit.");
        await _completionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_completed is not null) return _completed;
            var result = await record().ConfigureAwait(false);
            if (result.Succeeded) { _completed = result; Interlocked.Exchange(ref _claimState, (int)kind - 1); }
            return result;
        }
        finally { _completionGate.Release(); }
    }
    internal async Task<HomePermissionOperationResult> CompleteAsync(HomeResourceOperationBroker issuer, string outcomeDigest,
        Func<Task<HomePermissionOperationResult>> record, CancellationToken ct)
    {
        if (!ReferenceEquals(_issuer, issuer) || Volatile.Read(ref _claimState) <= 0)
            return new(false, "HOME_EXECUTION_COMPLETION_NOT_OWNED", "The completion requires this issuer's successfully claimed capability.");
        await _completionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_outcomeDigest is not null && _outcomeDigest != outcomeDigest)
                return new(false, "HOME_EXECUTION_OUTCOME_CHANGED", "Retry the exact observed outcome; a completion cannot be rewritten.");
            _outcomeDigest ??= outcomeDigest;
            if (_completed is not null) return _completed;
            var result = await record().ConfigureAwait(false);
            if (result.Succeeded) { _completed = result; Interlocked.Exchange(ref _claimState, 2); }
            return result;
        }
        finally { _completionGate.Release(); }
    }
    public string RequestId { get; }
    public string TargetAppId { get; }
    public string ActionId { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
}
