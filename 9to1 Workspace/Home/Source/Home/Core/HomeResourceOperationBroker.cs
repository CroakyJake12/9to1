using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Shared native/web operation bridge; resource ACLs intersect Home consent rather than being replaced by it.</summary>
public sealed class HomeResourceOperationBroker(ResourceAuthorizationService resources, HomePermissionTrustService permissions)
{
    private sealed record Binding(AuthenticatedResourceActor Actor, string TargetAppId, string ActionId, ResourceScope[] Scopes, string Digest);
    private readonly ConcurrentDictionary<HomeResourceExecutionCapability, Binding> _executions = new();
    private readonly ConcurrentDictionary<string, Binding> _bindings = new();

    public async Task<HomePermissionAuthorization> AuthorizeAsync(string targetAppId, string actionId,
        IReadOnlyList<ResourceScope> scopes, JsonElement arguments, string preview, string? backupId,
        string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (_bindings.Count >= 1024) throw new InvalidOperationException("Too many outstanding resource approval requests.");
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
        if (!(await permissions.BeginExecutionAsync(requestId, cancellationToken).ConfigureAwait(false)).IsAllowed) return null;
        var capability = new HomeResourceExecutionCapability(requestId, binding.TargetAppId, binding.ActionId,
            Array.AsReadOnly(binding.Scopes.ToArray()));
        return _executions.TryAdd(capability, binding) ? capability : null;
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
        var current = await resources.AuthorizeAsync(binding.ActionId, binding.Scopes, cancellationToken).ConfigureAwait(false);
        return current == binding.Actor && await permissions.IsExecutionCurrentAsync(capability.RequestId, cancellationToken).ConfigureAwait(false)
            ? current : null;
    }

    private static string Digest(JsonElement arguments) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText())));
}

/// <summary>Non-serializable issuer-bound handle. Its public metadata is descriptive and never independently grants access.</summary>
public sealed class HomeResourceExecutionCapability
{
    internal HomeResourceExecutionCapability(string requestId, string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes)
    { RequestId = requestId; TargetAppId = targetAppId; ActionId = actionId; Scopes = scopes; }
    public string RequestId { get; }
    public string TargetAppId { get; }
    public string ActionId { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
}
