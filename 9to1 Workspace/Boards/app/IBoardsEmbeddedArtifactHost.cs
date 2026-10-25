using Avalonia.Controls;
using Haven.Application;

namespace CakeOS.Apps.Boards.App;

/// <summary>The access mode requested for an owner-hosted page.</summary>
public enum BoardEmbeddedArtifactMode
{
    View,
    Edit,
}

/// <summary>
/// Home-owned bridge to existing artifact owners. Selection returns metadata identity only;
/// opening always resolves the source and rechecks its pinned revision and current permission.
/// </summary>
public interface IBoardsEmbeddedArtifactHost
{
    /// <summary>Supplies the real composed Boards surface for correctly parenting owner UI.</summary>
    void BindOwningControl(Control control);

    Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference reference,
        BoardEmbeddedArtifactMode mode, CancellationToken ct = default);

    /// <summary>Shows the existing Home Library selection flow and returns no content or grant.</summary>
    Task<BoardEmbeddedArtifactReference?> SelectAsync(string applicationId, CancellationToken ct = default);

    /// <summary>Creates through the named application's canonical owner and returns its stable identity.</summary>
    Task<BoardEmbeddedArtifactReference?> CreateAsync(string applicationId, CancellationToken ct = default);
}

/// <summary>The actual owner UI and lifetime for one pinned embedded artifact revision.</summary>
public interface IBoardsEmbeddedArtifactSession : IAsyncDisposable
{
    Control Content { get; }
    BoardEmbeddedArtifactReference CurrentReference { get; }
    bool IsPublicationCurrent { get; }
    Task<bool> TryPrepareToCloseAsync(CancellationToken ct = default);
    Task CloseAndDrainAsync();
}
