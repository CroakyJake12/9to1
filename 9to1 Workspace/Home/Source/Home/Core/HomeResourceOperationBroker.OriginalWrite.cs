using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

// Optional original-owner admission. Context metadata grants nothing: registered private issuer,
// original actor/resource policy, Home consent, exact capability tuple and final owner fence all remain mandatory.
public sealed partial class HomeResourceOperationBroker
{
    private sealed record OriginalWriteObservation(IOriginalCanonicalWriteContext Context);
    // These tables do not retain abandoned approvals/capabilities solely to keep observation tags alive.
    private readonly ConditionalWeakTable<Binding, OriginalWriteObservation> _originalWriteBindings = new();
    private readonly ConditionalWeakTable<HomeResourceExecutionCapability, OriginalWriteObservation> _originalWriteExecutions = new();

    public async Task<HomePermissionAuthorization> AuthorizeWithOriginalWriteForActorAsync(AuthenticatedResourceActor expectedActor,
        IOriginalCanonicalWriteContext originalWriteContext,
        string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes, JsonElement arguments,
        string preview, string? backupId, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(expectedActor);
        ArgumentNullException.ThrowIfNull(originalWriteContext);
        if (_bindings.Count + _rejectedBegins.Count >= 1024) throw new InvalidOperationException("Too many outstanding resource approval requests.");
        var capturedScopes = new List<ResourceScope>();
        foreach (var scope in scopes)
        {
            if (capturedScopes.Count == 1000 || scope is null) throw new ArgumentException("Resource scopes exceed admission capacity/shape.", nameof(scopes));
            capturedScopes.Add(scope);
        }
        var scopeSnapshot = capturedScopes.ToArray();
        var capturedArguments = arguments.Clone();
        var digest = Digest(capturedArguments);
        if (!resources.IsOriginalWriteContextForActor(expectedActor, originalWriteContext, actionId, scopeSnapshot))
            throw new UnauthorizedAccessException("The original canonical write observation was not issued by this registered owner.");
        var actor = await resources.AuthorizeOriginalWriteForActorAsync(expectedActor, originalWriteContext,
            actionId, scopeSnapshot, cancellationToken).ConfigureAwait(false);
        if (actor is null) throw new UnauthorizedAccessException("The authenticated actor lacks current canonical resource access.");
        if (actor != expectedActor)
            throw new UnauthorizedAccessException("The originating owner actor changed before Home review admission.");
        var objects = scopeSnapshot.Select(scope => new HomeObjectReference(scope.Kind, scope.Id)).ToArray();
        var caller = new HomePermissionCallerIdentity(actor.ActorId, actor.ActorId, actor.ProfileId, actor.AuthenticationRevision, true);
        // Retain the complete detached original tuple BEFORE durable Home authorization.
        // Resource metadata attests the original scope; it grants no permission independently.
        var submission = NormalizeOriginalSubmission(new HomePermissionRequestSubmission(Guid.NewGuid().ToString("N"), caller, sessionId,
            new HomePermissionScope(targetAppId, actionId, objects),
            new HomePermissionImpactPreview(objects.Select(item => item.ObjectType).Distinct().ToArray(), objects.Length,
                objects, false, preview, backupId, digest,
                new HomeCanonicalResourceBinding(1, actor, Array.AsReadOnly(scopeSnapshot.ToArray())))));
        var originalPolicy = permissions.ResolveTrustedActionPolicy(submission.Scope.TargetAppId, submission.Scope.ActionName);
        var result = await permissions.AuthorizeAsync(submission, cancellationToken).ConfigureAwait(false);
        if (result.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
        {
            var binding = new Binding(actor, targetAppId, actionId, scopeSnapshot, digest, submission, originalPolicy);
            _originalWriteBindings.Add(binding, new(originalWriteContext));
            _bindings[result.RequestId] = binding;
        }
        return result;
    }

    public async Task<HomeResourceExecutionCapability?> BeginExecutionWithOriginalWriteCapabilityAsync(string requestId, JsonElement arguments,
        IOriginalCanonicalWriteContext originalWriteContext,
        CancellationToken cancellationToken = default)
    {
        if (_executions.Count >= 1024 || !_bindings.TryGetValue(requestId, out var binding) || binding.Digest != Digest(arguments)) return null;
        if (!_originalWriteBindings.TryGetValue(binding, out var capturedOriginal)
            || !ReferenceEquals(capturedOriginal.Context, originalWriteContext)
            || !resources.IsOriginalWriteContextForActor(binding.Actor, originalWriteContext, binding.ActionId, binding.Scopes)) return null;
        var current = await resources.AuthorizeOriginalWriteForActorAsync(binding.Actor, originalWriteContext,
            binding.ActionId, binding.Scopes, cancellationToken).ConfigureAwait(false);
        if (current != binding.Actor) return null;
        var approval = await permissions.GetAuthorizationAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (!approval.IsAllowed || !_bindings.TryRemove(requestId, out var consumed) || consumed != binding) return null;
        _originalWriteBindings.Remove(binding);
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
        _originalWriteExecutions.Add(capability, new(originalWriteContext));
        if (_executions.TryAdd(capability, binding)) return capability;
        _originalWriteExecutions.Remove(capability);
        return null;
    }

    public async Task<HomeResourceClaimResult> ClaimExecutionWithOriginalWriteObservedAsync(HomeResourceExecutionCapability capability,
        string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes, JsonElement arguments,
        IOriginalCanonicalWriteContext originalWriteContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(scopes);
        var capturedScopes = new List<ResourceScope>();
        foreach (var scope in scopes)
        {
            if (capturedScopes.Count == 1000 || scope is null) throw new ArgumentException("Resource scopes exceed admission capacity/shape.", nameof(scopes));
            capturedScopes.Add(scope);
        }
        var scopeSnapshot = capturedScopes.ToArray();
        if (!_executions.TryGetValue(capability, out var binding))
            return new(HomeResourceClaimDisposition.Unavailable, null);
        if (binding.TargetAppId != targetAppId || binding.ActionId != actionId ||
            binding.Digest != Digest(arguments) || !binding.Scopes.SequenceEqual(scopeSnapshot))
            return new(HomeResourceClaimDisposition.InputNotConsumed, null);
        if (!_originalWriteExecutions.TryGetValue(capability, out var capturedOriginal)
            || !ReferenceEquals(capturedOriginal.Context, originalWriteContext)
            || !resources.IsOriginalWriteContextForActor(binding.Actor, originalWriteContext, binding.ActionId, scopeSnapshot))
            return new(HomeResourceClaimDisposition.InputNotConsumed, null);
        if (!_executions.TryRemove(capability, out var consumed) || consumed != binding)
            return new(HomeResourceClaimDisposition.Unavailable, null);
        _originalWriteExecutions.Remove(capability);
        try
        {
            var current = await resources.AuthorizeOriginalWriteForActorAsync(binding.Actor, originalWriteContext,
                binding.ActionId, binding.Scopes, cancellationToken).ConfigureAwait(false);
            if (current == binding.Actor && await permissions.IsExecutionCurrentAsync(capability.RequestId, cancellationToken).ConfigureAwait(false))
            {
                // Observe full original durable intent before claiming. Legacy bindings with no
                // retained full tuple may claim through their existing path, but cannot receive an attestation.
                var observed = binding.OriginalSubmission is null || binding.OriginalPolicy is null ? null
                    : await permissions.ReadRequestObservationAsync(capability.RequestId, cancellationToken).ConfigureAwait(false);
                var fullIntentMatches = binding.OriginalSubmission is null || binding.OriginalPolicy is null ||
                    MatchesClaimedOriginal(binding, observed);
                if (fullIntentMatches && capability.MarkClaimed(this))
                {
                    if (observed is not null) RetainClaimedAttestation(capability, binding, observed);
                    return new(HomeResourceClaimDisposition.Claimed, current);
                }
            }
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
        return new(HomeResourceClaimDisposition.ConsumedRejected, null);
    }

}
