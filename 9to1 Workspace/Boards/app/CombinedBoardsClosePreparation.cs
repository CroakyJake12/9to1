namespace CakeOS.Apps.Boards.App;

internal sealed class CombinedBoardsClosePreparation(
    IBoardsPresentationClosePreparation document, IBoardsPresentationClosePreparation databases)
    : IBoardsPresentationClosePreparation
{
    public bool IsCurrent => document.IsCurrent && databases.IsCurrent;
    public async Task CommitAsync(CancellationToken token = default)
    { await document.CommitAsync(token); await databases.CommitAsync(token); }
    public async Task AbortAsync()
    { await databases.AbortAsync(); await document.AbortAsync(); }
    public async ValueTask DisposeAsync()
    { await databases.DisposeAsync(); await document.DisposeAsync(); }
}
