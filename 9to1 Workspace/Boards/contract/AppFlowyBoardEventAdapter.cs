namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// Converts the interaction callbacks exposed by AppFlowy Board into Haven-owned
/// commands. Product/HUI code depends on these commands rather than Flutter or
/// AppFlowy types.
/// </summary>
public static class AppFlowyBoardEventAdapter
{
    public static MoveGroupCommand MoveGroup(int fromIndex, int toIndex) =>
        new(fromIndex, toIndex);

    public static MoveCardCommand MoveCardWithinGroup(string groupId, int fromIndex, int toIndex) =>
        new(groupId, fromIndex, groupId, toIndex);

    public static MoveCardCommand MoveCardBetweenGroups(
        string fromGroupId,
        int fromIndex,
        string toGroupId,
        int toIndex) =>
        new(fromGroupId, fromIndex, toGroupId, toIndex);

    /// <summary>Donor <c>addGroup</c>/<c>insertGroup</c> (optional index) as a neutral command.</summary>
    public static CreateGroupCommand CreateGroup(string groupId, string title, int? toIndex = null) =>
        new(groupId, title, toIndex);

    /// <summary>Donor <c>removeGroup</c> as a neutral command.</summary>
    public static RemoveGroupCommand RemoveGroup(string groupId) =>
        new(groupId);

    /// <summary>Donor <c>updateGroupName</c> as a neutral command.</summary>
    public static RenameGroupCommand RenameGroup(string groupId, string title) =>
        new(groupId, title);

    /// <summary>Donor <c>addGroupItem</c> as a neutral command.</summary>
    public static CreateCardCommand AddCard(string groupId, string cardId, string title) =>
        new(groupId, cardId, title);

    /// <summary>Donor <c>removeGroupItem</c>/<c>removeAt</c> as a neutral command.</summary>
    public static RemoveCardCommand RemoveCard(string cardId) =>
        new(cardId);

    /// <summary>Donor <c>updateGroupItem</c>/<c>replaceOrInsertItem</c> title update as a neutral command.</summary>
    public static RenameCardCommand RenameCard(string cardId, string title) =>
        new(cardId, title);
}

public interface IHavenBoardStore
{
    Task<HavenBoardSnapshot?> LoadAsync(string boardId, CancellationToken cancellationToken = default);
    Task SaveAsync(HavenBoardSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IHavenBoardCommandSink
{
    Task<HavenBoardSnapshot> ExecuteAsync(HavenBoardCommand command, CancellationToken cancellationToken = default);
}

public sealed class HavenBoardCommandService(IHavenBoardStore store, string boardId = "board-main") : IHavenBoardCommandSink
{
    public async Task<HavenBoardSnapshot> ExecuteAsync(
        HavenBoardCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await store.LoadAsync(boardId, cancellationToken).ConfigureAwait(false)
            ?? HavenBoardSnapshot.CreateDefault();
        var updated = HavenBoardReducer.Apply(current, command);
        await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }
}
