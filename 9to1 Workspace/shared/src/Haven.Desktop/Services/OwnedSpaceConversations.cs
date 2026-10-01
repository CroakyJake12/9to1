using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.Services;

/// <summary>Existing-chat assignment through the canonical SQL transaction. Spaces and SQL remain
/// separate stores: both Space snapshots are observed at admission and immediately before SQL publication.</summary>
internal sealed class OwnedSpaceConversations(
    AuthenticatedResourceActor actor,
    VersionedAtomicSettingsStore settings,
    IResourceStoreIdentitySource conversationIdentities,
    IResourceStoreOwnershipReceiptAuthority receipts,
    ConversationLocalStoreAuthority conversationAuthority,
    IConversationSpaceCommitStore conversations,
    Func<bool> hostAllowsWrites)
{
    private readonly SpaceRegistry _readOnlyRegistry = new(settings);

    internal sealed record SpaceSnapshot(Guid Id, long Revision);

    public async Task<ConversationSpaceCommitResult> AssignAsync(Conversation expected,
        SpaceSnapshot? source, SpaceSnapshot? destination, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.Id == Guid.Empty || expected.IsArchived || expected.SpaceId != source?.Id ||
            source?.Id == Guid.Empty || destination?.Id == Guid.Empty ||
            source is { Revision: < 1 } || destination is { Revision: < 1 } ||
            source?.Id == destination?.Id)
            throw new InvalidOperationException("Select a current chat and a different Space assignment.");
        if (!hostAllowsWrites() || actor.AccountId is not null || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("The displayed local profile cannot change this chat assignment.");

        // Conversation and SpaceSnapshot are immutable records. No caller-owned collection crosses an await.
        var now = DateTimeOffset.UtcNow;
        var proposed = expected with { SpaceId = destination?.Id, UpdatedAt = expected.UpdatedAt > now ? expected.UpdatedAt : now };
        IReadOnlyList<ConversationSpaceChange> changes = Array.AsReadOnly(new[] { new ConversationSpaceChange(expected, proposed) });
        var spaceIdentity = await settings.GetStoreIdentityAsync(token).ConfigureAwait(false);
        var sqlIdentity = await conversationIdentities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (spaceIdentity.SchemaVersion != 1 || sqlIdentity.SchemaVersion != 1 ||
            spaceIdentity.StoreId == Guid.Empty || sqlIdentity.StoreId == Guid.Empty)
            throw new UnauthorizedAccessException("The canonical stores are unavailable.");
        var binding = await receipts.GetVerifiedAsync("spaces", spaceIdentity.StoreId.ToString("D"), token).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "spaces" ||
            binding.StoreId != spaceIdentity.StoreId.ToString("D") || binding.ProfileId != actor.ProfileId)
            throw new UnauthorizedAccessException("Explicit Spaces ownership is required.");
        var spaceAdmission = new Admission(actor, spaceIdentity.StoreId, source, destination, binding,
            settings, receipts, _readOnlyRegistry, hostAllowsWrites);
        var admission = await conversationAuthority.CaptureAsync(actor, sqlIdentity.StoreId, changes, spaceAdmission, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Explicit conversation ownership is required.");
        return await conversations.CompareExchangeSpaceAsync(sqlIdentity.StoreId, changes, admission, token).ConfigureAwait(false);
    }

    private sealed class Admission(AuthenticatedResourceActor actor, Guid storeId,
        SpaceSnapshot? source, SpaceSnapshot? destination, VerifiedResourceStoreOwnership binding,
        IResourceStoreIdentitySource identities, IResourceStoreOwnershipReceiptAuthority receipts,
        SpaceRegistry registry, Func<bool> allowsWrites) : IConversationSpaceCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(ConversationSpaceCommitContext context, CancellationToken token)
        {
            // Only the separate settings store and raw Home receipt are read here; never SQL or ownership evidence.
            if (!allowsWrites() || !await receipts.IsCurrentAsync(binding, actor, token).ConfigureAwait(false)) return false;
            var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
            if (identity.SchemaVersion != 1 || identity.StoreId != storeId) return false;
            if (!await MatchesAsync(source, token).ConfigureAwait(false) ||
                !await MatchesAsync(destination, token).ConfigureAwait(false)) return false;
            return allowsWrites() && await receipts.IsCurrentAsync(binding, actor, token).ConfigureAwait(false);
        }

        private async Task<bool> MatchesAsync(SpaceSnapshot? expected, CancellationToken token)
        {
            if (expected is null) return true;
            var current = await registry.ReadExistingAsync(expected.Id, token).ConfigureAwait(false);
            return current is { IsArchived: false } && current.Revision == expected.Revision;
        }
    }
}
