namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Home-owned operation for creating a new Files board and opening its exact
/// authorized session. True means the new Browse session is published; false
/// means the user cancelled before an artifact was created.
/// </summary>
public interface IBoardsHomeBoardCreationHost
{
    Task<bool> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
        CancellationToken cancellationToken = default);
}
