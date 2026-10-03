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
    Func<bool> hostAllowsWrites) : ISpaceConversationWriter
{
    private readonly SpaceRegistry _readOnlyRegistry = new(settings);

    internal sealed record SpaceSnapshot(Guid Id, long Revision);

    public async Task<ConversationSpaceCommitResult> AssignAsync(Conversation expected,
        SpaceSnapshot? source, SpaceSnapshot? destination, CancellationToken token)
    {
        var proposed = CaptureAssignment(expected, source, destination);
        return await CommitAsync(new(expected, proposed), source, destination, token).ConfigureAwait(false);
    }

    private static Conversation CaptureAssignment(Conversation expected, SpaceSnapshot? source, SpaceSnapshot? destination)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.Id == Guid.Empty || expected.IsArchived ||
            expected.Kind is ConversationKind.Call or ConversationKind.AutomationRun or ConversationKind.Training || expected.SpaceId != source?.Id ||
            source?.Id == Guid.Empty || destination?.Id == Guid.Empty ||
            source is { Revision: < 1 } || destination is { Revision: < 1 } || source?.Id == destination?.Id)
            throw new InvalidOperationException("Select a current chat and a different Space assignment.");
        var now = DateTimeOffset.UtcNow;
        return expected with { SpaceId = destination?.Id, UpdatedAt = expected.UpdatedAt > now ? expected.UpdatedAt : now };
    }

    async Task<Conversation> ISpaceConversationWriter.AssignAsync(Conversation expected, SpaceDefinition? source,
        SpaceDefinition? destination, CancellationToken token)
    {
        var from = source is null ? null : new SpaceSnapshot(source.Id, source.Revision);
        var to = destination is null ? null : new SpaceSnapshot(destination.Id, destination.Revision);
        var proposed = CaptureAssignment(expected, from, to);
        var result = await CommitAsync(new(expected, proposed), from, to, token).ConfigureAwait(false);
        if (result.Status != ConversationSpaceCommitStatus.Committed) throw new SpaceConversationWriteException(result.Status);
        return proposed;
    }

    async Task<Conversation> ISpaceConversationWriter.CreateAsync(Conversation proposed, SpaceDefinition destination, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var result = await CreateChatAsync(proposed, new(destination.Id, destination.Revision), token).ConfigureAwait(false);
        if (result.Status != ConversationSpaceCommitStatus.Committed) throw new SpaceConversationWriteException(result.Status);
        return proposed;
    }

    public Task<ConversationSpaceCommitResult> CreateChatAsync(Conversation proposed, SpaceSnapshot destination, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(proposed); ArgumentNullException.ThrowIfNull(destination);
        if (proposed.Id == Guid.Empty || proposed.Kind != ConversationKind.Chat || proposed.IsArchived || proposed.IsTemporary ||
            destination.Id == Guid.Empty || destination.Revision < 1 || proposed.SpaceId != destination.Id)
            throw new InvalidOperationException("Create a canonical persistent chat in the selected current Space.");
        return CommitAsync(new(null, proposed), null, destination, token);
    }

    public async Task<Conversation> CreateBranchAsync(Conversation proposed, Conversation expectedSource,
        SpaceDefinition destination, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(proposed); ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(destination);
        if (proposed.Id == Guid.Empty || proposed.Id == expectedSource.Id || proposed.ParentConversationId != expectedSource.Id ||
            proposed.Kind != ConversationKind.Chat || proposed.IsArchived || proposed.IsTemporary || expectedSource.IsArchived ||
            expectedSource.Kind != ConversationKind.Chat || proposed.SpaceId != destination.Id || expectedSource.SpaceId != destination.Id ||
            destination.Id == Guid.Empty || destination.Revision < 1)
            throw new InvalidOperationException("Branch the displayed current chat inside its original Space.");
        var changes = Array.AsReadOnly(new[] { new ConversationSpaceChange(expectedSource, expectedSource),
            new ConversationSpaceChange(null, proposed) });
        var result = await CommitBatchAsync(changes, new(destination.Id, destination.Revision), null, null, token).ConfigureAwait(false);
        if (result.Status != ConversationSpaceCommitStatus.Committed) throw new SpaceConversationWriteException(result.Status);
        return proposed;
    }

    private Task<ConversationSpaceCommitResult> CommitAsync(ConversationSpaceChange change,
        SpaceSnapshot? source, SpaceSnapshot? destination, CancellationToken token) =>
        CommitBatchAsync(Array.AsReadOnly(new[] { change }), source, destination, null, token);

    public Task<ConversationSpaceCommitResult> DetachForDeletionAsync(SpaceDeletionOperation operation,
        IReadOnlyList<Conversation> expected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(expected);
        if (operation.Stage != SpaceDeletionStage.AwaitingConversationDetach || expected.Count is < 1 or > 1000)
            throw new InvalidOperationException("Select a pending deletion and a bounded canonical chat batch.");
        var rows = expected.ToArray();
        if (rows.Any(row => row is null || row.SpaceId != operation.SpaceId) || rows.Select(row => row.Id).Distinct().Count() != rows.Length)
            throw new InvalidOperationException("Deletion may detach only exact rows from its original Space.");
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<ConversationSpaceChange> changes = Array.AsReadOnly(rows.Select(row => new ConversationSpaceChange(row,
            row with { SpaceId = null, UpdatedAt = row.UpdatedAt > now ? row.UpdatedAt : now })).ToArray());
        return CommitBatchAsync(changes, null, null, operation, token);
    }

    public async Task<ConversationSpaceMembershipPage> ReadDeletionMembershipAsync(SpaceDeletionOperation operation, CancellationToken token)
    {
        if (!hostAllowsWrites() || actor.AccountId is not null || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("The displayed local profile cannot inspect deletion recovery.");
        var settingsId = await settings.GetStoreIdentityAsync(token).ConfigureAwait(false);
        var sqlId = await conversationIdentities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        var spacesBinding = await receipts.GetVerifiedAsync("spaces", settingsId.StoreId.ToString("D"), token).ConfigureAwait(false);
        var chatsBinding = await receipts.GetVerifiedAsync("conversation", sqlId.StoreId.ToString("D"), token).ConfigureAwait(false);
        async Task<bool> CurrentAsync()
        {
            if (!hostAllowsWrites() || settingsId.SchemaVersion != 1 || sqlId.SchemaVersion != 1 ||
                spacesBinding?.Receipt is null || chatsBinding?.Receipt is null ||
                spacesBinding.ProfileId != actor.ProfileId || chatsBinding.ProfileId != actor.ProfileId ||
                spacesBinding.ResourceKind != "spaces" || spacesBinding.StoreId != settingsId.StoreId.ToString("D") ||
                chatsBinding.ResourceKind != "conversation" || chatsBinding.StoreId != sqlId.StoreId.ToString("D")) return false;
            if (!await receipts.IsCurrentAsync(spacesBinding, actor, token).ConfigureAwait(false) ||
                !await receipts.IsCurrentAsync(chatsBinding, actor, token).ConfigureAwait(false)) return false;
            if (await _readOnlyRegistry.ReadDeletionAsync(operation.OperationId, token).ConfigureAwait(false) != operation ||
                (await settings.GetStoreIdentityAsync(token).ConfigureAwait(false)).StoreId != settingsId.StoreId) return false;
            return hostAllowsWrites() && await receipts.IsCurrentAsync(spacesBinding, actor, token).ConfigureAwait(false) &&
                await receipts.IsCurrentAsync(chatsBinding, actor, token).ConfigureAwait(false);
        }
        if (!await CurrentAsync().ConfigureAwait(false)) throw new UnauthorizedAccessException("Deletion recovery ownership changed.");
        var page = await conversations.ReadSpaceMembershipAsync(sqlId.StoreId, operation.SpaceId, null, 1000, token).ConfigureAwait(false);
        if (!await CurrentAsync().ConfigureAwait(false)) throw new UnauthorizedAccessException("Deletion recovery ownership changed.");
        return page;
    }

    private async Task<ConversationSpaceCommitResult> CommitBatchAsync(IReadOnlyList<ConversationSpaceChange> changes,
        SpaceSnapshot? source, SpaceSnapshot? destination, SpaceDeletionOperation? deletion, CancellationToken token)
    {
        if (!hostAllowsWrites() || actor.AccountId is not null || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("The displayed local profile cannot change this chat assignment.");
        // Exact immutable conversation and Space snapshots survive all admission waits.
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
            settings, receipts, _readOnlyRegistry, hostAllowsWrites, deletion);
        var admission = await conversationAuthority.CaptureAsync(actor, sqlIdentity.StoreId, changes, spaceAdmission, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Explicit conversation ownership is required.");
        return await conversations.CompareExchangeSpaceAsync(sqlIdentity.StoreId, changes, admission, token).ConfigureAwait(false);
    }

    private sealed class Admission(AuthenticatedResourceActor actor, Guid storeId,
        SpaceSnapshot? source, SpaceSnapshot? destination, VerifiedResourceStoreOwnership binding,
        IResourceStoreIdentitySource identities, IResourceStoreOwnershipReceiptAuthority receipts,
        SpaceRegistry registry, Func<bool> allowsWrites, SpaceDeletionOperation? deletion) : IConversationSpaceCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(ConversationSpaceCommitContext context, CancellationToken token)
        {
            // Only the separate settings store and raw Home receipt are read here; never SQL or ownership evidence.
            if (!allowsWrites() || !await receipts.IsCurrentAsync(binding, actor, token).ConfigureAwait(false)) return false;
            var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
            if (identity.SchemaVersion != 1 || identity.StoreId != storeId) return false;
            if (!await MatchesAsync(source, token).ConfigureAwait(false) ||
                !await MatchesAsync(destination, token).ConfigureAwait(false)) return false;
            if (deletion is not null && await registry.ReadDeletionAsync(deletion.OperationId, token).ConfigureAwait(false) != deletion) return false;
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
