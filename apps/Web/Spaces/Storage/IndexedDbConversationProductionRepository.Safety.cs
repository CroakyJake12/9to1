using System.Text.Json;
using Haven.Application;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>The same account-bound production CAS owns irreversible safety state.
/// Missing state is new, not a recovery policy for corrupt or unknown state.</summary>
public sealed partial class IndexedDbConversationProductionRepository : IConversationSafetyService
{
    private sealed record SafetyState(int SchemaVersion, ConfirmedSafetyFlag[] Flags, DateTimeOffset? LockedAt);
    private static SafetyState ReadSafety(State state)
    {
        if (state.Extensions is null || !state.Extensions.TryGetValue("safety", out var raw)) return new(1, [], null);
        var safety = raw.Deserialize<SafetyState>(BrowserConversationProductionStore.Json)
            ?? throw new InvalidDataException("The saved conversation safety state requires recovery.");
        if (safety.SchemaVersion != 1 || safety.Flags is null ||
            safety.Flags.Select(flag => flag.EventId).Distinct().Count() != safety.Flags.Length ||
            safety.Flags.Any(flag => flag.EventId == Guid.Empty || string.IsNullOrWhiteSpace(flag.Source) ||
                string.IsNullOrWhiteSpace(flag.Category) || string.IsNullOrWhiteSpace(flag.EvidenceHash)) ||
            (safety.Flags.Length >= 3) != (safety.LockedAt is not null))
            throw new InvalidDataException("The saved conversation safety identity or irreversible lock is invalid.");
        return safety;
    }
    private static ConversationSafetySnapshot Snapshot(Guid id, SafetyState state) => new(id, state.Flags.Length,
        state.LockedAt is null ? ConversationSafetyState.Active : ConversationSafetyState.Locked,
        state.LockedAt, state.Flags.LongLength);

    public async Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var (state, _) = await Load(conversationId, cancellationToken);
        return Snapshot(conversationId, ReadSafety(state));
    }
    public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid conversationId, ConfirmedSafetyFlag flag,
        CancellationToken cancellationToken)
    {
        Id(conversationId); ArgumentNullException.ThrowIfNull(flag); Id(flag.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.EvidenceHash);
        return Change(conversationId, (state, _) =>
        {
            var old = ReadSafety(state);
            if (old.Flags.Any(prior => prior.EventId == flag.EventId))
                return (state, new ConversationSafetyFlagResult(false, false, Snapshot(conversationId, old)));
            var confirmed = flag with { Source = flag.Source.Trim(), Category = flag.Category.Trim(),
                EvidenceHash = flag.EvidenceHash.Trim().ToLowerInvariant(), ConfirmedAt = flag.ConfirmedAt.ToUniversalTime() };
            var flags = old.Flags.Append(confirmed).ToArray();
            var lockedNow = old.LockedAt is null && flags.Length >= 3;
            var next = old with { Flags = flags, LockedAt = old.LockedAt ?? (lockedNow ? DateTimeOffset.UtcNow : null) };
            var extensions = state.Extensions is null ? new Dictionary<string, JsonElement>() : new(state.Extensions);
            extensions["safety"] = JsonSerializer.SerializeToElement(next, BrowserConversationProductionStore.Json);
            return (state with { Extensions = extensions },
                new ConversationSafetyFlagResult(true, lockedNow, Snapshot(conversationId, next)));
        }, cancellationToken);
    }
    public async Task EnsureMayActAsync(Guid conversationId, string operation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if ((await GetSnapshotAsync(conversationId, cancellationToken)).State == ConversationSafetyState.Locked)
            throw new ConversationSafetyLockException(conversationId, operation);
    }
}
