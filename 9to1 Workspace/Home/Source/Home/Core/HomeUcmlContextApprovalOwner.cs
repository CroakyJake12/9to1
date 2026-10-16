using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using TrustPermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
using TrustPermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using PermissionTrustLevel = HavenOS.Home.PermissionsTrustNotifications.HomeTrustLevel;

namespace HavenOS.Home.Core;

/// <summary>One explicit Home review per source-issued UCML selection. This owner issues
/// no data capability: the canonical source must recheck exact current records and policy
/// after the wait and immediately before its actual local provider discloses context.</summary>
public sealed class HomeUcmlContextApprovalOwner
{
    public const string Action = "assistants.ucml.context.disclose";
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly IChatOriginalPersistentMemorySource _memory;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private sealed class Entry(IChatOriginalPersistentMemoryInput input, AuthenticatedResourceActor actor,
        HomePermissionRequestSubmission submission, string digest, DateTimeOffset deadline)
    {
        internal IChatOriginalPersistentMemoryInput Input => input;
        internal AuthenticatedResourceActor Actor => actor;
        internal HomePermissionRequestSubmission Submission => submission;
        internal string Digest => digest;
        internal DateTimeOffset Deadline => deadline;
        internal Task<bool> Original = null!;
    }
    public HomeUcmlContextApprovalOwner(IHomeCoreStateStore actualState, HomeLocalProfileIdentity actualProfiles,
        HomePermissionTrustService actualPermissions, IChatOriginalPersistentMemorySource actualMemory)
    {
        ArgumentNullException.ThrowIfNull(actualState); ArgumentNullException.ThrowIfNull(actualProfiles);
        ArgumentNullException.ThrowIfNull(actualPermissions); ArgumentNullException.ThrowIfNull(actualMemory);
        if (!actualProfiles.IsBoundToStore(actualState) || !actualPermissions.IsBoundToStore(actualState))
            throw new UnauthorizedAccessException("UCML Ask requires the SAME Home state, profile and permission owner.");
        _profiles = actualProfiles; _permissions = actualPermissions; _memory = actualMemory;
    }
    public bool HasOriginalComposition(IChatOriginalPersistentMemorySource source, HomeLocalProfileIdentity profiles) =>
        ReferenceEquals(source, _memory) && ReferenceEquals(profiles, _profiles);
    internal bool HasOriginalPermissionOwner(HomePermissionTrustService permissions) => ReferenceEquals(_permissions, permissions);

    public async Task<bool> IsOriginalCallerAllowedWithinSourceAsync(IChatOriginalPersistentMemoryInput input,
        AuthenticatedResourceActor actor, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (!_memory.IsIssuedOriginalInput(input))
            throw new UnauthorizedAccessException("Only the SAME original UCML input may check current Home disclosure policy.");
        var source = new HomeOwnershipOriginalSourceCallbacks(scope, retain);
        if (await source.ReadAsync(() => _profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != actor)
            return false;
        return await source.ReadAsync(() => _permissions.IsOriginalUcmlCallerAllowedWithinSourceAsync(this,
            "assistants.ucml:" + actor.ProfileId, source.Run, source.Retain, token)).ConfigureAwait(false);
    }

    public async Task<bool> AuthorizeOriginalAsync(IChatOriginalPersistentMemoryInput input, Guid requestId,
        AuthenticatedResourceActor actor, string assistantId, Guid conversationId, string policySha256,
        IReadOnlyList<Guid> recordIds, IReadOnlyList<string> askCategories, DateTimeOffset? accessExpiresAt,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var ids = recordIds.ToArray(); var categories = askCategories.ToArray();
        if (!_memory.IsIssuedOriginalInput(input) || requestId == Guid.Empty || conversationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(assistantId) || assistantId.Length > 256 || assistantId.Any(char.IsControl) ||
            policySha256.Length != 64 || !policySha256.All(Uri.IsHexDigit) || ids.Length is < 1 or > 8 ||
            ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length || categories.Length is < 1 or > 64 ||
            categories.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl) || value.Contains('*')) ||
            categories.Distinct(StringComparer.Ordinal).Count() != categories.Length)
            throw new UnauthorizedAccessException("UCML Ask needs the actual source input and a bounded exact selection/policy.");
        var source = new HomeOwnershipOriginalSourceCallbacks(scope, retain);
        if (await source.ReadAsync(() => _profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != actor ||
            accessExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow) return false;
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { actor, assistantId, conversationId, policySha256, ids, categories, accessExpiresAt })));
        Entry entry; bool start = false; TaskCompletionSource? publication = null;
        lock (_gate)
        {
            if (_entries.TryGetValue(requestId, out entry!))
            {
                if (!ReferenceEquals(entry.Input, input) || entry.Actor != actor || entry.Digest != digest)
                    throw new UnauthorizedAccessException("The same UCML review cannot be rebound to another input, policy or selection.");
            }
            else
            {
                if (_entries.Count >= 1024) throw new InvalidOperationException("Reopen the original UCML owner before requesting more reviews.");
                var objects = ids.Select(id => new HomeObjectReference("ucml-record", id.ToString("D"))).Concat(
                    categories.Select(category => new HomeObjectReference("ucml-category", category))).Concat([
                    new HomeObjectReference("assistant", assistantId), new HomeObjectReference("conversation", conversationId.ToString("D")),
                    new HomeObjectReference("ucml-policy", policySha256), new HomeObjectReference("ucml-selection", digest)]).ToArray();
                var id = "ucml-ask:" + requestId.ToString("N");
                var submission = new HomePermissionRequestSubmission(id,
                    new("assistants.ucml:" + actor.ProfileId, "Assistant UCML context", "home-local-profile", actor.AuthenticationRevision, true),
                    id, new("assistants", Action, objects),
                    new(["ucml-record", "ucml-category"], ids.Length, objects, false,
                        "Allow this exact UCML category and record selection for one local Assistant request? No remote sharing is permitted.", ArgumentsDigest: digest));
                var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
                if (accessExpiresAt is { } expires && expires < deadline) deadline = expires;
                entry = new(input, actor, submission, digest, deadline);
                publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.Original = RunPublishedAsync(publication.Task, entry, source, scope, retain, token);
                _entries.Add(requestId, entry); start = true;
            }
        }
        if (start)
        {
            try { source.Run(() => retain(entry.Original)); }
            finally { publication!.SetResult(); }
        }
        var accepted = await entry.Original.ConfigureAwait(false);
        if (!accepted || DateTimeOffset.UtcNow >= entry.Deadline || accessExpiresAt is { } finalExpiry && finalExpiry <= DateTimeOffset.UtcNow ||
            !_memory.IsIssuedOriginalInput(input) ||
            await source.ReadAsync(() => _profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != actor) return false;
        // Repeated validation of the SAME logical request checks current Home state;
        // a completed approval is never reusable by a different source-issued request.
        return await source.ReadAsync(() => _permissions.VerifyCompletedOriginalUcmlAskWithinSourceAsync(this,
            entry.Submission.RequestId!, source.Run, source.Retain, token)).ConfigureAwait(false);
    }

    private async Task<bool> RunPublishedAsync(Task publication, Entry entry,
        HomeOwnershipOriginalSourceCallbacks source, Action<Action> originalScope, Action<Task> originalRetain, CancellationToken token)
    {
        await publication.ConfigureAwait(false);
        var requestId = entry.Submission.RequestId!;
        try
        {
            var decision = await source.ReadAsync(() => _permissions.AuthorizeImportWithinOriginalSourceAsync(
                entry.Submission, source.Run, source.Retain, token)).ConfigureAwait(false);
            while (decision.State == HomePermissionRequestState.PendingApproval && DateTimeOffset.UtcNow < entry.Deadline)
            {
                var remaining = entry.Deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                await source.ReadAsync(async () => { await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
                if (await source.ReadAsync(() => _profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != entry.Actor) break;
                decision = await source.ReadAsync(() => _permissions.GetImportAuthorizationWithinOriginalSourceAsync(
                    requestId, source.Run, source.Retain, token)).ConfigureAwait(false);
            }
            if (decision.State != HomePermissionRequestState.Approved || decision.Code != "HOME_PERMISSION_ACCEPTED" ||
                decision.TrustLevel != PermissionTrustLevel.Session || DateTimeOffset.UtcNow >= entry.Deadline ||
                !_memory.IsIssuedOriginalInput(entry.Input) ||
                await source.ReadAsync(() => _profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false) != entry.Actor)
            {
                await WithdrawAsync(entry, originalScope, originalRetain).ConfigureAwait(false); return false;
            }
            var current = await source.ReadAsync(() => _permissions.ReadImportRequestWithinOriginalSourceAsync(
                requestId, source.Run, source.Retain, token)).ConfigureAwait(false);
            if (current is null || !MatchesOriginalRequest(current))
                throw new UnauthorizedAccessException("The actual Home UCML request no longer matches its source selection.");
            var begun = await source.ReadAsync(() => _permissions.BeginImportExecutionWithinOriginalSourceAsync(
                requestId, source.Run, source.Retain, token)).ConfigureAwait(false);
            if (begun.State != HomePermissionRequestState.Executing) return false;
            var completed = await source.ReadAsync(() => _permissions.RecordImportExecutionWithinOriginalSourceAsync(requestId,
                new(HomePermissionRequestState.Succeeded, "HOME_UCML_SELECTION_ACCEPTED", "The exact one-use local UCML selection was accepted; current source checks still precede disclosure.", entry.Submission.Scope.Objects),
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (!completed.Succeeded) throw new InvalidOperationException("The Home UCML approval outcome was not durably recorded.");
            return true;
        }
        catch (Exception original)
        {
            try { await WithdrawAsync(entry, originalScope, originalRetain).ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("The original UCML approval and cancellation audit failed.", original, cleanup); }
            throw;
        }
    }
    // Withdrawal must use the same caller custody through a fresh callback ledger. A
    // cancelled polling child remains reported by the productive ledger; it must not
    // prevent the bounded cleanup from durably cancelling that pending review.
    private Task<bool> WithdrawAsync(Entry entry, Action<Action> scope, Action<Task> retain) =>
        _permissions.WithdrawOriginalUcmlAskWithinSourceAsync(this, entry.Submission.RequestId!, scope, retain, CancellationToken.None);

    internal bool MatchesOriginalRequest(TrustPermissionRequest request)
    {
        Entry? entry; lock (_gate) entry = _entries.Values.SingleOrDefault(value => value.Submission.RequestId == request.RequestId);
        if (entry is null) return false;
        var expected = entry.Submission;
        return request.Caller == expected.Caller && request.SessionId == expected.SessionId &&
            request.Scope.TargetAppId == expected.Scope.TargetAppId && request.Scope.ActionName == Action && !request.Scope.IncludesAllObjects &&
            request.Scope.Objects.OrderBy(value => value.ObjectType).ThenBy(value => value.ObjectId).SequenceEqual(
                expected.Scope.Objects.OrderBy(value => value.ObjectType).ThenBy(value => value.ObjectId)) &&
            request.Impact.ArgumentsDigest == entry.Digest && request.Policy.RequiresPerActionApproval;
    }
}

public sealed class HomeUcmlContextActionPolicy : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == "assistants" && actionId == HomeUcmlContextApprovalOwner.Action
        ? new(TrustPermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}
