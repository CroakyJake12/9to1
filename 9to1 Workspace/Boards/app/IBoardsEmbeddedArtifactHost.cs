using Avalonia.Controls;
using Haven.Application;
using CakeOS.Cui.Themes;

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
    CuiThemePreferenceContext? ThemeContext => null;
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
    /// <summary>Rechecks the exact owner artifact, revision and current permission before content is shown or resumed.</summary>
    Task<bool> RevalidateAsync(CancellationToken ct = default);
    /// <summary>Retains an owner close preparation that can still be committed or aborted by the outer Board close.</summary>
    Task<IBoardsEmbeddedArtifactClosePreparation?> PrepareToCloseAsync(CancellationToken ct = default);
    Task CloseAndDrainAsync();
}

/// <summary>A real, retained preparation returned by the artifact owner; disposing an uncommitted preparation aborts it.</summary>
public interface IBoardsEmbeddedArtifactClosePreparation : IAsyncDisposable
{
    bool IsCurrent { get; }
    Task BeginCloseAsync(CancellationToken ct = default);
    Task AbortAsync();
}

/// <summary>Outer Board close gate. Home commits only after every Board/source close gate accepts.</summary>
public interface IBoardsPresentationClosePreparation : IAsyncDisposable
{
    bool IsCurrent { get; }
    Task CommitAsync(CancellationToken ct = default);
    Task AbortAsync();
}

/// <summary>A completed close is idempotently accepted after the presentation has already disposed its surface.</summary>
internal sealed class CompletedBoardsPresentationClosePreparation : IBoardsPresentationClosePreparation
{
    public static CompletedBoardsPresentationClosePreparation Instance { get; } = new();
    private CompletedBoardsPresentationClosePreparation() { }
    public bool IsCurrent => true;
    public Task CommitAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task AbortAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
