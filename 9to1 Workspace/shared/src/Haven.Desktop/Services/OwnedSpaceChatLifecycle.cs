using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Production new-chat, branch and assignment entry points over the existing owning workspace.</summary>
internal sealed class OwnedSpaceChatLifecycle(Func<CancellationToken, Task<OwnedSpacesWorkspace>> openWorkspace,
    IConversationRepository conversations)
{
    public async Task<Conversation> CreateAsync(Conversation proposed, Guid? selectedSpaceId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        // Temporary conversations may retain context but never publish a SQL membership.
        if (proposed.IsTemporary) return proposed;
        var destinationId = proposed.SpaceId ?? (proposed.ParentConversationId is null ? selectedSpaceId : null);
        if (destinationId is null)
        {
            await conversations.UpsertConversationAsync(proposed, token);
            return proposed;
        }
        var workspace = await openWorkspace(token);
        await workspace.RequireCurrentAccessAsync(token);
        var destination = await workspace.Registry.ReadExistingAsync(destinationId.Value, token)
            ?? throw new InvalidOperationException("The selected Space is unavailable.");
        var scoped = proposed with { SpaceId = destination.Id };
        if (scoped.ParentConversationId is { } parentId)
        {
            var expectedSource = await conversations.GetAsync(parentId, token)
                ?? throw new InvalidOperationException("The source chat is unavailable.");
            return await workspace.Conversations.CreateBranchAsync(scoped, expectedSource, destination, token);
        }
        return await workspace.Writer.CreateAsync(scoped, destination, token);
    }

    public async Task<Conversation> AssignAsync(Conversation expected, Guid? destinationId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var workspace = await openWorkspace(token);
        await workspace.RequireCurrentAccessAsync(token);
        if (expected.SpaceId == destinationId) return expected;
        var source = expected.SpaceId is { } sourceId
            ? await workspace.Registry.ReadExistingAsync(sourceId, token)
                ?? throw new InvalidOperationException("The displayed source Space is unavailable.")
            : null;
        var destination = destinationId is { } id
            ? await workspace.Registry.ReadExistingAsync(id, token)
                ?? throw new InvalidOperationException("The selected destination Space is unavailable.")
            : null;
        return await workspace.Writer.AssignAsync(expected, source, destination, token);
    }
}
