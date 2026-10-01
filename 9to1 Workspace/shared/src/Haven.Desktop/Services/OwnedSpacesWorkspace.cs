using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Desktop.Services;

/// <summary>One displayed workspace over the existing settings and conversation stores.
/// Independent ownership receipts remain distinct; this does not make their writes atomic.</summary>
internal sealed class OwnedSpacesWorkspace
{
    private readonly OwnedSpacesSession _spaces;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IResourceStoreOwnershipReceiptAuthority _receipts;
    private readonly IResourceStoreIdentitySource _sqlIdentities;
    private readonly VerifiedResourceStoreOwnership _conversationBinding;
    private readonly Guid _sqlId;
    private readonly AuthenticatedResourceActor _actor;
    private readonly Func<bool> _allowsWrites;

    private OwnedSpacesWorkspace(OwnedSpacesSession spaces, IAuthenticatedResourceActorSource actors,
        IResourceStoreOwnershipReceiptAuthority receipts, IResourceStoreIdentitySource sqlIdentities,
        VerifiedResourceStoreOwnership conversationBinding, Guid sqlId, AuthenticatedResourceActor actor,
        OwnedSpaceConversations conversations, Func<bool> allowsWrites)
    {
        _spaces = spaces; _actors = actors; _receipts = receipts; _sqlIdentities = sqlIdentities;
        _conversationBinding = conversationBinding; _sqlId = sqlId; _actor = actor; _allowsWrites = allowsWrites;
        Conversations = conversations; Deletion = new(spaces.Registry, conversations);
    }
    public SpaceRegistry Registry => _spaces.Registry;
    public OwnedSpaceConversations Conversations { get; }
    public ISpaceConversationWriter Writer => Conversations;
    public OwnedSpaceDeletion Deletion { get; }
    public AuthenticatedResourceActor Actor => _actor;

    public static async Task<OwnedSpacesWorkspace> OpenAsync(VersionedAtomicSettingsStore settings,
        IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipReceiptAuthority receipts,
        IResourceStoreIdentitySource sqlIdentities, ConversationLocalStoreAuthority conversationAuthority,
        IConversationSpaceCommitStore conversationStore, Func<bool> allowsWrites, CancellationToken token)
    {
        var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Recover the current local profile in Home.");
        if (actor.AccountId is not null || actor.OrganisationId is not null || !allowsWrites())
            throw new UnauthorizedAccessException("This workspace requires its current local profile.");
        var authority = new SpaceLocalStoreAuthority(settings, actors, receipts, allowsWrites);
        var spaces = await OwnedSpacesSession.OpenAsync(settings, authority, actors, token).ConfigureAwait(false);
        // The actor captured before opening both stores remains the displayed actor.
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The displayed profile changed while opening Spaces.");
        var sql = await sqlIdentities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        var binding = await receipts.GetVerifiedAsync("conversation", sql.StoreId.ToString("D"), token).ConfigureAwait(false);
        if (sql.SchemaVersion != 1 || sql.StoreId == Guid.Empty || binding?.Receipt is null ||
            binding.ResourceKind != "conversation" || binding.StoreId != sql.StoreId.ToString("D") || binding.ProfileId != actor.ProfileId)
            throw new UnauthorizedAccessException("Set up this conversation store explicitly in Home before using Space chats.");
        var conversations = new OwnedSpaceConversations(actor, settings, sqlIdentities, receipts,
            conversationAuthority, conversationStore, allowsWrites);
        var workspace = new OwnedSpacesWorkspace(spaces, actors, receipts, sqlIdentities, binding, sql.StoreId,
            actor, conversations, allowsWrites);
        await workspace.RequireCurrentAccessAsync(token).ConfigureAwait(false);
        return workspace;
    }

    public async Task RequireCurrentAccessAsync(CancellationToken token)
    {
        await _spaces.RequireCurrentAccessAsync(token).ConfigureAwait(false);
        var identity = await _sqlIdentities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != _sqlId || !_allowsWrites() ||
            await _actors.GetCurrentAsync(token).ConfigureAwait(false) != _actor ||
            !await _receipts.IsCurrentAsync(_conversationBinding, _actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Space storage or the displayed profile changed. Reopen the workspace.");
    }
}
