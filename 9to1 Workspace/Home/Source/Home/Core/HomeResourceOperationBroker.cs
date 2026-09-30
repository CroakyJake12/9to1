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
    private sealed record Binding(AuthenticatedResourceActor Actor, string ActionId, ResourceScope[] Scopes, string Digest);
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
            _bindings[result.RequestId] = new(actor, actionId, scopeSnapshot, digest);
        return result;
    }

    public async Task<bool> BeginExecutionAsync(string requestId, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!_bindings.TryGetValue(requestId, out var binding) || binding.Digest != Digest(arguments)) return false;
        var current = await resources.AuthorizeAsync(binding.ActionId, binding.Scopes, cancellationToken).ConfigureAwait(false);
        if (current != binding.Actor) return false;
        var approval = await permissions.GetAuthorizationAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (!approval.IsAllowed || !_bindings.TryRemove(requestId, out var consumed) || consumed != binding) return false;
        return (await permissions.BeginExecutionAsync(requestId, cancellationToken).ConfigureAwait(false)).IsAllowed;
    }

    private static string Digest(JsonElement arguments) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText())));
}
